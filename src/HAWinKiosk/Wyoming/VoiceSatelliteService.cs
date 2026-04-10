using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using HAWinKiosk.Mqtt.Models;
using NAudio.CoreAudioApi;
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
    private IWaveIn? _waveIn;
    /// <summary>Holds the MMDevice used by <see cref="WasapiCapture"/> so it is not finalized while recording.</summary>
    private MMDevice? _captureMmDevice;
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
    private int _runGeneration;

    private bool _disposed;
    private volatile bool _micToHa;
    private readonly ConcurrentDictionary<string, double> _refractoryUntil = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Optional UI host (e.g. kiosk overlay). Called from background threads.</summary>
    public IVoiceAssistUiHost? VoiceUi { get; set; }

    private CancellationTokenSource? _sessionEndDelayCts;

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
        int previousGeneration;
        lock (_gate)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _runTask = null;
            previousGeneration = _runGeneration;
        }

        StopHardware(previousGeneration, force: true);

        if (!vs.Enabled || string.IsNullOrWhiteSpace(GetEffectiveWyomingHost()))
            return;

        var cts = new CancellationTokenSource();
        int newGeneration;
        lock (_gate)
        {
            newGeneration = unchecked(previousGeneration + 1);
            _runGeneration = newGeneration;
            _cts = cts;
            _runTask = Task.Run(() => RunAsync(newGeneration, cts.Token), CancellationToken.None);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancelSessionEndDelay();
        int generation;
        lock (_gate)
        {
            _cts?.Cancel();
            generation = _runGeneration;
        }
        try
        {
            _runTask?.Wait(TimeSpan.FromSeconds(8));
        }
        catch
        {
            // ignore
        }
        StopHardware(generation, force: true);
        _cts?.Dispose();
    }

    /// <summary>WASAPI capture by MMDevice ID (same IDs as the Settings list). Empty ID = default capture device.</summary>
    private void CreateMicrophoneCapture()
    {
        var vs = _settings.VoiceAssist;
        var id = (vs.InputDeviceId ?? "").Trim();

        if (!string.IsNullOrEmpty(id))
        {
            MMDevice? mmTry = null;
            try
            {
                using var en = new MMDeviceEnumerator();
                mmTry = en.GetDevice(id);
                var cap = new WasapiCapture(mmTry)
                {
                    WaveFormat = new WaveFormat(MicRate, MicWidth * 8, MicChannels)
                };
                _captureMmDevice = mmTry;
                mmTry = null;
                _waveIn = cap;
                return;
            }
            catch
            {
                mmTry?.Dispose();
            }
            // Invalid or unusable ID: fall through to default WASAPI capture.
        }

        MMDevice? def = null;
        try
        {
            def = WasapiCapture.GetDefaultCaptureDevice();
            var w = new WasapiCapture(def)
            {
                WaveFormat = new WaveFormat(MicRate, MicWidth * 8, MicChannels)
            };
            _captureMmDevice = def;
            def = null;
            _waveIn = w;
        }
        catch
        {
            def?.Dispose();
            throw;
        }
    }

    private async Task RunAsync(int generation, CancellationToken ct)
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
            CreateMicrophoneCapture();
            _waveIn!.DataAvailable += OnMicDataAvailable;
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
            StopHardware(generation);
        }
    }

    private void StopHardware(int generation, bool force = false)
    {
        lock (_gate)
        {
            if (!force && generation != _runGeneration)
                return;
        }

        try
        {
            _listener?.Stop();
        }
        catch
        {
            // ignore
        }
        _listener = null;

        CancelSessionEndDelay();

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

        try
        {
            _captureMmDevice?.Dispose();
        }
        catch
        {
            // ignore
        }

        _captureMmDevice = null;

        _tts?.Dispose();
        _tts = null;

        _wakeSend.Writer.TryComplete();
        _haSend.Writer.TryComplete();

        CloseHaSession();
        CloseWakeSession();
    }

    private void CloseHaSession()
    {
        NetworkStream? s;
        TcpClient? c;
        lock (_gate)
        {
            s = _haStream;
            c = _haClient;
            _haStream = null;
            _haClient = null;
            _micToHa = false;
        }
        DisposeTcpGracefully(s, c);
    }

    private void CloseWakeSession()
    {
        NetworkStream? s;
        TcpClient? c;
        lock (_gate)
        {
            s = _wakeStream;
            c = _wakeClient;
            _wakeStream = null;
            _wakeClient = null;
        }
        DisposeTcpGracefully(s, c);
    }

    /// <summary>
    /// Flush and send FIN before closing so the peer (e.g. wyoming-openwakeword) does not log ConnectionResetError from RST.
    /// </summary>
    private static void DisposeTcpGracefully(NetworkStream? stream, TcpClient? client)
    {
        try
        {
            if (stream != null)
            {
                try { stream.Flush(); } catch { /* ignore */ }
            }
            var socket = client?.Client;
            if (socket != null && socket.Connected)
            {
                try { socket.Shutdown(SocketShutdown.Send); } catch { /* peer already closed */ }
            }
        }
        catch
        {
            // ignore
        }
        try { stream?.Dispose(); } catch { }
        try { client?.Dispose(); } catch { }
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
                CloseWakeSession();
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
        // wyoming-openwakeword: if names is empty/null it only enables DEFAULT_MODEL (ok_nabu). Custom models must be named here.
        var raw = _settings.VoiceAssist.WakeWordNames;
        List<string>? names = null;
        if (raw is { Count: > 0 })
        {
            names = raw
                .Select(s => (s ?? "").Trim())
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (names.Count == 0)
                names = null;
        }

        var detect = WyomingOutgoingEvent.FromDataObject("detect", new Dictionary<string, object?> { ["names"] = names });
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
        CancelSessionEndDelay();
        NotifyVoice(v => v.VoiceAssistSessionStarted());
        return Task.CompletedTask;
    }

    private void CancelSessionEndDelay()
    {
        try
        {
            _sessionEndDelayCts?.Cancel();
        }
        catch
        {
            // ignore
        }

        _sessionEndDelayCts?.Dispose();
        _sessionEndDelayCts = null;
    }

    private void ScheduleSessionEndAfterTts()
    {
        CancelSessionEndDelay();
        _sessionEndDelayCts = new CancellationTokenSource();
        var token = _sessionEndDelayCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(450, token).ConfigureAwait(false);
                NotifyVoice(v => v.VoiceAssistSessionEnded());
            }
            catch (OperationCanceledException)
            {
            }
        }, CancellationToken.None);
    }

    private void NotifyVoice(Action<IVoiceAssistUiHost> action)
    {
        var ui = VoiceUi;
        if (ui == null || !_settings.VoiceAssist.Enabled) return;
        action(ui);
    }

    private async Task HandleHaClientAsync(TcpClient client, CancellationToken ct)
    {
        try
        {
            var stream = client.GetStream();
            TcpClient? prevClient;
            NetworkStream? prevStream;
            lock (_gate)
            {
                prevClient = _haClient;
                prevStream = _haStream;
                _haClient = client;
                _haStream = stream;
            }
            DisposeTcpGracefully(prevStream, prevClient);

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
            bool close;
            lock (_gate)
            {
                close = _haClient == client;
            }
            if (close)
                CloseHaSession();
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
            case "transcript-chunk":
            {
                var chunkText = GetJsonString(ev.Data, "text");
                NotifyVoice(v => v.VoiceTranscriptPartial(chunkText));
                return Task.CompletedTask;
            }
            case "transcript-start":
                return Task.CompletedTask;
            case "transcript-stop":
                return Task.CompletedTask;
            case "audio-start":
                NotifyVoice(v => v.VoiceTtsPlaybackStarted());
                _tts?.BeginUtterance(ev);
                return Task.CompletedTask;
            case "audio-chunk":
                _tts?.AddChunk(ev);
                return Task.CompletedTask;
            case "audio-stop":
                _tts?.EndUtterance();
                _haSend.Writer.TryWrite(WyomingOutgoingEvent.FromDataObject("played", new Dictionary<string, object?>()));
                ScheduleSessionEndAfterTts();
                return Task.CompletedTask;
            case "synthesize-start":
            {
                NotifyVoice(v =>
                {
                    v.VoiceAssistantReplyClear();
                    var t = GetJsonString(ev.Data, "text");
                    if (!string.IsNullOrEmpty(t))
                        v.VoiceAssistantReplyAppend(t);
                });
                return Task.CompletedTask;
            }
            case "synthesize-chunk":
            {
                var t = GetJsonString(ev.Data, "text");
                NotifyVoice(v => v.VoiceAssistantReplyAppend(t));
                return Task.CompletedTask;
            }
            case "synthesize":
            {
                NotifyVoice(v =>
                {
                    v.VoiceAssistantReplyClear();
                    var t = GetJsonString(ev.Data, "text");
                    if (!string.IsNullOrEmpty(t))
                        v.VoiceAssistantReplyAppend(t);
                });
                return Task.CompletedTask;
            }
            case "transcript":
            {
                var t = GetJsonString(ev.Data, "text");
                if (GetJsonBool(ev.Data, "partial") == true)
                {
                    NotifyVoice(v => v.VoiceTranscriptPartial(t));
                    return Task.CompletedTask;
                }

                NotifyVoice(v =>
                {
                    v.VoiceTranscriptFinal(t);
                    v.VoiceProcessing();
                });
                return EndStreamingFromHaAsync(ct);
            }
            case "error":
                CancelSessionEndDelay();
                NotifyVoice(v => v.VoiceAssistSessionEnded());
                return EndStreamingFromHaAsync(ct);
            case "pause-satellite":
                CancelSessionEndDelay();
                NotifyVoice(v => v.VoiceAssistSessionEnded());
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

    private static bool? GetJsonBool(Dictionary<string, JsonElement>? data, string key)
    {
        if (data == null || !data.TryGetValue(key, out var el)) return null;
        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(el.GetString(), out var b) ? b : null,
            _ => null
        };
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
