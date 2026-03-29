using System.Globalization;
using System.Text;
using HAWinKiosk.Mqtt.Commands;
using HAWinKiosk.Mqtt.Models;
using MQTTnet;
using MQTTnet.Client;

namespace HAWinKiosk.Mqtt;

/// <summary>
/// MQTT connect, discovery, command subscription, and sensor publish loop. For HASS.Agent–style discovery
/// and command routing, compare with <c>HASS-AGENT-2-REFERENCE/…/Mqtt/IMqttManager.cs</c> and related Home Assistant models
/// (not a port; our topic layout follows the HA WinKiosk plan).
/// </summary>
public class MqttClientService : IDisposable
{
    /// <summary>All command slugs that may have a retained <c>homeassistant/button/.../config</c> topic.</summary>
    private static readonly string[] AllKnownCommandSlugs =
    [
        "shutdown", "restart", "sleep", "monitorsleep", "monitorwake",
        "refresh", "clearcache", "opensettings", "closesettings", "windowsupdate",
        "powershellcommand", "monitorbrightness"
    ];

    /// <summary>All sensor slugs that may have a retained <c>homeassistant/sensor/.../config</c> topic.</summary>
    private static readonly string[] AllKnownSensorSlugs = ["battery", "last_active", "updates_pending"];

    private static readonly Dictionary<string, string> CommandDisplayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["shutdown"] = "Shutdown",
        ["restart"] = "Restart",
        ["sleep"] = "System sleep",
        ["monitorsleep"] = "Monitor sleep",
        ["monitorwake"] = "Monitor wake",
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

    private static readonly HashSet<string> PersistedSettingsSlugs = new(StringComparer.OrdinalIgnoreCase)
    {
        "brightness_default"
    };

    public bool IsConnected => _client?.IsConnected ?? false;

    public event EventHandler? Connected;
    public event EventHandler<string>? Disconnected;
    public event EventHandler<Exception>? Error;

    public async Task ConnectAsync(AppSettings settings, IKioskHostActions? host, CancellationToken ct = default)
    {
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
            .WithClientId($"HA-WinKiosk_{_devId}_{Environment.MachineName}");

        var hasCreds = !string.IsNullOrWhiteSpace(mqtt.Username) || !string.IsNullOrEmpty(mqtt.Password);
        if (hasCreds)
            builder.WithCredentials(mqtt.Username?.Trim() ?? "", mqtt.Password ?? "");

        _options = builder.Build();

        _client.ConnectedAsync += async _ =>
        {
            Connected?.Invoke(this, EventArgs.Empty);
            await PublishDiscoveryAndAvailabilityAsync(online: true, ct);
            await PublishPersistedSettingsStatesAsync(ct);
        };

        _client.DisconnectedAsync += args =>
        {
            Disconnected?.Invoke(this, args.Reason.ToString());
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

        await SubscribeCommandsAsync(ct);
        StartSensorLoop();
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
                var interval = TimeSpan.FromSeconds(Math.Max(5, _settings.Sensors.UpdateIntervalSeconds));
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
            MqttDiscovery.MqttNumber(_prefix, _deviceName, _availabilityTopic, _devId, "brightness_default", "Monitor brightness",
                0, 100, 1, "%")
        };

        foreach (var (topic, payload) in list)
            await _client.PublishAsync(new MqttApplicationMessageBuilder().WithTopic(topic).WithPayload(payload).WithRetainFlag().Build(), ct);
    }

    private async Task PublishPersistedSettingsStatesAsync(CancellationToken ct)
    {
        if (_client == null || !_client.IsConnected) return;

        await PublishNumberStateAsync("brightness_default", Math.Clamp(_settings.ScreenBrightness.DefaultPercent, 0, 100).ToString(CultureInfo.InvariantCulture), ct);
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
            case "brightness_default":
                if (int.TryParse(p, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pct))
                {
                    pct = Math.Clamp(pct, 0, 100);
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
            // Brightness is controlled via a number entity only (not duplicate buttons).
            if (slug is "monitorbrightness")
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
                "last_active" => MqttDiscovery.NumericSensor(_prefix, _deviceName, _availabilityTopic, _devId, slug, "Last Active", "s", null),
                "updates_pending" => MqttDiscovery.NumericSensor(_prefix, _deviceName, _availabilityTopic, _devId, slug, "Updates pending", null, null),
                _ => (null, null)!
            };
            if (t == null) continue;
            await _client.PublishAsync(new MqttApplicationMessageBuilder().WithTopic(t).WithPayload(p).WithRetainFlag().Build(), ct);
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
        foreach (var slug in AllKnownCommandSlugs)
        {
            if (enabledCmds.Contains(slug)) continue;
            var topic = $"{_prefix}/button/{_devId}_{slug}/config";
            await PublishEmptyRetainedConfigAsync(topic, ct);
        }

        var enabledSensors = _settings.Sensors.Enabled.Select(s => s.ToLowerInvariant()).ToHashSet();
        foreach (var slug in AllKnownSensorSlugs)
        {
            if (enabledSensors.Contains(slug)) continue;
            var topic = $"{_prefix}/sensor/{_devId}_{slug}/config";
            await PublishEmptyRetainedConfigAsync(topic, ct);
        }

        // Legacy entities no longer published by this app (clear retained configs if present).
        await PublishEmptyRetainedConfigAsync($"{_prefix}/select/{_devId}_orientation_default/config", ct);
        await PublishEmptyRetainedConfigAsync($"{_prefix}/sensor/{_devId}_sessionstate/config", ct);
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

    private async Task SubscribeCommandsAsync(CancellationToken ct)
    {
        if (_client == null || !_client.IsConnected) return;

        var filter = $"{_prefix}/command/{_devId}/+/set";
        await _client.SubscribeAsync(new MqttTopicFilterBuilder().WithTopic(filter).Build(), ct);
        _client.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
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
                case "monitorsleep":
                    MonitorSleepCommand.Execute();
                    break;
                case "monitorwake":
                    MonitorWakeCommand.Execute();
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
                    WindowsUpdateCommand.Execute();
                    break;
                case "powershellcommand":
                    PowerShellCommand.Execute(_settings.Commands.PowerShellCommand ?? "");
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

    public void Dispose()
    {
        if (_disposed) return;
        try
        {
            _sensorCts?.Cancel();
            _sensorCts?.Dispose();
        }
        catch { /* ignore */ }

        _client?.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
