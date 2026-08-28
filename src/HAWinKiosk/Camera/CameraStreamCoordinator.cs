using HAWinKiosk.Mqtt;
using HAWinKiosk.Mqtt.Models;

namespace HAWinKiosk.Camera;

/// <summary>Starts/stops capture and routes frames to MQTT camera and/or MJPEG HTTP.</summary>
public sealed class CameraStreamCoordinator : IDisposable
{
    private readonly CameraCaptureService _capture = new();
    private readonly MjpegHttpServer _mjpeg = new();
    private MqttClientService? _mqtt;
    private string _mode = "off";
    private bool _disposed;

    public CameraStreamCoordinator()
    {
        _capture.FrameCaptured += OnFrame;
    }

    public async Task ApplyAsync(AppSettings settings, MqttClientService? mqtt)
    {
        _mqtt = mqtt;
        var mode = (settings.Sensors.CameraStream.Mode ?? "off").Trim().ToLowerInvariant();
        var fps = Math.Clamp(settings.Sensors.CameraStream.Fps, 1, 15);
        var port = Math.Clamp(settings.Sensors.CameraStream.Port <= 0 ? 8081 : settings.Sensors.CameraStream.Port, 1, 65535);
        var deviceId = settings.Kiosk.CameraDeviceId;

        if (mode == _mode && mode == "off")
        {
            await PublishCameraDiscoveryStateAsync(mode);
            return;
        }

        StopRuntime();
        _mode = mode;

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
            // Keep MJPEG listening if it already started so preview URLs are not connection-refused.
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
    }

    private void StopRuntime()
    {
        _capture.Stop();
        _mjpeg.Stop();
    }

    private void OnFrame(byte[] jpeg)
    {
        if (_mode == "mjpeg")
            _mjpeg.UpdateFrame(jpeg);
        else if (_mode == "ha")
            _ = _mqtt?.PublishCameraJpegAsync(jpeg);
    }

    private async Task PublishCameraDiscoveryStateAsync(string mode)
    {
        if (_mqtt == null) return;
        if (mode == "ha")
            await _mqtt.PublishCameraDiscoveryAsync(enabled: true);
        else
            await _mqtt.PublishCameraDiscoveryAsync(enabled: false);
    }
}
