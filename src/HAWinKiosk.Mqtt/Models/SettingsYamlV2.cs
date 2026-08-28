using YamlDotNet.Serialization;

namespace HAWinKiosk.Mqtt.Models;

/// <summary>
/// Root shape for <c>settings.yaml</c> (<c>config</c>, <c>gestures</c>, nested <c>mqtt</c>).
/// Runtime code still uses <see cref="AppSettings"/>; this type is load/save only.
/// </summary>
public sealed class SettingsFileV2
{
    public ConfigSectionV2 Config { get; set; } = new();

    public GesturesConfig Gestures { get; set; } = new();

    public MqttSectionV2 Mqtt { get; set; } = new();
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
    /// <summary>Legacy YAML import only; saved to <c>pin-secrets.json</c> instead.</summary>
    public string? Pin { get; set; }

    [YamlMember(Alias = "pinHint")]
    public string? PinHint { get; set; }

    [YamlMember(Alias = "pinResetQuestion")]
    /// <summary>Legacy YAML import only; saved to <c>pin-secrets.json</c> instead.</summary>
    public string? PinResetQuestion { get; set; }

    [YamlMember(Alias = "pinResetAnswer")]
    /// <summary>Legacy YAML import only; saved to <c>pin-secrets.json</c> instead.</summary>
    public string? PinResetAnswer { get; set; }

    [YamlMember(Alias = "pinProtectionDisabled")]
    public bool PinProtectionDisabled { get; set; }

    [YamlMember(Alias = "showSettingsButton")]
    public bool ShowSettingsButton { get; set; } = true;

    [YamlMember(Alias = "customButton")]
    public CustomButtonConfig CustomButton { get; set; } = new();

    [YamlMember(Alias = "uiTheme")]
    public string UiTheme { get; set; } = "auto";

    [YamlMember(Alias = "betaUpdates")]
    public bool BetaUpdates { get; set; }

    [YamlMember(Alias = "playbackDeviceId")]
    public string PlaybackDeviceId { get; set; } = "";

    /// <summary>Capture device id for microphone selection in settings.</summary>
    [YamlMember(Alias = "inputDeviceId")]
    public string InputDeviceId { get; set; } = "";

    [YamlMember(Alias = "enableMic")]
    public bool EnableMic { get; set; } = true;

    [YamlMember(Alias = "cameraDeviceId")]
    public string CameraDeviceId { get; set; } = "";

    [YamlMember(Alias = "volumePercent")]
    public int VolumePercent { get; set; } = 100;

    /// <summary>Screen brightness at startup; replaces <c>screenBrightness.defaultPercent</c>.</summary>
    [YamlMember(Alias = "brightnessPercent")]
    public int BrightnessPercent { get; set; } = 100;

    [YamlMember(Alias = "autoStartEnabled")]
    public bool AutoStartEnabled { get; set; }

    [YamlMember(Alias = "allowZeroBrightness")]
    public bool AllowZeroBrightness { get; set; }

    [YamlMember(Alias = "windowsUpdateRespectActiveHours")]
    public bool WindowsUpdateRespectActiveHours { get; set; } = true;
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
        s.Kiosk.CustomButton = c.CustomButton ?? new CustomButtonConfig();
        s.Kiosk.UiTheme = c.UiTheme;
        s.Kiosk.BetaUpdates = c.BetaUpdates;
        s.Kiosk.WindowsUpdateRespectActiveHours = c.WindowsUpdateRespectActiveHours;
        s.Kiosk.Gestures = v.Gestures ?? new GesturesConfig();

        s.AudioOutput.PlaybackDeviceId = c.PlaybackDeviceId ?? "";
        s.AudioOutput.VolumePercent = c.VolumePercent;
        s.ScreenBrightness.DefaultPercent = c.BrightnessPercent;
        s.ScreenBrightness.AllowZeroBrightness = c.AllowZeroBrightness;
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

        s.Kiosk.InputDeviceId = c.InputDeviceId ?? "";
        s.Kiosk.EnableMic = c.EnableMic;
        s.Kiosk.CameraDeviceId = c.CameraDeviceId ?? "";
        s.Sensors.CameraStream ??= new CameraStreamConfig();

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
                PinHint = s.Kiosk.PinHint,
                PinProtectionDisabled = s.Kiosk.PinProtectionDisabled,
                ShowSettingsButton = s.Kiosk.ShowSettingsButton,
                CustomButton = s.Kiosk.CustomButton ?? new CustomButtonConfig(),
                UiTheme = s.Kiosk.UiTheme,
                BetaUpdates = s.Kiosk.BetaUpdates,
                WindowsUpdateRespectActiveHours = s.Kiosk.WindowsUpdateRespectActiveHours,
                PlaybackDeviceId = s.AudioOutput.PlaybackDeviceId,
                InputDeviceId = s.Kiosk.InputDeviceId ?? "",
                EnableMic = s.Kiosk.EnableMic,
                CameraDeviceId = s.Kiosk.CameraDeviceId ?? "",
                VolumePercent = s.AudioOutput.VolumePercent,
                BrightnessPercent = s.ScreenBrightness.DefaultPercent,
                AllowZeroBrightness = s.ScreenBrightness.AllowZeroBrightness,
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
            }
        };
    }
}
