using System.Runtime.InteropServices;

namespace HAWinKiosk.Mqtt;

/// <summary>Dxva2 brightness path when WMI is unavailable (common on some external panels).</summary>
internal static class MonitorBrightnessNative
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct PhysicalMonitor
    {
        public IntPtr hPhysicalMonitor;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szPhysicalMonitorDescription;
    }

    [DllImport("user32.dll")]
    internal static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    internal static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("dxva2.dll", SetLastError = true)]
    internal static extern bool GetNumberOfPhysicalMonitorsFromHDC(IntPtr hdc, out uint count);

    [DllImport("dxva2.dll", SetLastError = true)]
    internal static extern bool GetPhysicalMonitorsFromHDC(IntPtr hdc, uint physicalMonitorArraySize, [Out] PhysicalMonitor[] physicalMonitorArray);

    [DllImport("dxva2.dll", SetLastError = true)]
    internal static extern bool DestroyPhysicalMonitor(IntPtr hMonitor);

    [DllImport("dxva2.dll", SetLastError = true)]
    internal static extern bool SetMonitorBrightness(IntPtr hPhysicalMonitor, uint brightness);

    /// <summary>Sets brightness 0-100 on the primary physical monitor via Dxva2.</summary>
    internal static bool TrySetPrimaryBrightness(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        var hdc = GetDC(IntPtr.Zero);
        if (hdc == IntPtr.Zero) return false;

        try
        {
            if (!GetNumberOfPhysicalMonitorsFromHDC(hdc, out var n) || n == 0)
                return false;

            var arr = new PhysicalMonitor[n];
            if (!GetPhysicalMonitorsFromHDC(hdc, n, arr) || arr.Length == 0)
                return false;

            var ok = SetMonitorBrightness(arr[0].hPhysicalMonitor, (uint)percent);
            for (var i = 0; i < arr.Length; i++)
                DestroyPhysicalMonitor(arr[i].hPhysicalMonitor);
            return ok;
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, hdc);
        }
    }
}
