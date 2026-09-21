using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using static KennelBridge.Theme;

namespace KennelBridge;

/// <summary>
/// The window: header, sidebar with one page per bridge, activity console, status bar, tray icon.
/// The shared connection (role, other PC, passphrase, port) lives here; each bridge is a partial
/// class file (MainForm.Overlay.cs, MainForm.Audio.cs, MainForm.Hotkeys.cs, MainForm.Files.cs).
/// </summary>
public sealed partial class MainForm : Form
{
    internal readonly Settings S = Settings.Load();
    internal readonly Bridge Link = new();
    internal readonly Discovery Disc = new();

    readonly System.Windows.Forms.Timer _uiTimer = new() { Interval = 1000 };
    readonly System.Windows.Forms.Timer _flashTimer = new() { Interval = 220 };

    readonly NotifyIcon _tray;
    readonly ContextMenuStrip _menu;
    readonly ToolStripMenuItem _miEnabled;
    static readonly Bitmap Logo = LoadLogo();
    readonly Icon _iconOn = MakeIcon(null);
    readonly Icon _iconOff = MakeIcon(Color.FromArgb(120, 120, 120));
    readonly Icon _iconFlash = MakeIcon(Green);

    // header
    readonly Pill _pill = new();
    readonly Button _pauseBtn = Theme.Button("Pause");
    // activity
    readonly ListView _activity = new();
    readonly Panel _lamp = new();
    readonly Label _status = Theme.Label("", muted: true);
    // pages
    readonly Panel _pages = new() { Dock = DockStyle.Fill, Margin = new Padding(0) };
    readonly Dictionary<string, (Button nav, Control page)> _nav = new();
    string _currentPage = "";

    bool _allowVisible, _reallyExit, _shownTrayTip, _loadingUi, _restoredSize, _wizardShown;
    static readonly Regex Ipv4 = new(@"\b\d{1,3}(\.\d{1,3}){3}\b", RegexOptions.Compiled);

    public const string PageConnection = "Connection", PageOverlay = "Input Overlay", PageAudio = "Audio", PageHotkeys = "Hotkeys", PageFiles = "Files", PageActivity = "Activity";

    public MainForm(bool startHidden)
    {
        Text = "KennelBridge";
        Icon = _iconOn;
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        ApplyWindow(this);
        ClientSize = new Size(1080, 820);
        MinimumSize = new Size(940, 640);
        DoubleBuffered = true;

        BuildUi();

        _menu = new ContextMenuStrip();
        _menu.Items.Add("Open KennelBridge", null, (_, _) => ShowWindow());
        _miEnabled = new ToolStripMenuItem("Enabled", null, (_, _) => SetEnabled(!S.Enabled));
        _menu.Items.Add(_miEnabled);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Exit", null, (_, _) => ExitApp());
        _tray = new NotifyIcon { ContextMenuStrip = _menu, Visible = true };
        _tray.DoubleClick += (_, _) => ShowWindow();

        Link.Log += msg => Activity(msg, flash: false);
        Link.HotkeyReceived += (b, st, from) => { if (IsHandleCreated) BeginInvoke(() => OnHotkeyReceived(b, st, from)); };
        Link.InputReceived += OnPeerInput;
        Disc.Log += msg => SafeStatus(msg);
        Disc.State = () => (S.Port, S.Role, S.Enabled);
        WireOverlay();
        WireAudio();
        WireFiles();

        LoadSettingsIntoUi();
        ApplyRuntime();
        Disc.Start();

        _uiTimer.Tick += (_, _) => { RefreshPeers(); AudioTick(); };
        _uiTimer.Start();
        _flashTimer.Tick += (_, _) => { _flashTimer.Stop(); _lamp.BackColor = CardBorder; _lamp.Invalidate(); UpdateTray(); };

        _allowVisible = !(startHidden || S.StartMinimized) || !S.SetupDone;   // first run always shows the window + wizard
        if (!_allowVisible) CreateHandle();
    }

    // =====================================================================  UI shell

