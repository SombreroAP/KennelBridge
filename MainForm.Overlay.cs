using System.Diagnostics;
using System.Net;
using static KennelBridge.Theme;

namespace KennelBridge;

/// <summary>Input Overlay page: capture on the gaming PC, serve overlay pages for OBS on the streaming PC.</summary>
public sealed partial class MainForm
{
    readonly InputMonitor _input = new();
    readonly OverlayServer _overlay = new();
    readonly System.Windows.Forms.Timer _liveTimer = new() { Interval = 120 };

    readonly CheckBox _ovServe = Theme.Check("Serve overlays from this PC (the PC that runs OBS)");
    readonly TextBox _ovPort = Theme.TextBox();
    readonly Label _ovStatus = Theme.Label("", muted: true);
    readonly CheckBox _ovCapture = Theme.Check("Capture keyboard, mouse and controller on this PC and send them to the other PC");
    readonly Label _ovLive = Theme.Label("", muted: true);
    readonly ListView _ovList = new();
    readonly ComboBox _ovTheme = Theme.ComboBox();
    readonly ComboBox _ovStyle = Theme.ComboBox();
    readonly ComboBox _ovModel = Theme.ComboBox();
    const string ModelAuto = "By pad (Xbox / DualSense / DualShock 4)", ModelBuiltIn = "Built-in simple pad";
    readonly ComboBox _ovScale = Theme.ComboBox();
    readonly CheckBox _ovPlate = Theme.Check("Dark plate behind");
    readonly CheckBox _ovHistory = Theme.Check("Input history strip");
    readonly TextBox _ovUrl = Theme.TextBox();
    readonly TextBox _ovLiveUrl = Theme.TextBox();
    string _liveJson = "", _liveSource = "";
    DateTime _lastPeerInputUtc = DateTime.MinValue;

    // (id, name, description, OBS width, OBS height). An id may carry a ready-made look: "wasd?look=classic".
    internal static readonly (string id, string name, string desc, int w, int h)[] Overlays =
    {
        ("wasd?look=classic", "WASD + mouse · White outline", "The white see-through look everyone uses. Game keys up to T G B, and the mouse.", 640, 320),
        ("wasd?look=glass", "WASD + mouse · Glass", "Translucent grey blocks, white when pressed (the input-overlay plugin look).", 640, 320),
        ("wasd?look=neon", "WASD + mouse · Neon", "Thin glowing lines, lit up when pressed. Theme sets the colour.", 640, 320),
        ("wasd?look=paper", "WASD + mouse · Paper", "Solid white keys with dark text, accent when pressed.", 640, 320),
        ("wasd", "WASD + mouse · Dark", "Dark keys, accent when pressed. Theme and Style dropdowns apply.", 640, 320),
        ("wasd-mini", "WASD mini", "Just W A S D, Ctrl, Shift, Space. Fits a corner.", 200, 200),
        ("arrows", "Arrows + mouse", "Arrow keys and the mouse, for platformers and racers.", 400, 300),
        ("keyboard", "Full keyboard", "TKL keyboard with arrows.", 1000, 360),
        ("keyboard+mouse", "Full keyboard + mouse", "Everything on the desk.", 1200, 360),
        ("numpad", "Numpad", "The number pad.", 260, 320),
        ("mouse", "Mouse", "Buttons, wheel and movement only.", 220, 300),
        ("controller", "Controller (flat)", "Xbox or PlayStation pad: sticks, triggers, buttons. Labels follow the pad.", 460, 330),
        ("controller3d", "Controller (3D)", "Real DualSense or Xbox model (picked by the pad in use): buttons press, sticks tilt, triggers pull.", 500, 340),
        ("all", "WASD + mouse + controller", "For people who switch mid-session.", 1120, 340),
        ("history", "Input history strip", "A row of the last presses, newest on the right. Add it under any overlay with the checkbox.", 600, 70),
        ("custom", "Custom keys", "Your own keys: edit the URL, e.g. &keys=Q,W,E,R|A,S,D,F|Shift,Space", 600, 220),
    };

