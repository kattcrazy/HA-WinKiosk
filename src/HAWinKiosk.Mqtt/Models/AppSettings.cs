using YamlDotNet.Serialization;

namespace HAWinKiosk.Mqtt.Models;

public class AppSettings
{
    [YamlMember(Alias = "kiosk")]
    public KioskConfig Kiosk { get; set; } = new();

    [YamlMember(Alias = "mqtt")]
    public MqttConfig Mqtt { get; set; } = new();

    [YamlMember(Alias = "autoStart")]
    public AutoStartConfig AutoStart { get; set; } = new();

    [YamlMember(Alias = "sensors")]
    public SensorsConfig Sensors { get; set; } = new();

    [YamlMember(Alias = "commands")]
    public CommandsConfig Commands { get; set; } = new();

    [YamlMember(Alias = "screenBrightness")]
    public ScreenBrightnessConfig ScreenBrightness { get; set; } = new();

    [YamlMember(Alias = "audioOutput")]
    public AudioOutputConfig AudioOutput { get; set; } = new();

}

public class KioskConfig
{
    [YamlMember(Alias = "url")]
    public string Url { get; set; } = "http://homeassistant.local:8123";

    /// <summary>
    /// When true, HA WinKiosk enables a "Do Not Disturb"/quiet-hours style mode
    /// by suppressing toast notifications while the kiosk is running.
    /// Default true.
    /// </summary>
    [YamlMember(Alias = "doNotDisturb")]
    public bool DoNotDisturbEnabled { get; set; } = true;

    /// <summary>
    /// When true, WebView2 will automatically allow certificate errors for the kiosk URL
    /// (useful for self-signed certs / internal hosts). Default false.
    /// </summary>
    [YamlMember(Alias = "ignoreCertificateErrors")]
    public bool IgnoreCertificateErrors { get; set; }

    [YamlMember(Alias = "pin")]
    public string? Pin { get; set; }

    [YamlMember(Alias = "pinHint")]
    public string? PinHint { get; set; }

    [YamlMember(Alias = "pinResetQuestion")]
    public string? PinResetQuestion { get; set; }

    [YamlMember(Alias = "pinResetAnswer")]
    public string? PinResetAnswer { get; set; }

    /// <summary>When true, PIN UI is off and no PIN is required to open Settings (YAML default false = pin protection on).</summary>
    [YamlMember(Alias = "pinProtectionDisabled")]
    public bool PinProtectionDisabled { get; set; }

    /// <summary>Show the gear button on the kiosk. If false, Settings are only reachable via secret tap (or MQTT open settings).</summary>
    [YamlMember(Alias = "showSettingsButton")]
    public bool ShowSettingsButton { get; set; } = true;

    /// <summary>auto (follow Windows app mode) | light | dark</summary>
    [YamlMember(Alias = "uiTheme")]
    public string UiTheme { get; set; } = "auto";

    /// <summary>
    /// When true, auto-update may install GitHub prereleases; when false, only stable releases (non-prerelease).
    /// </summary>
    [YamlMember(Alias = "betaUpdates")]
    public bool BetaUpdates { get; set; }

    /// <summary>Windows MMDevice ID for capture; empty = system default input.</summary>
    [YamlMember(Alias = "inputDeviceId")]
    public string InputDeviceId { get; set; } = "";

    /// <summary>When false, kiosk denies microphone permission requests from WebView pages.</summary>
    [YamlMember(Alias = "enableMic")]
    public bool EnableMic { get; set; } = true;

    [YamlMember(Alias = "gestures")]
    public GesturesConfig Gestures { get; set; } = new();
}

public class GesturesConfig
{
    /// <summary>disabled | reload | clearcache_reload | settings | mqtt</summary>
    [YamlMember(Alias = "doubleTapAction")]
    public string DoubleTapAction { get; set; } = "disabled";

    /// <summary>disabled | reload | clearcache_reload | settings | mqtt</summary>
    [YamlMember(Alias = "swipeAction")]
    public string SwipeAction { get; set; } = "reload";

    /// <summary>disabled | reload | clearcache_reload | settings | mqtt</summary>
    [YamlMember(Alias = "twoFingerSwipeAction")]
    public string TwoFingerSwipeAction { get; set; } = "disabled";

    /// <summary>disabled | reload | clearcache_reload | settings | mqtt</summary>
    [YamlMember(Alias = "swipeHoldAction")]
    public string SwipeHoldAction { get; set; } = "clearcache_reload";

    /// <summary>disabled | reload | clearcache_reload | settings | mqtt</summary>
    [YamlMember(Alias = "twoFingerSwipeHoldAction")]
    public string TwoFingerSwipeHoldAction { get; set; } = "disabled";

    /// <summary>disabled | reload | clearcache_reload | settings | mqtt</summary>
    [YamlMember(Alias = "zoomAction")]
    public string ZoomAction { get; set; } = "disabled";

    /// <summary>disabled | reload | clearcache_reload | settings | mqtt</summary>
    [YamlMember(Alias = "pinchAction")]
    public string PinchAction { get; set; } = "disabled";

    /// <summary>disabled | reload | clearcache_reload | settings | mqtt</summary>
    [YamlMember(Alias = "tripleTapAction")]
    public string TripleTapAction { get; set; } = "disabled";

    /// <summary>disabled | reload | clearcache_reload | settings | mqtt</summary>
    [YamlMember(Alias = "quadrupleTapAction")]
    public string QuadrupleTapAction { get; set; } = "settings";

