using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HAWinKiosk.Mqtt;

/// <summary>
/// MQTT Discovery payloads for Home Assistant.
/// All entities are grouped under one device (deviceName).
/// </summary>
public static partial class MqttDiscovery
{
    /// <summary>
    /// Sanitize for object_id / unique_id: lowercase, alphanumeric and underscore only.
    /// </summary>
    public static string SanitizeId(string value)
    {
        if (string.IsNullOrEmpty(value)) return "unknown";
        var s = value.ToLowerInvariant();
        s = Regex.Replace(s, @"[^a-z0-9]+", "_");
        return s.Trim('_');
    }

    public static (string Topic, string Payload) MonitorSleepButton(string prefix, string deviceName, string availabilityTopic)
    {
        var devId = SanitizeId(deviceName);
        var objectId = $"{devId}_monitorsleep";
        var uniqueId = $"ha_winkiosk_{devId}_{Environment.MachineName}_monitorsleep";
        var commandTopic = $"{prefix}/command/{devId}/monitorsleep/set";
        var deviceIdentifiers = $"ha_winkiosk_{devId}_{Environment.MachineName}";

        var payload = JsonSerializer.Serialize(new
        {
            name = "Monitor Sleep",
            unique_id = uniqueId,
            command_topic = commandTopic,
            availability_topic = availabilityTopic,
            payload_available = "online",
            payload_not_available = "offline",
            device = new
            {
                identifiers = new[] { deviceIdentifiers },
                name = deviceName,
                model = "HA WinKiosk",
                manufacturer = "HA WinKiosk"
            }
        });

        var topic = $"{prefix}/button/{objectId}/config";
        return (topic, payload);
    }

    public static (string Topic, string Payload) MonitorWakeButton(string prefix, string deviceName, string availabilityTopic)
    {
        var devId = SanitizeId(deviceName);
        var objectId = $"{devId}_monitorwake";
        var uniqueId = $"ha_winkiosk_{devId}_{Environment.MachineName}_monitorwake";
        var commandTopic = $"{prefix}/command/{devId}/monitorwake/set";
        var deviceIdentifiers = $"ha_winkiosk_{devId}_{Environment.MachineName}";

        var payload = JsonSerializer.Serialize(new
        {
            name = "Monitor Wake",
            unique_id = uniqueId,
            command_topic = commandTopic,
            availability_topic = availabilityTopic,
            payload_available = "online",
            payload_not_available = "offline",
            device = new
            {
                identifiers = new[] { deviceIdentifiers },
                name = deviceName,
                model = "HA WinKiosk",
                manufacturer = "HA WinKiosk"
            }
        });

        var topic = $"{prefix}/button/{objectId}/config";
        return (topic, payload);
    }

    public static string GetAvailabilityTopic(string prefix, string deviceName)
    {
        var devId = SanitizeId(deviceName);
        return $"{prefix}/command/{devId}/availability";
    }
}
