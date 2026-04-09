using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using System.Threading.Channels;
using HAWinKiosk.Mqtt.Models;
using NAudio.Wave;

namespace HAWinKiosk.Wyoming;

/// <summary>
/// Wyoming satellite: TCP server for Home Assistant, outbound connection to openWakeWord, Windows mic capture, TTS playback.
/// Mirrors rhasspy/wyoming-satellite WakeStreamingSatellite behavior (remote wake service).
/// </summary>
public sealed class VoiceSatelliteService : IDisposable
{
    /// <summary>Fixed TCP bind for Home Assistant Wyoming integration (this PC).</summary>
    private const string SatelliteListenHost = "0.0.0.0";

    private const int SatelliteListenPort = 10700;

    private const int MicRate = 16000;
    private const int MicWidth = 2;
    private const int MicChannels = 1;

    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private Task? _runTask;

    private AppSettings _settings = new();

    private TcpListener? _listener;
    private WaveInEvent? _waveIn;
    private TtsAudioPlayer? _tts;

    private TcpClient? _wakeClient;
    private NetworkStream? _wakeStream;

    private TcpClient? _haClient;
    private NetworkStream? _haStream;

    private Channel<WyomingOutgoingEvent> _wakeSend = Channel.CreateUnbounded<WyomingOutgoingEvent>();
    private Channel<WyomingOutgoingEvent> _haSend = Channel.CreateUnbounded<WyomingOutgoingEvent>();

    private Task? _wakeWritePumpTask;
    private Task? _haWritePumpTask;
    private Task? _wakeReadTask;

    private bool _disposed;
    private volatile bool _micToHa;
    private readonly ConcurrentDictionary<string, double> _refractoryUntil = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Configured Wyoming host, or a literal IP taken from the kiosk URL when blank.</summary>
    private string? GetEffectiveWyomingHost()
    {
        var raw = (_settings.VoiceAssist.WyomingHostPc ?? "").Trim();
        if (raw.Length > 0)
            return raw;
        return KioskUrlLiteralHost.TryGetFromUrl(_settings.Kiosk.Url, out var ip) ? ip : null;
    }

    public void SyncSettings(AppSettings settings)
    {
        _settings = settings;
        var vs = settings.VoiceAssist;
        lock (_gate)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _runTask = null;
        }

        StopHardware();

        if (!vs.Enabled || string.IsNullOrWhiteSpace(GetEffectiveWyomingHost()))
            return;

        var cts = new CancellationTokenSource();
        lock (_gate)
        {
            _cts = cts;
            _runTask = Task.Run(() => RunAsync(cts.Token), CancellationToken.None);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_gate)
        {
            _cts?.Cancel();
        }
        try
        {
            _runTask?.Wait(TimeSpan.FromSeconds(8));
        }
        catch
        {
            // ignore
        }
        StopHardware();
        _cts?.Dispose();
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            _wakeSend = Channel.CreateUnbounded<WyomingOutgoingEvent>();
            _haSend = Channel.CreateUnbounded<WyomingOutgoingEvent>();
            _wakeWritePumpTask = WakeWritePumpAsync(_wakeSend, ct);
            _haWritePumpTask = HaWritePumpAsync(_haSend, ct);

            if (!IPAddress.TryParse(SatelliteListenHost, out var bindIp))
                bindIp = IPAddress.Any;
            _listener = new TcpListener(bindIp, SatelliteListenPort);
            _listener.Start();

            _tts = new TtsAudioPlayer();
            _waveIn = new WaveInEvent
            {
                WaveFormat = new WaveFormat(MicRate, MicWidth * 8, MicChannels),
                BufferMilliseconds = 50,
                NumberOfBuffers = 3
            };
            _waveIn.DataAvailable += OnMicDataAvailable;
            _waveIn.StartRecording();

            _wakeReadTask = WakeReadLoopAsync(ct);

