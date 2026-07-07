using System.Text.Json;
using System.Text.Json.Serialization;

namespace HAWinKiosk.Mqtt;

/// <summary>MQTT Discovery for Phase 2 sensors and command buttons.</summary>
public static partial class MqttDiscovery
{
    private static readonly JsonSerializerOptions JsonDiscovery = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string SensorStateTopic(string prefix, string objectId) => $"{prefix}/sensor/{objectId}/state";

    public static string BinarySensorStateTopic(string prefix, string objectId) => $"{prefix}/binary_sensor/{objectId}/state";

    public static (string Topic, string Payload) GenericButton(
        string prefix,
        string deviceName,
        string availabilityTopic,
        string devId,
        string slug,
        string displayName)
    {
        var objectId = $"{devId}_{slug}";
        var uniqueId = $"ha_winkiosk_{devId}_{Environment.MachineName}_{slug}";
        var commandTopic = $"{prefix}/command/{devId}/{slug}/set";
        var deviceIdentifiers = $"ha_winkiosk_{devId}_{Environment.MachineName}";

        var payload = JsonSerializer.Serialize(new
        {
            name = displayName,
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
        }, JsonDiscovery);

        var topic = $"{prefix}/button/{objectId}/config";
        return (topic, payload);
    }

    public static (string Topic, string Payload) NumericSensor(
        string prefix,
        string deviceName,
        string availabilityTopic,
        string devId,
        string slug,
        string displayName,
        string? unit,
        string? deviceClass)
    {
        var objectId = $"{devId}_{slug}";
        var uniqueId = $"ha_winkiosk_{devId}_{Environment.MachineName}_{slug}";
        var stateTopic = SensorStateTopic(prefix, objectId);
        var deviceIdentifiers = $"ha_winkiosk_{devId}_{Environment.MachineName}";

        var payload = JsonSerializer.Serialize(new
        {
            name = displayName,
            unique_id = uniqueId,
            state_topic = stateTopic,
            availability_topic = availabilityTopic,
            payload_available = "online",
            payload_not_available = "offline",
            unit_of_measurement = unit,
            device_class = deviceClass,
            device = new
            {
                identifiers = new[] { deviceIdentifiers },
                name = deviceName,
                model = "HA WinKiosk",
                manufacturer = "HA WinKiosk"
            }
        }, JsonDiscovery);

        var topic = $"{prefix}/sensor/{objectId}/config";
        return (topic, payload);
    }

    public static (string Topic, string Payload) BinarySensor(
        string prefix,
        string deviceName,
        string availabilityTopic,
        string devId,
        string slug,
        string displayName)
    {
        var objectId = $"{devId}_{slug}";
        var uniqueId = $"ha_winkiosk_{devId}_{Environment.MachineName}_{slug}";
        var stateTopic = BinarySensorStateTopic(prefix, objectId);
        var deviceIdentifiers = $"ha_winkiosk_{devId}_{Environment.MachineName}";

        var payload = JsonSerializer.Serialize(new
        {
            name = displayName,
            unique_id = uniqueId,
            state_topic = stateTopic,
            availability_topic = availabilityTopic,
            payload_available = "online",
            payload_not_available = "offline",
            payload_on = "on",
            payload_off = "off",
            device = new
            {
                identifiers = new[] { deviceIdentifiers },
                name = deviceName,
                model = "HA WinKiosk",
                manufacturer = "HA WinKiosk"
            }
        }, JsonDiscovery);

        var topic = $"{prefix}/binary_sensor/{objectId}/config";
        return (topic, payload);
    }

    public static (string Topic, string Payload) StringSensor(
        string prefix,
        string deviceName,
        string availabilityTopic,
        string devId,
        string slug,
        string displayName)
    {
        var objectId = $"{devId}_{slug}";
        var uniqueId = $"ha_winkiosk_{devId}_{Environment.MachineName}_{slug}";
        var stateTopic = SensorStateTopic(prefix, objectId);
        var deviceIdentifiers = $"ha_winkiosk_{devId}_{Environment.MachineName}";

        var payload = JsonSerializer.Serialize(new
        {
            name = displayName,
            unique_id = uniqueId,
            state_topic = stateTopic,
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
        }, JsonDiscovery);

        var topic = $"{prefix}/sensor/{objectId}/config";
        return (topic, payload);
    }

    public static string SelectStateTopic(string prefix, string devId, string slug) => $"{prefix}/select/{devId}_{slug}/state";

    public static string NumberStateTopic(string prefix, string devId, string slug) => $"{prefix}/number/{devId}_{slug}/state";

    public static (string Topic, string Payload) MqttSelect(
        string prefix,
        string deviceName,
        string availabilityTopic,
        string devId,
        string slug,
        string displayName,
        IReadOnlyList<string> options)
    {
        var objectId = $"{devId}_{slug}";
        var uniqueId = $"ha_winkiosk_{devId}_{Environment.MachineName}_{slug}";
        var commandTopic = $"{prefix}/command/{devId}/{slug}/set";
        var stateTopic = SelectStateTopic(prefix, devId, slug);
        var deviceIdentifiers = $"ha_winkiosk_{devId}_{Environment.MachineName}";

        var payload = JsonSerializer.Serialize(new
        {
            name = displayName,
            unique_id = uniqueId,
            command_topic = commandTopic,
            state_topic = stateTopic,
            options = options,
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
        }, JsonDiscovery);

        var topic = $"{prefix}/select/{objectId}/config";
        return (topic, payload);
    }

    public static (string Topic, string Payload) MqttNumber(
        string prefix,
        string deviceName,
        string availabilityTopic,
        string devId,
        string slug,
        string displayName,
        int min,
        int max,
        int step,
        string unit)
    {
        var objectId = $"{devId}_{slug}";
        var uniqueId = $"ha_winkiosk_{devId}_{Environment.MachineName}_{slug}";
        var commandTopic = $"{prefix}/command/{devId}/{slug}/set";
        var stateTopic = NumberStateTopic(prefix, devId, slug);
        var deviceIdentifiers = $"ha_winkiosk_{devId}_{Environment.MachineName}";

        var payload = JsonSerializer.Serialize(new
        {
            name = displayName,
            unique_id = uniqueId,
            command_topic = commandTopic,
            state_topic = stateTopic,
            min,
            max,
            step,
            unit_of_measurement = unit,
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
        }, JsonDiscovery);

        var topic = $"{prefix}/number/{objectId}/config";
        return (topic, payload);
    }
}