    void WireOverlay()
    {
        _overlay.Log += msg => Activity(msg, flash: false);
        _input.Snapshot += OnLocalInput;
        _input.Log += msg => Activity(msg, flash: false);
        _liveTimer.Tick += (_, _) => LiveTick();
        _liveTimer.Start();
    }

    Control BuildOverlayPage()
    {
        var col = Rows(182, 172, -1);

        var srv = new Card("Serve for OBS") { Dock = DockStyle.Fill, Hint = "streaming PC" };
        var sT = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2 };
        sT.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); sT.ColumnStyles.Add(Cpx(90)); sT.ColumnStyles.Add(Cpct(100));
        sT.RowStyles.Add(Px(40)); sT.RowStyles.Add(Pct(100));
        _ovServe.Margin = new Padding(0, 8, 16, 0);
        _ovServe.CheckedChanged += (_, _) => { if (_loadingUi) return; S.OverlayServer = _ovServe.Checked; S.OverlayPort = ParseOverlayPort(); S.Save(); ApplyOverlayRuntime(); };
        sT.Controls.Add(_ovServe, 0, 0);
        _ovPort.Dock = DockStyle.Fill; _ovPort.TextAlign = HorizontalAlignment.Center;
        _ovPort.Leave += (_, _) => { if (_loadingUi) return; var p = ParseOverlayPort(); if (p != S.OverlayPort) { S.OverlayPort = p; S.Save(); ApplyOverlayRuntime(); } _ovPort.Text = S.OverlayPort.ToString(); };
        sT.Controls.Add(_ovPort, 1, 0);
        sT.Controls.Add(Theme.Label("port", muted: true), 2, 0);
        _ovStatus.AutoSize = false; _ovStatus.Dock = DockStyle.Fill; _ovStatus.TextAlign = ContentAlignment.TopLeft;
        sT.Controls.Add(_ovStatus, 0, 1);
        sT.SetColumnSpan(_ovStatus, 3);
        srv.Controls.Add(sT);
        col.Controls.Add(srv, 0, 0);

        var inp = new Card("Capture") { Dock = DockStyle.Fill, Hint = "gaming PC" };
        var iT = Rows(32, -1);
        _ovCapture.Margin = new Padding(0, 4, 0, 0);
        _ovCapture.CheckedChanged += (_, _) => { if (_loadingUi) return; S.OverlayCapture = _ovCapture.Checked; S.Save(); ApplyOverlayRuntime(); };
        iT.Controls.Add(_ovCapture, 0, 0);
        _ovLive.AutoSize = false; _ovLive.Dock = DockStyle.Fill; _ovLive.TextAlign = ContentAlignment.TopLeft; _ovLive.Font = new Font("Consolas", 9.5f);
        iT.Controls.Add(_ovLive, 0, 1);
        inp.Controls.Add(iT);
        col.Controls.Add(inp, 0, 1);

        var ov = new Card("Overlay") { Dock = DockStyle.Fill, Hint = "OBS: Browser source, paste the live URL" };
        var oT = Rows(-1, 42, 42, 46, 42, 40, 30);
        StyleList(_ovList);
        _ovList.Dock = DockStyle.Fill;
        _ovList.Columns.Add("Overlay", 200); _ovList.Columns.Add("What it shows", 300); _ovList.Columns.Add("Size", 90);
        foreach (var o in Overlays) _ovList.Items.Add(new ListViewItem(new[] { o.name, o.desc, $"{o.w} × {o.h}" }) { Tag = o.id });
        _ovList.Resize += (_, _) => FitColumns(_ovList);
        _ovList.SelectedIndexChanged += (_, _) => RefreshOverlayUrl();
        _ovList.DoubleClick += (_, _) => OpenOverlayInBrowser();
        oT.Controls.Add(_ovList, 0, 0);

        var opt = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0), Padding = new Padding(0, 6, 0, 0) };
        opt.Controls.Add(Theme.Label("Theme", muted: true));
        _ovTheme.DropDownStyle = ComboBoxStyle.DropDownList; _ovTheme.Width = 100; _ovTheme.Margin = new Padding(6, 3, 12, 0);
        _ovTheme.Items.AddRange(new object[] { "amber", "white", "green", "pink", "blue", "olive" });
        _ovTheme.SelectedIndexChanged += (_, _) => { if (_loadingUi) return; S.OverlayTheme = _ovTheme.Text; S.Save(); RefreshOverlayUrl(); };
        opt.Controls.Add(_ovTheme);
        opt.Controls.Add(Theme.Label("Style", muted: true));
        _ovStyle.DropDownStyle = ComboBoxStyle.DropDownList; _ovStyle.Width = 90; _ovStyle.Margin = new Padding(6, 3, 12, 0);
        _ovStyle.Items.AddRange(new object[] { "solid", "outline", "glass", "neon", "paper" }); _ovStyle.SelectedIndex = 0;
        _ovStyle.SelectedIndexChanged += (_, _) => RefreshOverlayUrl();
        opt.Controls.Add(_ovStyle);
        _ovPlate.Margin = new Padding(0, 6, 0, 0);
        _ovPlate.CheckedChanged += (_, _) => RefreshOverlayUrl();
        opt.Controls.Add(_ovPlate);
        _ovHistory.Margin = new Padding(12, 6, 0, 0);
        _ovHistory.CheckedChanged += (_, _) => RefreshOverlayUrl();
        opt.Controls.Add(_ovHistory);
        oT.Controls.Add(opt, 0, 1);

        var opt2 = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0), Padding = new Padding(0, 4, 0, 0) };
        opt2.Controls.Add(Theme.Label("3D model", muted: true));
        _ovModel.DropDownStyle = ComboBoxStyle.DropDownList; _ovModel.Width = 150; _ovModel.Margin = new Padding(6, 3, 6, 0);
        _ovModel.SelectedIndexChanged += (_, _) => { if (_loadingUi) return; S.OverlayModel = _ovModel.Text == ModelAuto ? "auto" : _ovModel.Text == ModelBuiltIn ? "" : _ovModel.Text; S.Save(); RefreshOverlayUrl(); };
        _ovModel.DropDown += (_, _) => RefreshModelList();
        _ovModel.Width = 300;
        opt2.Controls.Add(_ovModel);
        var mf = Theme.Button("Models folder…", minWidth: 0); mf.MinimumSize = new Size(0, 30); mf.Margin = new Padding(0, 3, 12, 0);
        mf.Click += (_, _) => { try { Directory.CreateDirectory(Settings.ModelsDir); Process.Start(new ProcessStartInfo(Settings.ModelsDir) { UseShellExecute = true }); } catch { } };
        opt2.Controls.Add(mf);
        opt2.Controls.Add(Theme.Label("(3D controller overlay only)", muted: true, Small));
        oT.Controls.Add(opt2, 0, 2);
        opt.Controls.Add(Theme.Label("Scale", muted: true));
        _ovScale.DropDownStyle = ComboBoxStyle.DropDownList; _ovScale.Width = 70; _ovScale.Margin = new Padding(6, 3, 12, 0);
        _ovScale.Items.AddRange(new object[] { "0.75", "1", "1.25", "1.5", "2" }); _ovScale.SelectedIndex = 1;
        _ovScale.SelectedIndexChanged += (_, _) => RefreshOverlayUrl();
        opt.Controls.Add(_ovScale);

        var lv = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0), Padding = new Padding(0, 8, 0, 0) };
        lv.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); lv.ColumnStyles.Add(Cpct(100)); lv.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        lv.Controls.Add(Theme.Label("Live URL", muted: true), 0, 0);
        _ovLiveUrl.ReadOnly = true; _ovLiveUrl.Dock = DockStyle.Fill; _ovLiveUrl.Font = new Font("Consolas", 10f); _ovLiveUrl.Margin = new Padding(8, 0, 8, 0);
        lv.Controls.Add(_ovLiveUrl, 1, 0);
        var cl = Theme.Button("Copy live URL", primary: true); cl.MinimumSize = new Size(0, 30); cl.Margin = new Padding(0);
        cl.Click += (_, _) => { try { Clipboard.SetText(_ovLiveUrl.Text); SetStatus("Live URL copied. Paste it into one OBS Browser source (any size, the overlay fits itself) and pick overlays here; OBS follows."); } catch { } };
        lv.Controls.Add(cl, 2, 0);
        oT.Controls.Add(lv, 0, 3);

        _ovUrl.ReadOnly = true; _ovUrl.Dock = DockStyle.Fill; _ovUrl.Font = new Font("Consolas", 10f); _ovUrl.Margin = new Padding(0, 8, 0, 0);
        oT.Controls.Add(_ovUrl, 0, 4);
        var ob = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0), Padding = new Padding(0, 6, 0, 0) };
        ob.Controls.Add(On(Theme.Button("Copy fixed URL"), () => { try { Clipboard.SetText(_ovUrl.Text); SetStatus("Fixed URL copied: this one always shows this overlay. In OBS: Sources → + → Browser, paste it, set the size shown, untick 'Shutdown source when not visible'."); } catch { } }));
        ob.Controls.Add(On(Theme.Button("Open in browser"), () => OpenOverlayInBrowser()));
        ob.Controls.Add(On(Theme.Button("Demo in browser"), () => OpenOverlayInBrowser(demo: true)));
        oT.Controls.Add(ob, 0, 5);
        oT.Controls.Add(Theme.Label("The fixed URL is for a second OBS source that should not follow the selection above.", muted: true, Theme.Small), 0, 6);
        ov.Controls.Add(oT);
        col.Controls.Add(ov, 0, 2);
        return col;
    }

    void LoadOverlayUi()
    {
        _ovServe.Checked = S.OverlayServer;
        _ovPort.Text = S.OverlayPort.ToString();
        _ovCapture.Checked = S.OverlayCapture;
        _ovTheme.SelectedItem = _ovTheme.Items.Contains(S.OverlayTheme) ? S.OverlayTheme : "amber";
        RefreshModelList();
        if (_ovStyle.Items.Contains(S.OverlayStyle)) _ovStyle.SelectedItem = S.OverlayStyle;
        if (_ovScale.Items.Contains(S.OverlayScale)) _ovScale.SelectedItem = S.OverlayScale;
        _ovPlate.Checked = S.OverlayPlate; _ovHistory.Checked = S.OverlayHistory;
        foreach (ListViewItem it in _ovList.Items) if (it.Tag is string tid && tid == S.OverlayId) { it.Selected = true; it.EnsureVisible(); break; }
        if (_ovList.Items.Count > 0 && _ovList.SelectedItems.Count == 0) _ovList.Items[0].Selected = true;
    }

    void RefreshModelList()
    {
        var current = S.OverlayModel;
        _ovModel.BeginUpdate();
        _ovModel.Items.Clear();
        _ovModel.Items.Add(ModelAuto);
        _ovModel.Items.Add(ModelBuiltIn);
        var names = new List<string>();
        try { if (Directory.Exists(Settings.ModelsDir)) names.AddRange(Directory.GetFiles(Settings.ModelsDir, "*.gl*").Select(f => Path.GetFileName(f)!)); } catch { }
        foreach (var b in OverlayServer.BundledModels) if (!names.Contains(b, StringComparer.OrdinalIgnoreCase)) names.Add(b);
        foreach (var n in names.OrderBy(n => n)) _ovModel.Items.Add(n);
        _ovModel.EndUpdate();
        int idx = current == "auto" ? 0 : current.Length == 0 ? 1 : _ovModel.Items.IndexOf(current);
        _ovModel.SelectedIndex = idx < 0 ? 0 : idx;
    }

    int ParseOverlayPort() => int.TryParse(_ovPort.Text.Trim(), out var p) && p is >= 1024 and <= 65535 ? p : S.OverlayPort;

    (string id, string name, string desc, int w, int h) SelectedOverlay()
    {
        var id = _ovList.SelectedItems.Count > 0 ? _ovList.SelectedItems[0].Tag as string : null;
        return Overlays.FirstOrDefault(o => o.id == id, Overlays[0]);
    }

    internal string OverlayUrl(string id, bool demo = false) => $"http://localhost:{S.OverlayPort}/?{OverlayQuery(id, demo)}";
    internal string LiveUrl => $"http://localhost:{S.OverlayPort}/live";

    string OverlayQuery(string id, bool demo = false)
    {
        string extra = "";
        int qi = id.IndexOf('?');
        if (qi >= 0) { extra = "&" + id[(qi + 1)..]; id = id[..qi]; }
        var model = id == "controller3d" && S.OverlayModel.Length > 0 ? "&model=" + Uri.EscapeDataString(S.OverlayModel) : "";
        var keys = id == "custom" ? "&keys=Q,W,E,R|A,S,D,F|Shift,Space" : "";
        var hist = _ovHistory.Checked && id != "history" ? "&history=1" : "";
        // a ready-made look fixes theme + style; the Theme dropdown still recolours neon
        var ts = extra.Contains("look=classic") || extra.Contains("look=glass") || extra.Contains("look=paper") ? "" : extra.Contains("look=") ? $"&theme={S.OverlayTheme}" : $"&theme={S.OverlayTheme}&style={_ovStyle.Text}";
        return $"overlay={id}{extra}{ts}&scale={_ovScale.Text}{model}{keys}{hist}{(_ovPlate.Checked ? "&plate=1" : "")}{(demo ? "&demo=1" : "")}";
    }

    internal void RefreshOverlayUrl()
    {
        var o = SelectedOverlay();
        _ovUrl.Text = OverlayUrl(o.id);
        _ovLiveUrl.Text = LiveUrl;
        _overlay.LiveConfig = OverlayQuery(o.id);
        if (!_loadingUi)
        {
            S.OverlayId = o.id; S.OverlayStyle = _ovStyle.Text; S.OverlayScale = _ovScale.Text; S.OverlayPlate = _ovPlate.Checked; S.OverlayHistory = _ovHistory.Checked;
            S.Save();
        }
        _ovStatus.Text = !S.OverlayServer ? "Off. Turn it on, on the PC that runs OBS. The overlay shows the other PC's inputs (or this PC's when nothing arrives)."
            : !S.Enabled ? "Paused."
            : _overlay.Running ? $"Running at http://localhost:{S.OverlayPort}/  ·  {_overlay.ClientCount} browser source{(_overlay.ClientCount == 1 ? "" : "s")} connected  ·  size for '{o.name}': {o.w} × {o.h}"
            : $"Could not start on port {S.OverlayPort} - pick another port.";
    }

    void OpenOverlayInBrowser(bool demo = false)
    {
        if (!_overlay.Running) { SetStatus("Turn the overlay server on first."); return; }
        try { Process.Start(new ProcessStartInfo(OverlayUrl(SelectedOverlay().id, demo)) { UseShellExecute = true }); } catch (Exception ex) { SetStatus("Could not open browser: " + ex.Message); }
    }

    internal void ApplyOverlayRuntime()
    {
        bool serve = S.Enabled && S.OverlayServer;
        if (serve) { if (!_overlay.Running || _overlay.Port != S.OverlayPort) _overlay.Start(S.OverlayPort); }
        else _overlay.Stop();
        bool capture = S.Enabled && S.OverlayCapture && IsHandleCreated;
        if (capture && !_input.Running) _input.Start(Handle);
        else if (!capture && _input.Running) _input.Stop();
        RefreshOverlayUrl();
    }

    void OnLocalInput(string json)
    {
        if (S.Enabled && S.OverlayCapture && S.PeerHost.Length > 0) Link.SendInput(S.PeerHost, S.Port, json);
        bool peerQuiet = (DateTime.UtcNow - _lastPeerInputUtc).TotalSeconds > 1.5;
        if (_overlay.Running && peerQuiet) _overlay.Broadcast(json);
        if (peerQuiet) { _liveSource = "this PC"; _liveJson = json; }
    }

    void OnPeerInput(string json, IPEndPoint from)
    {
        if (!S.Enabled || !S.OverlayServer) return;
        _lastPeerInputUtc = DateTime.UtcNow;
        if (_overlay.Running) _overlay.Broadcast(json);
        _liveJson = json;
        _liveSource = Disc.Peers.TryGetValue(from.Address.ToString(), out var p) ? p.Name : Mask(from.Address.ToString());
    }

    void LiveTick()
    {
        if (_currentPage != PageOverlay) return;
        if (_liveJson.Length == 0) { _ovLive.Text = "Live: (nothing yet)"; return; }
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(_liveJson);
            var r = doc.RootElement;
            var parts = new List<string>();
            foreach (var k in r.GetProperty("k").EnumerateArray()) { int vk = k.GetInt32(); if (vk is 16 or 17 or 18) continue; parts.Add(KeyNames.KeyName(vk)); }
            var m = r.GetProperty("m");
            int mb = m[0].GetInt32();
            if ((mb & 1) != 0) parts.Add("LMB"); if ((mb & 2) != 0) parts.Add("RMB"); if ((mb & 4) != 0) parts.Add("MMB"); if ((mb & 8) != 0) parts.Add("M4"); if ((mb & 16) != 0) parts.Add("M5");
            int dx = m[1].GetInt32(), dy = m[2].GetInt32();
            if (dx != 0 || dy != 0) parts.Add($"mouse {dx:+#;-#;0},{dy:+#;-#;0}");
            var g = r.GetProperty("g");
            if (g.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                int b = g[0].GetInt32();
                bool ps = g.GetArrayLength() > 7 && g[7].ValueKind == System.Text.Json.JsonValueKind.String && g[7].GetString() is "ps" or "ps4";
                var names = ps
                    ? new (int, string)[] { (0x1000, "Cross"), (0x2000, "Circle"), (0x4000, "Square"), (0x8000, "Triangle"), (0x100, "L1"), (0x200, "R1"), (1, "D↑"), (2, "D↓"), (4, "D←"), (8, "D→"), (0x10, "Options"), (0x20, "Create"), (0x40, "L3"), (0x80, "R3"), (0x400, "PS") }
                    : new (int, string)[] { (0x1000, "A"), (0x2000, "B"), (0x4000, "X"), (0x8000, "Y"), (0x100, "LB"), (0x200, "RB"), (1, "D↑"), (2, "D↓"), (4, "D←"), (8, "D→"), (0x10, "Start"), (0x20, "Back"), (0x40, "LS"), (0x80, "RS"), (0x400, "Guide") };
                var pb = names.Where(n => (b & n.Item1) != 0).Select(n => n.Item2).ToList();
                if (g[1].GetInt32() > 30) pb.Add(ps ? "L2" : "LT"); if (g[2].GetInt32() > 30) pb.Add(ps ? "R2" : "RT");
                var src = _liveSource == "this PC" && _input.PadSource.Length > 0 ? _input.PadSource + ": " : "pad: ";
                parts.Add(src + (pb.Count > 0 ? string.Join(" ", pb) : "idle"));
            }
            _ovLive.Text = $"Live from {_liveSource}:  " + (parts.Count > 0 ? string.Join("  ·  ", parts) : "—");
        }
        catch { _ovLive.Text = "Live: " + _liveJson; }
    }

    void ShutdownOverlay()
    {
        _liveTimer.Stop();
        _input.Dispose();
        _overlay.Dispose();
    }
}
