using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace KennelBridge;

public sealed class Peer
{
    public string Ip { get; init; } = "";
    public string Name { get; set; } = "";
    public int Port { get; set; }
    public PcRole Role { get; set; }
    public bool Enabled { get; set; }
    public DateTime LastSeen { get; set; }

    public bool Online => (DateTime.UtcNow - LastSeen).TotalSeconds < 8;
    public string RoleText => Role switch { PcRole.Gaming => "gaming PC", PcRole.Streaming => "streaming PC", _ => "role not set" };
    public override string ToString() => $"{Name}  —  {Ip}  ({RoleText}{(Enabled ? "" : ", paused")})";
}

/// <summary>
/// Finds other KennelBridge instances on the LAN. Every instance broadcasts a small "here I am"
/// packet every 2 s on a fixed port and listens for the others' packets.
/// </summary>
public sealed class Discovery : IDisposable
{
    public const int Port = 47851;
    const string Magic = "KNL1";

    readonly string _id = Guid.NewGuid().ToString("N");
    UdpClient? _udp;
    CancellationTokenSource? _cts;
    System.Threading.Timer? _timer;

    public ConcurrentDictionary<string, Peer> Peers { get; } = new();

    /// <summary>Supplies what to announce: our data port, role and enabled flag.</summary>
    public Func<(int port, PcRole role, bool enabled)>? State { get; set; }
    public event Action<string>? Log;

    public void Start()
    {
        Stop();
        try
        {
            var udp = new UdpClient(AddressFamily.InterNetwork);
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.EnableBroadcast = true;
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, Port));
            try { udp.Client.IOControl(unchecked((IOControlCode)(-1744830452)), new byte[] { 0, 0, 0, 0 }, null); } catch { }
            _udp = udp;
            _cts = new CancellationTokenSource();
            _ = ReceiveLoop(udp, _cts.Token);
            _timer = new System.Threading.Timer(_ => Announce(), null, 300, 2000);
        }
        catch (Exception ex)
        {
            Log?.Invoke($"Auto-detect unavailable (port {Port}): {ex.Message}");
        }
    }

    public void Stop()
    {
        _timer?.Dispose(); _timer = null;
        _cts?.Cancel(); _cts = null;
        _udp?.Dispose(); _udp = null;
    }

    void Announce()
    {
        var udp = _udp;
        if (udp == null || State == null) return;
        var (port, role, enabled) = State();
        var msg = string.Join('\t', Magic, _id, Environment.MachineName, port, (int)role, enabled ? 1 : 0);
        var bytes = Encoding.UTF8.GetBytes(msg);
        foreach (var target in BroadcastTargets())
        {
            try { udp.Send(bytes, bytes.Length, new IPEndPoint(target, Port)); } catch { }
        }
    }

    // 255.255.255.255 only leaves on one adapter, so also hit every subnet's directed broadcast.
    static IEnumerable<IPAddress> BroadcastTargets()
    {
        var set = new HashSet<IPAddress> { IPAddress.Broadcast };
        try
        {
            foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (n.OperationalStatus != OperationalStatus.Up || n.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var u in n.GetIPProperties().UnicastAddresses)
                {
                    if (u.Address.AddressFamily != AddressFamily.InterNetwork || u.IPv4Mask == null) continue;
                    var a = u.Address.GetAddressBytes(); var m = u.IPv4Mask.GetAddressBytes();
                    var b = new byte[4];
                    for (int i = 0; i < 4; i++) b[i] = (byte)(a[i] | ~m[i]);
                    set.Add(new IPAddress(b));
                }
            }
        }
        catch { }
        return set;
    }

    async Task ReceiveLoop(UdpClient udp, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult r;
            try { r = await udp.ReceiveAsync(ct); }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch { continue; }

            string text;
            try { text = Encoding.UTF8.GetString(r.Buffer); } catch { continue; }
            var p = text.Split('\t');
            if (p.Length != 6 || p[0] != Magic || p[1] == _id) continue;
            if (!int.TryParse(p[3], out int port) || !int.TryParse(p[4], out int role) || !int.TryParse(p[5], out int en)) continue;

            var ip = r.RemoteEndPoint.Address.ToString();
            var peer = Peers.GetOrAdd(ip, _ => new Peer { Ip = ip });
            peer.Name = p[2]; peer.Port = port; peer.Role = (PcRole)role; peer.Enabled = en == 1;
            peer.LastSeen = DateTime.UtcNow;
        }
    }

    /// <summary>Drop peers not heard from for a long time.</summary>
    public void Prune()
    {
        foreach (var kv in Peers)
            if ((DateTime.UtcNow - kv.Value.LastSeen).TotalSeconds > 60) Peers.TryRemove(kv.Key, out _);
    }

    public void Dispose() => Stop();
}
