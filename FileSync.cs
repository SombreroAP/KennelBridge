using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace KennelBridge;

/// <summary>One file moving between the PCs, for the Files page list.</summary>
public sealed class FileTransfer
{
    public string Name { get; init; } = "";
    public long Size { get; init; }
    public long Done { get; set; }
    public string Status { get; set; } = "queued";     // queued, sending, receiving, done, skipped, failed
    public string Detail { get; set; } = "";
    public bool Outgoing { get; init; }
    public DateTime Updated { get; set; } = DateTime.Now;
    public int Percent => Size <= 0 ? 100 : (int)Math.Clamp(Done * 100 / Size, 0, 100);
}

/// <summary>
/// FileBridge, sending side: watches a folder (OBS's recording folder on the streaming PC) and pushes each
/// finished recording to the other PC over TCP. A recording counts as finished once nothing has it open for
/// writing and its size has not changed for <see cref="SettleSeconds"/>. Files already delivered are
/// remembered by name + size + modified time in a small manifest, so a restart never re-sends anything.
///
/// Wire: client sends one header line "KNLF1\t<passphrase>\t<name>\t<size>\t<mtimeTicks>\n", the receiver
/// answers "OK\n" (send it), "SKIP\n" (already has it) or "NO\n" (refused), the client streams the bytes,
/// and the receiver answers "DONE\n" once the file is written and renamed into place.
/// </summary>
public sealed class FileSender : IDisposable
{
    public string Folder { get; set; } = "";
    public string Extensions { get; set; } = "";
    public bool DeleteAfterSend { get; set; }
    public int SettleSeconds { get; set; } = 10;
    public string Passphrase { get; set; } = "";
    /// <summary>Where to send: host and TCP port. Null host = nowhere yet.</summary>
    public Func<(string host, int port)>? Target { get; set; }

    public event Action<string>? Log;
    public event Action<FileTransfer>? Progress;
    public bool Running => _timer != null;

    System.Threading.Timer? _timer;
    FileSystemWatcher? _watcher;
    readonly ConcurrentDictionary<string, (long size, DateTime seen)> _growing = new();
    readonly ConcurrentQueue<string> _queue = new();
    readonly ConcurrentDictionary<string, DateTime> _retryAfter = new();
    /// <summary>Folder + extensions the running watcher was started with, so the owner knows when a restart is due.</summary>
    public string RunningKey { get; private set; } = "";
    readonly HashSet<string> _queued = new();
    readonly object _lock = new();
    Dictionary<string, string> _sent = new();      // file name -> "size:mtimeTicks"
    int _working;
    CancellationTokenSource? _cts;

    public IReadOnlyCollection<string> SentNames { get { lock (_lock) return _sent.Keys.ToList(); } }

    public void Start()
    {
        Stop();
        LoadManifest();
        if (Folder.Length == 0 || !Directory.Exists(Folder)) { Log?.Invoke($"Files: send folder not found: {(Folder.Length == 0 ? "(none set)" : Folder)}"); return; }
        _cts = new CancellationTokenSource();
        try
        {
            _watcher = new FileSystemWatcher(Folder) { IncludeSubdirectories = false, NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite, EnableRaisingEvents = true };
            _watcher.Created += (_, e) => Note(e.FullPath);
            _watcher.Changed += (_, e) => Note(e.FullPath);
            _watcher.Renamed += (_, e) => Note(e.FullPath);
        }
        catch (Exception ex) { Log?.Invoke("Files: folder watch unavailable, scanning instead: " + ex.Message); }
        _timer = new System.Threading.Timer(_ => Scan(), null, 1500, 5000);
        RunningKey = Folder + "|" + Extensions;
        Log?.Invoke($"Files: watching {Folder} for {(Extensions.Length == 0 ? "any file" : Extensions)}.");
    }

    public void Stop()
    {
        _timer?.Dispose(); _timer = null;
        _watcher?.Dispose(); _watcher = null;
        _cts?.Cancel(); _cts = null;
        _growing.Clear(); _retryAfter.Clear();
        RunningKey = "";
        lock (_lock) { _queued.Clear(); }
        while (_queue.TryDequeue(out _)) { }
    }

