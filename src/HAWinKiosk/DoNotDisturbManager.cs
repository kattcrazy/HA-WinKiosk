using System;
using Microsoft.Win32;

namespace HAWinKiosk;

/// <summary>
/// Enables a "Do Not Disturb" style mode by suppressing toast notifications and
/// turning on quiet hours for the current user profile.
///
/// We store previous registry values and restore them when disabling/unloading.
/// </summary>
public sealed class DoNotDisturbManager
{
    private const string NotificationsSettingsPath = @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings";
    private const string QuietHoursPath = @"Software\Microsoft\Windows\CurrentVersion\Notifications\QuietHours";

    private bool _applied;

    private int? _prevToastsEnabled;
    private int? _prevAllowNotificationSound;
    private int? _prevAllowToastsAboveLock;
    private int? _prevAllowCriticalToastsAboveLock;
    private int? _prevQuietHoursEnabled;
    private int? _prevEntryTime;
    private int? _prevExitTime;

    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            if (_applied) return;
            CapturePreviousValues();
            ApplyNow();
            _applied = true;
            return;
        }

        if (!_applied) return;
        RestorePreviousValues();
        _applied = false;
    }

    private void CapturePreviousValues()
    {
        using var settingsKey = Registry.CurrentUser.OpenSubKey(NotificationsSettingsPath, writable: false);
        if (settingsKey != null)
        {
            _prevToastsEnabled = ReadDword(settingsKey, "NOC_GLOBAL_SETTING_TOASTS_ENABLED");
            _prevAllowNotificationSound = ReadDword(settingsKey, "NOC_GLOBAL_SETTING_ALLOW_NOTIFICATION_SOUND");
            _prevAllowToastsAboveLock = ReadDword(settingsKey, "NOC_GLOBAL_SETTING_ALLOW_TOASTS_ABOVE_LOCK");
            _prevAllowCriticalToastsAboveLock = ReadDword(settingsKey, "NOC_GLOBAL_SETTING_ALLOW_CRITICAL_TOASTS_ABOVE_LOCK");
        }

        using var quietKey = Registry.CurrentUser.OpenSubKey(QuietHoursPath, writable: false);
        if (quietKey != null)
        {
            _prevQuietHoursEnabled = ReadDword(quietKey, "EnableQuietHours");
            _prevEntryTime = ReadDword(quietKey, "EntryTime");
            _prevExitTime = ReadDword(quietKey, "ExitTime");
        }
    }

    private void ApplyNow()
    {
        // Suppress toast notifications (broadest approach for kiosk interstitials).
        using (var settingsKey = Registry.CurrentUser.CreateSubKey(NotificationsSettingsPath))
        {
            settingsKey.SetValue("NOC_GLOBAL_SETTING_TOASTS_ENABLED", 0, RegistryValueKind.DWord);
            settingsKey.SetValue("NOC_GLOBAL_SETTING_ALLOW_NOTIFICATION_SOUND", 0, RegistryValueKind.DWord);
            settingsKey.SetValue("NOC_GLOBAL_SETTING_ALLOW_TOASTS_ABOVE_LOCK", 0, RegistryValueKind.DWord);
            settingsKey.SetValue("NOC_GLOBAL_SETTING_ALLOW_CRITICAL_TOASTS_ABOVE_LOCK", 0, RegistryValueKind.DWord);
        }

        // Enable quiet hours for (nearly) the whole day.
        using (var quietKey = Registry.CurrentUser.CreateSubKey(QuietHoursPath))
        {
            quietKey.SetValue("EnableQuietHours", 1, RegistryValueKind.DWord);
            quietKey.SetValue("EntryTime", 0, RegistryValueKind.DWord);    // minutes after midnight
            quietKey.SetValue("ExitTime", 1439, RegistryValueKind.DWord);   // minutes after midnight
        }
    }

    private void RestorePreviousValues()
    {
        using var settingsKey = Registry.CurrentUser.OpenSubKey(NotificationsSettingsPath, writable: true);
        if (settingsKey != null)
        {
            RestoreDword(settingsKey, "NOC_GLOBAL_SETTING_TOASTS_ENABLED", _prevToastsEnabled);
            RestoreDword(settingsKey, "NOC_GLOBAL_SETTING_ALLOW_NOTIFICATION_SOUND", _prevAllowNotificationSound);
            RestoreDword(settingsKey, "NOC_GLOBAL_SETTING_ALLOW_TOASTS_ABOVE_LOCK", _prevAllowToastsAboveLock);
            RestoreDword(settingsKey, "NOC_GLOBAL_SETTING_ALLOW_CRITICAL_TOASTS_ABOVE_LOCK", _prevAllowCriticalToastsAboveLock);
        }

        using var quietKey = Registry.CurrentUser.OpenSubKey(QuietHoursPath, writable: true);
        if (quietKey != null)
        {
            RestoreDword(quietKey, "EnableQuietHours", _prevQuietHoursEnabled);
            RestoreDword(quietKey, "EntryTime", _prevEntryTime);
            RestoreDword(quietKey, "ExitTime", _prevExitTime);
        }
    }

    private static int? ReadDword(RegistryKey key, string valueName)
    {
        try
        {
            var obj = key.GetValue(valueName);
            if (obj is int i) return i;
            if (obj is uint u) return unchecked((int)u);
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static void RestoreDword(RegistryKey key, string valueName, int? previous)
    {
        try
        {
            if (previous == null)
            {
                if (Array.IndexOf(key.GetValueNames(), valueName) >= 0)
                    key.DeleteValue(valueName, throwOnMissingValue: false);
                return;
            }

            key.SetValue(valueName, previous.Value, RegistryValueKind.DWord);
        }
        catch
        {
            // best-effort restore
        }
    }
}

