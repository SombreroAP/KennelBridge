using static KennelBridge.Theme;

namespace KennelBridge;

/// <summary>Connection page: the one place the two PCs are linked. Every bridge uses what is set here.</summary>
public sealed partial class MainForm
{
    readonly Button _segGaming = Theme.Button("Gaming PC", minWidth: 0), _segStreaming = Theme.Button("Streaming PC", minWidth: 0);
    readonly ComboBox _peer = Theme.ComboBox();
    readonly Label _peerStatus = Theme.Label("", muted: true);
    readonly TextBox _port = Theme.TextBox();
    readonly TextBox _pass = Theme.TextBox("same on both PCs");
    readonly Label _localIps = Theme.Label("", muted: true, Theme.Small);
    readonly Label _portsInfo = Theme.Label("", muted: true, Theme.Small);
    readonly CheckBox _startWithWindows = Theme.Check("Start with Windows");
    readonly CheckBox _startMinimized = Theme.Check("Start minimized to tray");
    readonly CheckBox _streamerMode = Theme.Check("Streamer mode (hide IPs)");
    string _peerListSignature = "";
    const string HiddenPeerText = "(hidden - streamer mode)";

    Control BuildConnectionPage()
    {
        var col = Rows(352, 200, -1);

        var conn = new Card("Other PC") { Dock = DockStyle.Fill };
        var cT = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 7 };
        cT.ColumnStyles.Add(Cpx(110)); cT.ColumnStyles.Add(Cpct(100));
        foreach (var h in new[] { 40, 36, 28, 36, 36, 44, 24 }) cT.RowStyles.Add(Px(h));

