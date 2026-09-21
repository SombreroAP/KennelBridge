using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace KennelBridge;

/// <summary>
/// Tiny HTTP + WebSocket server for the OBS browser source. Serves the embedded overlay page and pushes
/// input snapshots to every connected page. Deliberately minimal: GET for three static files, one WS endpoint.
/// </summary>
public sealed class OverlayServer : IDisposable
{
    TcpListener? _listener;
    CancellationTokenSource? _cts;
    readonly List<Stream> _clients = new();
    readonly object _lock = new();
    string _lastState = "{}";
    string _liveConfig = "";

    /// <summary>Query string of the overlay the /live page should show. Setting it tells every open page to switch.</summary>
    public string LiveConfig
    {
        get => _liveConfig;
        set { if (value == _liveConfig) return; _liveConfig = value; Broadcast(CfgJson()); }
    }
    string CfgJson() => "{\"cfg\":" + System.Text.Json.JsonSerializer.Serialize(_liveConfig) + "}";

    public int Port { get; private set; }
    public bool Running => _listener != null;
    public int ClientCount { get { lock (_lock) return _clients.Count; } }
    public event Action<string>? Log;

    public bool Start(int port)
    {
        Stop();
        try
        {
            var l = new TcpListener(IPAddress.Any, port);
            l.Start();
            _listener = l;
            Port = port;
            _cts = new CancellationTokenSource();
            _ = AcceptLoop(l, _cts.Token);
            Log?.Invoke($"Overlay server running at http://localhost:{port}/");
            return true;
        }
        catch (Exception ex)
        {
            Log?.Invoke($"Overlay server could not start on port {port}: {ex.Message}");
            _listener = null;
            return false;
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _listener?.Stop(); } catch { }
        _listener = null;
        lock (_lock) { foreach (var c in _clients) { try { c.Dispose(); } catch { } } _clients.Clear(); }
    }

