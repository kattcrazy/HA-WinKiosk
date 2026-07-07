using System.IO;
using System.Linq;
using HAWinKiosk.Mqtt.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace HAWinKiosk.Mqtt;

public static class SettingsManager
{
    private static readonly HashSet<string> AllowedSensorIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "battery", "cpu", "memory", "monitor_on", "last_active", "updates_pending"
    };

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

        var yaml = File.ReadAllText(SettingsPath).TrimStart('\uFEFF');
        if (string.IsNullOrWhiteSpace(yaml))
            return new AppSettings();

        AppSettings s;
        try
        {
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
            var file = deserializer.Deserialize<SettingsFileV2>(yaml);
            s = file != null ? SettingsYamlConversion.ToAppSettings(file) : new AppSettings();
        }
        catch
        {
            s = new AppSettings();
        }

        NormalizeAppSettings(s);
        return s;
    }

    private static void NormalizeAppSettings(AppSettings s)
    {
        s.Sensors.Enabled = s.Sensors.Enabled
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .Where(x => AllowedSensorIds.Contains(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!SensorReader.HasSystemBattery())
        {
            s.Sensors.Enabled = s.Sensors.Enabled
                .Where(x => !x.Equals("battery", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        s.Commands.Enabled = s.Commands.Enabled
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var g = s.Kiosk.Gestures;
        g.DoubleTapAction = NormalizeGestureAction(g.DoubleTapAction, "disabled");
        g.SwipeAction = NormalizeGestureAction(g.SwipeAction, "reload");
        g.TwoFingerSwipeAction = NormalizeGestureAction(g.TwoFingerSwipeAction, "disabled");
        g.SwipeHoldAction = NormalizeGestureAction(g.SwipeHoldAction, "clearcache_reload");
        g.TwoFingerSwipeHoldAction = NormalizeGestureAction(g.TwoFingerSwipeHoldAction, "disabled");
        g.ZoomAction = NormalizeGestureAction(g.ZoomAction, "disabled");
        g.PinchAction = NormalizeGestureAction(g.PinchAction, "disabled");
        g.TripleTapAction = NormalizeGestureAction(g.TripleTapAction, "disabled");
        g.QuadrupleTapAction = NormalizeGestureAction(g.QuadrupleTapAction, "settings");
        g.QuintupleTapAction = NormalizeGestureAction(g.QuintupleTapAction, "disabled");
        g.DoubleTapLocation = NormalizeTapLocation(g.DoubleTapLocation);
        g.TripleTapLocation = NormalizeTapLocation(g.TripleTapLocation);
        g.QuadrupleTapLocation = NormalizeTapLocation(g.QuadrupleTapLocation);
        g.QuintupleTapLocation = NormalizeTapLocation(g.QuintupleTapLocation);
        g.SwipeDirection = NormalizeSwipeDirection(g.SwipeDirection);
        g.TwoFingerSwipeDirection = NormalizeSwipeDirection(g.TwoFingerSwipeDirection);
        g.SwipeHoldDirection = NormalizeSwipeDirection(g.SwipeHoldDirection);
        g.TwoFingerSwipeHoldDirection = NormalizeSwipeDirection(g.TwoFingerSwipeHoldDirection);
        g.SwipeHoldMs = Math.Max(100, g.SwipeHoldMs);
        g.TwoFingerSwipeHoldMs = Math.Max(100, g.TwoFingerSwipeHoldMs);
        g.ZoomDirection = NormalizeZoomDirection(g.ZoomDirection);
        s.Kiosk.PinResetQuestion = string.IsNullOrWhiteSpace(s.Kiosk.PinResetQuestion) ? null : s.Kiosk.PinResetQuestion.Trim();
        s.Kiosk.PinResetAnswer = string.IsNullOrWhiteSpace(s.Kiosk.PinResetAnswer) ? null : s.Kiosk.PinResetAnswer.Trim();
        s.Commands.PowerShellCommand = string.IsNullOrWhiteSpace(s.Commands.PowerShellCommand) ? null : s.Commands.PowerShellCommand.Trim();

        if (!s.ScreenBrightness.AllowZeroBrightness && s.ScreenBrightness.DefaultPercent < 1)
            s.ScreenBrightness.DefaultPercent = 1;

        s.Mqtt.DeviceName = MqttDiscovery.NormalizeDeviceDisplayName(s.Mqtt.DeviceName);
        s.Mqtt.DiscoveryPrefix = string.IsNullOrWhiteSpace(s.Mqtt.DiscoveryPrefix)
            ? "homeassistant"
            : s.Mqtt.DiscoveryPrefix.Trim();
    }

    private static string NormalizeGestureAction(string? raw, string fallback)
    {
        var s = (raw ?? fallback).Trim().ToLowerInvariant();
        return s is "disabled" or "reload" or "clearcache_reload" or "settings" or "mqtt" or "mqtt_publish" ? s : fallback;
    }

    private static string NormalizeTapLocation(string? raw)
    {
        var s = (raw ?? "top-left").Trim().ToLowerInvariant();
        return s is "top-left" or "top-right" or "bottom-right" or "bottom-left" or "anywhere" ? s : "top-left";
    }

    private static string NormalizeSwipeDirection(string? raw)
    {
        var s = (raw ?? "down").Trim().ToLowerInvariant();
        return s is "down" or "left" or "right" ? s : "down";
    }

    private static string NormalizeZoomDirection(string? raw)
    {
        var s = (raw ?? "any").Trim().ToLowerInvariant();
        return s is "any" or "in" or "out" ? s : "any";
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(AppDataDir);
        var file = SettingsYamlConversion.FromAppSettings(settings);
        var serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();
        var yaml = serializer.Serialize(file);
        File.WriteAllText(SettingsPath, yaml);
    }

    public static string GetUserDataFolder()
    {
        var path = Path.Combine(AppDataDir, "WebView2");
        Directory.CreateDirectory(path);
        return path;
    }
}
