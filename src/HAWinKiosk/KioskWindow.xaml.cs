using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using HAWinKiosk.Mqtt;
using HAWinKiosk.Mqtt.Models;
using Microsoft.Web.WebView2.Core;

namespace HAWinKiosk;

public partial class KioskWindow : Window, IKioskHostActions
{
    private MqttClientService? _mqtt;
    private AppSettings _settings = new();
    private bool _webHooksAttached;

    private readonly object _gestureFramesLock = new();
    private readonly HashSet<CoreWebView2Frame> _gestureFrames = new();
    private nint _keyboardHookHandle = nint.Zero;
    private LowLevelKeyboardProc? _keyboardHookProc;
    private bool _taskbarHidden;
    private bool _isClosing;

    public KioskWindow(bool showSettingsFirst = false)
    {
        InitializeComponent();
        _showSettingsFirst = showSettingsFirst;
        SettingsButtonPopup.PlacementTarget = this;
    }

    private readonly bool _showSettingsFirst;

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        SessionStateTracker.EnsureInitialized();
        ApplyKioskBounds();
        EnableKioskLockdown();

        _settings = SettingsManager.Load();
        ApplySettingsUiTheme();

        if (_showSettingsFirst)
        {
            PopulateSettingsForm();
            ShowSettings();
        }
        else
        {
            ShowKiosk();
            await EnsureWebView2();
            var url = _settings.Kiosk.Url;
            if (string.IsNullOrWhiteSpace(url)) url = "about:blank";
            NavigateTo(url);
            StartMqttIfConfigured();
        }
    }

    private void ApplyKioskBounds()
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        var screen = System.Windows.Forms.Screen.FromHandle(hwnd);
        WindowState = WindowState.Normal;
        Left = screen.Bounds.Left;
        Top = screen.Bounds.Top;
        Width = screen.Bounds.Width;
        Height = screen.Bounds.Height;
        Topmost = true;
    }

    private void Window_Activated(object sender, EventArgs e)
    {
        if (_isClosing) return;
        ApplyKioskBounds();
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        if (_isClosing) return;
        // Do not force-activate here; it can steal focus from modal dialogs (PIN entry).
        if (OwnedWindows.OfType<Window>().Any(w => w.IsVisible)) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (_isClosing) return;
            Topmost = true;
        });
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _isClosing = true;
        DisableKioskLockdown();
    }

    private static void SelectComboByTag(System.Windows.Controls.ComboBox cb, string tag)
    {
        var t = tag.ToLowerInvariant();
        foreach (System.Windows.Controls.ComboBoxItem item in cb.Items)
        {
            if (item.Tag?.ToString()?.ToLowerInvariant() == t)
            {
                cb.SelectedItem = item;
                return;
            }
        }
    }

    private static string SelectedTag(System.Windows.Controls.ComboBox cb, string fallback = "disabled")
    {
        return cb.SelectedItem is ComboBoxItem item && item.Tag is string tag && !string.IsNullOrWhiteSpace(tag)
            ? tag.ToLowerInvariant()
            : fallback;
    }

    private static bool IsEnabledGestureAction(System.Windows.Controls.ComboBox cb) => SelectedTag(cb) != "disabled";
    private static bool IsMqttGestureAction(System.Windows.Controls.ComboBox cb) => SelectedTag(cb) == "mqtt";

    private void GestureAction_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateGestureOptionsVisibility();
    }

    private void DeviceNameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateGestureTopicPrefixPreviews();
    }

    private void UpdateGestureTopicPrefixPreviews()
    {
        if (DeviceNameBox == null
            || GestureSwipeMqttPrefixText == null
            || GestureSwipeHoldMqttPrefixText == null
            || GesturePinchMqttPrefixText == null
            || GestureTripleTapMqttPrefixText == null
            || GestureQuadTapMqttPrefixText == null)
            return;

        var prefix = string.IsNullOrWhiteSpace(_settings.Mqtt.DiscoveryPrefix) ? "homeassistant" : _settings.Mqtt.DiscoveryPrefix.Trim();
        var rawDevice = string.IsNullOrWhiteSpace(DeviceNameBox.Text) ? (_settings.Mqtt.DeviceName ?? "living-room-kiosk") : DeviceNameBox.Text;
        var devId = MqttDiscovery.SanitizeId(rawDevice);
        var topicPrefix = $"{prefix}/command/{devId}/gesture/";
        GestureSwipeMqttPrefixText.Text = topicPrefix;
        GestureSwipeHoldMqttPrefixText.Text = topicPrefix;
        GesturePinchMqttPrefixText.Text = topicPrefix;
        GestureTripleTapMqttPrefixText.Text = topicPrefix;
        GestureQuadTapMqttPrefixText.Text = topicPrefix;
    }

    private void UpdateGestureOptionsVisibility()
    {
        GestureSwipeOptionsPanel.Visibility = IsEnabledGestureAction(GestureSwipeActionCombo) ? Visibility.Visible : Visibility.Collapsed;
        GestureSwipeHoldDirectionPanel.Visibility = IsEnabledGestureAction(GestureSwipeHoldActionCombo) ? Visibility.Visible : Visibility.Collapsed;
        GestureSwipeHoldOptionsPanel.Visibility = IsEnabledGestureAction(GestureSwipeHoldActionCombo) ? Visibility.Visible : Visibility.Collapsed;
        GestureTripleTapLocationPanel.Visibility = IsEnabledGestureAction(GestureTripleTapActionCombo) ? Visibility.Visible : Visibility.Collapsed;
        GestureQuadTapLocationPanel.Visibility = IsEnabledGestureAction(GestureQuadTapActionCombo) ? Visibility.Visible : Visibility.Collapsed;

        GestureSwipeMqttTopicPanel.Visibility = IsMqttGestureAction(GestureSwipeActionCombo) ? Visibility.Visible : Visibility.Collapsed;
        GestureSwipeHoldMqttTopicPanel.Visibility = IsMqttGestureAction(GestureSwipeHoldActionCombo) ? Visibility.Visible : Visibility.Collapsed;
        GesturePinchMqttTopicPanel.Visibility = IsMqttGestureAction(GesturePinchActionCombo) ? Visibility.Visible : Visibility.Collapsed;
        GestureTripleTapMqttTopicPanel.Visibility = IsMqttGestureAction(GestureTripleTapActionCombo) ? Visibility.Visible : Visibility.Collapsed;
        GestureQuadTapMqttTopicPanel.Visibility = IsMqttGestureAction(GestureQuadTapActionCombo) ? Visibility.Visible : Visibility.Collapsed;
        UpdateGestureTopicPrefixPreviews();
    }

    private void PopulateSettingsForm()
    {
        UrlBox.Text = _settings.Kiosk.Url ?? "";
        MqttHostBox.Text = _settings.Mqtt.Host ?? "";
        MqttPortBox.Text = _settings.Mqtt.Port.ToString();
        MqttUsernameBox.Text = _settings.Mqtt.Username ?? "";
        MqttPasswordBox.Text = _settings.Mqtt.Password ?? "";
        DeviceNameBox.Text = _settings.Mqtt.DeviceName ?? "";
        AutoStartToggle.IsChecked = _settings.AutoStart.Enabled;
        PinProtectionToggle.IsChecked = !_settings.Kiosk.PinProtectionDisabled;
        SettingsPinBox.Password = _settings.Kiosk.Pin ?? "";
        PinHintBox.Text = _settings.Kiosk.PinHint ?? "";
        PinResetQuestionBox.Text = _settings.Kiosk.PinResetQuestion ?? "";
        PinResetAnswerBox.Text = _settings.Kiosk.PinResetAnswer ?? "";

        SelectComboByTag(GestureSwipeActionCombo, _settings.Kiosk.Gestures.SwipeAction);
        SelectComboByTag(GestureSwipeHoldActionCombo, _settings.Kiosk.Gestures.SwipeHoldAction);
        SelectComboByTag(GesturePinchActionCombo, _settings.Kiosk.Gestures.PinchAction);
        SelectComboByTag(GestureTripleTapActionCombo, _settings.Kiosk.Gestures.TripleTapAction);
        SelectComboByTag(GestureQuadTapActionCombo, _settings.Kiosk.Gestures.QuadrupleTapAction);
        SelectComboByTag(GestureTripleTapLocationCombo, _settings.Kiosk.Gestures.TripleTapLocation);
        SelectComboByTag(GestureQuadTapLocationCombo, _settings.Kiosk.Gestures.QuadrupleTapLocation);
        SelectComboByTag(SwipeDirectionCombo, _settings.Kiosk.Gestures.SwipeDirection);
        SelectComboByTag(SwipeHoldDirectionCombo, _settings.Kiosk.Gestures.SwipeHoldDirection);
        SwipeHoldMsBox.Text = ((int)Math.Round(_settings.Kiosk.Gestures.SwipeHoldMs)).ToString(CultureInfo.InvariantCulture);
        GestureSwipeMqttTopicBox.Text = _settings.Kiosk.Gestures.SwipeMqttTopic ?? "";
        GestureSwipeHoldMqttTopicBox.Text = _settings.Kiosk.Gestures.SwipeHoldMqttTopic ?? "";
        GesturePinchMqttTopicBox.Text = _settings.Kiosk.Gestures.PinchMqttTopic ?? "";
        GestureTripleTapMqttTopicBox.Text = _settings.Kiosk.Gestures.TripleTapMqttTopic ?? "";
        GestureQuadTapMqttTopicBox.Text = _settings.Kiosk.Gestures.QuadrupleTapMqttTopic ?? "";

        DefaultBrightnessBox.Text = Math.Clamp(_settings.ScreenBrightness.DefaultPercent, 0, 100).ToString(CultureInfo.InvariantCulture);
        SelectComboByTag(DefaultOrientationCombo, CanonicalOrientationForCombo(_settings.ScreenOrientation.Default));

        var sensors = _settings.Sensors.Enabled.Select(s => s.ToLowerInvariant()).ToHashSet();
        MqttSensorBatteryToggle.IsChecked = sensors.Contains("battery");
        MqttSensorSessionToggle.IsChecked = sensors.Contains("sessionstate");
        MqttSensorIdleToggle.IsChecked = sensors.Contains("last_active");
        MqttSensorUpdatesPendingToggle.IsChecked = sensors.Contains("updates_pending");

        var cmds = _settings.Commands.Enabled.Select(s => s.ToLowerInvariant()).ToHashSet();
        MqttCmdShutdownToggle.IsChecked = cmds.Contains("shutdown");
        MqttCmdRestartToggle.IsChecked = cmds.Contains("restart");
        MqttCmdSleepToggle.IsChecked = cmds.Contains("sleep");
        MqttCmdMonSleepToggle.IsChecked = cmds.Contains("monitorsleep");
        MqttCmdMonWakeToggle.IsChecked = cmds.Contains("monitorwake");
        MqttCmdRefreshToggle.IsChecked = cmds.Contains("refresh");
        MqttCmdClearCacheToggle.IsChecked = cmds.Contains("clearcache");
        MqttCmdOpenSettingsToggle.IsChecked = cmds.Contains("opensettings");
        MqttCmdCloseSettingsToggle.IsChecked = cmds.Contains("closesettings");
        MqttCmdWindowsUpdateToggle.IsChecked = cmds.Contains("windowsupdate");

        ShowSettingsButtonToggle.IsChecked = _settings.Kiosk.ShowSettingsButton;
        SelectComboByTag(ThemeModeCombo, UiThemeHelper.NormalizeUiTheme(_settings.Kiosk.UiTheme));
        UpdateGestureOptionsVisibility();

        UpdatePinProtectedFieldsVisibility();
        ApplySettingsUiTheme();
    }

    private void PinProtectionToggle_Changed(object sender, RoutedEventArgs e)
    {
        UpdatePinProtectedFieldsVisibility();
    }

    private void UpdatePinProtectedFieldsVisibility()
    {
        PinProtectedFieldsPanel.Visibility = PinProtectionToggle.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private static string CanonicalOrientationForCombo(string? raw)
    {
        var s = (raw ?? "landscape").Trim().ToLowerInvariant().Replace(" ", "").Replace("-", "_");
        return s switch
        {
            "1" or "90" or "portrait" or "dmdo_90" => "portrait",
            "2" or "180" or "landscape_flipped" or "upside_down" or "dmdo_180" => "landscape_flipped",
            "3" or "270" or "portrait_flipped" or "dmdo_270" => "portrait_flipped",
            _ => "landscape"
        };
    }

    private void ShowSettings()
    {
        WebView.Visibility = Visibility.Collapsed;
        SettingsButtonPopup.IsOpen = false;
        SettingsPanel.Visibility = Visibility.Visible;
    }

    private void ShowKiosk()
    {
        SettingsPanel.Visibility = Visibility.Collapsed;
        WebView.Visibility = Visibility.Visible;
        UpdateSettingsButtonVisibility();
    }

    private void UpdateSettingsButtonVisibility()
    {
        if (!_settings.Kiosk.ShowSettingsButton)
        {
            SettingsButtonPopup.IsOpen = false;
            return;
        }

        if (SettingsPanel.Visibility == Visibility.Visible)
        {
            SettingsButtonPopup.IsOpen = false;
            return;
        }

        SettingsButtonPopup.IsOpen = true;
        PositionSettingsButtonPopup();
    }

    private void ThemeModeCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        ApplySettingsUiTheme();
    }

    private string GetEffectiveUiThemeMode()
    {
        if (SettingsPanel.Visibility == Visibility.Visible
            && ThemeModeCombo.SelectedItem is ComboBoxItem ci
            && ci.Tag is string t
            && !string.IsNullOrEmpty(t))
            return t;
        return UiThemeHelper.NormalizeUiTheme(_settings.Kiosk.UiTheme);
    }

    private void ApplySettingsUiTheme()
    {
        var dark = UiThemeHelper.ResolveEffectiveDark(GetEffectiveUiThemeMode());
        var r = Resources;

        void SetBrush(string key, System.Windows.Media.Color c)
        {
            r[key] = new SolidColorBrush(c);
        }

        if (dark)
        {
            SetBrush("Theme.Kiosk.Bg", System.Windows.Media.Color.FromRgb(0x00, 0x00, 0x00));
            SetBrush("Theme.Kiosk.SettingsButtonBg", System.Windows.Media.Color.FromRgb(0x16, 0xB9, 0xF0));
            SetBrush("Theme.Settings.PanelBg", System.Windows.Media.Color.FromRgb(0x1E, 0x1E, 0x1E));
            SetBrush("Theme.Settings.CardBg", System.Windows.Media.Color.FromRgb(0x25, 0x25, 0x26));
            SetBrush("Theme.Settings.CardBorder", System.Windows.Media.Color.FromRgb(0x3F, 0x3F, 0x46));
            SetBrush("Theme.Settings.HeaderBg", System.Windows.Media.Color.FromRgb(0x2D, 0x2D, 0x30));
            SetBrush("Theme.Settings.HeaderBorder", System.Windows.Media.Color.FromRgb(0x3F, 0x3F, 0x46));
            SetBrush("Theme.Settings.Fg", System.Windows.Media.Color.FromRgb(0xE8, 0xE8, 0xE8));
            SetBrush("Theme.Settings.FgMuted", System.Windows.Media.Color.FromRgb(0xB0, 0xB0, 0xB0));
            SetBrush("Theme.Settings.FgSub", System.Windows.Media.Color.FromRgb(0xCC, 0xCC, 0xCC));
            SetBrush("Theme.Settings.InputBg", System.Windows.Media.Color.FromRgb(0x30, 0x30, 0x30));
            SetBrush("Theme.Settings.InputBorder", System.Windows.Media.Color.FromRgb(0x50, 0x50, 0x50));
            SetBrush("Theme.Button.SecondaryBg", System.Windows.Media.Color.FromRgb(0x3A, 0x3A, 0x3D));
            SetBrush("Theme.Button.SecondaryFg", System.Windows.Media.Color.FromRgb(0xE0, 0xE0, 0xE0));
            SetBrush("Theme.Button.SecondaryBorder", System.Windows.Media.Color.FromRgb(0x5A, 0x5A, 0x60));
            SetBrush("Theme.Toggle.TrackOff", System.Windows.Media.Color.FromRgb(0x4A, 0x4A, 0x4E));
            SetBrush("Theme.Toggle.ThumbOff", System.Windows.Media.Color.FromRgb(0xD5, 0xD5, 0xD8));
            SetBrush("Theme.Toggle.TrackOn", System.Windows.Media.Color.FromRgb(0x16, 0xB9, 0xF0));
            SetBrush("Theme.Toggle.ThumbOn", System.Windows.Media.Color.FromRgb(0xB9, 0xE9, 0xFF));
            SetBrush("Theme.Toggle.DisabledTrack", System.Windows.Media.Color.FromRgb(0x3C, 0x3C, 0x40));
            SetBrush("Theme.Toggle.DisabledThumb", System.Windows.Media.Color.FromRgb(0x84, 0x84, 0x88));
        }
        else
        {
            SetBrush("Theme.Kiosk.Bg", System.Windows.Media.Color.FromRgb(0xF0, 0xF3, 0xF6));
            SetBrush("Theme.Kiosk.SettingsButtonBg", System.Windows.Media.Color.FromRgb(0x16, 0xB9, 0xF0));
            SetBrush("Theme.Settings.PanelBg", System.Windows.Media.Color.FromRgb(0xE8, 0xEC, 0xF0));
            SetBrush("Theme.Settings.CardBg", System.Windows.Media.Color.FromRgb(0xFF, 0xFF, 0xFF));
            SetBrush("Theme.Settings.CardBorder", System.Windows.Media.Color.FromRgb(0xD8, 0xDD, 0xE3));
            SetBrush("Theme.Settings.HeaderBg", System.Windows.Media.Color.FromRgb(0xF4, 0xF6, 0xF9));
            SetBrush("Theme.Settings.HeaderBorder", System.Windows.Media.Color.FromRgb(0xE0, 0xE4, 0xEA));
            SetBrush("Theme.Settings.Fg", System.Windows.Media.Color.FromRgb(0x1A, 0x1A, 0x1A));
            SetBrush("Theme.Settings.FgMuted", System.Windows.Media.Color.FromRgb(0x44, 0x44, 0x44));
            SetBrush("Theme.Settings.FgSub", System.Windows.Media.Color.FromRgb(0x33, 0x33, 0x33));
            SetBrush("Theme.Settings.InputBg", System.Windows.Media.Color.FromRgb(0xFF, 0xFF, 0xFF));
            SetBrush("Theme.Settings.InputBorder", System.Windows.Media.Color.FromRgb(0xCC, 0xCC, 0xCC));
            SetBrush("Theme.Button.SecondaryBg", System.Windows.Media.Color.FromRgb(0xF6, 0xF8, 0xFB));
            SetBrush("Theme.Button.SecondaryFg", System.Windows.Media.Color.FromRgb(0x1F, 0x29, 0x37));
            SetBrush("Theme.Button.SecondaryBorder", System.Windows.Media.Color.FromRgb(0xC8, 0xD2, 0xDE));
            SetBrush("Theme.Toggle.TrackOff", System.Windows.Media.Color.FromRgb(0xD0, 0xD8, 0xE4));
            SetBrush("Theme.Toggle.ThumbOff", System.Windows.Media.Color.FromRgb(0xFF, 0xFF, 0xFF));
            SetBrush("Theme.Toggle.TrackOn", System.Windows.Media.Color.FromRgb(0x16, 0xB9, 0xF0));
            SetBrush("Theme.Toggle.ThumbOn", System.Windows.Media.Color.FromRgb(0xB9, 0xE9, 0xFF));
            SetBrush("Theme.Toggle.DisabledTrack", System.Windows.Media.Color.FromRgb(0xB8, 0xC2, 0xD0));
            SetBrush("Theme.Toggle.DisabledThumb", System.Windows.Media.Color.FromRgb(0xE5, 0xE8, 0xEE));
        }
    }

    private void PositionSettingsButtonPopup()
    {
        const double margin = 16;
        const double size = 40;
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        SettingsButtonPopup.HorizontalOffset = ActualWidth - size - margin;
        SettingsButtonPopup.VerticalOffset = margin;
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (SettingsButtonPopup.IsOpen)
            PositionSettingsButtonPopup();
    }

    private async Task EnsureWebView2()
    {
        if (WebView.CoreWebView2 != null)
            return;

        var env = await CoreWebView2Environment.CreateAsync(
            null, SettingsManager.GetUserDataFolder(), null);
        await WebView.EnsureCoreWebView2Async(env);
        var wv = WebView.CoreWebView2!;
        wv.Settings.AreDefaultScriptDialogsEnabled = false;
        // Lock down browser-like behavior on touch kiosks.
        TrySetCoreWebView2BoolSetting(wv.Settings, "IsZoomControlEnabled", false);
        TrySetCoreWebView2BoolSetting(wv.Settings, "IsPinchZoomEnabled", false);
        TrySetCoreWebView2BoolSetting(wv.Settings, "IsSwipeNavigationEnabled", false);
        TrySetCoreWebView2BoolSetting(wv.Settings, "AreBrowserAcceleratorKeysEnabled", false);

        if (!_webHooksAttached)
        {
            _webHooksAttached = true;
            wv.WebMessageReceived += OnWebMessageReceived;
            wv.NavigationCompleted += OnNavigationCompleted;
            wv.FrameCreated += OnCoreWebViewFrameCreated;
        }
    }

    private void OnCoreWebViewFrameCreated(object? sender, CoreWebView2FrameCreatedEventArgs e)
    {
        RegisterGestureHooksOnFrame(e.Frame);
    }

    /// <summary>Top-level iframes from <see cref="CoreWebView2.FrameCreated"/> (nested child frames require a newer WebView2 SDK with <c>CoreWebView2Frame.FrameCreated</c>).</summary>
    private void RegisterGestureHooksOnFrame(CoreWebView2Frame frame)
    {
        lock (_gestureFramesLock)
            _gestureFrames.Add(frame);

        frame.Destroyed += (_, _) =>
        {
            lock (_gestureFramesLock)
                _gestureFrames.Remove(frame);
        };

        frame.NavigationCompleted += async (_, navArgs) =>
        {
            if (!navArgs.IsSuccess || frame.IsDestroyed() != 0)
                return;
            await TryInjectGestureScriptIntoFrameAsync(frame);
        };
    }

    private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess || WebView.CoreWebView2 == null) return;
        try
        {
            await TryInjectGestureScriptIntoMainAsync();
        }
        catch
        {
            // optional: page may block script injection
        }
    }

    private async Task TryInjectGestureScriptIntoMainAsync()
    {
        if (WebView.CoreWebView2 == null) return;
        var script = WebViewBridge.BuildDocumentScript(_settings.Kiosk);
        await WebView.CoreWebView2.ExecuteScriptAsync(script);
    }

    /// <summary>Inject into an iframe. Cross-origin frames may reject injection; failures are ignored.</summary>
    private async Task TryInjectGestureScriptIntoFrameAsync(CoreWebView2Frame frame)
    {
        try
        {
            if (frame.IsDestroyed() != 0) return;
            var script = WebViewBridge.BuildDocumentScript(_settings.Kiosk);
            await frame.ExecuteScriptAsync(script);
        }
        catch
        {
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var json = e.WebMessageAsJson;
            if (string.IsNullOrWhiteSpace(json)) return;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeEl)) return;
            var type = typeEl.GetString();
            if (type == "gesture" && root.TryGetProperty("gesture", out var gestureEl))
            {
                var gesture = gestureEl.GetString();
                if (!string.IsNullOrWhiteSpace(gesture))
                    Dispatcher.BeginInvoke(new Action(() => HandleGestureTrigger(gesture)));
                return;
            }
        }
        catch
        {
            // malformed message from page
        }
    }

    private string GestureActionFor(string gestureKey)
    {
        return gestureKey.ToLowerInvariant() switch
        {
            "swipe" => (_settings.Kiosk.Gestures.SwipeAction ?? "disabled").ToLowerInvariant(),
            "swipehold" => (_settings.Kiosk.Gestures.SwipeHoldAction ?? "disabled").ToLowerInvariant(),
            "pinch" => (_settings.Kiosk.Gestures.PinchAction ?? "disabled").ToLowerInvariant(),
            "tripletap" => (_settings.Kiosk.Gestures.TripleTapAction ?? "disabled").ToLowerInvariant(),
            "quadrupletap" => (_settings.Kiosk.Gestures.QuadrupleTapAction ?? "disabled").ToLowerInvariant(),
            _ => "disabled"
        };
    }

    private string? GestureMqttTopicFor(string gestureKey)
    {
        return gestureKey.ToLowerInvariant() switch
        {
            "swipe" => _settings.Kiosk.Gestures.SwipeMqttTopic,
            "swipehold" => _settings.Kiosk.Gestures.SwipeHoldMqttTopic,
            "pinch" => _settings.Kiosk.Gestures.PinchMqttTopic,
            "tripletap" => _settings.Kiosk.Gestures.TripleTapMqttTopic,
            "quadrupletap" => _settings.Kiosk.Gestures.QuadrupleTapMqttTopic,
            _ => null
        };
    }

    private void HandleGestureTrigger(string gestureKey)
    {
        var action = GestureActionFor(gestureKey);
        switch (action)
        {
            case "reload":
                ((IKioskHostActions)this).ReloadWebView();
                break;
            case "clearcache_reload":
                _ = Dispatcher.InvokeAsync(async () =>
                {
                    await ClearBrowsingCacheUiAsync();
                    ((IKioskHostActions)this).ReloadWebView();
                });
                break;
            case "settings":
                RequestOpenSettings();
                break;
            case "mqtt":
                var topic = GestureMqttTopicFor(gestureKey);
                if (!string.IsNullOrWhiteSpace(topic) && _mqtt != null)
                    _ = _mqtt.PublishGestureMessageAsync(topic);
                break;
        }
    }

    private async Task ClearBrowsingCacheUiAsync()
    {
        if (WebView.CoreWebView2?.Profile == null) return;
        await WebView.CoreWebView2.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.DiskCache);
    }

    private void NavigateTo(string url)
    {
        if (WebView.CoreWebView2 != null)
            WebView.CoreWebView2.Navigate(url);
    }

    private async void StartMqttIfConfigured()
    {
        if (string.IsNullOrWhiteSpace(_settings.Mqtt.Host)) return;

        _mqtt = new MqttClientService();
        _mqtt.Error += (_, _) => { };

        try
        {
            await _mqtt.ConnectAsync(_settings, this);
        }
        catch
        {
            // MQTT optional; fail silently
        }
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        RequestOpenSettings();
    }

    /// <summary>Opens Settings after optional PIN (gear and secret tap).</summary>
    private void RequestOpenSettings()
    {
        _settings = SettingsManager.Load();
        if (!_settings.Kiosk.PinProtectionDisabled && !string.IsNullOrEmpty(_settings.Kiosk.Pin))
        {
            var dlg = new PinEntryWindow(
                _settings.Kiosk.PinHint,
                _settings.Kiosk.Pin,
                _settings.Kiosk.PinResetQuestion,
                _settings.Kiosk.PinResetAnswer,
                GetEffectiveUiThemeMode()) { Owner = this };
            if (dlg.ShowDialog() != true) return;
            if (dlg.PinResetRequested)
            {
                _settings.Kiosk.Pin = null;
                _settings.Kiosk.PinHint = null;
                SettingsManager.Save(_settings);
            }
        }

        PopulateSettingsForm();
        ShowSettings();
    }

    /// <summary>MQTT opensettings command — no PIN gate (trusted broker).</summary>
    private void OpenSettingsFromMqtt()
    {
        _settings = SettingsManager.Load();
        PopulateSettingsForm();
        ShowSettings();
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyFormToSettings();

        ApplySettingsUiTheme();

        SettingsManager.Save(_settings);
        AutoStartManager.SetEnabled(_settings.AutoStart.Enabled);

        ShowKiosk();

        if (WebView.CoreWebView2 == null)
        {
            await EnsureWebView2();
        }

        var url = _settings.Kiosk.Url;
        if (string.IsNullOrWhiteSpace(url)) url = "about:blank";
        NavigateTo(url);

        RestartMqttIfNeeded();
    }

    private void ApplyFormToSettings()
    {
        var disk = SettingsManager.Load();

        _settings.Kiosk.Url = UrlBox.Text?.Trim() ?? "http://homeassistant.local:8123";
        _settings.Mqtt.Host = MqttHostBox.Text?.Trim() ?? "";
        if (int.TryParse(MqttPortBox.Text, out var port))
            _settings.Mqtt.Port = port;
        _settings.Mqtt.Username = string.IsNullOrWhiteSpace(MqttUsernameBox.Text) ? null : MqttUsernameBox.Text.Trim();
        _settings.Mqtt.Password = string.IsNullOrEmpty(MqttPasswordBox.Text) ? null : MqttPasswordBox.Text;
        _settings.Mqtt.DeviceName = DeviceNameBox.Text?.Trim() ?? "living-room-kiosk";
        _settings.Mqtt.DiscoveryPrefix = string.IsNullOrWhiteSpace(disk.Mqtt.DiscoveryPrefix) ? "homeassistant" : disk.Mqtt.DiscoveryPrefix;
        _settings.AutoStart.Enabled = AutoStartToggle.IsChecked == true;
        _settings.Kiosk.PinProtectionDisabled = PinProtectionToggle.IsChecked != true;
        if (_settings.Kiosk.PinProtectionDisabled)
        {
            _settings.Kiosk.Pin = null;
            _settings.Kiosk.PinHint = null;
        }
        else
        {
            var pin = SettingsPinBox.Password ?? "";
            _settings.Kiosk.Pin = string.IsNullOrEmpty(pin) ? null : pin;
            _settings.Kiosk.PinHint = string.IsNullOrWhiteSpace(PinHintBox.Text) ? null : PinHintBox.Text.Trim();
        }
        _settings.Kiosk.PinResetQuestion = string.IsNullOrWhiteSpace(PinResetQuestionBox.Text) ? null : PinResetQuestionBox.Text.Trim();
        _settings.Kiosk.PinResetAnswer = string.IsNullOrWhiteSpace(PinResetAnswerBox.Text) ? null : PinResetAnswerBox.Text.Trim();

        _settings.Kiosk.Gestures.SwipeAction = SelectedTag(GestureSwipeActionCombo);
        _settings.Kiosk.Gestures.SwipeHoldAction = SelectedTag(GestureSwipeHoldActionCombo);
        _settings.Kiosk.Gestures.PinchAction = SelectedTag(GesturePinchActionCombo);
        _settings.Kiosk.Gestures.TripleTapAction = SelectedTag(GestureTripleTapActionCombo);
        _settings.Kiosk.Gestures.QuadrupleTapAction = SelectedTag(GestureQuadTapActionCombo);
        _settings.Kiosk.Gestures.TripleTapLocation = SelectedTag(GestureTripleTapLocationCombo, "top-left");
        _settings.Kiosk.Gestures.QuadrupleTapLocation = SelectedTag(GestureQuadTapLocationCombo, "top-left");
        _settings.Kiosk.ShowSettingsButton = ShowSettingsButtonToggle.IsChecked == true;
        if (ThemeModeCombo.SelectedItem is ComboBoxItem themeItem && themeItem.Tag is string ut)
            _settings.Kiosk.UiTheme = UiThemeHelper.NormalizeUiTheme(ut);
        if (SwipeDirectionCombo.SelectedItem is System.Windows.Controls.ComboBoxItem swipeItem && swipeItem.Tag is string dir)
            _settings.Kiosk.Gestures.SwipeDirection = dir;
        if (SwipeHoldDirectionCombo.SelectedItem is System.Windows.Controls.ComboBoxItem holdDirItem && holdDirItem.Tag is string holdDir)
            _settings.Kiosk.Gestures.SwipeHoldDirection = holdDir;
        if (int.TryParse(SwipeHoldMsBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var holdMs))
            _settings.Kiosk.Gestures.SwipeHoldMs = Math.Max(100, holdMs);
        _settings.Kiosk.Gestures.SwipeMqttTopic = string.IsNullOrWhiteSpace(GestureSwipeMqttTopicBox.Text) ? null : GestureSwipeMqttTopicBox.Text.Trim();
        _settings.Kiosk.Gestures.SwipeHoldMqttTopic = string.IsNullOrWhiteSpace(GestureSwipeHoldMqttTopicBox.Text) ? null : GestureSwipeHoldMqttTopicBox.Text.Trim();
        _settings.Kiosk.Gestures.PinchMqttTopic = string.IsNullOrWhiteSpace(GesturePinchMqttTopicBox.Text) ? null : GesturePinchMqttTopicBox.Text.Trim();
        _settings.Kiosk.Gestures.TripleTapMqttTopic = string.IsNullOrWhiteSpace(GestureTripleTapMqttTopicBox.Text) ? null : GestureTripleTapMqttTopicBox.Text.Trim();
        _settings.Kiosk.Gestures.QuadrupleTapMqttTopic = string.IsNullOrWhiteSpace(GestureQuadTapMqttTopicBox.Text) ? null : GestureQuadTapMqttTopicBox.Text.Trim();

        if (int.TryParse(DefaultBrightnessBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dbp))
            _settings.ScreenBrightness.DefaultPercent = Math.Clamp(dbp, 0, 100);
        if (DefaultOrientationCombo.SelectedItem is System.Windows.Controls.ComboBoxItem oi && oi.Tag is string ot)
            _settings.ScreenOrientation.Default = ot;

        _settings.Sensors.Enabled = new List<string>();
        if (MqttSensorBatteryToggle.IsChecked == true) _settings.Sensors.Enabled.Add("battery");
        if (MqttSensorSessionToggle.IsChecked == true) _settings.Sensors.Enabled.Add("sessionstate");
        if (MqttSensorIdleToggle.IsChecked == true) _settings.Sensors.Enabled.Add("last_active");
        if (MqttSensorUpdatesPendingToggle.IsChecked == true) _settings.Sensors.Enabled.Add("updates_pending");

        _settings.Commands.Enabled = new List<string>();
        if (MqttCmdShutdownToggle.IsChecked == true) _settings.Commands.Enabled.Add("shutdown");
        if (MqttCmdRestartToggle.IsChecked == true) _settings.Commands.Enabled.Add("restart");
        if (MqttCmdSleepToggle.IsChecked == true) _settings.Commands.Enabled.Add("sleep");
        if (MqttCmdMonSleepToggle.IsChecked == true) _settings.Commands.Enabled.Add("monitorsleep");
        if (MqttCmdMonWakeToggle.IsChecked == true) _settings.Commands.Enabled.Add("monitorwake");
        if (MqttCmdRefreshToggle.IsChecked == true) _settings.Commands.Enabled.Add("refresh");
        if (MqttCmdClearCacheToggle.IsChecked == true) _settings.Commands.Enabled.Add("clearcache");
        if (MqttCmdOpenSettingsToggle.IsChecked == true) _settings.Commands.Enabled.Add("opensettings");
        if (MqttCmdCloseSettingsToggle.IsChecked == true) _settings.Commands.Enabled.Add("closesettings");
        if (MqttCmdWindowsUpdateToggle.IsChecked == true) _settings.Commands.Enabled.Add("windowsupdate");
    }

    private void ExitToWindows_Click(object sender, RoutedEventArgs e)
    {
        ApplyFormToSettings();
        SettingsManager.Save(_settings);
        AutoStartManager.SetEnabled(_settings.AutoStart.Enabled);
        _mqtt?.Dispose();
        _mqtt = null;
        System.Windows.Application.Current.Shutdown();
    }

    private async void RestartMqttIfNeeded()
    {
        if (_mqtt == null)
        {
            StartMqttIfConfigured();
            return;
        }
        await _mqtt.DisconnectAsync();
        _mqtt.Dispose();
        _mqtt = null;
        StartMqttIfConfigured();
    }

    protected override void OnClosed(EventArgs e)
    {
        _isClosing = true;
        DisableKioskLockdown();
        _mqtt?.Dispose();
        base.OnClosed(e);
    }

    private static void TrySetCoreWebView2BoolSetting(object settings, string propertyName, bool value)
    {
        try
        {
            var property = settings.GetType().GetProperty(propertyName);
            if (property?.CanWrite == true && property.PropertyType == typeof(bool))
                property.SetValue(settings, value);
        }
        catch
        {
            // keep kiosk running even if a specific SDK property is unavailable
        }
    }

    private void EnableKioskLockdown()
    {
        InstallKeyboardHook();
        HideTaskbar();
    }

    private void DisableKioskLockdown()
    {
        UninstallKeyboardHook();
        ShowTaskbar();
    }

    private void InstallKeyboardHook()
    {
        if (_keyboardHookHandle != nint.Zero) return;
        _keyboardHookProc = KeyboardHookCallback;
        _keyboardHookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardHookProc, GetModuleHandle(null), 0);
    }

    private void UninstallKeyboardHook()
    {
        if (_keyboardHookHandle == nint.Zero) return;
        _ = UnhookWindowsHookEx(_keyboardHookHandle);
        _keyboardHookHandle = nint.Zero;
    }

    private void HideTaskbar()
    {
        var taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar == nint.Zero) return;
        _taskbarHidden = ShowWindow(taskbar, SW_HIDE);
    }

    private void ShowTaskbar()
    {
        var taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar == nint.Zero) return;
        if (_taskbarHidden)
            _ = ShowWindow(taskbar, SW_SHOW);
        _taskbarHidden = false;
    }

    private nint KeyboardHookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            var msg = (uint)wParam;
            if (msg is WM_KEYDOWN or WM_SYSKEYDOWN)
            {
                var data = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
                var vk = data.vkCode;
                var isCtrlDown = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
                var isShiftDown = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
                var isAltDown = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
                if (vk is VK_LWIN or VK_RWIN or VK_APPS
                    || vk is VK_F11 or VK_F12
                    || (vk == VK_ESCAPE && isCtrlDown)
                    || (vk == VK_ESCAPE && isCtrlDown && isShiftDown)
                    || (vk == VK_F4 && isAltDown)
                    || (vk == VK_TAB && isAltDown))
                    return 1;
            }
        }
        return CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);
    }

    private delegate nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public nint dwExtraInfo;
    }

    private const int WH_KEYBOARD_LL = 13;
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_SYSKEYDOWN = 0x0104;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_APPS = 0x5D;
    private const int VK_MENU = 0x12;
    private const int VK_TAB = 0x09;
    private const int VK_F4 = 0x73;
    private const int VK_F11 = 0x7A;
    private const int VK_F12 = 0x7B;
    private const int VK_ESCAPE = 0x1B;
    private const int VK_CONTROL = 0x11;
    private const int VK_SHIFT = 0x10;
    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    void IKioskHostActions.ReloadWebView()
    {
        Dispatcher.Invoke(() => WebView.CoreWebView2?.Reload());
    }

    async Task IKioskHostActions.ClearBrowsingCacheAsync(CancellationToken cancellationToken)
    {
        await Dispatcher.InvokeAsync(ClearBrowsingCacheUiAsync);
    }

    void IKioskHostActions.OpenSettings()
    {
        Dispatcher.Invoke(OpenSettingsFromMqtt);
    }

    void IKioskHostActions.CloseSettings()
    {
        Dispatcher.Invoke(ShowKiosk);
    }

    void IKioskHostActions.NotifySettingsChangedFromMqtt()
    {
        _ = Dispatcher.InvokeAsync(async () =>
        {
            _settings = SettingsManager.Load();
            if (SettingsPanel.Visibility == Visibility.Visible)
                PopulateSettingsForm();
            else
                ApplySettingsUiTheme();
            await ReinjectScriptAsync();
        });
    }

    private async Task ReinjectScriptAsync()
    {
        try
        {
            if (WebView.CoreWebView2 == null) return;
            await TryInjectGestureScriptIntoMainAsync();
            CoreWebView2Frame[] snapshot;
            lock (_gestureFramesLock)
                snapshot = _gestureFrames.ToArray();
            foreach (var f in snapshot)
            {
                if (f.IsDestroyed() == 0)
                    await TryInjectGestureScriptIntoFrameAsync(f);
            }
        }
        catch
        {
            // page may block script injection
        }
    }
}
