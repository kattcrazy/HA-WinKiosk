using System.Runtime.InteropServices;

namespace HAWinKiosk.Mqtt;

/// <summary>
/// Windows API declarations. Adapted from HASS.Agent (hass-agent/HASS.Agent).
/// </summary>
internal static class NativeMethods
{
    internal static readonly IntPtr HWND_BROADCAST = (IntPtr)0xffff;
    internal const uint WM_SYSCOMMAND = 0x0112;
    internal const uint SC_MONITORPOWER = 0xF170;

    [DllImport("user32.dll")]
    internal static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    internal static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, IntPtr dwExtraInfo);

    internal const byte VK_Shift = 0x10;
    internal const uint KEYEVENTF_KEYUP = 0x0002;

    [DllImport("powrprof.dll", SetLastError = true)]
    internal static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    [StructLayout(LayoutKind.Sequential)]
    internal struct LastInputInfo
    {
        internal uint cbSize;
        internal uint dwTime;
    }

    [DllImport("user32.dll")]
    internal static extern bool GetLastInputInfo(ref LastInputInfo plii);

    [DllImport("kernel32.dll")]
    internal static extern uint GetTickCount();

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    internal struct MemoryStatusExStruct
    {
        internal uint dwLength;
        internal uint dwMemoryLoad;
        internal ulong ullTotalPhys;
        internal ulong ullAvailPhys;
        internal ulong ullTotalPageFile;
        internal ulong ullAvailPageFile;
        internal ulong ullTotalVirtual;
        internal ulong ullAvailVirtual;
        internal ulong ullAvailExtendedVirtual;
    }

    [return: MarshalAs(UnmanagedType.Bool)]
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    internal static extern bool GlobalMemoryStatusEx(ref MemoryStatusExStruct lpBuffer);
}