    /// <summary>disabled | reload | clearcache_reload | settings | mqtt</summary>
    [YamlMember(Alias = "quintupleTapAction")]
    public string QuintupleTapAction { get; set; } = "disabled";

    /// <summary>top-left | top-right | bottom-right | bottom-left | anywhere</summary>
    [YamlMember(Alias = "doubleTapLocation")]
    public string DoubleTapLocation { get; set; } = "top-left";

    /// <summary>top-left | top-right | bottom-right | bottom-left | anywhere</summary>
    [YamlMember(Alias = "tripleTapLocation")]
    public string TripleTapLocation { get; set; } = "top-left";

    /// <summary>top-left | top-right | bottom-right | bottom-left | anywhere</summary>
    [YamlMember(Alias = "quadrupleTapLocation")]
    public string QuadrupleTapLocation { get; set; } = "top-left";

    /// <summary>top-left | top-right | bottom-right | bottom-left | anywhere</summary>
    [YamlMember(Alias = "quintupleTapLocation")]
    public string QuintupleTapLocation { get; set; } = "top-left";

    /// <summary>down | left | right</summary>
    [YamlMember(Alias = "swipeDirection")]
    public string SwipeDirection { get; set; } = "down";

    /// <summary>down | left | right</summary>
    [YamlMember(Alias = "twoFingerSwipeDirection")]
    public string TwoFingerSwipeDirection { get; set; } = "down";

    /// <summary>down | left | right</summary>
    [YamlMember(Alias = "swipeHoldDirection")]
    public string SwipeHoldDirection { get; set; } = "down";

    /// <summary>down | left | right</summary>
    [YamlMember(Alias = "twoFingerSwipeHoldDirection")]
    public string TwoFingerSwipeHoldDirection { get; set; } = "down";

    [YamlMember(Alias = "swipeHoldMs")]
    public double SwipeHoldMs { get; set; } = 1000;

    [YamlMember(Alias = "twoFingerSwipeHoldMs")]
    public double TwoFingerSwipeHoldMs { get; set; } = 1000;

    /// <summary>any | in | out</summary>
    [YamlMember(Alias = "zoomDirection")]
    public string ZoomDirection { get; set; } = "any";

    [YamlMember(Alias = "minSwipePixels")]
    public int MinSwipePixels { get; set; } = 80;

    [YamlMember(Alias = "doubleTapMqttTopic")]
    public string? DoubleTapMqttTopic { get; set; }

    [YamlMember(Alias = "swipeMqttTopic")]
    public string? SwipeMqttTopic { get; set; }

    [YamlMember(Alias = "twoFingerSwipeMqttTopic")]
    public string? TwoFingerSwipeMqttTopic { get; set; }

    [YamlMember(Alias = "swipeHoldMqttTopic")]
    public string? SwipeHoldMqttTopic { get; set; }

    [YamlMember(Alias = "twoFingerSwipeHoldMqttTopic")]
    public string? TwoFingerSwipeHoldMqttTopic { get; set; }

    [YamlMember(Alias = "zoomMqttTopic")]
    public string? ZoomMqttTopic { get; set; }

    [YamlMember(Alias = "pinchMqttTopic")]
    public string? PinchMqttTopic { get; set; }

    [YamlMember(Alias = "tripleTapMqttTopic")]
    public string? TripleTapMqttTopic { get; set; }

    [YamlMember(Alias = "quadrupleTapMqttTopic")]
    public string? QuadrupleTapMqttTopic { get; set; }

    [YamlMember(Alias = "quintupleTapMqttTopic")]
    public string? QuintupleTapMqttTopic { get; set; }
}

public class MqttConfig
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
}

public class AutoStartConfig
{
    [YamlMember(Alias = "enabled")]
    public bool Enabled { get; set; } = false;
}

public class SensorsConfig
{
    [YamlMember(Alias = "enabled")]
    public List<string> Enabled { get; set; } =
    [
        "battery", "last_active", "updates_pending"
    ];

    /// <summary>Interval for all enabled sensors except <c>last_active</c> (minimum 5 seconds). Idle time (<c>last_active</c>) always publishes every 1 second when enabled.</summary>
    [YamlMember(Alias = "updateIntervalSeconds")]
    public int UpdateIntervalSeconds { get; set; } = 30;
}

public class CommandsConfig
{
    [YamlMember(Alias = "enabled")]
    public List<string> Enabled { get; set; } =
    [
        "shutdown", "restart", "sleep", "monitorsleep", "monitorwake",
        "refresh", "clearcache", "opensettings", "closesettings", "windowsupdate"
    ];

    [YamlMember(Alias = "powerShellCommand")]
    public string? PowerShellCommand { get; set; }
}

public class ScreenBrightnessConfig
{
    [YamlMember(Alias = "defaultPercent")]
    public int DefaultPercent { get; set; } = 100;
}

public class AudioOutputConfig
{
    /// <summary>Master volume 0–100 for the default playback device (after optional device switch).</summary>
    [YamlMember(Alias = "volumePercent")]
    public int VolumePercent { get; set; } = 100;

    /// <summary>Windows MMDevice ID to set as default playback device; empty = do not change OS default.</summary>
    [YamlMember(Alias = "playbackDeviceId")]
    public string PlaybackDeviceId { get; set; } = "";
}

