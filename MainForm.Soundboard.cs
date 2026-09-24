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
    readonly ComboBox _sbHold = Theme.ComboBox();
    readonly ToolStripMenuItem _sbHoldMenu = new("Hold while playing");
    readonly Dictionary<string, (Binding b, int n)> _sbHolds = new();

    /// <summary>A choice in the "hold while playing" lists: a hotkey row, or none, or the board default.</summary>
    sealed record HoldChoice(string? Key, string Text) { public override string ToString() => Text; }
    CancellationTokenSource? _sbSearchCts;
    const string DefaultDevice = "Windows default output";

    Control BuildSoundboardPage()
    {
        var col = Rows(196, -1);

        var outCard = new Card("Output") { Dock = DockStyle.Fill };
        var oT = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2 };
        oT.ColumnStyles.Add(Cpx(110)); oT.ColumnStyles.Add(Cpct(60)); oT.ColumnStyles.Add(Cpx(80)); oT.ColumnStyles.Add(Cpct(40));
        oT.RowStyles.Add(Px(40)); oT.RowStyles.Add(Px(40)); oT.RowStyles.Add(Px(44));
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
        oT.Controls.Add(Theme.Label("While playing"), 0, 2);
        _sbHold.Dock = DockStyle.Fill; _sbHold.DropDownStyle = ComboBoxStyle.DropDownList; _sbHold.Margin = new Padding(0, 6, 0, 4);
        _sbHold.DropDown += (_, _) => FillHoldChoices();
        _sbHold.SelectedIndexChanged += (_, _) => { if (_loadingUi || _sbHold.SelectedItem is not HoldChoice c) return; S.SoundHoldAction = c.Key ?? ""; S.Save(); };
        oT.Controls.Add(_sbHold, 1, 2);
        var holdHint = Theme.Label("held on the other PC, e.g. the game's proximity-chat key", muted: true, Small);
        holdHint.AutoSize = false; holdHint.Dock = DockStyle.Fill; holdHint.AutoEllipsis = true; holdHint.TextAlign = ContentAlignment.MiddleLeft;
        oT.Controls.Add(holdHint, 2, 2); oT.SetColumnSpan(holdHint, 2);
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
        _sbMenu.Items.Add(_sbHoldMenu);
        _sbMenu.Opening += (_, _) => FillHoldMenu();
        _sbMenu.Items.Add("Copy credit line", null, (_, _) => { if (_sbMenu.Tag is SoundInfo s) { try { Clipboard.SetText(s.Attribution); SetStatus("Credit copied."); } catch { } } });
        _sbMenu.Items.Add("Remove from board", null, (_, _) => { if (_sbMenu.Tag is SoundInfo s) { S.Sounds.RemoveAll(x => x.Id == s.Id); S.Save(); RefreshBoard(); } });
        split.Controls.Add(board, 0, 0);

        var find = new Card("Find sounds") { Dock = DockStyle.Fill, Margin = new Padding(8, 0, 0, 0), Hint = "Creative Commons, via Openverse" };
        var fT = Rows(40, 112, -1, 42, 26);
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
        var pop = Theme.Button("Popular", primary: true, minWidth: 0); pop.MinimumSize = new Size(0, 28); pop.Font = Small; pop.Margin = new Padding(0, 0, 6, 6); pop.Padding = new Padding(8, 0, 8, 0);
        pop.Click += (_, _) => { _sbQuery.Text = ""; ShowPopular(); };
        chips.Controls.Add(pop);
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
        FillHoldChoices();
        SyncVolumeUi();
        RefreshBoard();
        if (_sbResults.Items.Count == 0) ShowPopular();
    }

    /// <summary>The built-in popular list: what people see before they search.</summary>
    void ShowPopular()
    {
        _sbSearchCts?.Cancel();
        FillResults(Soundboard.Popular());
        _sbStatus.Text = "Popular sounds. Double-click to add, or search for anything else.";
    }

    void FillResults(List<SoundInfo> list)
    {
        _sbResults.BeginUpdate(); _sbResults.Items.Clear();
        foreach (var s in list)
            _sbResults.Items.Add(new ListViewItem(new[] { s.ShortTitle + (S.Sounds.Any(x => x.Id == s.Id) ? "   ✓" : ""), s.LengthText, s.License }) { Tag = s });
        _sbResults.EndUpdate();
    }

    IEnumerable<HoldChoice> HoldRows() => S.Bindings.GroupBy(b => b.ActionKey).Select(g => new HoldChoice(g.Key, $"Hold {g.First().DisplayAction}"));

    void FillHoldChoices()
    {
        bool loading = _loadingUi; _loadingUi = true;
        try
        {
            var items = new List<HoldChoice> { new("", "Hold nothing") };
            items.AddRange(HoldRows());
            if (S.Bindings.Count == 0) items.Add(new("", "(add rows on the Hotkeys page first)"));
            _sbHold.Items.Clear(); foreach (var i in items) _sbHold.Items.Add(i);
            _sbHold.SelectedItem = items.FirstOrDefault(i => i.Key == S.SoundHoldAction && i.Text != "(add rows on the Hotkeys page first)") ?? items[0];
        }
        finally { _loadingUi = loading; }
    }

    /// <summary>Right-click → Hold while playing: this sound's own choice, over the board default.</summary>
    void FillHoldMenu()
    {
        _sbHoldMenu.DropDownItems.Clear();
        if (_sbMenu.Tag is not SoundInfo s) return;
        var choices = new List<HoldChoice> { new(null, "Board default"), new("", "Nothing") };
        choices.AddRange(HoldRows());
        foreach (var c in choices)
        {
            var it = new ToolStripMenuItem(c.Text) { Checked = s.HoldAction == c.Key };
            it.Click += (_, _) => { s.HoldAction = c.Key; S.Save(); };
            _sbHoldMenu.DropDownItems.Add(it);
        }
    }

    Binding? ResolveHold(SoundInfo s)
    {
        var key = s.HoldAction ?? S.SoundHoldAction;
        return string.IsNullOrEmpty(key) ? null : S.Bindings.FirstOrDefault(b => b.ActionKey == key);
    }

    // ---- holding a key on the other PC for as long as sounds play ----
    // Same protocol as a hold row on the Hotkeys page: Down, a Down every 100 ms to keep it alive (the other
    // PC releases by itself after 500 ms of silence, so nothing sticks), then Up twice. Overlapping sounds
    // that hold the same key share one hold.

    void BeginHold(Binding b)
    {
        var k = b.ActionKey;
        if (_sbHolds.TryGetValue(k, out var h)) { _sbHolds[k] = (h.b, h.n + 1); return; }
        _sbHolds[k] = (b, 1);
        Link.SendHotkey(S.PeerHost, S.Port, b, PressState.Down);
        Activity($"→  holding {b.ActionText} on {PeerName()} while the sound plays", flash: false);
        _ = KeepHold(k);
    }

    async Task KeepHold(string k)
    {
        while (_sbHolds.TryGetValue(k, out var h))
        {
            await Task.Delay(100);
            if (_sbHolds.ContainsKey(k)) Link.SendHotkey(S.PeerHost, S.Port, h.b, PressState.Down);
        }
    }

    async void EndHold(Binding b)
    {
        await Task.Delay(200);   // let the tail of the sound through before the channel closes
        var k = b.ActionKey;
        if (!_sbHolds.TryGetValue(k, out var h)) return;
        if (h.n > 1) { _sbHolds[k] = (h.b, h.n - 1); return; }
        _sbHolds.Remove(k);
        Link.SendHotkey(S.PeerHost, S.Port, b, PressState.Up);
        Link.SendHotkey(S.PeerHost, S.Port, b, PressState.Up);
        Activity($"→  released {b.ActionText} on {PeerName()}", flash: false);
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
            b.Click += (_, _) => _ = PlaySound(s, broadcast: S.SoundBothPcs, origin: true);
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
            FillResults(list);
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
    async Task PlaySound(SoundInfo s, bool broadcast, bool preview = false, bool origin = false)
    {
        if (!S.Enabled && !preview) { SetStatus("Paused - resume to play sounds."); return; }
        if (!S.SoundboardEnabled && !preview) { SetStatus("The soundboard is off."); return; }
        if (broadcast && S.PeerHost.Length > 0) Link.SendSound(S.PeerHost, S.Port, JsonSerializer.Serialize(s));
        try
        {
            if (!Soundboard.IsCached(s)) SetStatus($"Downloading {s.ShortTitle}…");
            var path = await Soundboard.EnsureAsync(s);
            // only the PC where the button was pressed holds the key, so "both PCs" never presses it twice
            var hold = origin && !preview && S.PeerHost.Length > 0 ? ResolveHold(s) : null;
            if (hold != null) { BeginHold(hold); await Task.Delay(150); }   // open push-to-talk before the first syllable
            try { Soundboard.Play(path, preview ? null : S.SoundDeviceId, S.SoundVolume / 100f, hold == null ? null : () => BeginInvoke(() => EndHold(hold))); }
            catch { if (hold != null) EndHold(hold); throw; }
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
