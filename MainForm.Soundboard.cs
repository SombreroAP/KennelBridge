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
    readonly CheckBox _sbMuteMic = Theme.Check("Mute my mic while a sound plays");
    int _micMuteCount;
    /// <summary>Bumped by Stop all: a press still downloading or waiting for push-to-talk sees it changed and never starts.</summary>
    int _sbStopGen;
    bool _micMutedByUs;
    readonly ComboBox _sbDevice = Theme.ComboBox();
    readonly ComboBox _sbDevice2 = Theme.ComboBox();
    const string NoSecond = "Nothing";
    /// <summary>"Play into" value meaning: mix into the microphone the audio bridge sends to the gaming PC.</summary>
    const string MicTarget = "mic:";
    bool PlaysIntoMic => S.Role == PcRole.Streaming && S.SoundDeviceId == MicTarget;
    readonly Button[] _sbVol = { Theme.Button("25 %", minWidth: 0), Theme.Button("50 %", minWidth: 0), Theme.Button("75 %", minWidth: 0), Theme.Button("100 %", minWidth: 0) };
    readonly FlowLayoutPanel _sbBoard = new() { Dock = DockStyle.Fill, AutoScroll = true, Margin = new Padding(0), Padding = new Padding(0, 4, 0, 0) };
    readonly Label _sbEmpty = Theme.Label("Nothing here yet. Find a sound on the right and add it.", muted: true);
    readonly TextBox _sbQuery = Theme.TextBox("Search sounds, e.g. air horn");
    readonly ListView _sbResults = new();
    readonly Label _sbStatus = Theme.Label("", muted: true, Theme.Small);
    readonly ContextMenuStrip _sbMenu = new();
    readonly ComboBox _sbHold = Theme.ComboBox();
    readonly CheckBox _sbHoldHere = Theme.Check("Also press it on this PC");
    readonly ToolStripMenuItem _sbHoldMenu = new("Hold while playing");
    readonly Dictionary<string, (Binding b, bool local, int n)> _sbHolds = new();
    /// <summary>The other PC's hotkey rows, as it last reported them. Their keys are pressed on THIS PC.</summary>
    List<Binding> _peerRows = new();
    const string LocalPrefix = "here:";   // hold key for an other-PC row: its action is performed on this PC

    /// <summary>A choice in the "hold while playing" lists: a hotkey row, or none, or the board default.</summary>
    sealed record HoldChoice(string? Key, string Text) { public override string ToString() => Text; }
    CancellationTokenSource? _sbSearchCts;
    const string DefaultDevice = "Windows default output";

    Control BuildSoundboardPage()
    {
        var col = Rows(240, -1);

        var outCard = new Card("Output") { Dock = DockStyle.Fill };
        var oT = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2 };
        oT.ColumnStyles.Add(Cpx(110)); oT.ColumnStyles.Add(Cpct(60)); oT.ColumnStyles.Add(Cpx(80)); oT.ColumnStyles.Add(Cpct(40));
        oT.RowStyles.Add(Px(40)); oT.RowStyles.Add(Px(40)); oT.RowStyles.Add(Px(44)); oT.RowStyles.Add(Px(44));
        var tg = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
        _sbEnabled.CheckedChanged += (_, _) => { if (_loadingUi) return; S.SoundboardEnabled = _sbEnabled.Checked; S.Save(); };
        _sbBoth.CheckedChanged += (_, _) => { if (_loadingUi) return; S.SoundBothPcs = _sbBoth.Checked; S.Save(); };
        _sbMuteMic.CheckedChanged += (_, _) => { if (_loadingUi) return; S.SoundMuteMic = _sbMuteMic.Checked; S.Save(); };
        tg.Controls.Add(_sbEnabled); tg.Controls.Add(_sbBoth); tg.Controls.Add(_sbMuteMic);
        tg.Controls.Add(On(Theme.Button("Stop all sounds"), () => StopAllSounds(broadcast: true)));
        oT.Controls.Add(tg, 0, 0); oT.SetColumnSpan(tg, 4);
        oT.Controls.Add(Theme.Label("Play into"), 0, 1);
        _sbDevice.Dock = DockStyle.Fill; _sbDevice.DropDownStyle = ComboBoxStyle.DropDownList;
        _sbDevice.DropDown += (_, _) => FillSoundDevices();
        _sbDevice.SelectedIndexChanged += (_, _) => { if (_loadingUi) return; S.SoundDeviceId = (_sbDevice.SelectedItem as AudioDeviceInfo)?.Id; S.SoundDeviceSet = true; S.Save(); };
        oT.Controls.Add(_sbDevice, 1, 1);
        oT.Controls.Add(Theme.Label("Also play into"), 0, 3);
        _sbDevice2.Dock = DockStyle.Fill; _sbDevice2.DropDownStyle = ComboBoxStyle.DropDownList; _sbDevice2.Margin = new Padding(0, 6, 0, 4);
        _sbDevice2.DropDown += (_, _) => FillSoundDevices();
        _sbDevice2.SelectedIndexChanged += (_, _) => { if (_loadingUi) return; S.SoundDevice2Id = (_sbDevice2.SelectedItem as AudioDeviceInfo)?.Id; S.Save(); };
        oT.Controls.Add(_sbDevice2, 1, 3);
        var d2hint = Theme.Label("optional, e.g. a virtual cable your mic also feeds", muted: true, Small);
        d2hint.AutoSize = false; d2hint.Dock = DockStyle.Fill; d2hint.AutoEllipsis = true; d2hint.TextAlign = ContentAlignment.MiddleLeft; d2hint.Margin = new Padding(12, 0, 0, 0);
        oT.Controls.Add(d2hint, 2, 3); oT.SetColumnSpan(d2hint, 2);
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
        _sbHoldHere.Margin = new Padding(12, 8, 0, 0);
        _sbHoldHere.CheckedChanged += (_, _) => { if (_loadingUi) return; S.SoundHoldAlsoHere = _sbHoldHere.Checked; S.Save(); };
        oT.Controls.Add(_sbHoldHere, 2, 2); oT.SetColumnSpan(_sbHoldHere, 2);
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

    void WireSoundboard()
    {
        Link.SoundReceived += (json, from) => { if (IsHandleCreated) BeginInvoke(() => OnSoundFromPeer(json, from)); };
        Link.RowsReceived += (json, _) => { if (IsHandleCreated) BeginInvoke(() => { try { _peerRows = JsonSerializer.Deserialize<List<Binding>>(json) ?? new(); } catch { } }); };
    }

    /// <summary>Share this PC's hotkey rows with the other PC, so its soundboard can offer them too.</summary>
    void SendRows()
    {
        _rowsSentAt = DateTime.UtcNow;
        if (S.PeerHost.Length == 0) return;
        var rows = S.Bindings.Select(b => new Binding { Kind = b.Kind, ActionVk = b.ActionVk, ActionMods = b.ActionMods, Label = b.Label, TriggerKind = b.TriggerKind, TriggerVk = b.TriggerVk, TriggerMods = b.TriggerMods, Hold = b.Hold }).ToList();
        Link.SendRows(S.PeerHost, S.Port, JsonSerializer.Serialize(rows));
    }

    void LoadSoundboardUi()
    {
        _sbEnabled.Checked = S.SoundboardEnabled;
        _sbBoth.Checked = S.SoundBothPcs;
        FillSoundDevices();
        FillHoldChoices();
        _sbHoldHere.Checked = S.SoundHoldAlsoHere;
        _sbMuteMic.Checked = S.SoundMuteMic;
        _sbHoldHere.Visible = S.Role != PcRole.Gaming;   // on the gaming PC the key is pressed here anyway
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

    /// <summary>Every key from both PCs' Hotkeys lists, once each. Whatever is picked is held on the gaming PC.</summary>
    IEnumerable<HoldChoice> HoldRows()
        => S.Bindings.Concat(_peerRows).GroupBy(b => b.ActionKey).Select(g => new HoldChoice(g.Key, $"Hold {g.First().DisplayAction}"));

    static string Bare(string? key) => key != null && key.StartsWith(LocalPrefix) ? key[LocalPrefix.Length..] : key ?? "";

    void FillHoldChoices()
    {
        bool loading = _loadingUi; _loadingUi = true;
        try
        {
            var items = new List<HoldChoice> { new("", "Hold nothing") };
            items.AddRange(HoldRows());
            if (S.Bindings.Count == 0 && _peerRows.Count == 0) items.Add(new("", "(add rows on the Hotkeys page first)"));
            _sbHold.Items.Clear(); foreach (var i in items) _sbHold.Items.Add(i);
            var sel = items.FirstOrDefault(i => i.Key == Bare(S.SoundHoldAction) && i.Text != "(add rows on the Hotkeys page first)");
            if (sel == null && S.SoundHoldAction.Length > 0) { sel = new HoldChoice(S.SoundHoldAction, "(a row the other PC has not reported yet)"); items.Add(sel); _sbHold.Items.Add(sel); }
            _sbHold.SelectedItem = sel ?? items[0];
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
            var it = new ToolStripMenuItem(c.Text) { Checked = (s.HoldAction == null ? null : Bare(s.HoldAction)) == c.Key };
            it.Click += (_, _) => { s.HoldAction = c.Key; S.Save(); };
            _sbHoldMenu.DropDownItems.Add(it);
        }
    }

    Binding? ResolveHold(SoundInfo s)
    {
        var key = s.HoldAction == null ? Bare(S.SoundHoldAction) : Bare(s.HoldAction);
        if (key.Length == 0) return null;
        var r = S.Bindings.Concat(_peerRows).FirstOrDefault(b => b.ActionKey == key);
        if (r == null) { var p = key.Split(':'); if (p.Length == 3 && int.TryParse(p[0], out var kind) && int.TryParse(p[1], out var vk) && int.TryParse(p[2], out var mods)) r = new Binding { Kind = (ActionKind)kind, ActionVk = vk, ActionMods = mods }; }
        return r;
    }

    /// <summary>Where the key goes: always the gaming PC (here if this is it), plus this PC when asked.</summary>
    List<bool> HoldTargets()
    {
        var t = new List<bool>();                       // true = press on this PC, false = hold on the other PC
        if (S.Role == PcRole.Gaming) t.Add(true);
        else { if (S.PeerHost.Length > 0) t.Add(false); if (S.SoundHoldAlsoHere) t.Add(true); }
        return t;
    }

    // ---- holding a key on the other PC for as long as sounds play ----
    // Same protocol as a hold row on the Hotkeys page: Down, a Down every 100 ms to keep it alive (the other
    // PC releases by itself after 500 ms of silence, so nothing sticks), then Up twice. Overlapping sounds
    // that hold the same key share one hold.

    static string HoldKey(Binding b, bool local) => (local ? LocalPrefix : "") + b.ActionKey;

    void BeginHold(Binding b, bool local)
    {
        var k = HoldKey(b, local);
        if (_sbHolds.TryGetValue(k, out var h)) { _sbHolds[k] = (h.b, h.local, h.n + 1); return; }
        _sbHolds[k] = (b, local, 1);
        if (local)
        {
            _lastInjectUtc = DateTime.UtcNow;   // so our own injected key is not taken for a hotkey press
            Native.InjectDown(b);
            Activity($"←  holding {b.ActionText} here while the sound plays", flash: false);
            return;
        }
        Link.SendHotkey(S.PeerHost, S.Port, b, PressState.Down);
        Activity($"→  holding {b.ActionText} on {PeerName()} while the sound plays", flash: false);
        _ = KeepHold(k);
    }

    async Task KeepHold(string k)
    {
        while (_sbHolds.TryGetValue(k, out var h))
        {
            await Task.Delay(100);
            if (_sbHolds.TryGetValue(k, out var h2) && !h2.local) Link.SendHotkey(S.PeerHost, S.Port, h2.b, PressState.Down);
        }
    }

    async void EndHold(Binding b, bool local)
    {
        await Task.Delay(200);   // let the tail of the sound through before the channel closes
        var k = HoldKey(b, local);
        if (!_sbHolds.TryGetValue(k, out var h)) return;
        if (h.n > 1) { _sbHolds[k] = (h.b, h.local, h.n - 1); return; }
        _sbHolds.Remove(k);
        if (local) { Native.InjectUp(b); Activity($"←  released {b.ActionText} here", flash: false); return; }
        Link.SendHotkey(S.PeerHost, S.Port, b, PressState.Up);
        Link.SendHotkey(S.PeerHost, S.Port, b, PressState.Up);
        Activity($"→  released {b.ActionText} on {PeerName()}", flash: false);
    }

    /// <summary>Stop everything: playing sounds, sounds in the mic, presses not started yet, and (asked) the other PC's too.</summary>
    void StopAllSounds(bool broadcast)
    {
        _sbStopGen++;
        Soundboard.StopAll();
        _audioSession?.ClearMix();
        if (broadcast && S.SoundBothPcs && S.PeerHost.Length > 0) Link.SendSound(S.PeerHost, S.Port, StopMessage);
        SetStatus("All sounds stopped.");
    }
    const string StopMessage = "STOP";

    // ---- "mute my mic while a sound plays" ----
    // Streaming PC: the mic stops being sent to the gaming PC, and the capture device itself is muted in
    // Windows so Discord / OBS on this PC go quiet too; its own mute state is put back afterwards.
    // Gaming PC: the incoming mic is silenced before it reaches CABLE. Overlapping sounds share one mute.

    void MicMuteBegin()
    {
        if (_micMuteCount++ > 0) return;
        if (_audioSession != null) _audioSession.MuteMic = true;
        if (S.Role != PcRole.Streaming) return;
        try
        {
            using var dev = WindowsAudioDevices.Resolve(S.AudioCaptureDeviceId, NAudio.CoreAudioApi.DataFlow.Capture);
            if (dev != null && !dev.AudioEndpointVolume.Mute) { dev.AudioEndpointVolume.Mute = true; _micMutedByUs = true; }
        }
        catch (Exception ex) { Activity("Could not mute the microphone: " + ex.Message, flash: false); }
    }

    void MicMuteEnd()
    {
        if (_micMuteCount == 0 || --_micMuteCount > 0) return;
        if (_audioSession != null) _audioSession.MuteMic = false;
        if (!_micMutedByUs) return;
        _micMutedByUs = false;
        try
        {
            using var dev = WindowsAudioDevices.Resolve(S.AudioCaptureDeviceId, NAudio.CoreAudioApi.DataFlow.Capture);
            if (dev != null) dev.AudioEndpointVolume.Mute = false;
        }
        catch { }
    }

    /// <summary>On exit: never leave the mic muted.</summary>
    void ReleaseMicMute() { if (_micMuteCount > 0) { _micMuteCount = 1; MicMuteEnd(); } }

    void SyncVolumeUi() { for (int i = 0; i < 4; i++) SetSegment(_sbVol[i], S.SoundVolume == (i + 1) * 25); }

    /// <summary>Output list; the gaming PC defaults to CABLE Input so the game and Discord hear the sound in the mic.</summary>
    void FillSoundDevices()
    {
        bool loading = _loadingUi; _loadingUi = true;
        try
        {
            var devs = new List<AudioDeviceInfo>();
            if (S.Role == PcRole.Streaming) devs.Add(new(MicTarget, "My microphone (what the gaming PC hears)", false));
            devs.Add(new(null!, DefaultDevice, false));
            int firstReal = devs.Count;
            try { devs.AddRange(WindowsAudioDevices.GetRenderDevices()); } catch { }
            if (!S.SoundDeviceSet && S.Role == PcRole.Gaming && _virtualMic.InputEndpoint != null) S.SoundDeviceId = _virtualMic.InputEndpoint.Id;
            if (!S.SoundDeviceSet && S.Role == PcRole.Streaming) S.SoundDeviceId = MicTarget;
            if (S.SoundDeviceId == MicTarget && S.Role != PcRole.Streaming) S.SoundDeviceId = null;
            _sbDevice.Items.Clear(); foreach (var d in devs) _sbDevice.Items.Add(d);
            _sbDevice.DisplayMember = "Name";
            _sbDevice.SelectedItem = devs.FirstOrDefault(d => d.Id == S.SoundDeviceId) ?? devs[0];
            var second = new List<AudioDeviceInfo> { new(null!, NoSecond, false) };
            second.AddRange(devs.Skip(firstReal));
            _sbDevice2.Items.Clear(); foreach (var d in second) _sbDevice2.Items.Add(d);
            _sbDevice2.DisplayMember = "Name";
            _sbDevice2.SelectedItem = second.FirstOrDefault(d => d.Id != null && d.Id == S.SoundDevice2Id) ?? second[0];
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
        int gen = _sbStopGen;
        if (!S.Enabled && !preview) { SetStatus("Paused - resume to play sounds."); return; }
        if (!S.SoundboardEnabled && !preview) { SetStatus("The soundboard is off."); return; }
        if (broadcast && S.PeerHost.Length > 0)
        {
            var msg = JsonSerializer.Deserialize<SoundInfo>(JsonSerializer.Serialize(s))!;
            msg.ViaMic = !preview && PlaysIntoMic; msg.HoldAction = null;
            Link.SendSound(S.PeerHost, S.Port, JsonSerializer.Serialize(msg));
        }
        try
        {
            if (!Soundboard.IsCached(s)) SetStatus($"Downloading {s.ShortTitle}…");
            var path = await Soundboard.EnsureAsync(s);
            if (gen != _sbStopGen) return;   // Stop all was pressed while it downloaded
            // only the PC where the button was pressed holds the key, so "both PCs" never presses it twice
            var hold = origin && !preview ? ResolveHold(s) : null;
            var targets = hold == null ? new List<bool>() : HoldTargets();
            foreach (var local in targets) BeginHold(hold!, local);
            bool mute = !preview && S.SoundMuteMic;
            if (mute) MicMuteBegin();
            if (targets.Count > 0) await Task.Delay(150);   // open push-to-talk before the first syllable
            if (gen != _sbStopGen) { foreach (var local in targets) EndHold(hold!, local); if (mute) MicMuteEnd(); return; }
            void Release() { foreach (var local in targets) EndHold(hold!, local); if (mute) MicMuteEnd(); }
            try
            {
                if (!preview && PlaysIntoMic)
                {
                    // into the microphone itself: mixed into the stream the gaming PC receives in CABLE
                    if (_audioSession?.CanMixIntoMic != true) throw new InvalidOperationException("the audio bridge is not running, so there is no microphone to play into - turn it on on the Audio page");
                    var src = Soundboard.OpenForMix(path, S.SoundVolume / 100f, out var reader);
                    _audioSession.MixIntoMic(src, () => { reader.Dispose(); if (IsHandleCreated) BeginInvoke(Release); });
                }
                else Soundboard.Play(path, preview ? null : S.SoundDeviceId, S.SoundVolume / 100f, targets.Count == 0 && !mute ? null : () => BeginInvoke(Release));
            }
            catch { Release(); throw; }
            // the optional second output; it never holds keys (the first one does) and a failure there doesn't stop the first
            if (!preview && !string.IsNullOrEmpty(S.SoundDevice2Id) && S.SoundDevice2Id != S.SoundDeviceId)
            {
                try { Soundboard.Play(path, S.SoundDevice2Id, S.SoundVolume / 100f); }
                catch (Exception ex2) { Activity($"Sound {s.ShortTitle}: second output failed: {ex2.Message}", flash: false); }
            }
            if (!preview) Activity($"Sound: {s.ShortTitle}{(broadcast ? "  (and on " + PeerName() + ")" : "")}", flash: true);
        }
        catch (Exception ex) { Activity($"Sound {s.ShortTitle} could not play: {ex.Message}", flash: false); }
    }

    void OnSoundFromPeer(string json, IPEndPoint from)
    {
        if (json == StopMessage) { StopAllSounds(broadcast: false); return; }
        if (!S.Enabled || !S.SoundboardEnabled || !S.SoundBothPcs) return;
        SoundInfo? s;
        try { s = JsonSerializer.Deserialize<SoundInfo>(json); } catch { return; }
        if (s == null || s.Url.Length == 0 || !Uri.TryCreate(s.Url, UriKind.Absolute, out var u) || u.Scheme != "https") return;
        bool viaMic = s.ViaMic; s.ViaMic = false;
        if (!S.Sounds.Any(x => x.Id == s.Id)) { S.Sounds.Add(s); S.Save(); RefreshBoard(); }   // keep both boards the same
        // the streaming PC already mixed it into the mic that plays into CABLE here: playing it into CABLE again would double it
        if (viaMic && S.Role == PcRole.Gaming && _virtualMic.InputEndpoint != null && S.SoundDeviceId == _virtualMic.InputEndpoint.Id) return;
        _ = PlaySound(s, broadcast: false);
    }
}