    /// <summary>Forget what was sent, so everything in the folder goes again.</summary>
    public void ForgetSent()
    {
        lock (_lock) _sent.Clear();
        SaveManifest();
    }

    bool Wanted(string path)
    {
        if (Extensions.Trim().Length == 0) return true;
        var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        return Extensions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(e => e.TrimStart('.').Equals(ext, StringComparison.OrdinalIgnoreCase));
    }

    void Note(string path)
    {
        if (!Wanted(path)) return;
        try { if (File.Exists(path)) _growing[path] = (new FileInfo(path).Length, DateTime.UtcNow); } catch { }
    }

    /// <summary>Look at every file in the folder now; anything finished and not yet sent is queued.</summary>
    public void Scan()
    {
        if (Folder.Length == 0 || !Directory.Exists(Folder)) return;
        string[] files;
        try { files = Directory.GetFiles(Folder); } catch { return; }
        foreach (var f in files)
        {
            if (!Wanted(f) || f.EndsWith(".part", StringComparison.OrdinalIgnoreCase)) continue;
            FileInfo fi;
            try { fi = new FileInfo(f); } catch { continue; }
            if (AlreadySent(fi)) continue;
            if (_retryAfter.TryGetValue(f, out var after) && DateTime.UtcNow < after) continue;
            if (!IsSettled(fi)) continue;
            Enqueue(f);
        }
        if (Interlocked.CompareExchange(ref _working, 1, 0) == 0) _ = Task.Run(Worker);
    }

    bool AlreadySent(FileInfo fi)
    {
        lock (_lock) return _sent.TryGetValue(fi.Name, out var v) && v == $"{fi.Length}:{fi.LastWriteTimeUtc.Ticks}";
    }

    /// <summary>Finished = nobody has it open for writing, and the size has not changed for SettleSeconds.</summary>
    bool IsSettled(FileInfo fi)
    {
        var now = DateTime.UtcNow;
        if (!_growing.TryGetValue(fi.FullName, out var g) || g.size != fi.Length) { _growing[fi.FullName] = (fi.Length, now); return false; }
        if ((now - g.seen).TotalSeconds < SettleSeconds) return false;
        try { using var s = new FileStream(fi.FullName, FileMode.Open, FileAccess.Read, FileShare.Read); }   // fails while OBS still writes it
        catch { _growing[fi.FullName] = (fi.Length, now); return false; }
        return true;
    }

    void Enqueue(string path)
    {
        lock (_lock) { if (!_queued.Add(path)) return; }
        _queue.Enqueue(path);
    }

    async Task Worker()
    {
        try
        {
            while (_queue.TryDequeue(out var path))
            {
                lock (_lock) _queued.Remove(path);
                if (_cts == null || _cts.IsCancellationRequested) return;
                await SendOne(path, _cts.Token);
            }
        }
        finally { Interlocked.Exchange(ref _working, 0); }
    }