            while (!ct.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                _ = Task.Run(() => HandleHaClientAsync(client, ct), ct);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // best-effort voice path
        }
        finally
        {
            StopHardware();
        }
    }

    private void StopHardware()
    {
        try
        {
            _listener?.Stop();
        }
        catch
        {
            // ignore
        }
        _listener = null;

        if (_waveIn != null)
        {
            try
            {
                _waveIn.DataAvailable -= OnMicDataAvailable;
                _waveIn.StopRecording();
                _waveIn.Dispose();
            }
            catch
            {
                // ignore
            }
            _waveIn = null;
        }

        _tts?.Dispose();
        _tts = null;

        _wakeSend.Writer.TryComplete();
        _haSend.Writer.TryComplete();

        CloseHaSession();
        CloseWakeSession();
    }

    private void CloseHaSession()
    {
        try
        {
            _haStream?.Dispose();
            _haClient?.Dispose();
        }
        catch
        {
            // ignore
        }
        _haStream = null;
        _haClient = null;
        _micToHa = false;
    }

    private void CloseWakeSession()
    {
        try
        {
            _wakeStream?.Dispose();
            _wakeClient?.Dispose();
        }
        catch
        {
            // ignore
        }
        _wakeStream = null;
        _wakeClient = null;
    }

    private void OnMicDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0) return;
        var pcm = new byte[e.BytesRecorded];
        Buffer.BlockCopy(e.Buffer, 0, pcm, 0, e.BytesRecorded);
        var chunk = AudioChunkEvent(MicRate, MicWidth, MicChannels, pcm);
        if (_micToHa)
        {
            _haSend.Writer.TryWrite(chunk);
        }
        else
        {
            _wakeSend.Writer.TryWrite(chunk);
        }
    }

    private static WyomingOutgoingEvent AudioChunkEvent(int rate, int width, int channels, byte[] pcm)
    {
        var data = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object?>
        {
            ["rate"] = rate,
            ["width"] = width,
            ["channels"] = channels,
            ["timestamp"] = null
        });
        return new WyomingOutgoingEvent("audio-chunk", data, pcm);
    }

    private async Task WakeWritePumpAsync(Channel<WyomingOutgoingEvent> ch, CancellationToken ct)
    {
        try
        {
            await foreach (var ev in ch.Reader.ReadAllAsync(ct))
            {
                NetworkStream? s;
                lock (_gate)
                    s = _wakeStream;
                if (s == null) continue;
                try
                {
                    await WyomingProtocol.WriteEventAsync(s, ev, ct).ConfigureAwait(false);
                }
                catch
                {
                    // wake reconnect
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // ignore
        }
    }

    private async Task HaWritePumpAsync(Channel<WyomingOutgoingEvent> ch, CancellationToken ct)
    {
        try
        {
            await foreach (var ev in ch.Reader.ReadAllAsync(ct))
            {
                NetworkStream? s;
                lock (_gate)
                    s = _haStream;
                if (s == null) continue;
                try
                {
                    await WyomingProtocol.WriteEventAsync(s, ev, ct).ConfigureAwait(false);
                }
                catch
                {
                    // HA disconnected
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // ignore
        }
    }

    private async Task WakeReadLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var vs = _settings.VoiceAssist;
                var wakeHost = GetEffectiveWyomingHost();
                if (string.IsNullOrWhiteSpace(wakeHost))
                {
                    await Task.Delay(500, ct).ConfigureAwait(false);
                    continue;
                }

                var client = new TcpClient();
                await client.ConnectAsync(wakeHost, vs.WyomingHostPcPort, ct).ConfigureAwait(false);
                var stream = client.GetStream();
                lock (_gate)
                {
                    _wakeClient = client;
                    _wakeStream = stream;
                }
                await SendWakeDetectAsync(ct).ConfigureAwait(false);
                while (!ct.IsCancellationRequested)
                {
                    var ev = await WyomingProtocol.ReadEventAsync(stream, ct).ConfigureAwait(false);
                    if (ev == null) break;
                    if (ev.Type == "detection")
                        await HandleWakeDetectionAsync(ev, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // reconnect
            }
            finally
            {
                lock (_gate)
                {
                    CloseWakeSession();
                }
            }
            try
            {
                await Task.Delay(2000, ct).ConfigureAwait(false);
            }
            catch
            {
                break;
            }
        }
    }

    private Task SendWakeDetectAsync(CancellationToken ct)
    {
        var detect = WyomingOutgoingEvent.FromDataObject("detect", new Dictionary<string, object?> { ["names"] = null });
        var audioStart = WyomingOutgoingEvent.FromDataObject("audio-start", new Dictionary<string, object?>
        {
            ["rate"] = MicRate,
            ["width"] = MicWidth,
            ["channels"] = MicChannels,
            ["timestamp"] = null
        });
        _wakeSend.Writer.TryWrite(detect);
        _wakeSend.Writer.TryWrite(audioStart);
        return Task.CompletedTask;
    }

    private Task HandleWakeDetectionAsync(WyomingIncomingEvent ev, CancellationToken ct)
    {
        lock (_gate)
        {
            if (_haStream == null) return Task.CompletedTask;
        }

        var name = GetJsonString(ev.Data, "name") ?? "unknown";
        var vs = _settings.VoiceAssist;
        var now = Environment.TickCount64 / 1000.0;
        if (_refractoryUntil.TryGetValue(name, out var until) && until > now)
            return Task.CompletedTask;
        if (vs.WakeWordDelay > 0)
            _refractoryUntil[name] = now + vs.WakeWordDelay;

        var detectionPayload = new Dictionary<string, object?>
        {
            ["name"] = name,
            ["timestamp"] = GetJsonInt(ev.Data, "timestamp")
        };
        _haSend.Writer.TryWrite(WyomingOutgoingEvent.FromDataObject("detection", detectionPayload));
        _haSend.Writer.TryWrite(WyomingOutgoingEvent.FromDataObject("run-pipeline", new Dictionary<string, object?>
        {
            ["start_stage"] = "asr",
            ["end_stage"] = "tts",
            ["restart_on_end"] = true,
            ["wake_word_name"] = name
        }));
        _haSend.Writer.TryWrite(WyomingOutgoingEvent.FromDataObject("streaming-started", new Dictionary<string, object?>()));
        _micToHa = true;
        return Task.CompletedTask;
    }

    private async Task HandleHaClientAsync(TcpClient client, CancellationToken ct)
    {
        try
        {
            var stream = client.GetStream();
            lock (_gate)
            {
                try
                {
                    _haStream?.Dispose();
                    _haClient?.Dispose();
                }
                catch
                {
                    // ignore
                }
                _haClient = client;
                _haStream = stream;
            }

            while (!ct.IsCancellationRequested)
            {
                var ev = await WyomingProtocol.ReadEventAsync(stream, ct).ConfigureAwait(false);
                if (ev == null) break;
                await HandleHaEventAsync(ev, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // ignore
        }
        finally
        {
            lock (_gate)
            {
                if (_haClient == client)
                    CloseHaSession();
            }
        }
    }

    private Task HandleHaEventAsync(WyomingIncomingEvent ev, CancellationToken ct)
    {
        switch (ev.Type)
        {
            case "describe":
            {
                var asm = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0";
                var name = (_settings.Mqtt.DeviceName ?? "").Trim();
                if (name.Length == 0) name = "HA WinKiosk";
                var info = WyomingOutgoingEvent.FromDataObject("info", new Dictionary<string, object?>
                {
                    ["asr"] = Array.Empty<object>(),
                    ["tts"] = Array.Empty<object>(),
                    ["handle"] = Array.Empty<object>(),
                    ["intent"] = Array.Empty<object>(),
                    ["wake"] = Array.Empty<object>(),
                    ["mic"] = Array.Empty<object>(),
                    ["snd"] = Array.Empty<object>(),
                    ["satellite"] = new Dictionary<string, object?>
                    {
                        ["name"] = name,
                        ["description"] = "HA WinKiosk Wyoming satellite",
                        ["installed"] = true,
                        ["version"] = asm,
                        ["attribution"] = new Dictionary<string, object?> { ["name"] = "HA WinKiosk", ["url"] = "https://github.com/kattcrazy/HA-WinKiosk" },
                        ["area"] = null,
                        ["has_vad"] = false
                    }
                });
                _haSend.Writer.TryWrite(info);
                return Task.CompletedTask;
            }
            case "ping":
            {
                var text = GetJsonString(ev.Data, "text");
                var pong = WyomingOutgoingEvent.FromDataObject("pong", new Dictionary<string, object?> { ["text"] = text });
                _haSend.Writer.TryWrite(pong);
                return Task.CompletedTask;
            }
            case "pong":
                return Task.CompletedTask;
            case "audio-start":
                _tts?.BeginUtterance(ev);
                return Task.CompletedTask;
            case "audio-chunk":
                _tts?.AddChunk(ev);
                return Task.CompletedTask;
            case "audio-stop":
                _tts?.EndUtterance();
                _haSend.Writer.TryWrite(WyomingOutgoingEvent.FromDataObject("played", new Dictionary<string, object?>()));
                return Task.CompletedTask;
            case "transcript":
                return EndStreamingFromHaAsync(ct);
            case "error":
                return EndStreamingFromHaAsync(ct);
            case "pause-satellite":
                return EndStreamingFromHaAsync(ct);
            default:
                return Task.CompletedTask;
        }
    }

    private async Task EndStreamingFromHaAsync(CancellationToken ct)
    {
        _micToHa = false;
        _haSend.Writer.TryWrite(WyomingOutgoingEvent.FromDataObject("streaming-stopped", new Dictionary<string, object?>()));
        await SendWakeDetectAsync(ct).ConfigureAwait(false);
    }

    private static string? GetJsonString(Dictionary<string, JsonElement>? data, string key)
    {
        if (data == null || !data.TryGetValue(key, out var el)) return null;
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Null => null,
            _ => el.GetRawText().Trim('"')
        };
    }

    private static object? GetJsonInt(Dictionary<string, JsonElement>? data, string key)
    {
        if (data == null || !data.TryGetValue(key, out var el)) return null;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out var n)) return n;
        return null;
    }

    private sealed class TtsAudioPlayer : IDisposable
    {
        private WaveOutEvent? _out;
        private BufferedWaveProvider? _buf;

        public void BeginUtterance(WyomingIncomingEvent ev)
        {
            var rate = GetInt(ev.Data, "rate") ?? 22050;
            var width = GetInt(ev.Data, "width") ?? 2;
            var ch = GetInt(ev.Data, "channels") ?? 1;
            var fmt = new WaveFormat(rate, width * 8, ch);
            _out?.Dispose();
            _buf = new BufferedWaveProvider(fmt) { BufferLength = 1024 * 1024 };
            _out = new WaveOutEvent();
            _out.Init(_buf);
            _out.Play();
        }

        public void AddChunk(WyomingIncomingEvent ev)
        {
            if (_buf == null || ev.Payload == null || ev.Payload.Length == 0) return;
            _buf.AddSamples(ev.Payload, 0, ev.Payload.Length);
        }

        public void EndUtterance()
        {
            try
            {
                _out?.Stop();
            }
            catch
            {
                // ignore
            }
        }

        public void Dispose()
        {
            try
            {
                _out?.Dispose();
            }
            catch
            {
                // ignore
            }
            _out = null;
            _buf = null;
        }

        private static int? GetInt(Dictionary<string, JsonElement>? d, string k)
        {
            if (d == null || !d.TryGetValue(k, out var el)) return null;
            return el.TryGetInt32(out var v) ? v : null;
        }
    }
}
