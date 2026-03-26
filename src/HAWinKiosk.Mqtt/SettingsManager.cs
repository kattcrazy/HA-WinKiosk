using System.IO;
using System.Linq;
using HAWinKiosk.Mqtt.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace HAWinKiosk.Mqtt;

public static class SettingsManager
{
    private static readonly string AppDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HA-WinKiosk");

    private static readonly string SettingsPath = Path.Combine(AppDataDir, "settings.yaml");

    public static string SettingsFilePath => SettingsPath;

    public static bool SettingsExists => File.Exists(SettingsPath);

    public static AppSettings Load()
    {
        if (!File.Exists(SettingsPath))
            return new AppSettings();

        var yaml = File.ReadAllText(SettingsPath);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var s = deserializer.Deserialize<AppSettings>(yaml) ?? new AppSettings();
        NormalizeAppSettings(s);
        return s;
    }

    private static void NormalizeAppSettings(AppSettings s)
    {
        s.Sensors.Enabled = s.Sensors.Enabled
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        s.Commands.Enabled = s.Commands.Enabled
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var g = s.Kiosk.Gestures;
        g.SwipeAction = NormalizeGestureAction(g.SwipeAction, "reload");
        g.SwipeHoldAction = NormalizeGestureAction(g.SwipeHoldAction, "clearcache_reload");
        g.PinchAction = NormalizeGestureAction(g.PinchAction, "disabled");
        g.TripleTapAction = NormalizeGestureAction(g.TripleTapAction, "disabled");
        g.QuadrupleTapAction = NormalizeGestureAction(g.QuadrupleTapAction, "settings");
        g.TripleTapLocation = NormalizeTapLocation(g.TripleTapLocation);
        g.QuadrupleTapLocation = NormalizeTapLocation(g.QuadrupleTapLocation);
        g.SwipeDirection = NormalizeSwipeDirection(g.SwipeDirection);
        g.SwipeHoldDirection = NormalizeSwipeDirection(g.SwipeHoldDirection);
        g.SwipeHoldMs = Math.Max(100, g.SwipeHoldMs);
        g.MinSwipePixels = Math.Max(20, g.MinSwipePixels);
        s.Kiosk.PinResetQuestion = string.IsNullOrWhiteSpace(s.Kiosk.PinResetQuestion) ? null : s.Kiosk.PinResetQuestion.Trim();
        s.Kiosk.PinResetAnswer = string.IsNullOrWhiteSpace(s.Kiosk.PinResetAnswer) ? null : s.Kiosk.PinResetAnswer.Trim();
        s.Commands.PowerShellCommand = string.IsNullOrWhiteSpace(s.Commands.PowerShellCommand) ? null : s.Commands.PowerShellCommand.Trim();
    }

    private static string NormalizeGestureAction(string? raw, string fallback)
    {
        var s = (raw ?? fallback).Trim().ToLowerInvariant();
        return s is "disabled" or "reload" or "clearcache_reload" or "settings" or "mqtt" ? s : fallback;
    }

    private static string NormalizeTapLocation(string? raw)
    {
        var s = (raw ?? "top-left").Trim().ToLowerInvariant();
        return s is "top-left" or "top-right" or "bottom-right" or "bottom-left" or "anywhere" ? s : "top-left";
    }

    private static string NormalizeSwipeDirection(string? raw)
    {
        var s = (raw ?? "down").Trim().ToLowerInvariant();
        return s is "down" or "up" or "left" or "right" ? s : "down";
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(AppDataDir);
        var serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();
        var yaml = serializer.Serialize(settings);
        File.WriteAllText(SettingsPath, yaml);
    }

    public static string GetUserDataFolder()
    {
        var path = Path.Combine(AppDataDir, "WebView2");
        Directory.CreateDirectory(path);
        return path;
    }
}
