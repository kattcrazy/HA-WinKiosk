using Microsoft.Win32;

namespace HAWinKiosk;

/// <summary>Resolves Auto/Light/Dark; Auto follows Windows app light/dark preference.</summary>
public static class UiThemeHelper
{
    /// <summary>True when Windows is using dark mode for apps (registry AppsUseLightTheme = 0).</summary>
    public static bool IsWindowsAppDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var v = key?.GetValue("AppsUseLightTheme");
            return v is int i && i == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>mode: auto | light | dark (case-insensitive).</summary>
    public static bool ResolveEffectiveDark(string? mode)
    {
        var m = (mode ?? "auto").Trim().ToLowerInvariant();
        if (m == "dark") return true;
        if (m == "light") return false;
        return IsWindowsAppDarkMode();
    }

    public static string NormalizeUiTheme(string? raw)
    {
        var s = (raw ?? "auto").Trim().ToLowerInvariant();
        return s is "light" or "dark" ? s : "auto";
    }
}
