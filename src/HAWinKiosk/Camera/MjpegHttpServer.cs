using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace HAWinKiosk.Camera;

/// <summary>HTTP MJPEG server (multipart/x-mixed-replace), same shape as HA-AndroidDoorbell.</summary>
public sealed class MjpegHttpServer : IDisposable
{
    private const int MaxClients = 4;
    private static readonly byte[] CrLf = "\r\n"u8.ToArray();
    private readonly ConcurrentDictionary<int, ClientSession> _clients = new();
    private readonly object _frameGate = new();
    private byte[]? _latestJpeg;
    private int _nextClientId;
    private int _activeClients;
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;
    private TcpListener? _listener;
    private int _port;

    public int Port => _port;

    public void UpdateFrame(byte[] jpeg)
    {
        lock (_frameGate)
            _latestJpeg = jpeg;
    }

    public void Start(int port)
    {
        Stop();
        _port = port;

        // Bind on the calling thread so port conflicts fail immediately instead of silently.
        var listener = new TcpListener(IPAddress.Any, port);
        listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        listener.Start();
        _listener = listener;

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _acceptTask = Task.Run(() => AcceptLoop(listener, token), token);
    }

    public void Stop()
    {
        try
        {
            _cts?.Cancel();
        }
        catch
        {
            // ignore
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

        foreach (var kv in _clients)
        {
            try { kv.Value.Socket.Close(); } catch { /* ignore */ }
        }

        _clients.Clear();
        Interlocked.Exchange(ref _activeClients, 0);

        try
        {
            _acceptTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // ignore
        }

        _cts?.Dispose();
        _cts = null;
        _acceptTask = null;
    }

    public void Dispose() => Stop();

    private void AcceptLoop(TcpListener listener, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                if (!listener.Pending())
                {
                    Thread.Sleep(50);
                    continue;
                }

                Socket client;
                try
                {
                    client = listener.AcceptSocket();
                }
                catch when (token.IsCancellationRequested)
                {
                    break;
                }
                catch
                {
                    continue;
                }

                if (Volatile.Read(ref _activeClients) >= MaxClients)
                {
                    try { client.Close(); } catch { /* ignore */ }
                    continue;
                }

                client.NoDelay = true;
                client.SendTimeout = 2000;
                client.ReceiveTimeout = 2000;
                client.Blocking = true;

                var id = Interlocked.Increment(ref _nextClientId);
                var session = new ClientSession(id, client);
                if (!_clients.TryAdd(id, session))
                {
                    try { client.Close(); } catch { /* ignore */ }
                    continue;
                }

                Interlocked.Increment(ref _activeClients);
                _ = Task.Run(() => Serve(session, token), token);
            }
        }
        catch (Exception) when (token.IsCancellationRequested)
        {
            // shutting down
        }
        catch
        {
            // accept failure
        }
        finally
        {
            try { listener.Stop(); } catch { /* ignore */ }
        }
    }

    private void Serve(ClientSession session, CancellationToken token)
    {
        var socket = session.Socket;
        try
        {
            var request = ReadRequestHead(socket);
            var path = ParseRequestPath(request);

            using var stream = new NetworkStream(socket, ownsSocket: false);

            // HTML preview is safe for WebView2 top-level navigation; raw MJPEG is for NVRs / <img>.
            if (path is "/view" or "/view/" or "/preview" or "/preview/")
            {
                ServeHtmlPreview(stream);
                return;
            }

            ServeMjpeg(stream, token, socket);
        }
        catch
        {
            // client disconnected or write timed out
        }
        finally
        {
            _clients.TryRemove(session.Id, out _);
            Interlocked.Decrement(ref _activeClients);
            try { socket.Close(); } catch { /* ignore */ }
        }
    }

    private static void ServeHtmlPreview(NetworkStream stream)
    {
        const string html =
            "<!DOCTYPE html><html><head><meta charset=\"utf-8\"/>" +
            "<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"/>" +
            "<title>Camera</title>" +
            "<style>html,body{margin:0;height:100%;background:#000;}" +
            "img{display:block;width:100%;height:100%;object-fit:contain}</style></head>" +
            "<body><img src=\"/stream.mjpg\" alt=\"camera\"/></body></html>";
        var body = Encoding.UTF8.GetBytes(html);
        var header = Encoding.ASCII.GetBytes(
            "HTTP/1.0 200 OK\r\n" +
            "Connection: close\r\n" +
            "Cache-Control: no-cache\r\n" +
            "Content-Type: text/html; charset=utf-8\r\n" +
            $"Content-Length: {body.Length}\r\n\r\n");
        TryWrite(stream, header);
        TryWrite(stream, body);
    }

    private void ServeMjpeg(NetworkStream stream, CancellationToken token, Socket socket)
    {
        var header = Encoding.ASCII.GetBytes(
            "HTTP/1.0 200 OK\r\n" +
            "Connection: close\r\n" +
            "Cache-Control: no-cache, no-store, must-revalidate\r\n" +
            "Pragma: no-cache\r\n" +
            "Access-Control-Allow-Origin: *\r\n" +
            "Content-Type: multipart/x-mixed-replace; boundary=frame\r\n\r\n");
        if (!TryWrite(stream, header))
            return;

        byte[]? lastSent = null;
        while (!token.IsCancellationRequested && socket.Connected)
        {
            byte[]? jpeg;
            lock (_frameGate)
                jpeg = _latestJpeg;

            if (jpeg == null || ReferenceEquals(jpeg, lastSent))
            {
                Thread.Sleep(40);
                continue;
            }

            lastSent = jpeg;

            var part =
                $"--frame\r\nContent-Type: image/jpeg\r\nContent-Length: {jpeg.Length}\r\n\r\n";
            var partBytes = Encoding.ASCII.GetBytes(part);
            if (!TryWrite(stream, partBytes))
                return;
            if (!TryWrite(stream, jpeg))
                return;
            if (!TryWrite(stream, CrLf))
                return;

            try { stream.Flush(); } catch { return; }

            Thread.Sleep(80);
        }
    }

    private static string ReadRequestHead(Socket socket)
    {
        var buf = new byte[4096];
        var total = 0;
        var deadline = Environment.TickCount64 + 2000;
        while (Environment.TickCount64 < deadline && total < buf.Length)
        {
            if (socket.Available <= 0)
            {
                Thread.Sleep(10);
                if (total > 0 && Encoding.ASCII.GetString(buf, 0, total).Contains("\r\n\r\n", StringComparison.Ordinal))
                    break;
                continue;
            }

            var n = socket.Receive(buf, total, buf.Length - total, SocketFlags.None);
            if (n <= 0) break;
            total += n;
            if (Encoding.ASCII.GetString(buf, 0, total).Contains("\r\n\r\n", StringComparison.Ordinal))
                break;
        }

        return total > 0 ? Encoding.ASCII.GetString(buf, 0, total) : "";
    }

    private static string ParseRequestPath(string request)
    {
        // "GET /view HTTP/1.1"
        if (string.IsNullOrEmpty(request))
            return "/";

        var lineEnd = request.IndexOf('\r');
        if (lineEnd < 0) lineEnd = request.IndexOf('\n');
        var line = lineEnd > 0 ? request[..lineEnd] : request;
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return "/";

        var target = parts[1];
        var q = target.IndexOf('?', StringComparison.Ordinal);
        if (q >= 0)
            target = target[..q];
        if (string.IsNullOrEmpty(target))
            return "/";
        return target;
    }

    private static bool TryWrite(NetworkStream stream, byte[] data)
    {
        try
        {
            stream.Write(data, 0, data.Length);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private sealed class ClientSession(int id, Socket socket)
    {
        public int Id { get; } = id;
        public Socket Socket { get; } = socket;
    }
}