        cT.Controls.Add(Theme.Label("This PC"), 0, 0);
        var seg = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0, 3, 0, 3) };
        seg.ColumnStyles.Add(Cpct(50)); seg.ColumnStyles.Add(Cpct(50));
        foreach (var (b, r) in new[] { (_segGaming, PcRole.Gaming), (_segStreaming, PcRole.Streaming) })
        {
            b.Dock = DockStyle.Fill; b.Margin = new Padding(0, 0, 4, 0); b.MinimumSize = new Size(0, 30); b.AutoSize = false;
            b.Click += (_, _) => { if (!_loadingUi && S.Role != r) SetRole(r); };
            seg.Controls.Add(b);
        }
        cT.Controls.Add(seg, 1, 0);

        cT.Controls.Add(Theme.Label("Other PC"), 0, 1);
        _peer.Dock = DockStyle.Fill;
        _peer.SelectionChangeCommitted += (_, _) =>
        {
            if (_peer.SelectedItem is Peer pk)
                BeginInvoke(() => { _peer.SelectedIndex = -1; _peer.Text = pk.Ip; SetStatus($"Using {pk.Name}. Click Apply."); });
        };
        cT.Controls.Add(_peer, 1, 1);
        _peerStatus.AutoSize = false; _peerStatus.Dock = DockStyle.Fill; _peerStatus.AutoEllipsis = true; _peerStatus.Font = Small;
        _peerStatus.TextAlign = ContentAlignment.MiddleLeft;
        cT.Controls.Add(_peerStatus, 0, 2);
        cT.SetColumnSpan(_peerStatus, 2);
        cT.Controls.Add(Theme.Label("Passphrase"), 0, 3);
        _pass.Dock = DockStyle.Fill;
        cT.Controls.Add(_pass, 1, 3);
        cT.Controls.Add(Theme.Label("Port"), 0, 4);
        var portRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0) };
        portRow.ColumnStyles.Add(Cpx(90)); portRow.ColumnStyles.Add(Cpct(100));
        _port.Dock = DockStyle.Fill; _port.TextAlign = HorizontalAlignment.Center;
        portRow.Controls.Add(_port, 0, 0);
        _localIps.AutoSize = false; _localIps.Dock = DockStyle.Fill; _localIps.AutoEllipsis = true; _localIps.TextAlign = ContentAlignment.MiddleLeft; _localIps.Margin = new Padding(10, 0, 0, 0);
        portRow.Controls.Add(_localIps, 1, 0);
        cT.Controls.Add(portRow, 1, 4);
        var cb = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0), Padding = new Padding(0, 6, 0, 0) };
        cb.Controls.Add(On(Theme.Button("Apply", primary: true, 80), SaveAndApply));
        cb.Controls.Add(On(Theme.Button("Test link", minWidth: 80), () => _ = TestLink()));
        cb.Controls.Add(On(Theme.Button("Firewall…", minWidth: 80), () => { SaveAndApply(); AllowFirewall(); }));
        cT.Controls.Add(cb, 0, 5);
        cT.SetColumnSpan(cb, 2);
        _portsInfo.AutoSize = false; _portsInfo.Dock = DockStyle.Fill; _portsInfo.AutoEllipsis = true; _portsInfo.TextAlign = ContentAlignment.MiddleLeft;
        cT.Controls.Add(_portsInfo, 0, 6);
        cT.SetColumnSpan(_portsInfo, 2);
        conn.Controls.Add(cT);
        col.Controls.Add(conn, 0, 0);

        var opts = new Card("Startup") { Dock = DockStyle.Fill };
        var oF = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        _startWithWindows.CheckedChanged += (_, _) => { if (_loadingUi) return; try { SetStartWithWindows(_startWithWindows.Checked); } catch (Exception ex) { SetStatus("Could not change Start with Windows: " + ex.Message); } };
        _startMinimized.CheckedChanged += (_, _) => { if (_loadingUi) return; S.StartMinimized = _startMinimized.Checked; S.Save(); };
        _streamerMode.CheckedChanged += (_, _) => { if (!_loadingUi && _streamerMode.Checked != S.StreamerMode) SetStreamerMode(_streamerMode.Checked); };
        foreach (var c in new[] { _startWithWindows, _startMinimized, _streamerMode }) { c.Margin = new Padding(0, 4, 0, 6); oF.Controls.Add(c); }
        opts.Controls.Add(oF);
        col.Controls.Add(opts, 0, 1);

        col.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = Bg }, 0, 2);
        return col;
    }

    void LoadConnectionUi()
    {
        _peer.Text = S.PeerHost;
        _port.Text = S.Port.ToString();
        _pass.Text = S.Passphrase;
        _startMinimized.Checked = S.StartMinimized;
        _streamerMode.Checked = S.StreamerMode;
        _startWithWindows.Checked = GetStartWithWindows();
        ApplyRoleToUi();
        _portsInfo.Text = $"Ports: UDP {S.Port} (link), {Discovery.Port} (find PCs), {S.AudioPort} (audio)  ·  TCP {S.FilePort} (files)  ·  overlay pages on localhost:{S.OverlayPort}";
    }

    int ParsePort() => int.TryParse(_port.Text.Trim(), out var p) && p is >= 1024 and <= 65535 ? p : S.Port;

    void SaveAndApply()
    {
        if (!S.StreamerMode) S.PeerHost = _peer.Text.Trim();
        S.Port = ParsePort();
        _port.Text = S.Port.ToString();
        S.Passphrase = _pass.Text;
        S.Save();
        ApplyRuntime();
        SetStatus("Applied. " + Summary());
    }

    void SetRole(PcRole r)
    {
        ReleaseAllHeldTriggers(); ReleaseAllHeldActions("role changed");
        S.Role = r;
        S.ApplyRoleDefaults();
        S.Save();
        LoadSettingsIntoUi();
        ApplyRuntime();
        SetStatus(Summary());
    }

    void ApplyRoleToUi()
    {
        SetSegment(_segGaming, S.Role == PcRole.Gaming);
        SetSegment(_segStreaming, S.Role == PcRole.Streaming);
    }

    DateTime _rowsSentAt = DateTime.MinValue;

    void RefreshPeers()
    {
        if ((DateTime.UtcNow - _rowsSentAt).TotalSeconds >= 15) SendRows();   // so a PC that starts later catches up
        Disc.Prune();
        var peers = Disc.Peers.Values.OrderBy(p => p.Name).ToList();
        var sig = string.Join("|", peers.Select(p => p.ToString())) + S.StreamerMode;
        if (sig != _peerListSignature && !_peer.DroppedDown && !S.StreamerMode)
        {
            _peerListSignature = sig;
            var text = _peer.Text;
            _peer.BeginUpdate(); _peer.Items.Clear();
            foreach (var p in peers) _peer.Items.Add(p);
            _peer.EndUpdate();
            _peer.Text = text;
        }
        if (peers.Count == 0) _peerStatus.Text = "Looking for other PCs running KennelBridge…";
        else
        {
            var cur = S.StreamerMode ? S.PeerHost : _peer.Text.Trim();
            _peerStatus.Text = "Found: " + string.Join("  ·  ", peers.Select(p =>
                $"{(p.Ip == cur ? "✓ " : "")}{p.Name}{(S.StreamerMode ? "" : " " + p.Ip)} ({p.RoleText}{(p.Online ? "" : ", offline")})"));
        }
        if (S.PeerHost.Length == 0 && (S.StreamerMode || _peer.Text.Trim().Length == 0) && peers.Count > 0)
        {
            var pick = peers.FirstOrDefault(p => p.Online && p.Role != S.Role) ?? peers.FirstOrDefault(p => p.Online);
            if (pick != null)
            {
                if (!S.StreamerMode) _peer.Text = pick.Ip;
                S.PeerHost = pick.Ip;
                S.Save();
                UpdateTray();
                Activity($"Auto-detected {pick.Name} at {pick.Ip} - using it as the other PC.", flash: false);
                ApplyFileRuntime();
            }
        }
    }

    async Task TestLink()
    {
        var host = S.StreamerMode ? S.PeerHost : _peer.Text.Trim();
        if (host.Length == 0) { SetStatus("No other PC set yet."); return; }
        Link.Passphrase = _pass.Text;
        SetStatus("Testing link…");
        int ms = await Link.PingAsync(host, ParsePort());
        Activity(ms >= 0 ? $"✓ Link OK - {PeerLabel()} replied in {ms} ms." : $"✗ No reply from {Mask(host)}. Check the other PC's firewall and passphrase.", flash: ms >= 0);
    }

    void SetStreamerMode(bool on)
    {
        S.StreamerMode = on;
        S.Save();
        ApplyStreamerModeToUi();
        RefreshPeers();
        UpdateTray();
        Activity(on ? "Streamer mode on - IP addresses hidden on screen." : "Streamer mode off.", flash: false);
    }

    void ApplyStreamerModeToUi()
    {
        _localIps.Text = S.StreamerMode ? "This PC: (hidden)" : "This PC: " + LocalIps();
        if (S.StreamerMode) { _peer.Text = HiddenPeerText; _peer.Enabled = false; }
        else { _peer.Enabled = true; if (_peer.Text == HiddenPeerText) _peer.Text = S.PeerHost; }
        _peerListSignature = "";
        _peerStatus.Text = "";
        foreach (ListViewItem it in _activity.Items) it.SubItems[1].Text = Mask(it.SubItems[1].Text);
        _status.Text = Mask(_status.Text);
        _liveSource = Mask(_liveSource);
    }
}
