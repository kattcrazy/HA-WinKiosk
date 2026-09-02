using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace HAWinKiosk.Mqtt;

/// <summary>
/// Listens for session display on/off via Windows power notifications and updates <see cref="MonitorPowerTracker"/>.
/// </summary>
public static class DisplayPowerMonitor
{
    private static IntPtr _registration;
    private static HwndSourceHook? _hook;
    private static HwndSource? _source;

    public static void Register(nint hwnd)
    {
        Unregister();
        if (hwnd == 0)
            return;

        _source = HwndSource.FromHwnd(hwnd);
        if (_source == null)
            return;

        var guid = NativeMethods.GuidSessionDisplayStatus;
        _registration = NativeMethods.RegisterPowerSettingNotification(
            hwnd,
            ref guid,
            NativeMethods.DeviceNotifyWindowHandle);

        _hook = HwndHook;
        _source.AddHook(_hook);
    }

    public static void Unregister()
    {
        if (_source != null && _hook != null)
        {
            try { _source.RemoveHook(_hook); } catch { /* ignore */ }
        }

        _hook = null;
        _source = null;

        if (_registration != IntPtr.Zero)
        {
            try { NativeMethods.UnregisterPowerSettingNotification(_registration); } catch { /* ignore */ }
            _registration = IntPtr.Zero;
        }
    }

    private static nint HwndHook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg != NativeMethods.WmPowerBroadcast)
            return 0;

        if (wParam.ToInt32() != NativeMethods.PbtPowerSettingChange)
            return 0;

        if (lParam == 0)
            return 0;

        try
        {
            var header = Marshal.PtrToStructure<NativeMethods.PowerBroadcastSettingHeader>(lParam);
            if (header.PowerSetting != NativeMethods.GuidSessionDisplayStatus)
                return 0;

            if (header.DataLength < 4)
                return 0;

            // POWERBROADCAST_SETTING: GUID (16) + DataLength (4) + Data (DWORD).
            var displayState = Marshal.ReadInt32(lParam + 20);
            // 0=off, 1=on, 2=dimmed — treat dimmed as on so wake automations still run.
            MonitorPowerTracker.SetOn(displayState != 0);
        }
        catch
        {
            // ignore malformed notifications
        }

        return 0;
    }
}
