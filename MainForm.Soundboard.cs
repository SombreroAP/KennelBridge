using System.Net;
using System.Text.Json;
using KennelBridge.Audio;
using static KennelBridge.Theme;

namespace KennelBridge;

/// <summary>Soundboard page: find sounds, keep them on a board, play them into the mic on both PCs.</summary>
public sealed partial class MainForm
{
    readonly CheckBox _sbEnabled = Theme.Check("Soundboard on");
    readonly CheckBox _sbBoth = Theme.Check("Play on both PCs");
    readonly ComboBox _sbDevice = Theme.ComboBox();
    readonly Button[] _sbVol = { Theme.Button("25 %", minWidth: 0), Theme.Button("50 %", minWidth: 0), Theme.Button("75 %", minWidth: 0), Theme.Button("100 %", minWidth: 0) };
    readonly FlowLayoutPanel _sbBoard = new() { Dock = DockStyle.Fill, AutoScroll = true, Margin = new Padding(0), Padding = new Padding(0, 4, 0, 0) };
    readonly Label _sbEmpty = Theme.Label("Nothing here yet. Find a sound on the right and add it.", muted: true);
    readonly TextBox _sbQuery = Theme.TextBox("Search sounds, e.g. air horn");
    readonly ListView _sbResults = new();
    readonly Label _sbStatus = Theme.Label("", muted: true, Theme.Small);
    readonly ContextMenuStrip _sbMenu = new();
    CancellationTokenSource? _sbSearchCts;
    const string DefaultDevice = "Windows default output";

