using System.IO;
using System.Linq;
using HAWinKiosk.Mqtt.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace HAWinKiosk.Mqtt;

public static class SettingsManager
{
    // =============================================================================================
    // settings.yaml FORMAT (v2) — introduced with flattened `config`, top-level `gestures`, mqtt nesting.
    //
    // RELEASE PLAN (manual):
    // - First ship this as a prerelease / beta so early adopters migrate disk format.
    // - First stable release that includes v2 save: still KEEP the legacy v1 reader below (users may
    //   restore old files or sync from backup).
    // - After one additional stable release with v2-only on disk for typical users, DELETE the entire
    //   #region "Legacy settings.yaml v1" block and only accept v2 (or fail closed with defaults).
    //   Bump README when removing. Search: Legacy settings.yaml v1
    // =============================================================================================

    private static readonly HashSet<string> AllowedSensorIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "battery", "last_active", "updates_pending"
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
        if (LooksLikeSettingsYamlV2(yaml))
        {
            try
            {
                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .IgnoreUnmatchedProperties()
                    .Build();
                var v2 = deserializer.Deserialize<SettingsFileV2>(yaml);
                s = v2 != null ? SettingsYamlConversion.ToAppSettings(v2) : new AppSettings();
            }
            catch
            {
                s = TryLoadLegacyAppSettings(yaml);
            }
        }
        else
            s = TryLoadLegacyAppSettings(yaml);

        NormalizeAppSettings(s, yaml);
        return s;
    }

    private static bool LooksLikeSettingsYamlV2(string yaml)
    {
        foreach (var raw in yaml.Replace("\r\n", "\n").Split('\n'))
        {
            var t = raw.Trim();
            if (t.Length == 0) continue;
            if (t.StartsWith('#')) continue;
            if (t == "---") continue;
            return t.StartsWith("config:", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    #region Legacy settings.yaml v1 (remove after stable+1 — see file header)

    private static AppSettings TryLoadLegacyAppSettings(string yaml)
    {
        try
        {
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
            return deserializer.Deserialize<AppSettings>(yaml) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    #endregion

    private static void NormalizeAppSettings(AppSettings s, string? rawYaml = null)
    {
        s.Sensors.Enabled = s.Sensors.Enabled
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .Where(x => AllowedSensorIds.Contains(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

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
        g.MinSwipePixels = Math.Max(20, g.MinSwipePixels);
        s.Kiosk.PinResetQuestion = string.IsNullOrWhiteSpace(s.Kiosk.PinResetQuestion) ? null : s.Kiosk.PinResetQuestion.Trim();
        s.Kiosk.PinResetAnswer = string.IsNullOrWhiteSpace(s.Kiosk.PinResetAnswer) ? null : s.Kiosk.PinResetAnswer.Trim();
        s.Commands.PowerShellCommand = string.IsNullOrWhiteSpace(s.Commands.PowerShellCommand) ? null : s.Commands.PowerShellCommand.Trim();

        s.Mqtt.DeviceName = MqttDiscovery.NormalizeDeviceDisplayName(s.Mqtt.DeviceName);
        s.Mqtt.DiscoveryPrefix = string.IsNullOrWhiteSpace(s.Mqtt.DiscoveryPrefix)
            ? "homeassistant"
            : s.Mqtt.DiscoveryPrefix.Trim();

        MigrateVoiceAssist(s, rawYaml);
    }

    private static void MigrateVoiceAssist(AppSettings s, string? rawYaml)
    {
        var leg = s.VoiceSatelliteMigration;
        if (leg == null)
            return;

        var hasVoiceAssistSection = rawYaml != null && YamlHasTopLevelKey(rawYaml, "voiceAssist");
        if (!hasVoiceAssistSection)
        {
            s.VoiceAssist.Enabled = leg.Enabled;
            s.VoiceAssist.WyomingHostPc = (leg.WakeServiceHost ?? "").Trim();
            if (leg.WakeServicePort > 0)
                s.VoiceAssist.WyomingHostPcPort = leg.WakeServicePort;
            if (leg.RefractorySeconds >= 0)
                s.VoiceAssist.WakeWordDelay = leg.RefractorySeconds;
        }
        else if (string.IsNullOrWhiteSpace(s.VoiceAssist.WyomingHostPc) && !string.IsNullOrWhiteSpace(leg.WakeServiceHost))
        {
            s.VoiceAssist.WyomingHostPc = leg.WakeServiceHost.Trim();
        }

        s.VoiceSatelliteMigration = null;
    }

    private static bool YamlHasTopLevelKey(string yaml, string key)
    {
        foreach (var raw in yaml.Replace("\r\n", "\n").Split('\n'))
        {
            if (raw.Length == 0) continue;
            if (char.IsWhiteSpace(raw[0]))
                continue;
            var t = raw.TrimEnd();
            if (t.Length == 0) continue;
            if (t.TrimStart().StartsWith("#", StringComparison.Ordinal))
                continue;
            if (t.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
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
        var v2 = SettingsYamlConversion.FromAppSettings(settings);
        var serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();
        var yaml = serializer.Serialize(v2);
        File.WriteAllText(SettingsPath, yaml);
    }

    public static string GetUserDataFolder()
    {
        var path = Path.Combine(AppDataDir, "WebView2");
        Directory.CreateDirectory(path);
        return path;
    }
}
