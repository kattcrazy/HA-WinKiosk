using YamlDotNet.Serialization;

namespace HAWinKiosk.Mqtt.Models;

/// <summary>
/// Root shape for <c>settings.yaml</c> v2 (<c>config</c>, <c>gestures</c>, nested <c>mqtt</c>, <c>voiceAssist</c>).
/// Runtime code still uses <see cref="AppSettings"/>; this type is load/save only.
/// </summary>
public sealed class SettingsFileV2
{
    public ConfigSectionV2 Config { get; set; } = new();

    public GesturesConfig Gestures { get; set; } = new();

    public MqttSectionV2 Mqtt { get; set; } = new();

    public VoiceAssistYamlV2 VoiceAssist { get; set; } = new();
}

/// <summary>
/// Flat kiosk + audio + brightness + autostart fields (formerly split across kiosk, audioOutput, screenBrightness, autoStart).
/// </summary>
public sealed class ConfigSectionV2
{
    [YamlMember(Alias = "url")]
    public string Url { get; set; } = "http://homeassistant.local:8123";

    [YamlMember(Alias = "ignoreCertificateErrors")]
    public bool IgnoreCertificateErrors { get; set; }

    [YamlMember(Alias = "doNotDisturb")]
    public bool DoNotDisturb { get; set; } = true;

    [YamlMember(Alias = "pin")]
    public string? Pin { get; set; }

    [YamlMember(Alias = "pinHint")]
    public string? PinHint { get; set; }

    [YamlMember(Alias = "pinResetQuestion")]
    public string? PinResetQuestion { get; set; }

    [YamlMember(Alias = "pinResetAnswer")]
    public string? PinResetAnswer { get; set; }

    [YamlMember(Alias = "pinProtectionDisabled")]
    public bool PinProtectionDisabled { get; set; }

    [YamlMember(Alias = "showSettingsButton")]
    public bool ShowSettingsButton { get; set; } = true;

    [YamlMember(Alias = "uiTheme")]
    public string UiTheme { get; set; } = "auto";

    [YamlMember(Alias = "betaUpdates")]
    public bool BetaUpdates { get; set; }

    [YamlMember(Alias = "playbackDeviceId")]
    public string PlaybackDeviceId { get; set; } = "";

    /// <summary>Voice capture device id (Wyoming); same as former <c>voiceAssist.inputDeviceId</c>.</summary>
    [YamlMember(Alias = "inputDeviceId")]
    public string InputDeviceId { get; set; } = "";

    [YamlMember(Alias = "volumePercent")]
    public int VolumePercent { get; set; } = 100;

    /// <summary>Screen brightness at startup; replaces <c>screenBrightness.defaultPercent</c>.</summary>
    [YamlMember(Alias = "brightnessPercent")]
    public int BrightnessPercent { get; set; } = 100;

    [YamlMember(Alias = "autoStartEnabled")]
    public bool AutoStartEnabled { get; set; }
}

/// <summary>
/// MQTT broker + discovery + nested sensors/commands (v2 layout).
/// </summary>
public sealed class MqttSectionV2
{
    [YamlMember(Alias = "host")]
    public string Host { get; set; } = "192.168.1.?";

    [YamlMember(Alias = "port")]
    public int Port { get; set; } = 1883;

    [YamlMember(Alias = "username")]
    public string? Username { get; set; }

    [YamlMember(Alias = "password")]
    public string? Password { get; set; }

    [YamlMember(Alias = "deviceName")]
    public string DeviceName { get; set; } = "living-room-kiosk";

    [YamlMember(Alias = "discoveryPrefix")]
    public string DiscoveryPrefix { get; set; } = "homeassistant";

    [YamlMember(Alias = "sensors")]
    public SensorsConfig Sensors { get; set; } = new();

    [YamlMember(Alias = "commands")]
    public CommandsConfig Commands { get; set; } = new();
}

/// <summary>
/// Wyoming wake streaming fields for YAML v2 (no <c>inputDeviceId</c> here — lives under <see cref="ConfigSectionV2"/>).
/// </summary>
public sealed class VoiceAssistYamlV2
{
    [YamlMember(Alias = "enabled")]
    public bool Enabled { get; set; }

    [YamlMember(Alias = "wyomingHostPc")]
    public string WyomingHostPc { get; set; } = "";

    [YamlMember(Alias = "wyomingHostPcPort")]
    public int WyomingHostPcPort { get; set; } = 10400;

    [YamlMember(Alias = "wakeWordDelay")]
    public double WakeWordDelay { get; set; } = 5;

    [YamlMember(Alias = "wakeWordNames")]
    public List<string> WakeWordNames { get; set; } = new();
}

