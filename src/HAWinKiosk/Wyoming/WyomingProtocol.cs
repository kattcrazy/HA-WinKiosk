using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace HAWinKiosk.Wyoming;

/// <summary>
/// Wyoming protocol (rhasspy/wyoming): one JSON line per event, optional <c>data_length</c> JSON merge, optional binary <c>payload</c>.
/// Matches wyoming 1.7.x used by Home Assistant.
/// </summary>
internal static class WyomingProtocol
{
    public const string Version = "1.7.2";

    public static async Task<WyomingIncomingEvent?> ReadEventAsync(NetworkStream stream, CancellationToken ct)
    {
        var line = await ReadLineUtf8Async(stream, ct).ConfigureAwait(false);
        if (line.Length == 0) return null;

        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;
        if (!root.TryGetProperty("type", out var typeEl)) return null;
        var type = typeEl.GetString() ?? "";

        Dictionary<string, JsonElement>? data = null;
        if (root.TryGetProperty("data_length", out var dl) && dl.TryGetInt32(out var dataLen) && dataLen > 0)
        {
            var extra = await ReadExactlyAsync(stream, dataLen, ct).ConfigureAwait(false);
            using var dataDoc = JsonDocument.Parse(extra);
            data = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            if (root.TryGetProperty("data", out var inline) && inline.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in inline.EnumerateObject())
                    data[p.Name] = p.Value.Clone();
            }
            foreach (var p in dataDoc.RootElement.EnumerateObject())
                data[p.Name] = p.Value.Clone();
        }
        else if (root.TryGetProperty("data", out var inlineOnly) && inlineOnly.ValueKind == JsonValueKind.Object)
        {
            data = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var p in inlineOnly.EnumerateObject())
                data[p.Name] = p.Value.Clone();
        }

        byte[]? payload = null;
        if (root.TryGetProperty("payload_length", out var pl) && pl.TryGetInt32(out var payloadLen) && payloadLen > 0)
            payload = await ReadExactlyAsync(stream, payloadLen, ct).ConfigureAwait(false);

        return new WyomingIncomingEvent(type, data, payload);
    }

    public static async Task WriteEventAsync(NetworkStream stream, WyomingOutgoingEvent ev, CancellationToken ct)
    {
        var dict = new Dictionary<string, object?>
        {
            ["type"] = ev.Type,
            ["version"] = Version
        };

        byte[]? dataBytes = ev.DataJson;
        if (dataBytes is { Length: > 0 })
            dict["data_length"] = dataBytes.Length;

        if (ev.Payload is { Length: > 0 })
            dict["payload_length"] = ev.Payload.Length;

        var header = JsonSerializer.SerializeToUtf8Bytes(dict);
        await stream.WriteAsync(header, ct).ConfigureAwait(false);
        await stream.WriteAsync(Encoding.UTF8.GetBytes("\n"), ct).ConfigureAwait(false);

        if (dataBytes is { Length: > 0 })
            await stream.WriteAsync(dataBytes, ct).ConfigureAwait(false);
        if (ev.Payload is { Length: > 0 })
            await stream.WriteAsync(ev.Payload, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    private static async Task<string> ReadLineUtf8Async(NetworkStream stream, CancellationToken ct)
    {
        var ms = new MemoryStream(512);
        var one = new byte[1];
        while (true)
        {
            var n = await stream.ReadAsync(one.AsMemory(0, 1), ct).ConfigureAwait(false);
            if (n == 0) break;
            if (one[0] == (byte)'\n') break;
            ms.WriteByte(one[0]);
        }
        return Encoding.UTF8.GetString(ms.GetBuffer().AsSpan(0, (int)ms.Length));
    }

    private static async Task<byte[]> ReadExactlyAsync(NetworkStream stream, int len, CancellationToken ct)
    {
        var buf = new byte[len];
        var o = 0;
        while (o < len)
        {
            var n = await stream.ReadAsync(buf.AsMemory(o, len - o), ct).ConfigureAwait(false);
            if (n == 0) throw new IOException("Unexpected end of stream");
            o += n;
        }
        return buf;
    }
}

internal sealed class WyomingIncomingEvent
{
    public WyomingIncomingEvent(string type, Dictionary<string, JsonElement>? data, byte[]? payload)
    {
        Type = type;
        Data = data;
        Payload = payload;
    }

    public string Type { get; }
    public Dictionary<string, JsonElement>? Data { get; }
    public byte[]? Payload { get; }
}

internal sealed class WyomingOutgoingEvent
{
    public WyomingOutgoingEvent(string type, byte[]? dataJson, byte[]? payload)
    {
        Type = type;
        DataJson = dataJson;
        Payload = payload;
    }

    public string Type { get; }
    public byte[]? DataJson { get; }
    public byte[]? Payload { get; }

    public static WyomingOutgoingEvent FromDataObject(string type, object data)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(data);
        return new WyomingOutgoingEvent(type, json, null);
    }
}
