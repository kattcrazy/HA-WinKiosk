using System.IO;
using HAWinKiosk.Mqtt;
using HAWinKiosk.Mqtt.Models;

namespace HAWinKiosk.Camera;

/// <summary>Starts/stops capture and routes frames to MQTT camera and/or MJPEG HTTP.</summary>
public sealed class CameraStreamCoordinator : IDisposable
{
    private const int HaCameraFps = 5;

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HA-WinKiosk",
        "camera.log");

    private readonly CameraCaptureService _capture = new();
    private readonly MjpegHttpServer _mjpeg = new();
    private readonly SemaphoreSlim _haPublishLock = new(1, 1);
    private MqttClientService? _mqtt;
    private string _mode = "off";
    private int _fps = 5;
    private int _port = 8081;
    private string? _deviceId;
    private byte[]? _pendingHaJpeg;
    private bool _disposed;

    public CameraStreamCoordinator()
    {
        _capture.FrameCaptured += OnFrame;
    }

    /// <summary>Detach MQTT during broker reconnect so frames are not sent to a disposed client.</summary>
    public void SetMqttClient(MqttClientService? mqtt) => _mqtt = mqtt;

    public async Task ApplyAsync(AppSettings settings, MqttClientService? mqtt)
    {
        _mqtt = mqtt;
        var mode = (settings.Sensors.CameraStream.Mode ?? "off").Trim().ToLowerInvariant();
        var fps = mode == "ha"
            ? HaCameraFps
            : Math.Clamp(settings.Sensors.CameraStream.Fps, 1, 15);
        var port = Math.Clamp(settings.Sensors.CameraStream.Port <= 0 ? 8081 : settings.Sensors.CameraStream.Port, 1, 65535);
        var deviceId = string.IsNullOrWhiteSpace(settings.Kiosk.CameraDeviceId)
            ? null
            : settings.Kiosk.CameraDeviceId.Trim();

        if (mode == _mode && mode == "off")
        {
            await PublishCameraDiscoveryStateAsync(mode);
            return;
        }

        // Settings save reconnects MQTT but often leaves camera options unchanged — keep capture running.
        if (mode is "ha" or "mjpeg"
            && mode == _mode
            && fps == _fps
            && port == _port
            && string.Equals(deviceId, _deviceId, StringComparison.Ordinal))
        {
            await PublishCameraDiscoveryStateAsync(mode);
            return;
        }

        // FPS-only change: restart capture pacing without touching MQTT discovery or image topic.
        if (mode is "ha" or "mjpeg"
            && mode == _mode
            && fps != _fps
            && port == _port
            && string.Equals(deviceId, _deviceId, StringComparison.Ordinal))
        {
            _fps = fps;
            try
            {
                await _capture.StartAsync(deviceId, fps);
            }
            catch (Exception)
            {
                StopRuntime();
                _mode = "off";
                await PublishCameraDiscoveryStateAsync("off");
                throw;
            }

            return;
        }

        StopRuntime();
        _mode = mode;
        _fps = fps;
        _port = port;
        _deviceId = deviceId;

        if (mode is not ("ha" or "mjpeg"))
        {
            await PublishCameraDiscoveryStateAsync("off");
            return;
        }

        try
        {
            if (mode == "mjpeg")
                _mjpeg.Start(port);

            await _capture.StartAsync(deviceId, fps);
            await PublishCameraDiscoveryStateAsync(mode);
        }
        catch (Exception)
        {
            if (mode != "mjpeg")
            {
                StopRuntime();
                _mode = "off";
                await PublishCameraDiscoveryStateAsync("off");
            }

            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _capture.FrameCaptured -= OnFrame;
        StopRuntime();
        _capture.Dispose();
        _mjpeg.Dispose();
        _haPublishLock.Dispose();
    }

    private void StopRuntime()
    {
        _pendingHaJpeg = null;
        _capture.Stop();
        _mjpeg.Stop();
    }

    private void OnFrame(byte[] jpeg)
    {
        if (_mode == "mjpeg")
            _mjpeg.UpdateFrame(jpeg);
        else if (_mode == "ha")
        {
            _pendingHaJpeg = jpeg;
            _ = PublishPendingHaFramesAsync();
        }
    }

    private async Task PublishPendingHaFramesAsync()
    {
        if (_mqtt == null)
            return;

        if (!await _haPublishLock.WaitAsync(0))
            return;

        try
        {
            while (_pendingHaJpeg is { Length: > 0 } jpeg)
            {
                _pendingHaJpeg = null;
                try
                {
                    await _mqtt.PublishCameraJpegAsync(jpeg);
                }
                catch (Exception ex)
                {
                    Log($"PublishCameraJpegAsync: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }
        finally
        {
            _haPublishLock.Release();
        }
    }

    private async Task PublishCameraDiscoveryStateAsync(string mode)
    {
        if (_mqtt == null) return;
        if (mode == "ha")
            await _mqtt.PublishCameraDiscoveryAsync(enabled: true);
        else
            await _mqtt.PublishCameraDiscoveryAsync(enabled: false);
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
            // never break streaming for logging
        }
    }
}