/// <summary>Maps between <see cref="SettingsFileV2"/> and the runtime <see cref="AppSettings"/> graph.</summary>
public static class SettingsYamlConversion
{
    public static AppSettings ToAppSettings(SettingsFileV2 v)
    {
        var s = new AppSettings();
        var c = v.Config ?? new ConfigSectionV2();
        s.Kiosk.Url = c.Url;
        s.Kiosk.IgnoreCertificateErrors = c.IgnoreCertificateErrors;
        s.Kiosk.DoNotDisturbEnabled = c.DoNotDisturb;
        s.Kiosk.Pin = c.Pin;
        s.Kiosk.PinHint = c.PinHint;
        s.Kiosk.PinResetQuestion = c.PinResetQuestion;
        s.Kiosk.PinResetAnswer = c.PinResetAnswer;
        s.Kiosk.PinProtectionDisabled = c.PinProtectionDisabled;
        s.Kiosk.ShowSettingsButton = c.ShowSettingsButton;
        s.Kiosk.UiTheme = c.UiTheme;
        s.Kiosk.BetaUpdates = c.BetaUpdates;
        s.Kiosk.Gestures = v.Gestures ?? new GesturesConfig();

        s.AudioOutput.PlaybackDeviceId = c.PlaybackDeviceId ?? "";
        s.AudioOutput.VolumePercent = c.VolumePercent;
        s.ScreenBrightness.DefaultPercent = c.BrightnessPercent;
        s.AutoStart.Enabled = c.AutoStartEnabled;

        var m = v.Mqtt ?? new MqttSectionV2();
        s.Mqtt.Host = m.Host;
        s.Mqtt.Port = m.Port;
        s.Mqtt.Username = m.Username;
        s.Mqtt.Password = m.Password;
        s.Mqtt.DeviceName = m.DeviceName;
        s.Mqtt.DiscoveryPrefix = m.DiscoveryPrefix;
        s.Sensors = m.Sensors ?? new SensorsConfig();
        s.Commands = m.Commands ?? new CommandsConfig();

        var va = v.VoiceAssist ?? new VoiceAssistYamlV2();
        s.VoiceAssist.Enabled = va.Enabled;
        s.VoiceAssist.WyomingHostPc = va.WyomingHostPc ?? "";
        s.VoiceAssist.WyomingHostPcPort = va.WyomingHostPcPort;
        s.VoiceAssist.WakeWordDelay = va.WakeWordDelay;
        s.VoiceAssist.WakeWordNames = va.WakeWordNames ?? new List<string>();
        s.VoiceAssist.InputDeviceId = c.InputDeviceId ?? "";

        return s;
    }

    public static SettingsFileV2 FromAppSettings(AppSettings s)
    {
        return new SettingsFileV2
        {
            Config = new ConfigSectionV2
            {
                Url = s.Kiosk.Url,
                IgnoreCertificateErrors = s.Kiosk.IgnoreCertificateErrors,
                DoNotDisturb = s.Kiosk.DoNotDisturbEnabled,
                Pin = s.Kiosk.Pin,
                PinHint = s.Kiosk.PinHint,
                PinResetQuestion = s.Kiosk.PinResetQuestion,
                PinResetAnswer = s.Kiosk.PinResetAnswer,
                PinProtectionDisabled = s.Kiosk.PinProtectionDisabled,
                ShowSettingsButton = s.Kiosk.ShowSettingsButton,
                UiTheme = s.Kiosk.UiTheme,
                BetaUpdates = s.Kiosk.BetaUpdates,
                PlaybackDeviceId = s.AudioOutput.PlaybackDeviceId,
                InputDeviceId = s.VoiceAssist.InputDeviceId ?? "",
                VolumePercent = s.AudioOutput.VolumePercent,
                BrightnessPercent = s.ScreenBrightness.DefaultPercent,
                AutoStartEnabled = s.AutoStart.Enabled
            },
            Gestures = s.Kiosk.Gestures,
            Mqtt = new MqttSectionV2
            {
                Host = s.Mqtt.Host,
                Port = s.Mqtt.Port,
                Username = s.Mqtt.Username,
                Password = s.Mqtt.Password,
                DeviceName = s.Mqtt.DeviceName,
                DiscoveryPrefix = s.Mqtt.DiscoveryPrefix,
                Sensors = s.Sensors,
                Commands = s.Commands
            },
            VoiceAssist = new VoiceAssistYamlV2
            {
                Enabled = s.VoiceAssist.Enabled,
                WyomingHostPc = s.VoiceAssist.WyomingHostPc,
                WyomingHostPcPort = s.VoiceAssist.WyomingHostPcPort,
                WakeWordDelay = s.VoiceAssist.WakeWordDelay,
                WakeWordNames = s.VoiceAssist.WakeWordNames ?? new List<string>()
            }
        };
    }
}
