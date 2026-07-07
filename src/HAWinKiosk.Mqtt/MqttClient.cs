using System.Globalization;
using System.Text;
using HAWinKiosk.Mqtt.Commands;
using HAWinKiosk.Mqtt.Models;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

namespace HAWinKiosk.Mqtt;

/// <summary>
/// MQTT connect, discovery, command subscription, and sensor publish loop. For HASS.Agent-style discovery
/// and command routing, compare with <c>HASS-AGENT-2-REFERENCE/…/Mqtt/IMqttManager.cs</c> and related Home Assistant models
/// (not a port; our topic layout follows the HA WinKiosk plan).
/// </summary>
public class MqttClientService : IDisposable
{
    /// <summary>All command slugs that may have a retained <c>homeassistant/button/.../config</c> topic.</summary>
    private static readonly string[] AllKnownCommandSlugs =
    [
        "shutdown", "restart", "sleep", "monitor",
        "refresh", "clearcache", "opensettings", "closesettings", "windowsupdate",
        "powershellcommand", "monitorbrightness", "navigate", "updatesensors"
    ];

    /// <summary>Retired button commands — retained discovery is always cleared on connect.</summary>
    private static readonly string[] LegacyButtonSlugs = ["monitorsleep", "monitorwake"];

    /// <summary>All switch slugs that may have a retained <c>homeassistant/switch/.../config</c> topic.</summary>
    private static readonly string[] AllKnownSwitchSlugs = ["monitor"];

    /// <summary>All sensor slugs that may have a retained <c>homeassistant/sensor/.../config</c> topic.</summary>
    private static readonly string[] AllKnownSensorSlugs = ["battery", "cpu", "memory", "current_url", "release_info", "last_active", "updates_pending"];

    /// <summary>Retired binary sensors — retained discovery is always cleared on connect.</summary>
    private static readonly string[] LegacyBinarySensorSlugs = ["monitor_on"];

    private const int StandardSensorUpdateIntervalSeconds = 30;

