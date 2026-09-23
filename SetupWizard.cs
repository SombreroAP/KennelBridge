using static KennelBridge.Theme;

namespace KennelBridge;

/// <summary>First-run guide: role → network → find the other PC → bridges → (Discord) → done.</summary>
public sealed class SetupWizard : Form
{
    public enum Page { Role, Network, Find, Bridges, Discord, Done }

    readonly MainForm _host;
    Settings S => _host.S;
    List<Page> _steps = new();
    int _i;
    readonly Page? _startAt;

    readonly FlowLayoutPanel _crumbs = new() { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
    readonly Panel _content = new() { Dock = DockStyle.Fill, Margin = new Padding(0) };
    readonly Button _back = Theme.Button("Back");
    readonly Button _next = Theme.Button("Next", primary: true);
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };

    PcRole _role;
    OptionCard? _optGaming, _optStreaming;
    TextBox? _pass, _port;
    Label? _fwStatus;
    ListView? _found;
    TextBox? _manualIp;
    Label? _testResult;
    string _foundSig = "";
    CheckBox? _bOverlay, _bAudio, _bHotkeys, _bFiles;
    TextBox? _bFolder;
    CheckBox? _startWin, _startMin, _streamer;

    public SetupWizard(MainForm host, Page? startAt = null)
    {
        _host = host;
        _role = S.Role;
        _startAt = startAt;
        Text = "KennelBridge setup";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96f, 96f);
        ApplyWindow(this);
        ClientSize = new Size(780, 720);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(24, 18, 24, 16) };
        root.ColumnStyles.Add(Cpct(100));
        root.RowStyles.Add(Px(40)); root.RowStyles.Add(Pct(100)); root.RowStyles.Add(Px(48));
        root.Controls.Add(_crumbs, 0, 0);
        root.Controls.Add(_content, 0, 1);
        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = new Padding(0) };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); bottom.ColumnStyles.Add(Cpct(100)); bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _back.Anchor = AnchorStyles.Left; _back.Margin = new Padding(0, 8, 0, 0);
        _back.Click += (_, _) => Go(-1);
        var skip = Theme.Button("Finish later"); skip.Anchor = AnchorStyles.Right; skip.Margin = new Padding(0, 8, 8, 0);
        skip.Click += (_, _) => Close();
        _next.Anchor = AnchorStyles.Right; _next.Margin = new Padding(0, 8, 0, 0);
        _next.Click += (_, _) => Go(+1);
        bottom.Controls.Add(_back, 0, 0); bottom.Controls.Add(skip, 1, 0); bottom.Controls.Add(_next, 2, 0);
        root.Controls.Add(bottom, 0, 2);
        Controls.Add(root);

        _timer.Tick += (_, _) => RefreshFound();
        RebuildSteps();
        int start = _startAt is Page p && _steps.Contains(p) ? _steps.IndexOf(p) : 0;
        Show(start);
    }

    void RebuildSteps()
    {
        _steps = new() { Page.Role, Page.Network, Page.Find, Page.Bridges };
        if (_role == PcRole.Gaming || _startAt == Page.Discord) _steps.Add(Page.Discord);
        _steps.Add(Page.Done);
    }

    static string StepName(Page s) => s switch { Page.Role => "Role", Page.Network => "Network", Page.Find => "Find PC", Page.Bridges => "Bridges", Page.Discord => "Discord", _ => "Done" };

    void Go(int delta)
    {
        if (delta > 0 && !Commit(_steps[_i])) return;
        if (delta > 0 && _steps[_i] == Page.Done) { Close(); return; }
        Show(Math.Clamp(_i + delta, 0, _steps.Count - 1));
    }

    void Show(int i)
    {
        _i = i;
        var step = _steps[i];
        _crumbs.Controls.Clear();
        for (int k = 0; k < _steps.Count; k++)
        {
            var l = Theme.Label($"{k + 1}  {StepName(_steps[k])}", font: Semibold);
            l.ForeColor = k == i ? Amber : k < i ? Fg : Muted;
            l.Margin = new Padding(0, 8, 6, 0);
            _crumbs.Controls.Add(l);
            if (k < _steps.Count - 1) { var sep = Theme.Label("›", muted: true); sep.Margin = new Padding(0, 8, 6, 0); _crumbs.Controls.Add(sep); }
        }
        _content.Controls.Clear();
        _timer.Stop();
        switch (step)
        {
            case Page.Role: BuildRole(); break;
            case Page.Network: BuildNetwork(); break;
            case Page.Find: BuildFind(); _timer.Start(); break;
            case Page.Bridges: BuildBridges(); break;
            case Page.Discord: BuildDiscord(); break;
            case Page.Done: BuildDone(); break;
        }
        Theme.ApplyDpi(_content);
        _back.Enabled = i > 0;
        _next.Text = step == Page.Done ? "Finish" : "Next";
    }

    TableLayoutPanel PageLayout(string title, string intro, params int[] rows)
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = rows.Length + 2, Margin = new Padding(0) };
        t.ColumnStyles.Add(Cpct(100));
        t.RowStyles.Add(Px(44)); t.RowStyles.Add(Px(52));
        foreach (var r in rows) t.RowStyles.Add(r < 0 ? Pct(100) : Px(r));
        t.Controls.Add(Theme.Label(title, font: Big), 0, 0);
        t.Controls.Add(Wrapped(intro), 0, 1);
        _content.Controls.Add(t);
        return t;
    }

    // ---------- role ----------

    void BuildRole()
    {
        var t = PageLayout("Which PC is this?", "KennelBridge runs on both PCs. Pick the job of the one you are sitting at now; every bridge works out its direction from this.", 110, 110, -1);
        _optGaming = new OptionCard("Gaming PC", "Runs the games. Captures keyboard, mouse and controller for the overlay, sends game audio, receives the microphone and the finished recordings.") { Dock = DockStyle.Fill };
        _optStreaming = new OptionCard("Streaming PC", "Runs OBS. Serves the overlay pages, plays game audio in the headphones, sends the microphone and pushes recordings to the gaming PC.") { Dock = DockStyle.Fill };
        foreach (var (c, r) in new[] { (_optGaming, PcRole.Gaming), (_optStreaming, PcRole.Streaming) })
            c.Click += (_, _) => { _role = r; SyncRole(); };
        SyncRole();
        t.Controls.Add(_optGaming, 0, 2); t.Controls.Add(_optStreaming, 0, 3);
        t.Controls.Add(Wrapped("Hotkeys go both ways whatever you pick. Each bridge can be switched on or off on its own page afterwards."), 0, 4);
    }

    void SyncRole()
    {
        if (_optGaming != null) _optGaming.Selected = _role == PcRole.Gaming;
        if (_optStreaming != null) _optStreaming.Selected = _role == PcRole.Streaming;
    }

    // ---------- network ----------

    void BuildNetwork()
    {
        var t = PageLayout("Network", "Both PCs must be on the same network and share a passphrase. Windows Firewall also has to let KennelBridge in, or the PCs will not see each other.", 70, 70, 96, -1);
        var f1 = Row("Passphrase", out _pass, "same on both PCs - anything you like");
        _pass.Text = S.Passphrase;
        var gen = Theme.Button("Generate", minWidth: 90); gen.Margin = new Padding(8, 4, 0, 0);
        gen.Click += (_, _) => _pass.Text = NewPassphrase();
        f1.Controls.Add(gen, 2, 1);
        t.Controls.Add(f1, 0, 2);
        var f2 = Row("Link port (leave unless it clashes)", out _port, "");
        _port.Text = S.Port.ToString(); _port.Width = 100;
        t.Controls.Add(f2, 0, 3);
        var fw = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = new Padding(0, 10, 0, 0) };
        fw.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); fw.ColumnStyles.Add(Cpct(100));
        fw.RowStyles.Add(Px(40)); fw.RowStyles.Add(Px(40));
        var btn = Theme.Button("Allow through Windows Firewall", primary: true);
        btn.Click += (_, _) => { CommitNetwork(); bool ok = _host.AllowFirewall(); _fwStatus!.Text = ok ? "✓  Done - all four bridges can now be reached." : "Not added. You can retry, or do it later from the Connection page."; _fwStatus.ForeColor = ok ? Green : Red; };
        fw.Controls.Add(btn, 0, 0);
        _fwStatus = Theme.Label("Windows will ask for permission (UAC). One rule covers every bridge.", muted: true); _fwStatus.Margin = new Padding(12, 0, 0, 0);
        fw.Controls.Add(_fwStatus, 1, 0);
        var me = Theme.Label("This PC: " + _host.Mask(MainForm.LocalIps()) + "   ·   " + Environment.MachineName, muted: true, Small);
        fw.Controls.Add(me, 0, 1); fw.SetColumnSpan(me, 2);
        t.Controls.Add(fw, 0, 4);
    }

    static TableLayoutPanel Row(string label, out TextBox box, string placeholder)
    {
        var r = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, Margin = new Padding(0) };
        r.ColumnStyles.Add(Cpct(100)); r.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); r.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        r.RowStyles.Add(Px(24)); r.RowStyles.Add(Px(40));
        r.Controls.Add(Theme.Label(label, muted: true, Small), 0, 0);
        box = Theme.TextBox(placeholder); box.Dock = DockStyle.Fill;
        r.Controls.Add(box, 0, 1);
        return r;
    }

    static string NewPassphrase()
    {
        string[] words = { "kennel", "amber", "olive", "bone", "graphite", "wardog", "squad", "bulkhead", "sombrero", "radio", "muzzle", "collar", "patrol", "howl", "fetch", "kibble" };
        var r = Random.Shared;
        return $"{words[r.Next(words.Length)]}-{words[r.Next(words.Length)]}-{r.Next(10, 99)}";
    }

    // ---------- find ----------

    void BuildFind()
    {
        var t = PageLayout("Find the other PC", "Run KennelBridge on the other PC and finish its first two steps. It will appear below within a few seconds. Pick it, then test the link.", -1, 40, 44, 40);
        _found = new ListView { Dock = DockStyle.Fill };
        StyleList(_found);
        _found.Columns.Add("PC", 200); _found.Columns.Add("Address", 150); _found.Columns.Add("Role", 130); _found.Columns.Add("", 80);
        _found.SelectedIndexChanged += (_, _) => { if (_found.SelectedItems.Count > 0 && _found.SelectedItems[0].Tag is Peer p) _manualIp!.Text = p.Ip; };
        t.Controls.Add(_found, 0, 2);
        var m = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0, 6, 0, 0) };
        m.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); m.ColumnStyles.Add(Cpct(100));
        m.Controls.Add(Theme.Label("Or type its address:", muted: true), 0, 0);
        _manualIp = Theme.TextBox("192.168.x.x"); _manualIp.Dock = DockStyle.Fill; _manualIp.Text = S.StreamerMode ? "" : S.PeerHost;
        m.Controls.Add(_manualIp, 1, 0);
        t.Controls.Add(m, 0, 3);
        var tb = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0), Padding = new Padding(0, 6, 0, 0) };
        var test = Theme.Button("Test link", primary: true);
        test.Click += async (_, _) =>
        {
            CommitFind();
            _testResult!.Text = "Testing…"; _testResult.ForeColor = Muted;
            int ms = await _host.Link.PingAsync(S.PeerHost, S.Port);
            _testResult.Text = ms >= 0 ? $"✓  Connected - the other PC replied in {ms} ms." : "✗  No reply. On the other PC: same passphrase? firewall allowed? KennelBridge running?";
            _testResult.ForeColor = ms >= 0 ? Green : Red;
        };
        tb.Controls.Add(test);
        _testResult = Theme.Label("", muted: true); _testResult.Margin = new Padding(6, 8, 0, 0);
        tb.Controls.Add(_testResult);
        t.Controls.Add(tb, 0, 4);
        t.Controls.Add(Theme.Label("The same passphrase, port and other PC are used by every bridge.", muted: true, Small), 0, 5);
        RefreshFound();
    }

    void RefreshFound()
    {
        if (_found == null || IsDisposed) return;
        _host.Disc.Prune();
        var peers = _host.Disc.Peers.Values.OrderBy(p => p.Name).ToList();
        var sig = string.Join("|", peers.Select(p => p.ToString() + p.Online));
        if (sig == _foundSig) return;
        _foundSig = sig;
        var selected = _manualIp?.Text.Trim();
        _found.BeginUpdate();
        _found.Items.Clear();
        foreach (var p in peers)
        {
            var it = new ListViewItem(new[] { p.Name, _host.Mask(p.Ip), p.RoleText, p.Online ? "online" : "offline" }) { Tag = p };
            if (p.Ip == selected) it.Selected = true;
            _found.Items.Add(it);
        }
        if (peers.Count == 0) _found.Items.Add(new ListViewItem(new[] { "Searching…", "", "", "" }) { ForeColor = Muted });
        _found.EndUpdate();
    }

    // ---------- bridges ----------

    void BuildBridges()
    {
        bool gaming = _role == PcRole.Gaming;
        var t = PageLayout("Which bridges do you want?", $"This is the {(gaming ? "gaming" : "streaming")} PC. Tick what it should do; each has its own page in the main window for the details.", 56, 56, 56, 56, 40, -1);
        _bOverlay = Theme.Check(gaming ? "Input overlay - capture keyboard, mouse and controller here for the overlay in OBS" : "Input overlay - serve the overlay pages for OBS from this PC", gaming ? S.OverlayCapture : S.OverlayServer);
        _bAudio = Theme.Check(gaming ? "Audio - send game audio to the streaming PC and receive its microphone (needs VB-CABLE here)" : "Audio - play game audio in the headphones here and send the microphone", S.AudioEnabled);
        _bHotkeys = Theme.Check("Hotkeys - press a key here, the other PC presses too (push-to-mute, scene switches…)", S.HotkeysEnabled);
        _bFiles = Theme.Check(gaming ? "Files - receive finished recordings from the streaming PC into a folder here" : "Files - send finished OBS recordings from a folder here to the gaming PC", gaming ? S.FileReceiveEnabled : S.FileSendEnabled);
        foreach (var c in new[] { _bOverlay, _bAudio, _bHotkeys, _bFiles }) { c.Margin = new Padding(0, 10, 0, 0); c.AutoSize = false; c.Dock = DockStyle.Fill; }
        t.Controls.Add(_bOverlay, 0, 2); t.Controls.Add(_bAudio, 0, 3); t.Controls.Add(_bHotkeys, 0, 4); t.Controls.Add(_bFiles, 0, 5);
        var fr = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = new Padding(0, 4, 0, 0) };
        fr.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); fr.ColumnStyles.Add(Cpct(100)); fr.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        fr.Controls.Add(Theme.Label(gaming ? "Recordings folder here:" : "OBS recording folder:", muted: true), 0, 0);
        _bFolder = Theme.TextBox(); _bFolder.Dock = DockStyle.Fill; _bFolder.Text = gaming ? S.FileReceiveFolder : S.FileSendFolder;
        fr.Controls.Add(_bFolder, 1, 0);
        var br = Theme.Button("Browse…", minWidth: 90); br.Margin = new Padding(6, 4, 0, 0); br.MinimumSize = new Size(90, 30);
        br.Click += (_, _) => { using var d = new FolderBrowserDialog { ShowNewFolderButton = true }; if (_bFolder.Text.Length > 0 && Directory.Exists(_bFolder.Text)) d.SelectedPath = _bFolder.Text; if (d.ShowDialog(this) == DialogResult.OK) _bFolder.Text = d.SelectedPath; };
        fr.Controls.Add(br, 2, 0);
        t.Controls.Add(fr, 0, 6);
        t.Controls.Add(Wrapped(gaming
            ? "The audio bridge needs VB-CABLE on the gaming PC so the microphone can appear as a real mic; the Audio page explains and links to it."
            : "For the overlay, OBS gets one Browser source with the live URL from the Input Overlay page. Recordings are sent once OBS has finished writing them."), 0, 7);
    }

    // ---------- discord ----------

    static readonly (string label, bool hold, string discordAction, string blurb)[] DiscordActions =
    {
        ("Discord: Push to mute", true, "Push to Mute", "hold to mute yourself on Discord while you talk in-game"),
        ("Discord: Toggle mute", false, "Toggle Mute", "tap to mute / unmute on Discord"),
        ("Discord: Toggle deafen", false, "Toggle Deafen", "tap to deafen / undeafen on Discord"),
    };
    readonly Label?[] _dcState = new Label?[3];
    readonly Button?[] _dcTest = new Button?[3];
    Label? _dcLink;

    void BuildDiscord()
    {
        var t = PageLayout("Discord on the other PC",
            "Discord runs on the other PC with voice activity. Pick what you want to control, press the button you will use on THIS PC, then teach Discord that button. Skip this if you do not use Discord that way.",
            50, 50, 50, 26, -1);
        for (int i = 0; i < DiscordActions.Length; i++)
        {
            int idx = i;
            var (label, hold, _, blurb) = DiscordActions[i];
            var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, Margin = new Padding(0) };
            row.ColumnStyles.Add(Cpx(150)); row.ColumnStyles.Add(Cpx(120)); row.ColumnStyles.Add(Cpct(100)); row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.Controls.Add(Theme.Label(label.Replace("Discord: ", ""), font: Semibold), 0, 0);
            var set = Theme.Button(hold ? "Hold this…" : "Press this…", minWidth: 110);
            set.Margin = new Padding(0, 4, 8, 0);
            set.Click += (_, _) => AssignDiscord(idx);
            row.Controls.Add(set, 1, 0);
            var st = Theme.Label(blurb, muted: true);
            st.AutoSize = false; st.Dock = DockStyle.Fill; st.AutoEllipsis = true; st.TextAlign = ContentAlignment.MiddleLeft;
            _dcState[i] = st;
            row.Controls.Add(st, 2, 0);
            var test = Theme.Button("Test", minWidth: 60); test.Margin = new Padding(0, 4, 0, 0); test.Enabled = false;
            test.Click += (_, _) => { var b = S.Bindings.FirstOrDefault(x => x.Label == label); if (b != null) _host.SendTest(b); };
            _dcTest[i] = test;
            row.Controls.Add(test, 3, 0);
            t.Controls.Add(row, 0, 2 + i);
        }
        _dcLink = Theme.Label("", muted: true, Small);
        t.Controls.Add(_dcLink, 0, 5);
        var guide = new Card("Teach Discord the button (20 seconds, on the other PC)") { Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 0) };
        guide.Controls.Add(Wrapped(
            "1.  Discord  →  User Settings (cog)  →  Keybinds  →  Add a Keybind.\n" +
            "2.  Action: pick Push to Mute, Toggle Mute or Toggle Deafen.\n" +
            "3.  Click Record Keybind, then press the button HERE on this PC. Discord shows a key such as F13 - that is KennelBridge delivering your press.\n" +
            "4.  Click Stop Recording. Repeat for the others. Under Voice & Video keep Input Mode on Voice Activity.", muted: false));
        t.Controls.Add(guide, 0, 6);
        RefreshDiscordState();
        _ = CheckLinkForDiscord();
    }

    void AssignDiscord(int i)
    {
        var (label, hold, discordAction, _) = DiscordActions[i];
        var prompt = hold
            ? $"Press the key or mouse button you want to HOLD for {discordAction}. Mouse 4 or Mouse 5 work well."
            : $"Press the key or mouse button you want to use for {discordAction}.";
        var r = _host.PromptPress(discordAction, prompt);
        if (r == null) return;
        var b = _host.SetLabelledBinding(label, r.Value.kind, r.Value.vk, r.Value.mods, hold);
        if (b == null) MessageBox.Show(this, "That button is already used by another row. Pick a different one, or remove the other row first.", "KennelBridge", MessageBoxButtons.OK, MessageBoxIcon.Information);
        RefreshDiscordState();
    }

    void RefreshDiscordState()
    {
        for (int i = 0; i < DiscordActions.Length; i++)
        {
            var (label, _, _, blurb) = DiscordActions[i];
            var b = S.Bindings.FirstOrDefault(x => x.Label == label);
            if (_dcState[i] is not Label st || _dcTest[i] is not Button test) continue;
            st.Text = b == null ? blurb : $"✓  {b.TriggerText} here  →  Discord sees {b.ActionText}";
            st.ForeColor = b == null ? Muted : Green;
            test.Enabled = b != null;
        }
    }

    async Task CheckLinkForDiscord()
    {
        if (_dcLink == null) return;
        if (S.PeerHost.Length == 0) { _dcLink.Text = "No other PC chosen yet - go back to Find PC."; _dcLink.ForeColor = Red; return; }
        _dcLink.Text = "Checking the link to the other PC…";
        int ms = await _host.Link.PingAsync(S.PeerHost, S.Port);
        if (IsDisposed || _dcLink.IsDisposed) return;
        _dcLink.Text = ms >= 0
            ? $"✓  Linked to {_host.PeerName()} ({ms} ms). Presses you set here reach it right away."
            : "✗  The other PC is not answering yet - Discord cannot record the button until it does. Check it is on, same passphrase, firewall allowed.";
        _dcLink.ForeColor = ms >= 0 ? Green : Red;
    }

    // ---------- done ----------

    void BuildDone()
    {
        var t = PageLayout("All set", $"This PC is the {(_role == PcRole.Gaming ? "gaming" : "streaming")} PC. KennelBridge lives in the system tray; close the window and it keeps running.", 40, 40, 40, -1);
        _startWin = Theme.Check("Start with Windows", MainForm.GetStartWithWindows());
        _startMin = Theme.Check("Start minimized to the tray", S.StartMinimized);
        _streamer = Theme.Check("Streamer mode - hide IP addresses on screen (recommended)", S.StreamerMode);
        t.Controls.Add(_startWin, 0, 2); t.Controls.Add(_startMin, 0, 3); t.Controls.Add(_streamer, 0, 4);
        t.Controls.Add(Wrapped("Each bridge has its own page: Input Overlay (pick an overlay and copy the OBS URL), Audio (devices and VB-CABLE), Hotkeys (add presses), Files (folders). You can reopen this guide any time with the Setup wizard button."), 0, 5);
    }

    // ---------- commit ----------

    bool Commit(Page step)
    {
        switch (step)
        {
            case Page.Role:
                if (_role == PcRole.Unset) { MessageBox.Show(this, "Pick Gaming PC or Streaming PC to continue.", "KennelBridge", MessageBoxButtons.OK, MessageBoxIcon.Information); return false; }
                if (S.Role != _role) { S.Role = _role; S.ApplyRoleDefaults(); S.Save(); _host.ApplyFromWizard(); }
                RebuildSteps();
                return true;
            case Page.Network: CommitNetwork(); return true;
            case Page.Find: CommitFind(); return true;
            case Page.Bridges: CommitBridges(); return true;
            case Page.Discord: return true;
            case Page.Done:
                try { MainForm.SetStartWithWindows(_startWin!.Checked); } catch { }
                S.StartMinimized = _startMin!.Checked;
                S.StreamerMode = _streamer!.Checked;
                S.SetupDone = true;
                S.Save();
                _host.ApplyFromWizard();
                return true;
        }
        return true;
    }

    void CommitNetwork()
    {
        if (_pass == null) return;
        S.Passphrase = _pass.Text;
        if (int.TryParse(_port?.Text.Trim(), out var p) && p is >= 1024 and <= 65535) S.Port = p;
        S.Save();
        _host.ApplyFromWizard();
    }

    void CommitFind()
    {
        if (_manualIp == null) return;
        var ip = _manualIp.Text.Trim();
        if (ip.Length > 0 && ip != S.PeerHost) { S.PeerHost = ip; S.Save(); _host.RefreshFromSettings(); }
    }

    void CommitBridges()
    {
        if (_bOverlay == null) return;
        bool gaming = _role == PcRole.Gaming;
        if (gaming) S.OverlayCapture = _bOverlay.Checked; else S.OverlayServer = _bOverlay.Checked;
        S.AudioEnabled = _bAudio!.Checked;
        S.HotkeysEnabled = _bHotkeys!.Checked;
        var folder = _bFolder!.Text.Trim();
        if (gaming) { S.FileReceiveEnabled = _bFiles!.Checked; if (folder.Length > 0) S.FileReceiveFolder = folder; }
        else { S.FileSendEnabled = _bFiles!.Checked; if (folder.Length > 0) S.FileSendFolder = folder; }
        S.Save();
        _host.ApplyFromWizard();
    }

    protected override void OnFormClosed(FormClosedEventArgs e) { _timer.Stop(); base.OnFormClosed(e); }

    protected override void OnLoad(EventArgs e) { base.OnLoad(e); Theme.ApplyDpi(this); Theme.FitToScreen(this); }
    protected override void OnDpiChanged(DpiChangedEventArgs e) { base.OnDpiChanged(e); Theme.ApplyDpi(this); }
}
