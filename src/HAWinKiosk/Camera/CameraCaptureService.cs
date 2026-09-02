using System.IO;
using OpenCvSharp;
using Windows.Devices.Enumeration;
using Windows.Media.Devices;

namespace HAWinKiosk.Camera;

/// <summary>
/// Captures JPEG frames via OpenCvSharp (MSMF/DSHOW).
/// WinRT MediaCapture hits "Element not found" on some USB cams (e.g. Logitech Brio 100).
/// </summary>
public sealed class CameraCaptureService : IDisposable
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HA-WinKiosk",
        "camera.log");

    private VideoCapture? _capture;
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private int _fps = 5;
    private int _framesEncoded;
    private bool _disposed;

    public event Action<byte[]>? FrameCaptured;
    public event Action<Exception>? Error;

    public static async Task<IReadOnlyList<(string Id, string Name)>> ListDevicesAsync()
    {
        try
        {
            var viaSelector = await DeviceInformation.FindAllAsync(MediaDevice.GetVideoCaptureSelector());
            if (viaSelector.Count > 0)
                return viaSelector.Select(d => (d.Id, string.IsNullOrWhiteSpace(d.Name) ? d.Id : d.Name)).ToList();
        }
        catch
        {
            // fall through
        }

        var devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
        return devices.Select(d => (d.Id, string.IsNullOrWhiteSpace(d.Name) ? d.Id : d.Name)).ToList();
    }

    public async Task StartAsync(string? deviceId, int fps)
    {
        Stop();
        _fps = Math.Clamp(fps, 1, 15);
        _framesEncoded = 0;

        var listed = await ListDevicesAsync();
        Log($"StartAsync device='{deviceId}' fps={_fps} listed={listed.Count}");
        foreach (var (id, name) in listed)
            Log($"  - {name}");

        if (listed.Count == 0)
            throw new InvalidOperationException("No video capture devices found.");

        var index = 0;
        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            for (var i = 0; i < listed.Count; i++)
            {
                if (listed[i].Id == deviceId)
                {
                    index = i;
                    break;
                }
            }
        }

        Log($"Opening OpenCV index={index} name={listed[index].Name}");

        // Prefer MSMF, then DSHOW — both bypass broken WinRT MediaCapture on this machine.
        _capture = OpenCapture(index, VideoCaptureAPIs.MSMF)
                   ?? OpenCapture(index, VideoCaptureAPIs.DSHOW)
                   ?? OpenCapture(index, VideoCaptureAPIs.ANY);

        if (_capture is null || !_capture.IsOpened())
            throw new InvalidOperationException("OpenCvSharp could not open the camera.");

        // MSMF/DSHOW often default to 640x480 if unset — ask for the highest mode the driver accepts.
        PreferHighestResolution(_capture);
        Log($"OpenCV opened backend={_capture.GetBackendName()} size={_capture.FrameWidth}x{_capture.FrameHeight}");

        _loopCts = new CancellationTokenSource();
        var token = _loopCts.Token;
        _loopTask = Task.Run(() => CaptureLoop(token), token);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Try common capture sizes descending. Drivers ignore unsupported sizes; we keep the best that sticks.
    /// </summary>
    private static void PreferHighestResolution(VideoCapture capture)
    {
        var beforeW = (int)capture.FrameWidth;
        var beforeH = (int)capture.FrameHeight;
        Log($"PreferHighestResolution start {beforeW}x{beforeH}");

        // Landscape common modes first, then a few portrait / odd laptop cams.
        (int W, int H)[] candidates =
        [
            (1920, 1080),
            (1600, 1200),
            (1280, 720),
            (1280, 960),
            (1024, 768),
            (960, 540),
            (800, 600),
            (640, 480)
        ];

        var bestW = beforeW;
        var bestH = beforeH;
        var bestArea = Math.Max(0, beforeW * beforeH);

        foreach (var (w, h) in candidates)
        {
            try
            {
                capture.FrameWidth = w;
                capture.FrameHeight = h;
            }
            catch
            {
                continue;
            }

            var gotW = (int)capture.FrameWidth;
            var gotH = (int)capture.FrameHeight;
            var area = gotW * gotH;
            Log($"  tried {w}x{h} -> {gotW}x{gotH}");
            if (area > bestArea)
            {
                bestArea = area;
                bestW = gotW;
                bestH = gotH;
            }
        }

        // Re-apply best so we do not end on a failed lower attempt.
        if (bestArea > 0)
        {
            try
            {
                capture.FrameWidth = bestW;
                capture.FrameHeight = bestH;
            }
            catch
            {
                // keep whatever the driver left
            }
        }

        Log($"PreferHighestResolution chose {capture.FrameWidth}x{capture.FrameHeight}");
    }

    private static VideoCapture? OpenCapture(int index, VideoCaptureAPIs api)
    {
        try
        {
            var cap = new VideoCapture(index, api);
            if (cap.IsOpened())
            {
                Log($"OpenCapture OK index={index} api={api}");
                return cap;
            }

            Log($"OpenCapture not opened index={index} api={api}");
            cap.Dispose();
        }
        catch (Exception ex)
        {
            Log($"OpenCapture exception index={index} api={api}: {ex.GetType().Name}: {ex.Message}");
        }

        return null;
    }

    public void Stop()
    {
        try { _loopCts?.Cancel(); } catch { /* ignore */ }
        try { _loopTask?.Wait(TimeSpan.FromSeconds(2)); } catch { /* ignore */ }
        _loopCts?.Dispose();
        _loopCts = null;
        _loopTask = null;

        try { _capture?.Release(); } catch { /* ignore */ }
        try { _capture?.Dispose(); } catch { /* ignore */ }
        _capture = null;
        Log($"Stop (framesEncoded={_framesEncoded})");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }

    private void CaptureLoop(CancellationToken token)
    {
        var intervalMs = (int)Math.Round(1000.0 / _fps);
        using var frame = new Mat();

        while (!token.IsCancellationRequested)
        {
            var started = Environment.TickCount64;
            try
            {
                var cap = _capture;
                if (cap is null || !cap.IsOpened())
                    break;

                if (!cap.Read(frame) || frame.Empty())
                {
                    Thread.Sleep(40);
                    continue;
                }

                if (!Cv2.ImEncode(".jpg", frame, out var jpeg, new[] { (int)ImwriteFlags.JpegQuality, 55 }))
                {
                    Thread.Sleep(40);
                    continue;
                }

                var n = Interlocked.Increment(ref _framesEncoded);
                if (n == 1 || n % 30 == 0)
                    Log($"Encoded frame #{n} {frame.Width}x{frame.Height} bytes={jpeg.Length}");
                FrameCaptured?.Invoke(jpeg);
            }
            catch (Exception ex)
            {
                Log($"CaptureLoop error: {ex.GetType().Name}: {ex.Message}");
                Error?.Invoke(ex);
                Thread.Sleep(200);
            }

            var delay = intervalMs - (int)(Environment.TickCount64 - started);
            if (delay > 0)
            {
                try { Task.Delay(delay, token).Wait(token); }
                catch { break; }
            }
        }
    }

    private static void Log(string message)
    {
        try
        {
            var dir = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.AppendAllText(LogPath, $"{DateTime.Now:O} {message}{Environment.NewLine}");
        }
        catch
        {
            // never break capture for logging
        }
    }
}
