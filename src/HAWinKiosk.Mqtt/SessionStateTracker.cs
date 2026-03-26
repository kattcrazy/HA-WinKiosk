using Microsoft.Win32;

namespace HAWinKiosk.Mqtt;

/// <summary>Tracks session lock state via <see cref="SystemEvents.SessionSwitch"/>.</summary>
public static class SessionStateTracker
{
    private static volatile string _state = "active";
    private static bool _hooked;

    public static string State => _state;

    /// <summary>Call once from the UI thread (STA) during app startup.</summary>
    public static void EnsureInitialized()
    {
        if (_hooked) return;
        _hooked = true;
        SystemEvents.SessionSwitch += (_, e) =>
        {
            _state = e.Reason switch
            {
                SessionSwitchReason.SessionLock => "locked",
                SessionSwitchReason.SessionUnlock => "active",
                SessionSwitchReason.SessionLogon => "active",
                SessionSwitchReason.SessionLogoff => "logged_off",
                SessionSwitchReason.ConsoleConnect => "active",
                SessionSwitchReason.ConsoleDisconnect => "disconnected",
                _ => _state
            };
        };
    }
}
