using System;
using System.Windows;
using System.Windows.Media;

namespace HAWinKiosk;

/// <summary>App UI colours - light swatches are canonical; dark surfaces are derived from them.</summary>
public static class ThemePalette
{
    // Accents
    public const byte AccentBoldR = 0x59, AccentBoldG = 0x93, AccentBoldB = 0xF6;       // #5993F6
    public const byte AccentMediumR = 0x87, AccentMediumG = 0xBB, AccentMediumB = 0xFF; // #87BBFF
    public const byte AccentLightR = 0xC1, AccentLightG = 0xDE, AccentLightB = 0xFF;     // #C1DEFF

    // Surface endpoints (#171B21 = darkest surface, #F2F4F9 = lightest surface)
    private static readonly System.Windows.Media.Color SurfaceDarkest = Rgb(0x17, 0x1B, 0x21); // #171B21
    private static readonly System.Windows.Media.Color SurfaceLightest = Rgb(0xF2, 0xF4, 0xF9); // #F2F4F9

    // Light surfaces (canonical mid steps)
    private static readonly System.Windows.Media.Color LightShade1 = Rgb(0xF4, 0xF6, 0xF8); // #F4F6F8
    private static readonly System.Windows.Media.Color LightShade2 = Rgb(0xD0, 0xD8, 0xE4); // #D0D8E4

    public static System.Windows.Media.Color Accent => Rgb(AccentBoldR, AccentBoldG, AccentBoldB);
    public static System.Windows.Media.Color AccentMedium => Rgb(AccentMediumR, AccentMediumG, AccentMediumB);
    public static System.Windows.Media.Color AccentPale => Rgb(AccentLightR, AccentLightG, AccentLightB);

    public static string GestureTickLightStroke => "#F2F4F9";
    public static string GestureTickDarkStroke => "#171B21";

    /// <summary>CSS radial-gradient for gesture-ack glow in the WebView overlay.</summary>
    public static string GestureGlowGradient =>
        $"radial-gradient(circle closest-side, rgba({AccentBoldR},{AccentBoldG},{AccentBoldB},0.58) 0%, "
        + $"rgba({AccentBoldR},{AccentBoldG},{AccentBoldB},0.28) 38%, rgba({AccentBoldR},{AccentBoldG},{AccentBoldB},0.09) 58%, transparent 76%)";

    public static void Apply(ResourceDictionary resources, bool dark)
    {
        static SolidColorBrush B(System.Windows.Media.Color c) => new(c);
        void Set(string key, System.Windows.Media.Color color) => resources[key] = B(color);

        // Light: panel -> card -> input uses shade 2 -> shade 1 -> lightest surface (#F2F4F9).
        // Dark: panel -> card -> input uses darkest surface (#171B21) then steps upward toward #F2F4F9.
        var (darkPanel, darkSurface, darkInput) = BuildDarkSurfaces();

        var panel = dark ? darkPanel : LightShade2;
        var surface = dark ? darkSurface : LightShade1;
        var border = dark ? darkPanel : LightShade2;
        var fg = dark ? SurfaceLightest : SurfaceDarkest;
        var fgMuted = dark ? LightShade2 : SurfaceDarkest;
        var fgSub = fgMuted;
        var input = dark ? darkInput : SurfaceLightest;
        var inputBorder = dark ? darkPanel : LightShade2;
        var inputFg = dark ? SurfaceLightest : SurfaceDarkest;
        var disabledTrack = dark ? darkPanel : LightShade2;
        var disabledThumb = AccentPale;
        var thumbOff = dark ? darkInput : SurfaceLightest;

        Set("Theme.Accent", Accent);
        Set("Theme.Accent.Light", AccentMedium);
        Set("Theme.Accent.Pale", AccentPale);
        Set("Theme.Accent.Fg", SurfaceLightest);

        Set("Theme.Kiosk.Bg", panel);
        Set("Theme.Kiosk.SettingsButtonBg", Accent);
        Set("Theme.Settings.PanelBg", panel);
        Set("Theme.Settings.CardBg", surface);
        Set("Theme.Settings.CardBorder", border);
        Set("Theme.Settings.HeaderBg", surface);
        Set("Theme.Settings.HeaderBorder", border);
        Set("Theme.Settings.Fg", fg);
        Set("Theme.Settings.FgMuted", fgMuted);
        Set("Theme.Settings.FgSub", fgSub);
        Set("Theme.Settings.InputBg", input);
        Set("Theme.Settings.InputBorder", inputBorder);
        Set("Theme.Settings.InputFg", inputFg);
        Set("Theme.Button.SecondaryBg", input);
        Set("Theme.Button.SecondaryFg", inputFg);
        Set("Theme.Button.SecondaryBorder", inputBorder);
        Set("Theme.Toggle.TrackOff", border);
        Set("Theme.Toggle.ThumbOff", thumbOff);
        Set("Theme.Toggle.TrackOn", Accent);
        Set("Theme.Toggle.ThumbOn", AccentMedium);
        Set("Theme.Toggle.DisabledTrack", disabledTrack);
        Set("Theme.Toggle.DisabledThumb", disabledThumb);
        Set("Theme.Pin.CardBg", surface);
        Set("Theme.Pin.CardBorder", border);
        Set("Theme.Pin.Fg", fg);
        Set("Theme.Pin.FgMuted", fgMuted);
        Set("Theme.SecretInput.EyeMuted", fgMuted);
        Set("Theme.SecretInput.EyeActive", Accent);
    }

    public static void ApplyAppWide(bool dark) => Apply(System.Windows.Application.Current.Resources, dark);

    /// <summary>
    /// Derive dark panel/card/input from light surface spacing.
    /// Panel is always <see cref="SurfaceDarkest"/> (#171B21); card and input step toward <see cref="SurfaceLightest"/>.
    /// </summary>
    private static (System.Windows.Media.Color panel, System.Windows.Media.Color surface, System.Windows.Media.Color input) BuildDarkSurfaces()
    {
        const double cardStep = 0.5; // mid rung between panel and input (mirrors shade 1 on shade 2)

        var lightSpan = LStar(SurfaceLightest) - LStar(LightShade2);
        var paletteSpan = LStar(SurfaceLightest) - LStar(SurfaceDarkest);
        var darkMaxT = lightSpan / paletteSpan;

        return (
            SurfaceDarkest,
            Lerp(SurfaceDarkest, SurfaceLightest, cardStep * darkMaxT),
            Lerp(SurfaceDarkest, SurfaceLightest, darkMaxT));
    }

    private static System.Windows.Media.Color Lerp(System.Windows.Media.Color from, System.Windows.Media.Color to, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Rgb(
            (byte)Math.Round(from.R + (to.R - from.R) * t),
            (byte)Math.Round(from.G + (to.G - from.G) * t),
            (byte)Math.Round(from.B + (to.B - from.B) * t));
    }

    private static double LStar(System.Windows.Media.Color c)
    {
        var y = RelativeLuminance(c);
        return y <= 0.008856 ? 903.3 * y : 116 * Math.Pow(y, 1.0 / 3.0) - 16;
    }

    private static double RelativeLuminance(System.Windows.Media.Color c)
    {
        static double Linearize(byte channel)
        {
            var v = channel / 255.0;
            return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Linearize(c.R) + 0.7152 * Linearize(c.G) + 0.0722 * Linearize(c.B);
    }

    private static System.Windows.Media.Color Rgb(byte r, byte g, byte b) => System.Windows.Media.Color.FromRgb(r, g, b);
}
