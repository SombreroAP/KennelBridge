using System.Net;
using System.Net.Sockets;
using System.Text;

namespace KennelBridge;

/// <summary>
/// The one UDP link to the other PC, shared by the hotkey and input-overlay bridges (audio and files
/// have their own sockets because they move far more data). Every instance both listens and sends.
///
/// Packets are tab-separated text: MAGIC, passphrase, then the payload.
///   KNLP  ping/pong for the link test
///   KNLK  a hotkey request: kind, vk, mods, state
///   KNLI  an input snapshot (JSON) for the overlay
/// </summary>
public sealed class Bridge : IDisposable
{
    const string PingMagic = "KNLP", HotkeyMagic = "KNLK", InputMagic = "KNLI";

    UdpClient? _udp;
    CancellationTokenSource? _cts;
    TaskCompletionSource<IPEndPoint>? _pong;
    readonly Dictionary<string, IPEndPoint> _resolved = new();

    public string Passphrase { get; set; } = "";
    public event Action<string>? Log;
    /// <summary>A hotkey request arrived from the peer. Thread-pool thread.</summary>
    public event Action<Binding, PressState, IPEndPoint>? HotkeyReceived;
    /// <summary>An input snapshot (JSON) arrived from the peer. Thread-pool thread.</summary>
    public event Action<string, IPEndPoint>? InputReceived;
    public bool Listening => _udp != null;

    public void Start(int port)
    {
        Stop();
        try
        {
            var udp = new UdpClient(AddressFamily.InterNetwork);
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, port));
            // Windows quirk: an ICMP "port unreachable" from a peer that isn't listening would otherwise
            // surface as a SocketException on our next receive. Turn that off.
            try { udp.Client.IOControl(unchecked((IOControlCode)(-1744830452)), new byte[] { 0, 0, 0, 0 }, null); } catch { }
            _udp = udp;
            _cts = new CancellationTokenSource();
            _ = ReceiveLoop(udp, _cts.Token);
            Log?.Invoke($"Listening on UDP port {port}.");
        }
        catch (Exception ex) { Log?.Invoke($"Cannot listen on port {port}: {ex.Message}"); }
    }

    public void Stop()
    {
        _cts?.Cancel();
        _udp?.Dispose();
        _cts = null; _udp = null;
        _resolved.Clear();
    }

    async Task ReceiveLoop(UdpClient udp, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult r;
            try { r = await udp.ReceiveAsync(ct); }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { continue; }
            catch (Exception ex) { Log?.Invoke("Receive error: " + ex.Message); continue; }
            HandlePacket(r);
        }
    }

    void HandlePacket(UdpReceiveResult r)
    {
        string text;
        try { text = Encoding.UTF8.GetString(r.Buffer); } catch { return; }
        var p = text.Split('\t');
        if (p.Length < 3 || p[0] is not (PingMagic or HotkeyMagic or InputMagic)) return;   // not ours; ignore silently
        if (!string.Equals(p[1], Passphrase, StringComparison.Ordinal))
        {
            if (p[0] != InputMagic) Log?.Invoke($"Ignored a request from {r.RemoteEndPoint.Address}: passphrase does not match.");
            return;
        }

        switch (p[0])
        {
            case PingMagic:
                if (p[2] == "PING")
                {
                    try { var reply = Encoding.UTF8.GetBytes(string.Join('\t', PingMagic, Passphrase, "PONG")); _udp?.Send(reply, reply.Length, r.RemoteEndPoint); } catch { }
                }
                else if (p[2] == "PONG") _pong?.TrySetResult(r.RemoteEndPoint);
                break;
            case HotkeyMagic:
                if (p.Length != 6) return;
                if (!int.TryParse(p[2], out int kind) || !int.TryParse(p[3], out int vk) || !int.TryParse(p[4], out int mods) || !int.TryParse(p[5], out int st)) return;
                HotkeyReceived?.Invoke(new Binding { Kind = (ActionKind)kind, ActionVk = vk, ActionMods = mods }, (PressState)st, r.RemoteEndPoint);
                break;
            case InputMagic:
                InputReceived?.Invoke(p[2], r.RemoteEndPoint);
                break;
        }
    }

    /// <summary>Ask the peer to perform <paramref name="b"/>'s action. Returns false (and logs) on failure.</summary>
    public bool SendHotkey(string host, int port, Binding b, PressState state = PressState.Tap)
    {
        if (string.IsNullOrWhiteSpace(host)) { Log?.Invoke("No other PC set - pick it on the Connection page first."); return false; }
        try
        {
            var msg = string.Join('\t', HotkeyMagic, Passphrase, (int)b.Kind, b.ActionVk, b.ActionMods, (int)state);
            var bytes = Encoding.UTF8.GetBytes(msg);
            var udp = _udp ?? new UdpClient(AddressFamily.InterNetwork);
            udp.Send(bytes, bytes.Length, host.Trim(), port);
            if (udp != _udp) udp.Dispose();
            return true;
        }
        catch (Exception ex)
        {
            Log?.Invoke($"Send to {host} failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Fire-and-forget input state to the peer (no logging: this runs up to 60 times a second).</summary>
    public void SendInput(string host, int port, string json)
    {
        var udp = _udp;
        if (udp == null || string.IsNullOrWhiteSpace(host)) return;
        try
        {
            var key = host.Trim() + ":" + port;
            if (!_resolved.TryGetValue(key, out var ep))
            {
                var addr = IPAddress.TryParse(host.Trim(), out var ip) ? ip
                    : Dns.GetHostAddresses(host.Trim()).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
                if (addr == null) return;
                ep = new IPEndPoint(addr, port);
                if (_resolved.Count > 32) _resolved.Clear();
                _resolved[key] = ep;
            }
            var bytes = Encoding.UTF8.GetBytes(string.Join('\t', InputMagic, Passphrase, json));
            udp.Send(bytes, bytes.Length, ep);
        }
        catch { }
    }

    /// <summary>Round-trip check: does a KennelBridge with our passphrase answer at host:port? Latency in ms, or -1.</summary>
    public async Task<int> PingAsync(string host, int port, int timeoutMs = 2000)
    {
        if (string.IsNullOrWhiteSpace(host)) return -1;
        var udp = _udp;
        if (udp == null) return -1;
        var tcs = new TaskCompletionSource<IPEndPoint>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pong = tcs;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var bytes = Encoding.UTF8.GetBytes(string.Join('\t', PingMagic, Passphrase, "PING"));
            for (int i = 0; i < 3 && !tcs.Task.IsCompleted; i++)
            {
                try { udp.Send(bytes, bytes.Length, host.Trim(), port); } catch (Exception ex) { Log?.Invoke($"Ping to {host} failed: {ex.Message}"); return -1; }
                var done = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs / 3));
                if (done == tcs.Task) return (int)sw.ElapsedMilliseconds;
            }
            return -1;
        }
        finally { if (_pong == tcs) _pong = null; }
    }

    public void Dispose() => Stop();
}
