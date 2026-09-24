using System.Net;
using static KennelBridge.Theme;

namespace KennelBridge;

/// <summary>Hotkeys page: press here, the other PC presses too. Works in both directions.</summary>
public sealed partial class MainForm
{
    readonly System.Windows.Forms.Timer _holdTimer = new() { Interval = 40 };
    readonly List<int> _registeredIds = new();
    readonly CheckBox _hkEnabled = Theme.Check("Hotkey bridge on");
    readonly Button _hkSend = Theme.Button("Sends", minWidth: 0), _hkReceive = Theme.Button("Receives", minWidth: 0), _hkBoth = Theme.Button("Both", minWidth: 0);
    readonly Card _pressesCard = new("Presses");
    readonly ListView _list = new();

    DateTime _lastInjectUtc = DateTime.MinValue;
    readonly Dictionary<Binding, DateTime> _heldTriggers = new();
    readonly Dictionary<string, (Binding b, DateTime last)> _heldActions = new();
    const int KeepAliveMs = 100;
    const int HoldTimeoutMs = 500;
    bool _capturing, _hooksInstalled;

    bool HotkeysSend => S.Enabled && S.HotkeysEnabled && S.HotkeyMode != BridgeMode.ReceiveOnly;
    bool HotkeysReceive => S.Enabled && S.HotkeysEnabled && S.HotkeyMode != BridgeMode.SendOnly;