    async Task AcceptLoop(TcpListener l, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await l.AcceptTcpClientAsync(ct); }
            catch { break; }
            _ = Task.Run(() => Handle(client, ct), ct);
        }
    }

    async Task Handle(TcpClient client, CancellationToken ct)
    {
        using var _ = client;
        client.NoDelay = true;
        var stream = client.GetStream();
        try
        {
            // read headers
            var buf = new byte[8192];
            int total = 0;
            stream.ReadTimeout = 5000;
            while (true)
            {
                int n = await stream.ReadAsync(buf.AsMemory(total, buf.Length - total), ct);
                if (n <= 0) return;
                total += n;
                if (total >= 4 && Encoding.ASCII.GetString(buf, 0, total).Contains("\r\n\r\n")) break;
                if (total >= buf.Length) return;
            }
            var text = Encoding.ASCII.GetString(buf, 0, total);
            var lines = text.Split("\r\n");
            var req = lines[0].Split(' ');
            if (req.Length < 2) return;
            var path = req[1].Split('?')[0];
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var h in lines.Skip(1)) { int i = h.IndexOf(':'); if (i > 0) headers[h[..i].Trim()] = h[(i + 1)..].Trim(); }

            if (headers.TryGetValue("Upgrade", out var up) && up.Equals("websocket", StringComparison.OrdinalIgnoreCase) && headers.TryGetValue("Sec-WebSocket-Key", out var key))
            {
                var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
                var resp = "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + accept + "\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(resp), ct);
                lock (_lock) _clients.Add(stream);
                await SendFrame(stream, CfgJson());
                await SendFrame(stream, _lastState);
                await ReadFrames(stream, ct);      // until the page closes
                return;
            }

            if (path.StartsWith("/models/")) await ServeModel(stream, path, ct);
            else await ServeStatic(stream, path, ct);
        }
        catch { }
        finally { lock (_lock) _clients.Remove(stream); }
    }

    static async Task ServeStatic(Stream s, string path, CancellationToken ct)
    {
        var (name, type) = path switch
        {
            "/" or "/index.html" or "/live" => ("overlay/index.html", "text/html; charset=utf-8"),
            "/app.js" => ("overlay/app.js", "application/javascript; charset=utf-8"),
            "/style.css" => ("overlay/style.css", "text/css; charset=utf-8"),
            "/three.min.js" => ("overlay/three.min.js", "application/javascript; charset=utf-8"),
            "/pad3d.js" => ("overlay/pad3d.js", "application/javascript; charset=utf-8"),
            "/GLTFLoader.js" => ("overlay/GLTFLoader.js", "application/javascript; charset=utf-8"),
            _ => ("", ""),
        };
        byte[] body;
        string status = "200 OK";
        if (name.Length == 0) { body = Encoding.UTF8.GetBytes("Not found"); type = "text/plain"; status = "404 Not Found"; }
        else
        {
            using var rs = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
            if (rs == null) { body = Encoding.UTF8.GetBytes("Missing resource " + name); type = "text/plain"; status = "500 Internal Server Error"; }
            else { using var ms = new MemoryStream(); await rs.CopyToAsync(ms, ct); body = ms.ToArray(); }
        }
        var head = $"HTTP/1.1 {status}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\nCache-Control: no-cache\r\nAccess-Control-Allow-Origin: *\r\nConnection: close\r\n\r\n";
        await s.WriteAsync(Encoding.ASCII.GetBytes(head), ct);
        await s.WriteAsync(body, ct);
        await s.FlushAsync(ct);
    }

    /// <summary>Models bundled in the exe (CC-BY, see README). A file of the same name in the user's models folder wins.</summary>
    public static readonly string[] BundledModels = { "dualsense.glb", "dualshock4.glb", "xbox.glb" };

    /// <summary>3D models: the user's %APPDATA%\InputOverlayBridge\models first, then the bundled ones. "/models/" lists them; "/models/x.glb" serves one.</summary>
    static async Task ServeModel(Stream s, string path, CancellationToken ct)
    {
        var dir = Settings.ModelsDir;
        byte[] body; string type = "application/octet-stream", status = "200 OK";
        var rel = Uri.UnescapeDataString(path["/models/".Length..]);
        if (rel.Length == 0)
        {
            var names = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.gl*").Select(Path.GetFileName).OrderBy(n => n).ToList() : new List<string?>();
            foreach (var b in BundledModels) if (!names.Contains(b, StringComparer.OrdinalIgnoreCase)) names.Add(b);
            body = Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(names)); type = "application/json";
        }
        else if (rel.Contains("..") || rel.Contains('/') || rel.Contains('\\'))
        { body = Encoding.UTF8.GetBytes("Bad path"); type = "text/plain"; status = "400 Bad Request"; }
        else
        {
            var file = Path.Combine(dir, rel);
            byte[]? data = null;
            if (File.Exists(file)) data = await File.ReadAllBytesAsync(file, ct);
            else
            {
                using var rs = Assembly.GetExecutingAssembly().GetManifestResourceStream("models/" + rel.ToLowerInvariant());
                if (rs != null) { using var ms = new MemoryStream(); await rs.CopyToAsync(ms, ct); data = ms.ToArray(); }
            }
            if (data == null) { body = Encoding.UTF8.GetBytes("Not found"); type = "text/plain"; status = "404 Not Found"; }
            else
            {
                body = data;
                type = Path.GetExtension(file).ToLowerInvariant() switch
                {
                    ".glb" => "model/gltf-binary", ".gltf" => "model/gltf+json", ".json" => "application/json",
                    ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".bin" => "application/octet-stream", _ => "application/octet-stream",
                };
            }
        }
        var head = $"HTTP/1.1 {status}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\nCache-Control: no-cache\r\nAccess-Control-Allow-Origin: *\r\nConnection: close\r\n\r\n";
        await s.WriteAsync(Encoding.ASCII.GetBytes(head), ct);
        await s.WriteAsync(body, ct);
        await s.FlushAsync(ct);
    }

    /// <summary>Consume client frames: answer pings, stop on close. Client text frames are ignored.</summary>
    static async Task ReadFrames(Stream s, CancellationToken ct)
    {
        var hdr = new byte[2];
        while (!ct.IsCancellationRequested)
        {
            if (!await ReadExact(s, hdr, 2, ct)) return;
            int op = hdr[0] & 0x0F;
            bool masked = (hdr[1] & 0x80) != 0;
            long len = hdr[1] & 0x7F;
            if (len == 126) { var b = new byte[2]; if (!await ReadExact(s, b, 2, ct)) return; len = (b[0] << 8) | b[1]; }
            else if (len == 127) { var b = new byte[8]; if (!await ReadExact(s, b, 8, ct)) return; len = 0; foreach (var x in b) len = (len << 8) | x; }
            if (len > 65536) return;
            var mask = new byte[4];
            if (masked && !await ReadExact(s, mask, 4, ct)) return;
            var payload = new byte[len];
            if (len > 0 && !await ReadExact(s, payload, (int)len, ct)) return;
            if (masked) for (int i = 0; i < payload.Length; i++) payload[i] ^= mask[i & 3];
            if (op == 0x8) return;                                        // close
            if (op == 0x9) await WriteFrame(s, 0xA, payload, ct);         // ping -> pong
        }
    }

    static async Task<bool> ReadExact(Stream s, byte[] buf, int count, CancellationToken ct)
    {
        int got = 0;
        while (got < count)
        {
            int n = await s.ReadAsync(buf.AsMemory(got, count - got), ct);
            if (n <= 0) return false;
            got += n;
        }
        return true;
    }

    static async Task WriteFrame(Stream s, int op, byte[] payload, CancellationToken ct)
    {
        byte[] head;
        if (payload.Length < 126) head = new[] { (byte)(0x80 | op), (byte)payload.Length };
        else if (payload.Length < 65536) head = new[] { (byte)(0x80 | op), (byte)126, (byte)(payload.Length >> 8), (byte)payload.Length };
        else
        {
            head = new byte[10]; head[0] = (byte)(0x80 | op); head[1] = 127;
            long l = payload.Length; for (int i = 9; i >= 2; i--) { head[i] = (byte)(l & 0xFF); l >>= 8; }
        }
        await s.WriteAsync(head, ct);
        await s.WriteAsync(payload, ct);
    }

    static Task SendFrame(Stream s, string text) => WriteFrame(s, 0x1, Encoding.UTF8.GetBytes(text), CancellationToken.None);

    string? _pending;
    int _pumping;

    /// <summary>Push a snapshot to every connected overlay page. Never blocks the caller; bursts coalesce to the newest state.</summary>
    public void Broadcast(string json)
    {
        if (!json.StartsWith("{\"cfg\"")) _lastState = json;
        Interlocked.Exchange(ref _pending, json);
        if (Interlocked.Exchange(ref _pumping, 1) == 0) Task.Run(Pump);
    }

    void Pump()
    {
        while (true)
        {
            var json = Interlocked.Exchange(ref _pending, null);
            if (json == null)
            {
                Interlocked.Exchange(ref _pumping, 0);
                if (_pending != null && Interlocked.Exchange(ref _pumping, 1) == 0) continue;   // something slipped in
                return;
            }
            WriteAll(json);
        }
    }

    void WriteAll(string json)
    {
        Stream[] targets;
        lock (_lock) targets = _clients.ToArray();
        if (targets.Length == 0) return;
        var payload = Encoding.UTF8.GetBytes(json);
        foreach (var c in targets)
        {
            try
            {
                lock (c) { WriteFrame(c, 0x1, payload, CancellationToken.None).GetAwaiter().GetResult(); }
            }
            catch { lock (_lock) _clients.Remove(c); try { c.Dispose(); } catch { } }
        }
    }

    public void Dispose() => Stop();
}
