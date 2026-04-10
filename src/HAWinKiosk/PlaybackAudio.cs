using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using HAWinKiosk.Mqtt.Models;
using NAudio.CoreAudioApi;

namespace HAWinKiosk;

/// <summary>
/// Default playback device + master volume via WASAPI (NAudio). Changing default device uses the
/// Policy Config COM object (same approach as EarTrumpet / AudioSwitcher).
/// </summary>
[SupportedOSPlatform("windows")]
internal static class PlaybackAudio
{
    private static readonly Guid PolicyConfigClsid = new("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9");

    public static IReadOnlyList<(string Id, string Name)> EnumerateRenderDevices()
    {
        var list = new List<(string, string)>();
        try
        {
            var en = new MMDeviceEnumerator();
            foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                try
                {
                    list.Add((d.ID, d.FriendlyName));
                }
                finally
                {
                    d.Dispose();
                }
            }

            en.Dispose();
        }
        catch
        {
            // no devices or access denied
        }

        return list;
    }

    public static bool TryGetDefaultVolumePercent(out int percent)
    {
        percent = 100;
        try
        {
            using var en = new MMDeviceEnumerator();
            using var d = en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            percent = (int)Math.Round(d.AudioEndpointVolume.MasterVolumeLevelScalar * 100f);
            percent = Math.Clamp(percent, 0, 100);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void TrySetDefaultVolumePercent(int percent)
    {
        var s = Math.Clamp(percent, 0, 100) / 100f;
        try
        {
            using var en = new MMDeviceEnumerator();
            using var d = en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            d.AudioEndpointVolume.MasterVolumeLevelScalar = s;
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>Sets Windows default playback device (multimedia role). Returns false if COM is unavailable.</summary>
    public static bool TrySetDefaultPlaybackDevice(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            return false;
        try
        {
            var t = Type.GetTypeFromCLSID(PolicyConfigClsid);
            if (t == null) return false;
            dynamic policy = Activator.CreateInstance(t)!;
            try
            {
                // ERole.eMultimedia = 1
                policy.SetDefaultEndpoint(deviceId, 1);
                return true;
            }
            finally
            {
                if (Marshal.IsComObject(policy))
                    Marshal.ReleaseComObject(policy);
            }
        }
        catch
        {
            return false;
        }
    }

    public static void ApplyPersisted(AudioOutputConfig cfg)
    {
        if (!string.IsNullOrWhiteSpace(cfg.PlaybackDeviceId))
            TrySetDefaultPlaybackDevice(cfg.PlaybackDeviceId.Trim());
        TrySetDefaultVolumePercent(cfg.VolumePercent);
    }
}
