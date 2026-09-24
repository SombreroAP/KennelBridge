using System.Net;
using System.Net.Sockets;
using System.Text;

namespace KennelBridge;

/// <summary>
/// Sends a saved sound file straight to the other PC over TCP, for sounds the other PC cannot download
/// itself (MyInstants serves its files only to a real browser session). Port: the file bridge's + 1.
///
/// Wire: "KNLSND1\t&lt;passphrase&gt;\t&lt;file name&gt;\t&lt;size&gt;\n", then the bytes, then the receiver answers "OK\n".
/// The file lands in the sounds folder under the same name, so both PCs resolve the sound to the same path.
/// </summary>
public sealed class SoundShare : IDisposable
{
    public string Passphrase { get; set; } = "";
    public event Action<string>? Log;
    /// <summary>A file arrived (its name in the sounds folder).</summary>
    public event Action<string>? Received;
    TcpListener? _listener;
    CancellationTokenSource? _cts;
    public int Port { get; private set; }
    public bool Running => _listener != null;
    const long MaxBytes = 20 * 1024 * 1024;

    public void Start(int port)
    {
        if (Running && Port == port) return;
        Stop();
        try
        {
            var l = new TcpListener(IPAddress.Any, port); l.Start();
            _listener = l; Port = port; _cts = new CancellationTokenSource();
            _ = Accept(l, _cts.Token);
        }
        catch (Exception ex) { Log?.Invoke($"Soundboard: could not listen on TCP {port} for sounds from the other PC: {ex.Message}"); }
    }

    public void Stop() { _cts?.Cancel(); try { _listener?.Stop(); } catch { } _listener = null; }

    async Task Accept(TcpListener l, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient c;
            try { c = await l.AcceptTcpClientAsync(ct); } catch { break; }
            _ = Task.Run(() => Handle(c, ct), ct);
        }
    }

    async Task Handle(TcpClient client, CancellationToken ct)
    {
        using var _ = client;
        string? part = null;
        try
        {
            using var s = client.GetStream();
            var p = (await ReadLine(s, ct)).Split('\t');
            if (p.Length != 4 || p[0] != "KNLSND1" || !string.Equals(p[1], Passphrase, StringComparison.Ordinal)) return;
            var name = Path.GetFileName(p[2]);
            if (name.Length == 0 || name.Contains("..") || !long.TryParse(p[3], out long size) || size <= 0 || size > MaxBytes) return;
            Directory.CreateDirectory(Soundboard.Dir);
            var final = Path.Combine(Soundboard.Dir, name);
            part = final + ".part";
            using (var f = File.Create(part))
            {
                var buf = new byte[65536]; long left = size;
                while (left > 0)
                {
                    int n = await s.ReadAsync(buf.AsMemory(0, (int)Math.Min(buf.Length, left)), ct);
                    if (n <= 0) throw new IOException("connection closed early");
                    await f.WriteAsync(buf.AsMemory(0, n), ct); left -= n;
                }
            }
            File.Move(part, final, overwrite: true); part = null;
            await s.WriteAsync(Encoding.ASCII.GetBytes("OK\n"), ct);
            Received?.Invoke(name);
        }
        catch (Exception ex) { Log?.Invoke("Soundboard: receiving a sound failed: " + ex.Message); }
        finally { if (part != null) try { File.Delete(part); } catch { } }
    }

    /// <summary>Push one file from the sounds folder to the other PC. Returns true when it confirmed.</summary>
    public async Task<bool> SendAsync(string host, int port, string path)
    {
        try
        {
            var fi = new FileInfo(path);
            using var c = new TcpClient(AddressFamily.InterNetwork);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await c.ConnectAsync(host.Trim(), port, cts.Token);
            using var s = c.GetStream();
            await s.WriteAsync(Encoding.UTF8.GetBytes($"KNLSND1\t{Passphrase}\t{fi.Name}\t{fi.Length}\n"), cts.Token);
            await using (var f = File.OpenRead(path)) await f.CopyToAsync(s, cts.Token);
            return await ReadLine(s, cts.Token) == "OK";
        }
        catch (Exception ex) { Log?.Invoke($"Soundboard: could not send {Path.GetFileName(path)} to the other PC ({ex.Message}). Is TCP {port} allowed? Press Firewall… on the Connection page."); return false; }
    }

    static async Task<string> ReadLine(NetworkStream s, CancellationToken ct)
    {
        var sb = new StringBuilder(); var one = new byte[1];
        while (sb.Length < 1024)
        {
            if (await s.ReadAsync(one, ct) <= 0) break;
            if (one[0] == '\n') break;
            sb.Append((char)one[0]);
        }
        return sb.ToString().TrimEnd('\r');
    }

    public void Dispose() => Stop();
}