    async Task SendOne(string path, CancellationToken ct)
    {
        var (host, port) = Target?.Invoke() ?? ("", 0);
        FileInfo fi;
        try { fi = new FileInfo(path); if (!fi.Exists) return; } catch { return; }
        var t = new FileTransfer { Name = fi.Name, Size = fi.Length, Outgoing = true, Status = "sending" };
        if (string.IsNullOrWhiteSpace(host)) { t.Status = "queued"; t.Detail = "no other PC set"; Progress?.Invoke(t); _retryAfter[path] = DateTime.UtcNow.AddSeconds(15); return; }
        Progress?.Invoke(t);
        try
        {
            using var client = new TcpClient(AddressFamily.InterNetwork) { NoDelay = true, SendBufferSize = 1 << 20 };
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(5000);
            await client.ConnectAsync(host.Trim(), port, connectCts.Token);
            using var s = client.GetStream();
            var header = Encoding.UTF8.GetBytes($"KNLF1\t{Passphrase}\t{fi.Name}\t{fi.Length}\t{fi.LastWriteTimeUtc.Ticks}\n");
            await s.WriteAsync(header, ct);
            var reply = await ReadLine(s, ct);
            if (reply == "SKIP") { t.Status = "skipped"; t.Detail = "the other PC already has it"; t.Done = t.Size; MarkSent(fi); Progress?.Invoke(t); return; }
            if (reply != "OK") { t.Status = "failed"; t.Detail = reply.Length == 0 ? "no answer from the other PC" : reply == "NO" ? "refused (passphrase?)" : reply; Progress?.Invoke(t); Requeue(path); return; }

            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true))
            {
                var buf = new byte[1 << 16];
                var last = DateTime.UtcNow;
                int n;
                while ((n = await file.ReadAsync(buf, ct)) > 0)
                {
                    await s.WriteAsync(buf.AsMemory(0, n), ct);
                    t.Done += n;
                    if ((DateTime.UtcNow - last).TotalMilliseconds > 250) { last = DateTime.UtcNow; Progress?.Invoke(t); }
                }
            }
            await s.FlushAsync(ct);
            var done = await ReadLine(s, ct);
            if (done != "DONE") { t.Status = "failed"; t.Detail = "the other PC did not confirm"; Progress?.Invoke(t); Requeue(path); return; }
            t.Status = "done"; t.Done = t.Size; t.Detail = "";
            _retryAfter.TryRemove(path, out _);
            MarkSent(fi);
            Log?.Invoke($"Files: sent {fi.Name} ({Human(fi.Length)}).");
            if (DeleteAfterSend) { try { File.Delete(path); t.Detail = "deleted here"; Log?.Invoke($"Files: deleted {fi.Name} here after sending."); } catch (Exception ex) { t.Detail = "could not delete here: " + ex.Message; } }
            Progress?.Invoke(t);
        }
        catch (OperationCanceledException) { t.Status = "failed"; t.Detail = "cancelled"; Progress?.Invoke(t); }
        catch (Exception ex)
        {
            t.Status = "failed"; t.Detail = ex.Message;
            Progress?.Invoke(t);
            Log?.Invoke($"Files: {fi.Name} failed: {ex.Message}. Will retry.");
            Requeue(path);
        }
    }

    /// <summary>A failed file is picked up again by a later scan, but not for a while.</summary>
    void Requeue(string path) => _retryAfter[path] = DateTime.UtcNow.AddSeconds(20);

    void MarkSent(FileInfo fi)
    {
        lock (_lock) _sent[fi.Name] = $"{fi.Length}:{fi.LastWriteTimeUtc.Ticks}";
        SaveManifest();
    }

    static async Task<string> ReadLine(NetworkStream s, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var one = new byte[1];
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(120000);
        while (sb.Length < 512)
        {
            int n;
            try { n = await s.ReadAsync(one, cts.Token); } catch (OperationCanceledException) { return ""; }
            if (n <= 0) return sb.ToString();
            if (one[0] == '\n') break;
            sb.Append((char)one[0]);
        }
        return sb.ToString().TrimEnd('\r');
    }

    void LoadManifest()
    {
        try { if (File.Exists(Settings.SentManifestPath)) { var d = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Settings.SentManifestPath)); lock (_lock) _sent = d ?? new(); } }
        catch { }
    }

    void SaveManifest()
    {
        try { Directory.CreateDirectory(Settings.Dir); string json; lock (_lock) json = JsonSerializer.Serialize(_sent); File.WriteAllText(Settings.SentManifestPath, json); } catch { }
    }

    public static string Human(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.0} MB",
        >= 1L << 10 => $"{bytes / (double)(1L << 10):0} KB",
        _ => $"{bytes} B",
    };

    public void Dispose() => Stop();
}

/// <summary>FileBridge, receiving side: accepts files from the other PC into a folder. See <see cref="FileSender"/> for the wire format.</summary>
public sealed class FileReceiver : IDisposable
{
    public string Folder { get; set; } = "";
    public string Passphrase { get; set; } = "";
    public event Action<string>? Log;
    public event Action<FileTransfer>? Progress;
    public bool Running => _listener != null;
    public int Port { get; private set; }

    TcpListener? _listener;
    CancellationTokenSource? _cts;

