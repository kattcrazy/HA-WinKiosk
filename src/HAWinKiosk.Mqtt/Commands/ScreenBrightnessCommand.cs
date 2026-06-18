using System.Management;

namespace HAWinKiosk.Mqtt.Commands;

/// <summary>
/// Sets monitor brightness 0-100. Tries WMI (internal panels), then Dxva2 on the primary monitor.
/// </summary>
public static class ScreenBrightnessCommand
{
    public static void Execute(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        if (TryWmiSetBrightness(percent))
            return;
        if (MonitorBrightnessNative.TrySetPrimaryBrightness(percent))
            return;
        throw new InvalidOperationException(
            "Brightness could not be changed (WMI and Dxva2 both failed - driver or panel may not support software brightness).");
    }

    private static bool TryWmiSetBrightness(int percent)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
            using var results = searcher.Get();
            var any = false;
            foreach (ManagementObject mo in results)
            {
                try
                {
                    mo.InvokeMethod("WmiSetBrightness", new object[] { uint.MaxValue, (byte)percent });
                    any = true;
                }
                finally
                {
                    mo.Dispose();
                }
            }

            return any;
        }
        catch
        {
            return false;
        }
    }
}