    void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(10, 6, 10, 6), BackColor = Bg };
        root.ColumnStyles.Add(Cpx(190)); root.ColumnStyles.Add(Cpct(100));
        root.RowStyles.Add(Px(70)); root.RowStyles.Add(Pct(100)); root.RowStyles.Add(Px(34));
        Controls.Add(root);

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = new Padding(6, 0, 6, 0) };
        header.ColumnStyles.Add(Cpx(56)); header.ColumnStyles.Add(Cpct(100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.Controls.Add(new PictureBox { Image = Logo, SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(44, 44), Anchor = AnchorStyles.Left, Margin = new Padding(0) }, 0, 0);
        var titles = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0), WrapContents = false };
        titles.Controls.Add(Theme.Label("KennelBridge", font: Big));
        titles.Controls.Add(Theme.Label("Your two-PC stream, bridged: overlays, audio, hotkeys, recordings.", muted: true, Small));
        header.Controls.Add(titles, 1, 0);
        var right = new FlowLayoutPanel { AutoSize = true, Anchor = AnchorStyles.Right, WrapContents = false, Margin = new Padding(0) };
        _pill.Margin = new Padding(0, 0, 10, 0);
        right.Controls.Add(_pill);
        _pauseBtn.Click += (_, _) => SetEnabled(!S.Enabled);
        right.Controls.Add(_pauseBtn);
        var wiz = Theme.Button("Setup wizard"); wiz.Margin = new Padding(0);
        wiz.Click += (_, _) => RunWizard();
        right.Controls.Add(wiz);
        header.Controls.Add(right, 2, 0);
        root.Controls.Add(header, 0, 0);
        root.SetColumnSpan(header, 2);

        var side = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(6, 6, 0, 6), Padding = new Padding(0, 4, 0, 0) };
        root.Controls.Add(side, 0, 1);
        root.Controls.Add(_pages, 1, 1);

        void AddPage(string name, Control page)
        {
            var b = new Button
            {
                Text = "   " + name, TextAlign = ContentAlignment.MiddleLeft, FlatStyle = FlatStyle.Flat, Width = 174, Height = 44,
                BackColor = Bg, ForeColor = Fg, Font = Body, Cursor = Cursors.Hand, Margin = new Padding(0, 0, 0, 4), UseVisualStyleBackColor = false,
            };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = CardBg;
            b.Click += (_, _) => ShowPage(name);
            side.Controls.Add(b);
            // each page sits in a scrolling host with a minimum height, so a small window scrolls instead of crushing the cards
            var host = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Visible = false, Margin = new Padding(0) };
            page.Dock = DockStyle.Top; page.Height = 700; page.MinimumSize = new Size(0, 700);
            host.Controls.Add(page);
            host.Resize += (_, _) => page.Height = Math.Max(700, host.ClientSize.Height);
            _pages.Controls.Add(host);
            _nav[name] = (b, host);
        }
        AddPage(PageConnection, BuildConnectionPage());
        AddPage(PageOverlay, BuildOverlayPage());
        AddPage(PageAudio, BuildAudioPage());
        AddPage(PageHotkeys, BuildHotkeysPage());
        AddPage(PageFiles, BuildFilesPage());
        AddPage(PageActivity, BuildActivityPage());
        ShowPage(PageConnection);

        _status.AutoSize = false; _status.Dock = DockStyle.Fill; _status.AutoEllipsis = true; _status.TextAlign = ContentAlignment.MiddleLeft; _status.Margin = new Padding(8, 0, 8, 0);
        root.Controls.Add(_status, 0, 2);
        root.SetColumnSpan(_status, 2);
    }

    internal void ShowPage(string name)
    {
        foreach (var (n, (btn, page)) in _nav)
        {
            bool on = n == name;
            page.Visible = on;
            btn.BackColor = on ? CardBg : Bg;
            btn.ForeColor = on ? Amber : Fg;
            btn.Font = on ? Semibold : Body;
        }
        _currentPage = name;
    }

    static Button On(Button b, Action a) { b.Click += (_, _) => a(); return b; }

    static void FitColumns(ListView lv)
    {
        if (lv.Columns.Count < 2) return;
        int w = lv.ClientSize.Width - lv.Columns[0].Width - 4;
        for (int i = 2; i < lv.Columns.Count; i++) w -= lv.Columns[i].Width;
        if (w > 60) lv.Columns[1].Width = w;
    }

    /// <summary>A folder-style two-column panel: fixed rows of the given heights, the last one may be -1 for "fill".</summary>
    static TableLayoutPanel Rows(params int[] heights)
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = heights.Length, Margin = new Padding(0) };
        t.ColumnStyles.Add(Cpct(100));
        foreach (var h in heights) t.RowStyles.Add(h < 0 ? Pct(100) : Px(h));
        return t;
    }

    Control BuildActivityPage()
    {
        var act = new Card("Activity") { Hint = "every bridge logs here" };
        var aT = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        aT.ColumnStyles.Add(Cpct(100)); aT.ColumnStyles.Add(Cpx(28));
        aT.RowStyles.Add(Pct(100)); aT.RowStyles.Add(Px(40));
        StyleList(_activity);
        _activity.Dock = DockStyle.Fill;
        _activity.Columns.Add("Time", 100);
        _activity.Columns.Add("What happened", 400);
        _activity.Resize += (_, _) => FitColumns(_activity);
        aT.Controls.Add(_activity, 0, 0);
        _lamp.Size = new Size(14, 14); _lamp.Anchor = AnchorStyles.Top | AnchorStyles.Right; _lamp.Margin = new Padding(6, 4, 0, 0); _lamp.BackColor = CardBorder;
        _lamp.Paint += (_, e) => { e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; using var b = new SolidBrush(_lamp.BackColor); e.Graphics.Clear(CardBg); e.Graphics.FillEllipse(b, 0, 0, 13, 13); };
        aT.Controls.Add(_lamp, 1, 0);
        var ab = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0), Padding = new Padding(0, 6, 0, 0) };
        ab.Controls.Add(On(Theme.Button("Open log file"), OpenLogFile));
        ab.Controls.Add(On(Theme.Button("Clear", minWidth: 70), () => _activity.Items.Clear()));
        aT.Controls.Add(ab, 0, 1);
        aT.SetColumnSpan(ab, 2);
        act.Controls.Add(aT);
        return act;
    }

    // =====================================================================  activity / log

    internal void Activity(string text, bool flash = true)
    {
        if (IsHandleCreated && InvokeRequired) { BeginInvoke(() => Activity(text, flash)); return; }
        var now = DateTime.Now;
        WriteLog(now, text);
        _activity.Items.Insert(0, new ListViewItem(new[] { now.ToString("HH:mm:ss.fff"), Mask(text) }));
        while (_activity.Items.Count > 500) _activity.Items.RemoveAt(_activity.Items.Count - 1);
        SetStatus(text);
        if (!flash) return;
        _lamp.BackColor = Green; _lamp.Invalidate();
        _tray.Icon = _iconFlash;
        _flashTimer.Stop(); _flashTimer.Start();
    }

    internal string Mask(string text) => S.StreamerMode ? Ipv4.Replace(text, "•.•.•.•") : text;

    /// <summary>Short form for the header pill and tray: the PC's name if we know it, else its (masked) address.</summary>
    internal string PeerName()
    {
        if (S.PeerHost.Length == 0) return "";
        if (Disc.Peers.TryGetValue(S.PeerHost, out var p) && p.Name.Length > 0) return p.Name;
        return Mask(S.PeerHost);
    }

    internal string PeerLabel()
    {
        if (S.PeerHost.Length == 0) return "(no other PC set)";
        if (Disc.Peers.TryGetValue(S.PeerHost, out var p) && p.Name.Length > 0) return S.StreamerMode ? p.Name : $"{p.Name} ({p.Ip})";
        return Mask(S.PeerHost);
    }

    static readonly object LogLock = new();
    static void WriteLog(DateTime when, string text)
    {
        try
        {
            lock (LogLock)
            {
                Directory.CreateDirectory(Settings.Dir);
                var fi = new FileInfo(Settings.LogPath);
                if (fi.Exists && fi.Length > 2_000_000) File.Move(Settings.LogPath, Settings.LogPath + ".old", overwrite: true);
                File.AppendAllText(Settings.LogPath, $"{when:yyyy-MM-dd HH:mm:ss.fff}  {text}{Environment.NewLine}");
            }
        }
        catch { }
    }

    void OpenLogFile()
    {
        try
        {
            if (!File.Exists(Settings.LogPath)) WriteLog(DateTime.Now, "Log started.");
            Process.Start(new ProcessStartInfo(Settings.LogPath) { UseShellExecute = true });
        }
        catch (Exception ex) { SetStatus("Could not open the log: " + ex.Message); }
    }

    // =====================================================================  settings <-> runtime

    void LoadSettingsIntoUi()
    {
        _loadingUi = true;
        LoadConnectionUi();
        LoadOverlayUi();
        LoadAudioUi();
        LoadHotkeysUi();
        LoadFilesUi();
        ApplyStreamerModeToUi();
        UpdateTray();
        RefreshOverlayUrl();
        _loadingUi = false;
    }

    /// <summary>Called by the wizard after it changed settings.</summary>
    internal void ApplyFromWizard()
    {
        LoadSettingsIntoUi();
        ApplyRuntime();
        SetStatus(Summary());
    }

    internal void RefreshFromSettings() => LoadSettingsIntoUi();

    internal string Summary()
    {
        var parts = new List<string>();
        if (S.OverlayCapture || S.OverlayServer) parts.Add("overlay");
        if (S.AudioEnabled) parts.Add("audio");
        if (S.HotkeysEnabled) parts.Add("hotkeys");
        if (S.FileSendEnabled || S.FileReceiveEnabled) parts.Add("files");
        return $"{S.RoleText}  ·  other PC: {PeerLabel()}  ·  on: {(parts.Count == 0 ? "nothing yet" : string.Join(", ", parts))}";
    }

    internal void ApplyRuntime()
    {
        Link.Passphrase = S.Passphrase;
        Link.Start(S.Port);
        UpdateTray();
        ApplyHotkeyRuntime();
        ApplyOverlayRuntime();
        ApplyFileRuntime();
        ApplyAudioRuntime();
    }

    void SetEnabled(bool on)
    {
        S.Enabled = on;
        S.Save();
        if (!on) { ReleaseAllHeldTriggers(); ReleaseAllHeldActions("paused"); }
        ApplyRuntime();
        SetStatus(on ? "Active. " + Summary() : "Paused - nothing is sent, received or shown until you resume.");
    }

    internal void UpdateTray()
    {
        _miEnabled.Checked = S.Enabled;
        _tray.Icon = S.Enabled ? _iconOn : _iconOff;
        var peer = S.PeerHost.Length == 0 ? "" : "  →  " + PeerName();
        var text = $"KennelBridge: {(S.Enabled ? S.RoleText : "paused")}{peer}";
        _tray.Text = text.Length > 63 ? text[..63] : text;
        _pill.Text = S.Enabled ? $"Active · {S.RoleText}{peer}" : "Paused";
        _pill.Dot = S.Enabled ? Green : Muted;
        _pill.Fill = S.Enabled ? Color.FromArgb(24, 38, 30) : Field;
        _pauseBtn.Text = S.Enabled ? "Pause" : "Resume";
    }

    void RunWizard(SetupWizard.Page? startAt = null)
    {
        UnregisterHotkeys();
        try
        {
            using var w = new SetupWizard(this, startAt);
            w.ShowDialog(this);
        }
        finally { ApplyFromWizard(); }
    }

    // =====================================================================  window / tray

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        OnHotkeyHandleCreated();
        ApplyOverlayRuntime();
    }

    protected override void OnHandleDestroyed(EventArgs e) { UnregisterHotkeys(); base.OnHandleDestroyed(e); }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_HOTKEY) { OnWmHotkey(m.WParam.ToInt32()); return; }
        if (m.Msg == InputMonitor.WM_INPUT) _input.HandleRawInput(m.LParam);
        else if (m.Msg == InputMonitor.WM_INPUT_DEVICE_CHANGE) _input.HandleDeviceChange(m.WParam, m.LParam);
        base.WndProc(ref m);
    }

    protected override void SetVisibleCore(bool value)
    {
        if (!_allowVisible) value = false;
        base.SetVisibleCore(value);
    }

    void ShowWindow()
    {
        _allowVisible = true;
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (!_restoredSize && S.WindowWidth >= MinimumSize.Width && S.WindowHeight >= MinimumSize.Height)
        {
            Size = new Size(S.WindowWidth, S.WindowHeight);
            CenterToScreen();
        }
        _restoredSize = true;
        if (!S.SetupDone && !_wizardShown) { _wizardShown = true; BeginInvoke(() => RunWizard()); }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        RememberWindowSize();
        if (!_reallyExit && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            if (!_shownTrayTip)
            {
                _shownTrayTip = true;
                _tray.ShowBalloonTip(3000, "KennelBridge", "Still running in the system tray. Right-click the icon to exit.", ToolTipIcon.Info);
            }
            return;
        }
        base.OnFormClosing(e);
    }

    void RememberWindowSize()
    {
        if (WindowState != FormWindowState.Normal || !Visible) return;
        if (S.WindowWidth == Width && S.WindowHeight == Height) return;
        S.WindowWidth = Width; S.WindowHeight = Height;
        try { S.Save(); } catch { }
    }

    void ExitApp() { _reallyExit = true; Close(); }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _uiTimer.Stop(); _flashTimer.Stop();
        ShutdownHotkeys();
        ShutdownOverlay();
        ShutdownFiles();
        ShutdownAudio();
        Disc.Dispose();
        Link.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        base.OnFormClosed(e);
    }

    internal void SetStatus(string text) => _status.Text = Mask(text);

    void SafeStatus(string text)
    {
        if (IsHandleCreated && InvokeRequired) BeginInvoke(() => SetStatus(text));
        else SetStatus(text);
    }

    // =====================================================================  helpers

    internal static string LocalIps()
    {
        try
        {
            var ips = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Select(u => u.Address)
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !a.ToString().StartsWith("169.254."))
                .Select(a => a.ToString()).Distinct().ToList();
            return ips.Count == 0 ? "(no network)" : string.Join(", ", ips);
        }
        catch { return "(unknown)"; }
    }

    /// <summary>Inbound rules for every port the bridges use: UDP data + discovery + audio, TCP files. One UAC prompt.</summary>
    internal bool AllowFirewall()
    {
        var udp = $"{S.Port},{Discovery.Port},{S.AudioPort}";
        var script = "netsh advfirewall firewall delete rule name=\"KennelBridge\" >nul 2>&1 & " +
                     $"netsh advfirewall firewall add rule name=\"KennelBridge\" dir=in action=allow protocol=UDP localport={udp} & " +
                     $"netsh advfirewall firewall add rule name=\"KennelBridge\" dir=in action=allow protocol=TCP localport={S.FilePort}";
        try
        {
            var p = Process.Start(new ProcessStartInfo("cmd.exe", "/c " + script) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden });
            p?.WaitForExit(15000);
            bool ok = p is { ExitCode: 0 };
            SetStatus(ok ? $"Firewall now allows UDP {udp} and TCP {S.FilePort} in." : "Firewall rule was not added.");
            return ok;
        }
        catch (Exception ex) { SetStatus("Firewall change cancelled: " + ex.Message); return false; }
    }

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    internal static bool GetStartWithWindows()
    {
        try { using var k = Registry.CurrentUser.OpenSubKey(RunKey); return k?.GetValue("KennelBridge") != null; }
        catch { return false; }
    }

    internal static void SetStartWithWindows(bool on)
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey, writable: true) ?? Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) k.SetValue("KennelBridge", $"\"{Environment.ProcessPath}\" --minimized");
        else k.DeleteValue("KennelBridge", throwOnMissingValue: false);
    }

    /// <summary>The Kennel dog-head logo, embedded as app.png.</summary>
    static Bitmap LoadLogo()
    {
        try
        {
            using var rs = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("app.png");
            if (rs != null) return new Bitmap(rs);
        }
        catch { }
        var b = new Bitmap(64, 64); using var g = Graphics.FromImage(b); g.Clear(Olive); return b;
    }

    /// <summary>Tray icon: the Kennel logo, with a status dot in the corner (grey = paused, green = activity).</summary>
    static Icon MakeIcon(Color? dot)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.Clear(Color.Transparent);
            using var path = Theme.Rounded(new Rectangle(0, 0, 31, 31), 7);
            g.SetClip(path);
            g.DrawImage(Logo, new Rectangle(0, 0, 32, 32));
            g.ResetClip();
            if (dot is Color c)
            {
                using var edge = new SolidBrush(Bg); g.FillEllipse(edge, 18, 18, 14, 14);
                using var br = new SolidBrush(c); g.FillEllipse(br, 20, 20, 10, 10);
            }
        }
        return Icon.FromHandle(bmp.GetHicon());   // lives for the process lifetime (three icons total) - intentional
    }
}