    public bool Start(int port)
    {
        Stop();
        if (Folder.Length == 0) { Log?.Invoke("Files: no receive folder set."); return false; }
        try { Directory.CreateDirectory(Folder); } catch (Exception ex) { Log?.Invoke("Files: cannot create the receive folder: " + ex.Message); return false; }
        try
        {
            var l = new TcpListener(IPAddress.Any, port);
            l.Start();
            _listener = l; Port = port;
            _cts = new CancellationTokenSource();
            _ = AcceptLoop(l, _cts.Token);
            Log?.Invoke($"Files: receiving into {Folder} on TCP port {port}.");
            return true;
        }
        catch (Exception ex) { Log?.Invoke($"Files: could not listen on TCP port {port}: {ex.Message}"); _listener = null; return false; }
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _listener?.Stop(); } catch { }
        _listener = null; _cts = null;
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
        client.NoDelay = true; client.ReceiveBufferSize = 1 << 20;
        var from = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "?";
        FileTransfer? t = null;
        string? partPath = null;
        try
        {
            using var s = client.GetStream();
            var header = await ReadLine(s, ct);
            var p = header.Split('\t');
            if (p.Length != 5 || p[0] != "KNLF1") return;
            if (!string.Equals(p[1], Passphrase, StringComparison.Ordinal)) { await Reply(s, "NO", ct); Log?.Invoke($"Files: refused a file from {from}: passphrase does not match."); return; }
            var name = Path.GetFileName(p[2]);
            if (name.Length == 0 || name is "." or ".." || !long.TryParse(p[3], out long size) || !long.TryParse(p[4], out long mtimeTicks)) { await Reply(s, "NO", ct); return; }
            var final = Path.Combine(Folder, name);
            if (File.Exists(final) && new FileInfo(final).Length == size) { await Reply(s, "SKIP", ct); return; }
            if (File.Exists(final)) final = Unique(final);
            partPath = final + ".part";
            t = new FileTransfer { Name = Path.GetFileName(final), Size = size, Outgoing = false, Status = "receiving" };
            Progress?.Invoke(t);
            await Reply(s, "OK", ct);

            using (var file = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
            {
                var buf = new byte[1 << 16];
                long left = size;
                var last = DateTime.UtcNow;
                while (left > 0)
                {
                    int n = await s.ReadAsync(buf.AsMemory(0, (int)Math.Min(buf.Length, left)), ct);
                    if (n <= 0) throw new IOException("connection closed early");
                    await file.WriteAsync(buf.AsMemory(0, n), ct);
                    left -= n; t.Done += n;
                    if ((DateTime.UtcNow - last).TotalMilliseconds > 250) { last = DateTime.UtcNow; Progress?.Invoke(t); }
                }
            }
            try { File.SetLastWriteTimeUtc(partPath, new DateTime(mtimeTicks, DateTimeKind.Utc)); } catch { }
            File.Move(partPath, final, overwrite: true);
            partPath = null;
            await Reply(s, "DONE", ct);
            t.Status = "done"; t.Done = size;
            Progress?.Invoke(t);
            Log?.Invoke($"Files: received {t.Name} ({FileSender.Human(size)}) from {from}.");
        }
        catch (Exception ex)
        {
            if (t != null) { t.Status = "failed"; t.Detail = ex.Message; Progress?.Invoke(t); }
            Log?.Invoke($"Files: receive from {from} failed: {ex.Message}");
            if (partPath != null) { try { File.Delete(partPath); } catch { } }
        }
    }

    static string Unique(string path)
    {
        var dir = Path.GetDirectoryName(path)!; var stem = Path.GetFileNameWithoutExtension(path); var ext = Path.GetExtension(path);
        for (int i = 2; ; i++) { var c = Path.Combine(dir, $"{stem} ({i}){ext}"); if (!File.Exists(c)) return c; }
    }

    static Task Reply(NetworkStream s, string text, CancellationToken ct) => s.WriteAsync(Encoding.ASCII.GetBytes(text + "\n"), ct).AsTask();

    static async Task<string> ReadLine(NetworkStream s, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var one = new byte[1];
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(10000);
        while (sb.Length < 1024)
        {
            int n;
            try { n = await s.ReadAsync(one, cts.Token); } catch (OperationCanceledException) { return ""; }
            if (n <= 0) return sb.ToString();
            if (one[0] == '\n') break;
            sb.Append((char)one[0]);
        }
        return sb.ToString().TrimEnd('\r');
    }

    public void Dispose() => Stop();
}