    Control BuildHotkeysPage()
    {
        var col = Rows(132, -1);

        var top = new Card("Mode") { Dock = DockStyle.Fill };
        var tT = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        tT.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); tT.ColumnStyles.Add(Cpct(100));
        tT.RowStyles.Add(Px(40));
        _hkEnabled.Margin = new Padding(0, 8, 24, 0);
        _hkEnabled.CheckedChanged += (_, _) => { if (_loadingUi) return; S.HotkeysEnabled = _hkEnabled.Checked; S.Save(); if (!S.HotkeysEnabled) { ReleaseAllHeldTriggers(); ReleaseAllHeldActions("hotkeys off"); } ApplyHotkeyRuntime(); };
        tT.Controls.Add(_hkEnabled, 0, 0);
        var seg = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, Margin = new Padding(0, 3, 0, 3) };
        seg.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); for (int i = 0; i < 3; i++) seg.ColumnStyles.Add(Cpx(110));
        seg.Controls.Add(Theme.Label("This PC", muted: true), 0, 0);
        foreach (var (b, m) in new[] { (_hkSend, BridgeMode.SendOnly), (_hkReceive, BridgeMode.ReceiveOnly), (_hkBoth, BridgeMode.Both) })
        {
            b.Dock = DockStyle.Fill; b.Margin = new Padding(4, 0, 0, 0); b.MinimumSize = new Size(0, 30); b.AutoSize = false;
            b.Click += (_, _) => { if (!_loadingUi && S.HotkeyMode != m) SetHotkeyMode(m); };
            seg.Controls.Add(b);
        }
        tT.Controls.Add(seg, 1, 0);
        top.Controls.Add(tT);
        col.Controls.Add(top, 0, 0);

        _pressesCard.Dock = DockStyle.Fill;
        var pT = Rows(-1, 44, 40);
        StyleList(_list);
        _list.Dock = DockStyle.Fill;
        _list.Columns.Add("Press on this PC", 220);
        _list.Columns.Add("Other PC does", 200);
        _list.DoubleClick += (_, _) => EditPress();
        _list.Resize += (_, _) => FitColumns(_list);
        pT.Controls.Add(_list, 0, 0);
        var pb = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0), Padding = new Padding(0, 8, 0, 0) };
        pb.Controls.Add(On(Theme.Button("Add press…", primary: true), () => AddPress()));
        pb.Controls.Add(On(Theme.Button("Edit…"), EditPress));
        pb.Controls.Add(On(Theme.Button("Remove"), RemoveSelected));
        pb.Controls.Add(On(Theme.Button("Test on other PC"), TestSelected));
        pb.Controls.Add(On(Theme.Button("Discord…"), () => RunWizard(SetupWizard.Page.Discord)));
        pT.Controls.Add(pb, 0, 1);
        pT.Controls.Add(Wrapped("Double-click a row to change it. F13–F24 are free on every PC, which makes them ideal targets."), 0, 2);
        _pressesCard.Controls.Add(pT);
        col.Controls.Add(_pressesCard, 0, 1);
        return col;
    }

    void LoadHotkeysUi()
    {
        _hkEnabled.Checked = S.HotkeysEnabled;
        RefreshList();
        ApplyHotkeyModeToUi();
    }

    void SetHotkeyMode(BridgeMode m)
    {
        ReleaseAllHeldTriggers(); ReleaseAllHeldActions("mode changed");
        S.HotkeyMode = m;
        S.Save();
        ApplyHotkeyModeToUi();
        ApplyHotkeyRuntime();
    }

    void ApplyHotkeyModeToUi()
    {
        SetSegment(_hkSend, S.HotkeyMode == BridgeMode.SendOnly);
        SetSegment(_hkReceive, S.HotkeyMode == BridgeMode.ReceiveOnly);
        SetSegment(_hkBoth, S.HotkeyMode == BridgeMode.Both);
        bool sends = S.HotkeyMode != BridgeMode.ReceiveOnly && S.HotkeysEnabled;
        _pressesCard.Enabled = sends;
        _pressesCard.Hint = !S.HotkeysEnabled ? "off" : sends ? "press here  →  other PC does" : "off - this PC only receives";
    }

    void ApplyHotkeyRuntime()
    {
        RegisterHotkeys();
        ApplyHotkeyModeToUi();
    }

    // ---- presses list ----

    void RefreshList()
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var b in S.Bindings)
            _list.Items.Add(new ListViewItem(new[] { b.TriggerText + (b.PassThrough ? "" : "   (only other PC)"), b.DisplayAction + (b.Hold ? "" : "  (tap)") }) { Tag = b });
        _list.EndUpdate();
        FitColumns(_list);
        SendRows();
    }

    Binding? Selected => _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as Binding : null;

    internal bool AddPress()
    {
        var b = RunCaptureDialog(null);
        if (b == null) return false;
        var clash = S.Bindings.FirstOrDefault(x => x.SameTriggerAs(b));
        if (clash != null) { SetStatus($"{b.TriggerText} is already used by another row - edit that one instead."); return false; }
        S.Bindings.Add(b);
        S.Save();
        RefreshList();
        RegisterHotkeys();
        Activity($"Added: {b.TriggerText}  →  other PC does {b.ActionText}.", flash: false);
        return true;
    }

    void EditPress()
    {
        var target = Selected;
        if (target == null) { SetStatus("Select a row to edit first."); return; }
        var b = RunCaptureDialog(target);
        if (b == null) return;
        var clash = S.Bindings.FirstOrDefault(x => x != target && x.SameTriggerAs(b));
        if (clash != null) { SetStatus($"{b.TriggerText} is already used by another row."); return; }
        target.TriggerKind = b.TriggerKind; target.TriggerVk = b.TriggerVk; target.TriggerMods = b.TriggerMods;
        target.Kind = b.Kind; target.ActionVk = b.ActionVk; target.ActionMods = b.ActionMods; target.Hold = b.Hold; target.PassThrough = b.PassThrough;
        if (target.Label.Length > 0 && (target.Kind != ActionKind.Key || target.ActionVk < 0x7C || target.ActionVk > 0x87)) target.Label = "";   // no longer the Discord key
        S.Save();
        RefreshList();
        RegisterHotkeys();
        Activity($"Updated: {b.TriggerText}  →  other PC does {b.ActionText}.", flash: false);
    }

    /// <summary>Ask the user to press one key/button, with our own hotkeys released so the press reaches the prompt.</summary>
    internal (ActionKind kind, int vk, int mods)? PromptPress(string title, string prompt)
    {
        UnregisterHotkeys();
        MouseHook.ButtonDown = null;
        _capturing = true;
        try
        {
            using var dlg = new PressPrompt(title, prompt);
            return dlg.ShowDialog(this) == DialogResult.OK ? (dlg.Kind, dlg.Vk, dlg.Mods) : null;
        }
        finally
        {
            _capturing = false;
            MouseHook.ButtonDown = OnMouseTrigger;
            RegisterHotkeys();
        }
    }

    /// <summary>First of F13–F24 not already used as an action by any row.</summary>
    internal int NextFreeFKey()
    {
        for (int vk = 0x7C; vk <= 0x87; vk++)
            if (!S.Bindings.Any(b => b.Kind == ActionKind.Key && b.ActionVk == vk)) return vk;
        return 0;
    }

    /// <summary>Create or re-point a labelled binding (e.g. "Discord: Push to mute") to a new trigger. Returns null if the trigger is taken.</summary>
    internal Binding? SetLabelledBinding(string label, ActionKind tk, int tvk, int tmods, bool hold)
    {
        var probe = new Binding { TriggerKind = tk, TriggerVk = tvk, TriggerMods = tmods };
        var existing = S.Bindings.FirstOrDefault(b => b.Label == label);
        var clash = S.Bindings.FirstOrDefault(b => b != existing && b.SameTriggerAs(probe));
        if (clash != null) { SetStatus($"{probe.TriggerText} is already used by '{clash.DisplayAction}'."); return null; }
        if (existing == null)
        {
            int vk = NextFreeFKey();
            if (vk == 0) { SetStatus("All of F13–F24 are in use - remove a row first."); return null; }
            existing = new Binding { Label = label, Kind = ActionKind.Key, ActionVk = vk, ActionMods = 0 };
            S.Bindings.Add(existing);
        }
        existing.TriggerKind = tk; existing.TriggerVk = tvk; existing.TriggerMods = tmods; existing.Hold = hold;
        S.Save();
        RefreshList();
        RegisterHotkeys();
        Activity($"{label}: {existing.TriggerText} here  →  other PC {(hold ? "holds" : "taps")} {existing.ActionText}.", flash: false);
        return existing;
    }

    internal void SendTest(Binding b)
    {
        if (Link.SendHotkey(S.PeerHost, S.Port, b)) Activity($"→  test: {PeerLabel()} should do {b.ActionText} now");
    }

    internal Binding? RunCaptureDialog(Binding? existing)
    {
        UnregisterHotkeys();
        MouseHook.ButtonDown = null;
        _capturing = true;
        try
        {
            using var dlg = new CaptureDialog(existing);
            return dlg.ShowDialog(this) == DialogResult.OK ? dlg.Result : null;
        }
        finally
        {
            _capturing = false;
            MouseHook.ButtonDown = OnMouseTrigger;
            RegisterHotkeys();
        }
    }

    void RemoveSelected()
    {
        var b = Selected;
        if (b == null) { SetStatus("Select a row to remove."); return; }
        S.Bindings.Remove(b);
        S.Save();
        RefreshList();
        RegisterHotkeys();
        SetStatus("Removed.");
    }

    void TestSelected()
    {
        var b = Selected;
        if (b == null) { SetStatus("Select a row to test, or add one with 'Add press…'."); return; }
        Link.Passphrase = _pass.Text;
        var host = S.StreamerMode ? S.PeerHost : _peer.Text.Trim();
        if (Link.SendHotkey(host, ParsePort(), b))
            Activity($"→  test: {(S.StreamerMode ? PeerLabel() : host)} should do {b.ActionText} now  (nothing? use 'Test link' on the Connection page)");
    }

    // ---- hotkeys / hooks ----

    void RegisterHotkeys()
    {
        UnregisterHotkeys();
        if (!HotkeysSend || !IsHandleCreated) return;
        var failed = new List<string>();
        for (int i = 0; i < S.Bindings.Count; i++)
        {
            var b = S.Bindings[i];
            if (!b.IsKeyTrigger || b.TriggerVk == 0 || b.PassThrough) continue;   // pass-through keys are watched by the keyboard hook instead
            if (Native.RegisterHotKey(Handle, i, (uint)b.TriggerMods | Native.MOD_NOREPEAT, (uint)b.TriggerVk)) _registeredIds.Add(i);
            else failed.Add(b.TriggerText);
        }
        if (failed.Count > 0) SetStatus("Could not grab " + string.Join(", ", failed) + " - another app already owns that hotkey.");
    }

    void UnregisterHotkeys()
    {
        if (IsHandleCreated) foreach (var id in _registeredIds) Native.UnregisterHotKey(Handle, id);
        _registeredIds.Clear();
    }

    void OnHotkeyHandleCreated()
    {
        RegisterHotkeys();
        if (_hooksInstalled) return;   // the handle can be recreated; hooks and the timer are per process
        _hooksInstalled = true;
        if (MouseHook.ButtonDown == null) MouseHook.ButtonDown = OnMouseTrigger;
        MouseHook.Watch = OnMouseWatch;
        MouseHook.ButtonUp = k => BeginInvoke(() => OnMouseTriggerUp(k));
        KeyboardHook.KeyDown = OnHookKeyDown;
        KeyboardHook.KeyUp = vk => BeginInvoke(() => OnHookKeyUp(vk));
        if (!KeyboardHook.Install()) SetStatus("Could not install the keyboard hook - pass-through keys will not work as triggers.");
        if (!MouseHook.Install()) SetStatus("Could not install the mouse hook - mouse buttons will not work as triggers.");
        _holdTimer.Tick += (_, _) => HoldTick();
        _holdTimer.Start();
    }

    bool OnMouseTrigger(ActionKind k)
    {
        if (!HotkeysSend) return false;
        var b = S.Bindings.FirstOrDefault(x => x.TriggerKind == k);
        if (b == null) return false;
        BeginInvoke(() => FireBinding(b));
        return !b.PassThrough;          // pass-through rows let the game see the button too
    }

    bool OnMouseWatch(ActionKind k) => HotkeysSend && S.Bindings.Any(x => x.TriggerKind == k && x.PassThrough);

    void OnHookKeyDown(int vk, int mods)
    {
        if (!HotkeysSend || _capturing) return;
        var b = S.Bindings.FirstOrDefault(x => x.IsKeyTrigger && x.PassThrough && x.TriggerVk == vk && x.TriggerMods == mods);
        if (b != null) BeginInvoke(() => FireBinding(b));
    }

    void OnHookKeyUp(int vk)
    {
        foreach (var b in _heldTriggers.Keys.Where(x => x.IsKeyTrigger && x.TriggerVk == vk).ToList()) ReleaseTrigger(b);
    }

    void OnWmHotkey(int id)
    {
        if ((DateTime.UtcNow - _lastInjectUtc).TotalMilliseconds > 150 && id >= 0 && id < S.Bindings.Count)
            FireBinding(S.Bindings[id]);
    }

    // ---- hold state machines ----

    void FireBinding(Binding b)
    {
        if (!HotkeysSend) return;
        if (!b.Hold)
        {
            if (Link.SendHotkey(S.PeerHost, S.Port, b, PressState.Tap))
                Activity($"→  sent {b.TriggerText}   ({PeerLabel()} taps {b.ActionText})");
            return;
        }
        if (_heldTriggers.ContainsKey(b)) return;
        if (Link.SendHotkey(S.PeerHost, S.Port, b, PressState.Down))
        {
            _heldTriggers[b] = DateTime.UtcNow;
            Activity($"→  holding {b.TriggerText}   ({PeerLabel()} holds {b.ActionText})");
        }
    }

    void ReleaseTrigger(Binding b, string? why = null)
    {
        if (!_heldTriggers.Remove(b)) return;
        Link.SendHotkey(S.PeerHost, S.Port, b, PressState.Up);
        Link.SendHotkey(S.PeerHost, S.Port, b, PressState.Up);
        Activity($"→  released {b.TriggerText}{(why == null ? "" : $"  ({why})")}", flash: false);
    }

    void ReleaseAllHeldTriggers() { foreach (var b in _heldTriggers.Keys.ToList()) ReleaseTrigger(b); }

    void OnMouseTriggerUp(ActionKind k)
    {
        foreach (var b in _heldTriggers.Keys.Where(x => x.TriggerKind == k).ToList()) ReleaseTrigger(b);
    }

    void HoldTick()
    {
        var now = DateTime.UtcNow;
        foreach (var kv in _heldTriggers.ToList())
        {
            var b = kv.Key;
            if (b.IsKeyTrigger && !Native.IsKeyDown(b.TriggerVk)) { ReleaseTrigger(b); continue; }
            if ((now - kv.Value).TotalMilliseconds >= KeepAliveMs) { Link.SendHotkey(S.PeerHost, S.Port, b, PressState.Down); _heldTriggers[b] = now; }
        }
        foreach (var kv in _heldActions.ToList())
        {
            if ((now - kv.Value.last).TotalMilliseconds > HoldTimeoutMs)
            {
                _heldActions.Remove(kv.Key);
                Native.InjectUp(kv.Value.b);
                Activity($"←  released {kv.Value.b.ActionText}   (no signal from the other PC - safety release)", flash: false);
            }
        }
    }

    void ReleaseAllHeldActions(string why)
    {
        foreach (var kv in _heldActions.ToList())
        {
            _heldActions.Remove(kv.Key);
            Native.InjectUp(kv.Value.b);
            Activity($"←  released {kv.Value.b.ActionText}   ({why})", flash: false);
        }
    }

    void OnHotkeyReceived(Binding b, PressState state, IPEndPoint from)
    {
        if (!HotkeysReceive) return;
        var who = Disc.Peers.TryGetValue(from.Address.ToString(), out var pk) ? pk.Name : from.Address.ToString();
        var now = DateTime.UtcNow;
        switch (state)
        {
            case PressState.Tap:
                _lastInjectUtc = now;
                Native.Inject(b);
                Activity($"←  tapped {b.ActionText} here   (from {who})");
                break;
            case PressState.Down:
                if (_heldActions.TryGetValue(b.ActionKey, out var held)) _heldActions[b.ActionKey] = (held.b, now);
                else
                {
                    _lastInjectUtc = now;
                    Native.InjectDown(b);
                    _heldActions[b.ActionKey] = (b, now);
                    Activity($"←  holding {b.ActionText} here   (from {who})");
                }
                break;
            case PressState.Up:
                if (_heldActions.Remove(b.ActionKey)) { Native.InjectUp(b); Activity($"←  released {b.ActionText}", flash: false); }
                break;
        }
    }

    void ShutdownHotkeys()
    {
        ReleaseAllHeldTriggers();
        ReleaseAllHeldActions("KennelBridge closing");
        UnregisterHotkeys();
        _holdTimer.Stop();
        MouseHook.Uninstall();
        KeyboardHook.Uninstall();
    }
}
