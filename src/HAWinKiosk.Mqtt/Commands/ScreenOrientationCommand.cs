using HAWinKiosk.Mqtt;

namespace HAWinKiosk.Mqtt.Commands;

/// <summary>
/// Rotates the primary display (single-display kiosk). Uses <see cref="DisplaySettings.SetPrimaryOrientation"/>.
/// Payload examples: landscape, portrait, 0–3, 90/180/270, landscape_flipped, portrait_flipped.
/// </summary>
public static class ScreenOrientationCommand
{
    public static void Execute(string? orientation)
    {
        var dmdo = ParseOrientation(orientation);
        DisplaySettings.SetPrimaryOrientation(dmdo);
    }

    internal static uint ParseOrientation(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return 0;

        var s = raw.Trim().ToLowerInvariant().Replace(" ", "").Replace("-", "_");
        return s switch
        {
            "0" or "landscape" or "default" or "primary" => 0,
            "1" or "90" or "portrait" or "dmdo_90" => 1,
            "2" or "180" or "landscape_flipped" or "upside_down" or "dmdo_180" => 2,
            "3" or "270" or "portrait_flipped" or "dmdo_270" => 3,
            _ => uint.TryParse(s, out var n) && n <= 3 ? n : 0
        };
    }
}