    private static readonly Dictionary<string, string> CommandDisplayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["shutdown"] = "Shutdown",
        ["restart"] = "Restart",
        ["sleep"] = "System sleep",
        ["refresh"] = "Refresh kiosk",
        ["clearcache"] = "Clear kiosk cache",
        ["opensettings"] = "Open settings",
        ["closesettings"] = "Close settings",
        ["windowsupdate"] = "Run Windows updates",
        ["powershellcommand"] = "PowerShell command"
    };

    private IMqttClient? _client;
    private MqttClientOptions? _options;
    private AppSettings _settings = new();
    private IKioskHostActions? _host;
    private string _deviceName = "living-room-kiosk";
    private string _devId = "living_room_kiosk";
    private string _prefix = "homeassistant";
    private string _availabilityTopic = "";
    private CancellationTokenSource? _sensorCts;
    private Task? _sensorLoopTask;
    private Task? _lastActiveLoopTask;
    private bool _disposed;
    private bool _intentionalShutdown;
    private bool _messageHandlerAttached;
    private readonly SemaphoreSlim _reconnectGate = new(1, 1);

    private static readonly HashSet<string> PersistedSettingsSlugs = new(StringComparer.OrdinalIgnoreCase)
    {
        "monitor_brightness",
        "brightness_default" // legacy command topic; discovery uses monitor_brightness
    };

    public bool IsConnected => _client?.IsConnected ?? false;

    public event EventHandler? Connected;
    public event EventHandler<string>? Disconnected;
    public event EventHandler<Exception>? Error;

    public async Task ConnectAsync(AppSettings settings, IKioskHostActions? host, CancellationToken ct = default)
    {
        _intentionalShutdown = false;
        _messageHandlerAttached = false;
        _settings = settings;
        _host = host;
        var mqtt = settings.Mqtt;
        _deviceName = MqttDiscovery.NormalizeDeviceDisplayName(mqtt.DeviceName);
        _devId = MqttDiscovery.SanitizeId(_deviceName);
        _prefix = string.IsNullOrWhiteSpace(mqtt.DiscoveryPrefix) ? "homeassistant" : mqtt.DiscoveryPrefix.Trim();
        _availabilityTopic = MqttDiscovery.GetAvailabilityTopic(_prefix, _deviceName);

        var factory = new MqttFactory();
        _client = factory.CreateMqttClient();

        var builder = new MqttClientOptionsBuilder()
            .WithTcpServer(mqtt.Host, mqtt.Port)
            .WithClientId($"HA-WinKiosk_{_devId}_{Environment.MachineName}")
            .WithWillTopic(_availabilityTopic)
            .WithWillPayload(Encoding.UTF8.GetBytes("offline"))
            .WithWillRetain()
            .WithWillQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce);

        var hasCreds = !string.IsNullOrWhiteSpace(mqtt.Username) || !string.IsNullOrEmpty(mqtt.Password);
        if (hasCreds)
            builder.WithCredentials(mqtt.Username?.Trim() ?? "", mqtt.Password ?? "");

        _options = builder.Build();

        _client.ConnectedAsync += OnConnectedAsync;

        _client.DisconnectedAsync += args =>
        {
            Disconnected?.Invoke(this, args.Reason.ToString());
            if (_intentionalShutdown || _disposed)
                return Task.CompletedTask;
            _ = Task.Run(TryReconnectLoopAsync);
            return Task.CompletedTask;
        };

        Exception? connectErr = null;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                await _client.ConnectAsync(_options, ct);
                connectErr = null;
                break;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                connectErr = ex;
                var sec = Math.Min(60, 1 << Math.Min(attempt, 5));
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(sec), ct);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
            }
        }

        if (connectErr != null)
            throw connectErr;

        // First connect: ConnectedAsync may have run before we get here; ensure subscription exists.
        await EnsureCommandSubscriptionAsync(CancellationToken.None);
        StartSensorLoop();
    }

    private async Task OnConnectedAsync(MqttClientConnectedEventArgs _)
    {
        try
        {
            Connected?.Invoke(this, EventArgs.Empty);
            await PublishDiscoveryAndAvailabilityAsync(online: true, CancellationToken.None);
            await PublishPersistedSettingsStatesAsync(CancellationToken.None);
            await EnsureCommandSubscriptionAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            Error?.Invoke(this, ex);
        }
    }

    private async Task EnsureCommandSubscriptionAsync(CancellationToken ct)
    {
        if (_client == null || !_client.IsConnected) return;

        var filter = $"{_prefix}/command/{_devId}/+/set";
        await _client.SubscribeAsync(new MqttTopicFilterBuilder().WithTopic(filter).Build(), ct);
        if (!_messageHandlerAttached)
        {
            _client.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
            _messageHandlerAttached = true;
        }
    }

    private async Task TryReconnectLoopAsync()
    {
        if (!await _reconnectGate.WaitAsync(0))
            return;
        try
        {
            var delaySec = 1;
            while (!_disposed && !_intentionalShutdown && _client != null)
            {
                if (_client.IsConnected)
                    return;

                await Task.Delay(TimeSpan.FromSeconds(Math.Min(60, delaySec)));
                delaySec = Math.Min(60, delaySec * 2);
                if (_disposed || _intentionalShutdown)
                    return;

                try
                {
                    await _client.ConnectAsync(_options!, CancellationToken.None);
                    return;
                }
                catch
                {
                    // keep backing off until success or shutdown
                }
            }
        }
        finally
        {
            try
            {
                _reconnectGate.Release();
            }
            catch
            {
                // ignore
            }
        }
    }

    private void StartSensorLoop()
    {
        _sensorCts?.Cancel();
        _sensorCts?.Dispose();
        _sensorCts = new CancellationTokenSource();
        var token = _sensorCts.Token;

        var enabled = _settings.Sensors.Enabled.Select(s => s.ToLowerInvariant()).ToHashSet();
        var hasLastActive = enabled.Contains("last_active");
        var hasOther = enabled.Any(k => k != "last_active");

        _lastActiveLoopTask = null;
        if (hasLastActive)
        {
            _lastActiveLoopTask = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        if (_client?.IsConnected == true)
                            await PublishSensorStatesAsync(token, lastActiveOnly: true);
                    }
                    catch (Exception ex)
                    {
                        Error?.Invoke(this, ex);
                    }

                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1), token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }, token);
        }

        _sensorLoopTask = null;
        if (hasOther)
        {
            _sensorLoopTask = Task.Run(async () =>
            {
                var interval = TimeSpan.FromSeconds(StandardSensorUpdateIntervalSeconds);
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        if (_client?.IsConnected == true)
                            await PublishSensorStatesAsync(token, lastActiveOnly: false);
                    }
                    catch (Exception ex)
                    {
                        Error?.Invoke(this, ex);
                    }

                    try
                    {
                        await Task.Delay(interval, token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }, token);
        }
    }

    private async Task PublishSensorStatesAsync(CancellationToken ct, bool lastActiveOnly)
    {
        if (_client == null || !_client.IsConnected) return;

        var enabled = _settings.Sensors.Enabled.Select(s => s.ToLowerInvariant()).ToHashSet();
        foreach (var key in enabled)
        {
            if (lastActiveOnly)
            {
                if (key != "last_active") continue;
            }
            else
            {
                if (key == "last_active") continue;
            }

            var objectId = $"{_devId}_{key}";
            var topic = MqttDiscovery.SensorStateTopic(_prefix, objectId);
            var state = key switch
            {
                "battery" => SensorReader.BatteryPercentOrUnavailable(),
                "cpu" => SensorReader.CpuLoadPercent(),
                "memory" => SensorReader.MemoryUsagePercent(),
                "current_url" => _host?.GetCurrentUrl(),
                "release_info" => ReleaseInfo.GetSensorValue(),
                "last_active" => SensorReader.LastActiveSeconds(),
                "updates_pending" => SensorReader.UpdatesPendingCount(),
                _ => null
            };
            if (state == null) continue;

            await _client.PublishAsync(
                new MqttApplicationMessageBuilder().WithTopic(topic).WithPayload(state).Build(),
                ct);
        }
    }

    private async Task PublishPersistedSettingsDiscoveryAsync(CancellationToken ct)
    {
        if (_client == null || !_client.IsConnected) return;

        var list = new List<(string Topic, string Payload)>
        {
            MqttDiscovery.MqttNumber(_prefix, _deviceName, _availabilityTopic, _devId, "monitor_brightness", "Monitor brightness",
                _settings.ScreenBrightness.AllowZeroBrightness ? 0 : 1, 100, 1, "%")
        };

        foreach (var (topic, payload) in list)
            await _client.PublishAsync(new MqttApplicationMessageBuilder().WithTopic(topic).WithPayload(payload).WithRetainFlag().Build(), ct);
    }

    private async Task PublishPersistedSettingsStatesAsync(CancellationToken ct)
    {
        if (_client == null || !_client.IsConnected) return;

        await PublishNumberStateAsync("monitor_brightness",
            ClampBrightnessPercent(_settings.ScreenBrightness.DefaultPercent).ToString(CultureInfo.InvariantCulture), ct);
    }

    private int ClampBrightnessPercent(int pct)
    {
        pct = Math.Clamp(pct, 0, 100);
        if (!_settings.ScreenBrightness.AllowZeroBrightness && pct < 1)
            return 1;
        return pct;
    }

    private async Task PublishNumberStateAsync(string slug, string value, CancellationToken ct)
    {
        if (_client == null || !_client.IsConnected) return;
        var topic = MqttDiscovery.NumberStateTopic(_prefix, _devId, slug);
        await _client.PublishAsync(
            new MqttApplicationMessageBuilder().WithTopic(topic).WithPayload(value).Build(),
            ct);
    }

    private async Task PersistSettingsFromMqttAsync(CancellationToken ct)
    {
        SettingsManager.Save(_settings);
        _settings = SettingsManager.Load();
        await PublishPersistedSettingsStatesAsync(ct);
        try
        {
            _host?.NotifySettingsChangedFromMqtt();
        }
        catch (Exception ex)
        {
            Error?.Invoke(this, ex);
        }
    }

    private async Task HandlePersistedSettingsEntityAsync(string slug, string payload, CancellationToken ct)
    {
        var p = payload.Trim();
        switch (slug.ToLowerInvariant())
        {
            case "monitor_brightness":
            case "brightness_default": // legacy MQTT slug before rename
                if (int.TryParse(p, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pct))
                {
                    pct = ClampBrightnessPercent(pct);
                    _settings.ScreenBrightness.DefaultPercent = pct;
                    try
                    {
                        ScreenBrightnessCommand.Execute(pct);
                    }
                    catch (Exception ex)
                    {
                        Error?.Invoke(this, ex);
                    }

                    await PersistSettingsFromMqttAsync(ct);
                }

                return;
        }
    }

    private async Task PublishDiscoveryAndAvailabilityAsync(bool online, CancellationToken ct)
    {
        if (_client == null || !_client.IsConnected) return;

        var availPayload = online ? "online" : "offline";

        await ClearStaleMqttDiscoveryAsync(ct);

        foreach (var slug in _settings.Commands.Enabled.Select(s => s.ToLowerInvariant()))
        {
            if (slug == "monitor")
            {
                var (switchTopic, switchPayload) = MqttDiscovery.MqttSwitch(
                    _prefix, _deviceName, _availabilityTopic, _devId, slug, "Monitor");
                await _client.PublishAsync(
                    new MqttApplicationMessageBuilder().WithTopic(switchTopic).WithPayload(switchPayload).WithRetainFlag().Build(),
                    ct);
                await PublishMonitorSwitchStateAsync(ct);
                continue;
            }

            // Brightness is a number entity; navigate is mqtt.publish-only (no button entity).
            if (slug is "monitorbrightness" or "navigate")
                continue;
            if (!CommandDisplayNames.TryGetValue(slug, out var title))
                title = slug;
            var (t, p) = MqttDiscovery.GenericButton(_prefix, _deviceName, _availabilityTopic, _devId, slug, title);
            await _client.PublishAsync(new MqttApplicationMessageBuilder().WithTopic(t).WithPayload(p).WithRetainFlag().Build(), ct);
        }

        foreach (var slug in _settings.Sensors.Enabled.Select(s => s.ToLowerInvariant()))
        {
            var (t, p) = slug switch
            {
                "battery" => MqttDiscovery.NumericSensor(_prefix, _deviceName, _availabilityTopic, _devId, slug, "Battery level", "%", "battery"),
                "cpu" => MqttDiscovery.NumericSensor(_prefix, _deviceName, _availabilityTopic, _devId, slug, "CPU usage", "%", null),
                "memory" => MqttDiscovery.NumericSensor(_prefix, _deviceName, _availabilityTopic, _devId, slug, "Memory usage", "%", null),
                "current_url" => MqttDiscovery.StringSensor(_prefix, _deviceName, _availabilityTopic, _devId, slug, "Current URL"),
                "release_info" => MqttDiscovery.StringSensor(_prefix, _deviceName, _availabilityTopic, _devId, slug, "Release info"),
                "last_active" => MqttDiscovery.NumericSensor(_prefix, _deviceName, _availabilityTopic, _devId, slug, "Last Active", "s", null),
                "updates_pending" => MqttDiscovery.NumericSensor(_prefix, _deviceName, _availabilityTopic, _devId, slug, "Windows updates pending", null, null),
                _ => (null, null)!
            };
            if (t == null) continue;
            await _client.PublishAsync(new MqttApplicationMessageBuilder().WithTopic(t).WithPayload(p).WithRetainFlag().Build(), ct);
        }

        if (_settings.Sensors.Enabled.Count > 0)
        {
            var (updateTopic, updatePayload) = MqttDiscovery.GenericButton(
                _prefix, _deviceName, _availabilityTopic, _devId, "updatesensors", "Update sensors");
            await _client.PublishAsync(
                new MqttApplicationMessageBuilder().WithTopic(updateTopic).WithPayload(updatePayload).WithRetainFlag().Build(),
                ct);
        }

        await PublishPersistedSettingsDiscoveryAsync(ct);

        await _client.PublishAsync(
            new MqttApplicationMessageBuilder().WithTopic(_availabilityTopic).WithPayload(availPayload).WithRetainFlag().Build(),
            ct);
    }

    /// <summary>
    /// Remove retained discovery for disabled entities and for features that were removed from the app.
    /// Prevents Home Assistant from keeping orphan configs that can split entities across duplicate devices.
    /// </summary>
    private async Task ClearStaleMqttDiscoveryAsync(CancellationToken ct)
    {
        if (_client == null || !_client.IsConnected) return;

        var enabledCmds = _settings.Commands.Enabled.Select(s => s.ToLowerInvariant()).ToHashSet();
        var enabledSensors = _settings.Sensors.Enabled.Select(s => s.ToLowerInvariant()).ToHashSet();

        foreach (var slug in AllKnownCommandSlugs)
        {
            if (slug == "updatesensors")
            {
                if (enabledSensors.Count > 0) continue;
            }
            else if (enabledCmds.Contains(slug))
            {
                continue;
            }

            var topic = $"{_prefix}/button/{_devId}_{slug}/config";
            await PublishEmptyRetainedConfigAsync(topic, ct);
        }

        foreach (var slug in AllKnownSensorSlugs)
        {
            if (enabledSensors.Contains(slug)) continue;
            var topic = $"{_prefix}/sensor/{_devId}_{slug}/config";
            await PublishEmptyRetainedConfigAsync(topic, ct);
        }

        foreach (var slug in AllKnownSwitchSlugs)
        {
            if (enabledCmds.Contains(slug)) continue;
            var topic = $"{_prefix}/switch/{_devId}_{slug}/config";
            await PublishEmptyRetainedConfigAsync(topic, ct);
        }

        foreach (var slug in LegacyButtonSlugs)
        {
            var topic = $"{_prefix}/button/{_devId}_{slug}/config";
            await PublishEmptyRetainedConfigAsync(topic, ct);
        }

        foreach (var slug in LegacyBinarySensorSlugs)
        {
            var topic = $"{_prefix}/binary_sensor/{_devId}_{slug}/config";
            await PublishEmptyRetainedConfigAsync(topic, ct);
        }

        // Legacy entities no longer published by this app (clear retained configs if present).
        await PublishEmptyRetainedConfigAsync($"{_prefix}/select/{_devId}_orientation_default/config", ct);
        await PublishEmptyRetainedConfigAsync($"{_prefix}/sensor/{_devId}_sessionstate/config", ct);
        await PublishEmptyRetainedConfigAsync($"{_prefix}/number/{_devId}_brightness_default/config", ct);
    }

    private async Task PublishMonitorSwitchStateAsync(CancellationToken ct)
    {
        if (_client == null || !_client.IsConnected) return;

        var topic = MqttDiscovery.SwitchStateTopic(_prefix, $"{_devId}_monitor");
        await _client.PublishAsync(
            new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(MonitorPowerTracker.GetSwitchState())
                .WithRetainFlag()
                .Build(),
            ct);
    }

    private async Task PublishEmptyRetainedConfigAsync(string topic, CancellationToken ct)
    {
        if (_client == null || !_client.IsConnected) return;
        await _client.PublishAsync(
            new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(Array.Empty<byte>())
                .WithRetainFlag()
                .Build(),
            ct);
    }

    private async Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        try
        {
            var topic = e.ApplicationMessage.Topic ?? "";
            var parts = topic.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || parts[^1] != "set")
                return;

            var slug = parts[^2].ToLowerInvariant();

            var payload = e.ApplicationMessage.PayloadSegment.Count > 0
                ? Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment)
                : "";

            if (PersistedSettingsSlugs.Contains(slug))
            {
                await HandlePersistedSettingsEntityAsync(slug, payload, CancellationToken.None);
                return;
            }

            if (slug == "monitor")
            {
                var enabledCmds = _settings.Commands.Enabled.Select(s => s.ToLowerInvariant()).ToHashSet();
                if (!enabledCmds.Contains(slug))
                    return;

                var command = payload.Trim().ToUpperInvariant();
                if (command is "ON" or "1")
                {
                    MonitorWakeCommand.Execute();
                    await PublishMonitorSwitchStateAsync(CancellationToken.None);
                }
                else if (command is "OFF" or "0")
                {
                    MonitorSleepCommand.Execute();
                    await PublishMonitorSwitchStateAsync(CancellationToken.None);
                }

                return;
            }

            if (slug == "updatesensors")
            {
                if (_settings.Sensors.Enabled.Count > 0)
                    await PublishSensorStatesAsync(CancellationToken.None, lastActiveOnly: false);
                return;
            }

            var enabled = _settings.Commands.Enabled.Select(s => s.ToLowerInvariant()).ToHashSet();
            if (!enabled.Contains(slug))
                return;

            switch (slug)
            {
                case "shutdown":
                    ShutdownCommand.Execute();
                    break;
                case "restart":
                    RestartCommand.Execute();
                    break;
                case "sleep":
                    SystemSleepCommand.Execute();
                    break;
                case "refresh":
                    _host?.ReloadWebView();
                    break;
                case "clearcache":
                    if (_host != null)
                    {
                        await _host.ClearBrowsingCacheAsync();
                        _host.ReloadWebView();
                    }
                    break;
                case "opensettings":
                    _host?.OpenSettings();
                    break;
                case "closesettings":
                    _host?.CloseSettings();
                    break;
                case "windowsupdate":
                    WindowsUpdateCommand.Execute(_settings.Kiosk.WindowsUpdateRespectActiveHours);
                    break;
                case "powershellcommand":
                    PowerShellCommand.Execute(_settings.Commands.PowerShellCommand ?? "");
                    break;
                case "navigate":
                    if (_host != null && !string.IsNullOrWhiteSpace(payload))
                        await _host.NavigateHaPathAsync(payload.Trim(), CancellationToken.None);
                    break;
            }
        }
        catch (Exception ex)
        {
            Error?.Invoke(this, ex);
        }
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        _intentionalShutdown = true;
        try
        {
            _sensorCts?.Cancel();
        }
        catch { /* ignore */ }

        if (_client?.IsConnected == true)
        {
            await _client.PublishAsync(
                new MqttApplicationMessageBuilder().WithTopic(_availabilityTopic).WithPayload("offline").WithRetainFlag().Build(),
                ct);
            await _client.DisconnectAsync(new MqttClientDisconnectOptions());
        }
    }

    public async Task PublishGestureMessageAsync(string topicSuffix, CancellationToken ct = default)
    {
        if (_client == null || !_client.IsConnected) return;
        var suffix = MqttDiscovery.SanitizeId(topicSuffix);
        if (string.IsNullOrWhiteSpace(suffix)) return;
        var topic = $"{_prefix}/command/{_devId}/gesture/{suffix}";
        await _client.PublishAsync(
            new MqttApplicationMessageBuilder().WithTopic(topic).WithPayload("ON").Build(),
            ct);
    }

    /// <summary>Manual reconnect: disconnects (if connected) so the same backoff loop as automatic recovery runs, or kicks reconnect if already disconnected.</summary>
    public async Task ReconnectManuallyAsync(CancellationToken ct = default)
    {
        if (_disposed || _client == null) return;
        _intentionalShutdown = false;
        try
        {
            if (_client.IsConnected)
            {
                await _client.PublishAsync(
                    new MqttApplicationMessageBuilder().WithTopic(_availabilityTopic).WithPayload("offline").WithRetainFlag().Build(),
                    ct);
                await _client.DisconnectAsync(new MqttClientDisconnectOptions());
            }
            else
            {
                _ = Task.Run(TryReconnectLoopAsync);
            }
        }
        catch (Exception ex)
        {
            Error?.Invoke(this, ex);
            _ = Task.Run(TryReconnectLoopAsync);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _intentionalShutdown = true;
        try
        {
            _sensorCts?.Cancel();
            _sensorCts?.Dispose();
        }
        catch { /* ignore */ }

        _client?.Dispose();
        _disposed = true;
        try
        {
            _reconnectGate.Dispose();
        }
        catch
        {
            // ignore
        }

        GC.SuppressFinalize(this);
    }
}
