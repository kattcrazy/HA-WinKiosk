using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace HAWinKiosk.Mqtt;

/// <summary>
/// Reads host metrics for MQTT telemetry. Conceptually aligned with HASS.Agent sensors; see bundled
/// <c>HASS-AGENT-2-REFERENCE/…/HASS.Agent.Shared/HomeAssistant/Sensors/…</c> (e.g. CpuLoadSensor, MemoryUsageSensor,
/// GpuLoadSensor, BatterySensors, LastActiveSensor) for the upstream implementations we did not copy verbatim.
/// </summary>
public static class SensorReader
{
    private static PerformanceCounter? _cpuCounter;

    private static void EnsureCpuCounter()
    {
        if (_cpuCounter != null) return;
        try
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _cpuCounter.NextValue();
        }
        catch
        {
            _cpuCounter = null;
        }
    }

    public static string? BatteryPercentOrUnavailable()
    {
        try
        {
            var ps = SystemInformation.PowerStatus;
            if (ps.BatteryChargeStatus == BatteryChargeStatus.NoSystemBattery
                || ps.BatteryChargeStatus == BatteryChargeStatus.Unknown)
                return "unavailable";

            var pct = ps.BatteryLifePercent;
            if (pct < 0 || pct > 100) return "unavailable";
            return pct.ToString();
        }
        catch
        {
            return "unavailable";
        }
    }

    public static string? CpuLoadPercent()
    {
        try
        {
            EnsureCpuCounter();
            if (_cpuCounter == null) return "unavailable";
            Thread.Sleep(150);
            var v = _cpuCounter.NextValue();
            return Math.Clamp((int)Math.Round(v), 0, 100).ToString();
        }
        catch
        {
            return "unavailable";
        }
    }

    public static string? MemoryUsagePercent()
    {
        try
        {
            var mem = new NativeMethods.MemoryStatusExStruct();
            mem.dwLength = (uint)Marshal.SizeOf<NativeMethods.MemoryStatusExStruct>();
            if (!NativeMethods.GlobalMemoryStatusEx(ref mem))
                return "unavailable";

            var total = mem.ullTotalPhys;
            if (total == 0) return "unavailable";

            var used = total - mem.ullAvailPhys;
            var pct = 100.0 * used / total;
            return Math.Clamp((int)Math.Round(pct), 0, 100).ToString();
        }
        catch
        {
            return "unavailable";
        }
    }

    public static string? GpuLoadPercent()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "root\\CIMV2",
                "SELECT PercentProcessorTime FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine");
            using var results = searcher.Get();
            double sum = 0;
            var n = 0;
            foreach (ManagementObject mo in results)
            {
                try
                {
                    var v = mo["PercentProcessorTime"];
                    if (v is uint u) { sum += u; n++; }
                    else if (v is ulong ul) { sum += ul; n++; }
                    else if (v is string s && double.TryParse(s, out var d)) { sum += d; n++; }
                }
                finally
                {
                    mo.Dispose();
                }
            }

            if (n == 0) return "unavailable";
            return Math.Clamp((int)Math.Round(sum / n), 0, 100).ToString();
        }
        catch
        {
            return "unavailable";
        }
    }

    public static string SessionState() => SessionStateTracker.State;

    public static string LastActiveSeconds()
    {
        var lii = new NativeMethods.LastInputInfo();
        lii.cbSize = (uint)Marshal.SizeOf<NativeMethods.LastInputInfo>();
        if (!NativeMethods.GetLastInputInfo(ref lii))
            return "0";

        var idleMs = NativeMethods.GetTickCount() - lii.dwTime;
        return (idleMs / 1000).ToString();
    }

    public static string? UpdatesPendingCount()
    {
        try
        {
            var t = Type.GetTypeFromProgID("Microsoft.Update.Session");
            if (t == null) return "unavailable";
            dynamic session = Activator.CreateInstance(t)!;
            dynamic searcher = session.CreateUpdateSearcher();
            dynamic result = searcher.Search("IsInstalled=0 and IsHidden=0");
            return ((int)result.Updates.Count).ToString();
        }
        catch
        {
            return "unavailable";
        }
    }
}