    Control BuildSoundboardPage()
    {
        var col = Rows(150, -1);

        var outCard = new Card("Output") { Dock = DockStyle.Fill };
        var oT = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2 };
        oT.ColumnStyles.Add(Cpx(110)); oT.ColumnStyles.Add(Cpct(60)); oT.ColumnStyles.Add(Cpx(80)); oT.ColumnStyles.Add(Cpct(40));
        oT.RowStyles.Add(Px(40)); oT.RowStyles.Add(Px(40));
        var tg = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
        _sbEnabled.CheckedChanged += (_, _) => { if (_loadingUi) return; S.SoundboardEnabled = _sbEnabled.Checked; S.Save(); };
        _sbBoth.CheckedChanged += (_, _) => { if (_loadingUi) return; S.SoundBothPcs = _sbBoth.Checked; S.Save(); };
        tg.Controls.Add(_sbEnabled); tg.Controls.Add(_sbBoth);
        tg.Controls.Add(On(Theme.Button("Stop all sounds"), Soundboard.StopAll));
        oT.Controls.Add(tg, 0, 0); oT.SetColumnSpan(tg, 4);
        oT.Controls.Add(Theme.Label("Play into"), 0, 1);
        _sbDevice.Dock = DockStyle.Fill; _sbDevice.DropDownStyle = ComboBoxStyle.DropDownList;
        _sbDevice.DropDown += (_, _) => FillSoundDevices();
        _sbDevice.SelectedIndexChanged += (_, _) => { if (_loadingUi) return; S.SoundDeviceId = (_sbDevice.SelectedItem as AudioDeviceInfo)?.Id; S.SoundDeviceSet = true; S.Save(); };
        oT.Controls.Add(_sbDevice, 1, 1);
        oT.Controls.Add(Theme.Label("Volume", muted: true), 2, 1);
        var vol = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, Margin = new Padding(0, 3, 0, 3) };
        for (int i = 0; i < 4; i++)
        {
            vol.ColumnStyles.Add(Cpct(25));
            int v = (i + 1) * 25; var b = _sbVol[i];
            b.Dock = DockStyle.Fill; b.AutoSize = false; b.Margin = new Padding(0, 0, 4, 0);
            b.Click += (_, _) => { S.SoundVolume = v; S.Save(); SyncVolumeUi(); };
            vol.Controls.Add(b, i, 0);
        }
        oT.Controls.Add(vol, 3, 1);
        outCard.Controls.Add(oT);
        col.Controls.Add(outCard, 0, 0);

        var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0) };
        split.ColumnStyles.Add(Cpct(50)); split.ColumnStyles.Add(Cpct(50));
        split.RowStyles.Add(Pct(100));

        var board = new Card("Your board") { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 8, 0), Hint = "right-click a sound for more" };
        var bT = Rows(-1, 28);
        bT.Controls.Add(_sbBoard, 0, 0);
        _sbEmpty.Margin = new Padding(2, 8, 0, 0);
        bT.Controls.Add(Theme.Label("Sounds are saved on this PC after the first play.", muted: true, Theme.Small), 0, 1);
        board.Controls.Add(bT);
        _sbMenu.Items.Add("Preview on this PC only", null, (_, _) => { if (_sbMenu.Tag is SoundInfo s) _ = PlaySound(s, broadcast: false, preview: true); });
        _sbMenu.Items.Add("Copy credit line", null, (_, _) => { if (_sbMenu.Tag is SoundInfo s) { try { Clipboard.SetText(s.Attribution); SetStatus("Credit copied."); } catch { } } });
        _sbMenu.Items.Add("Remove from board", null, (_, _) => { if (_sbMenu.Tag is SoundInfo s) { S.Sounds.RemoveAll(x => x.Id == s.Id); S.Save(); RefreshBoard(); } });
        split.Controls.Add(board, 0, 0);

        var find = new Card("Find sounds") { Dock = DockStyle.Fill, Margin = new Padding(8, 0, 0, 0), Hint = "Creative Commons, via Openverse" };
        var fT = Rows(40, 76, -1, 42, 26);
        var sr = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0) };
        sr.ColumnStyles.Add(Cpct(100)); sr.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _sbQuery.Dock = DockStyle.Fill;
        _sbQuery.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; _ = Search(_sbQuery.Text); } };
        sr.Controls.Add(_sbQuery, 0, 0);
        var go = Theme.Button("Search", primary: true); go.Margin = new Padding(8, 2, 0, 0);
        go.Click += (_, _) => _ = Search(_sbQuery.Text);
        sr.Controls.Add(go, 1, 0);
        fT.Controls.Add(sr, 0, 0);
        var chips = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0), Padding = new Padding(0, 4, 0, 0) };
        foreach (var c in Soundboard.Categories)
        {
            var b = Theme.Button(c, minWidth: 0); b.MinimumSize = new Size(0, 28); b.Font = Small; b.Margin = new Padding(0, 0, 6, 6);
            b.Padding = new Padding(8, 0, 8, 0);
            b.Click += (_, _) => { _sbQuery.Text = c; _ = Search(c); };
            chips.Controls.Add(b);
        }
        fT.Controls.Add(chips, 0, 1);
        StyleList(_sbResults);
        _sbResults.Dock = DockStyle.Fill;
        _sbResults.Columns.Add("Sound", 220); _sbResults.Columns.Add("Length", 70); _sbResults.Columns.Add("Licence", 90);
        _sbResults.Resize += (_, _) => FitColumns(_sbResults);
        _sbResults.DoubleClick += (_, _) => AddSelected();
        fT.Controls.Add(_sbResults, 0, 2);
        var fb = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0), Padding = new Padding(0, 6, 0, 0) };
        fb.Controls.Add(On(Theme.Button("Add to board", primary: true), AddSelected));
        fb.Controls.Add(On(Theme.Button("Preview"), () => { if (_sbResults.SelectedItems.Count > 0 && _sbResults.SelectedItems[0].Tag is SoundInfo s) _ = PlaySound(s, broadcast: false, preview: true); }));
        fT.Controls.Add(fb, 0, 3);
        _sbStatus.AutoSize = false; _sbStatus.Dock = DockStyle.Fill; _sbStatus.AutoEllipsis = true; _sbStatus.TextAlign = ContentAlignment.MiddleLeft;
        fT.Controls.Add(_sbStatus, 0, 4);
        find.Controls.Add(fT);
        split.Controls.Add(find, 1, 0);
        col.Controls.Add(split, 0, 1);
        return col;
    }

    void WireSoundboard() => Link.SoundReceived += (json, from) => { if (IsHandleCreated) BeginInvoke(() => OnSoundFromPeer(json, from)); };

    void LoadSoundboardUi()
    {
        _sbEnabled.Checked = S.SoundboardEnabled;
        _sbBoth.Checked = S.SoundBothPcs;
        FillSoundDevices();
        SyncVolumeUi();
        RefreshBoard();
    }

    void SyncVolumeUi() { for (int i = 0; i < 4; i++) SetSegment(_sbVol[i], S.SoundVolume == (i + 1) * 25); }

    /// <summary>Output list; the gaming PC defaults to CABLE Input so the game and Discord hear the sound in the mic.</summary>
    void FillSoundDevices()
    {
        bool loading = _loadingUi; _loadingUi = true;
        try
        {
            var devs = new List<AudioDeviceInfo> { new(null!, DefaultDevice, false) };
            try { devs.AddRange(WindowsAudioDevices.GetRenderDevices()); } catch { }
            if (!S.SoundDeviceSet && S.Role == PcRole.Gaming && _virtualMic.InputEndpoint != null) S.SoundDeviceId = _virtualMic.InputEndpoint.Id;
            _sbDevice.Items.Clear(); foreach (var d in devs) _sbDevice.Items.Add(d);
            _sbDevice.DisplayMember = "Name";
            _sbDevice.SelectedItem = devs.FirstOrDefault(d => d.Id == S.SoundDeviceId) ?? devs[0];
        }
        finally { _loadingUi = loading; }
    }

    void RefreshBoard()
    {
        _sbBoard.SuspendLayout();
        _sbBoard.Controls.Clear();
        if (S.Sounds.Count == 0) _sbBoard.Controls.Add(_sbEmpty);
        foreach (var s in S.Sounds)
        {
            var b = Theme.Button(s.ShortTitle, minWidth: 0);
            b.AutoSize = false; b.Size = new Size(Theme.S(this, 150), Theme.S(this, 48)); b.Margin = new Padding(0, 0, 8, 8);
            b.Tag = s;
            new ToolTip().SetToolTip(b, $"{s.Title}\n{s.LengthText} · {s.License}\n{s.Attribution}");
            b.Click += (_, _) => _ = PlaySound(s, broadcast: S.SoundBothPcs);
            b.MouseUp += (_, e) => { if (e.Button == MouseButtons.Right) { _sbMenu.Tag = s; _sbMenu.Show(b, e.Location); } };
            _sbBoard.Controls.Add(b);
        }
        _sbBoard.ResumeLayout();
    }

    async Task Search(string q)
    {
        q = q.Trim();
        if (q.Length == 0) return;
        _sbSearchCts?.Cancel();
        var cts = _sbSearchCts = new CancellationTokenSource();
        _sbStatus.Text = $"Searching for \"{q}\"…";
        try
        {
            var list = await Soundboard.SearchAsync(q, 1, cts.Token);
            if (cts.IsCancellationRequested) return;
            _sbResults.BeginUpdate(); _sbResults.Items.Clear();
            foreach (var s in list)
                _sbResults.Items.Add(new ListViewItem(new[] { s.ShortTitle + (S.Sounds.Any(x => x.Id == s.Id) ? "   ✓" : ""), s.LengthText, s.License }) { Tag = s });
            _sbResults.EndUpdate();
            _sbStatus.Text = list.Count == 0 ? $"Nothing for \"{q}\". Try a simpler word." : $"{list.Count} sounds. Double-click to add; Preview plays it on this PC only.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _sbStatus.Text = "Search failed: " + ex.Message; }
    }

    void AddSelected()
    {
        if (_sbResults.SelectedItems.Count == 0 || _sbResults.SelectedItems[0].Tag is not SoundInfo s) { _sbStatus.Text = "Pick a sound in the list first."; return; }
        if (S.Sounds.Any(x => x.Id == s.Id)) { _sbStatus.Text = "Already on your board."; return; }
        S.Sounds.Add(s); S.Save();
        RefreshBoard();
        _sbResults.SelectedItems[0].Text = s.ShortTitle + "   ✓";
        _sbStatus.Text = $"Added {s.ShortTitle}. Downloading it now so it plays instantly…";
        _ = Soundboard.EnsureAsync(s).ContinueWith(t => BeginInvoke(() => _sbStatus.Text = t.IsFaulted ? "Download failed: " + t.Exception?.GetBaseException().Message : $"{s.ShortTitle} is saved on this PC."));
    }

    /// <summary>Play here and, when asked, tell the other PC to play it too (it downloads the sound itself the first time).</summary>
    async Task PlaySound(SoundInfo s, bool broadcast, bool preview = false)
    {
        if (!S.Enabled && !preview) { SetStatus("Paused - resume to play sounds."); return; }
        if (!S.SoundboardEnabled && !preview) { SetStatus("The soundboard is off."); return; }
        if (broadcast && S.PeerHost.Length > 0) Link.SendSound(S.PeerHost, S.Port, JsonSerializer.Serialize(s));
        try
        {
            if (!Soundboard.IsCached(s)) SetStatus($"Downloading {s.ShortTitle}…");
            var path = await Soundboard.EnsureAsync(s);
            Soundboard.Play(path, preview ? null : S.SoundDeviceId, S.SoundVolume / 100f);
            if (!preview) Activity($"Sound: {s.ShortTitle}{(broadcast ? "  (and on " + PeerName() + ")" : "")}", flash: true);
        }
        catch (Exception ex) { Activity($"Sound {s.ShortTitle} could not play: {ex.Message}", flash: false); }
    }

    void OnSoundFromPeer(string json, IPEndPoint from)
    {
        if (!S.Enabled || !S.SoundboardEnabled || !S.SoundBothPcs) return;
        SoundInfo? s;
        try { s = JsonSerializer.Deserialize<SoundInfo>(json); } catch { return; }
        if (s == null || s.Url.Length == 0 || !Uri.TryCreate(s.Url, UriKind.Absolute, out var u) || u.Scheme != "https") return;
        if (!S.Sounds.Any(x => x.Id == s.Id)) { S.Sounds.Add(s); S.Save(); RefreshBoard(); }   // keep both boards the same
        _ = PlaySound(s, broadcast: false);
    }
}
