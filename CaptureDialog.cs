using static KennelBridge.Theme;

namespace KennelBridge;

/// <summary>
/// "Add press": waits for the user to press the key or mouse button to use on this PC,
/// then what the other PC should do. Keys are captured from the keyboard; Middle / Mouse 4 /
/// Mouse 5 are captured anywhere via the mouse hook; Left / Right click are offered as buttons.
/// </summary>
public sealed class CaptureDialog : Form
{
    public Binding Result { get; } = new();

    readonly Label _box1 = new(), _box2 = new();
    readonly Button _ok = Theme.Button("OK", primary: true);
    readonly CheckBox _hold = Theme.Check("Hold: the other PC keeps it pressed for as long as I hold it here  (untick for a single tap)", true);
    readonly CheckBox _pass = Theme.Check("Also press it on this PC  (the game still sees it; untick to send it only to the other PC)", true);
    int _step = 1;
    bool _have1, _have2;

    public CaptureDialog(Binding? existing)
    {
        Text = existing == null ? "Add press" : "Edit press";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96f, 96f);
        ApplyWindow(this);
        ClientSize = new Size(560, 428);
        KeyPreview = true;

        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 9, Padding = new Padding(20, 16, 20, 14) };
        t.ColumnStyles.Add(Cpct(100));
        foreach (var h in new[] { 28, 62, 28, 62, 44, 36, 36, 30, 44 }) t.RowStyles.Add(Px(h));
        Controls.Add(t);

        t.Controls.Add(Theme.Label("1.  Press the key or mouse button you will use on THIS PC", font: Semibold), 0, 0);
        StyleBox(_box1); _box1.Click += (_, _) => SetStep(1);
        t.Controls.Add(_box1, 0, 1);

        t.Controls.Add(Theme.Label("2.  Now press what the OTHER PC should do", font: Semibold), 0, 2);
        StyleBox(_box2); _box2.Click += (_, _) => SetStep(2);
        t.Controls.Add(_box2, 0, 3);

        var quick = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0), Padding = new Padding(0, 6, 0, 0) };
        Button Q(string text, Action a) { var b = Theme.Button(text, minWidth: 90); b.MinimumSize = new Size(90, 30); b.Click += (_, _) => a(); return b; }
        quick.Controls.Add(Q("Same as step 1", () => { if (_have1) SetAction(Result.TriggerKind, Result.TriggerVk, Result.TriggerMods); }));
        quick.Controls.Add(Q("Left click", () => SetAction(ActionKind.LeftClick, 0, 0)));
        quick.Controls.Add(Q("Right click", () => SetAction(ActionKind.RightClick, 0, 0)));
        t.Controls.Add(quick, 0, 4);

        _hold.Margin = new Padding(0, 6, 0, 0);
        t.Controls.Add(_hold, 0, 5);
        _pass.Margin = new Padding(0, 6, 0, 0);
        t.Controls.Add(_pass, 0, 6);

        t.Controls.Add(Theme.Label("Middle, Mouse 4 and Mouse 5 are detected wherever you click. Esc cancels.", muted: true, Small), 0, 7);

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = new Padding(0), Padding = new Padding(0, 8, 0, 0) };
        var cancel = Theme.Button("Cancel"); cancel.DialogResult = DialogResult.Cancel; cancel.Margin = new Padding(8, 0, 0, 0);
        _ok.Enabled = false; _ok.Margin = new Padding(8, 0, 0, 0);
        _ok.Click += (_, _) => { Result.Hold = _hold.Checked; Result.PassThrough = _pass.Checked; DialogResult = DialogResult.OK; Close(); };
        bottom.Controls.Add(cancel); bottom.Controls.Add(_ok);
        t.Controls.Add(bottom, 0, 8);
        CancelButton = cancel;

        if (existing != null)
        {
            Result.TriggerKind = existing.TriggerKind; Result.TriggerVk = existing.TriggerVk; Result.TriggerMods = existing.TriggerMods;
            Result.Kind = existing.Kind; Result.ActionVk = existing.ActionVk; Result.ActionMods = existing.ActionMods;
            _hold.Checked = existing.Hold;
            _pass.Checked = existing.PassThrough;
            _have1 = _have2 = true;
        }
        Render();
    }

    static void StyleBox(Label l)
    {
        l.Dock = DockStyle.Fill; l.AutoSize = false;
        l.TextAlign = ContentAlignment.MiddleCenter;
        l.BorderStyle = BorderStyle.FixedSingle;
        l.Font = new Font("Segoe UI Semibold", 15f);
        l.ForeColor = Fg;
        l.Cursor = Cursors.Hand;
        l.Margin = new Padding(0, 2, 0, 6);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        MouseHook.ButtonDown = OnMouseButton;
        Focus();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        MouseHook.ButtonDown = null;
        base.OnFormClosed(e);
    }

    void SetStep(int s) { _step = s; Render(); }

    bool OnMouseButton(ActionKind k)
    {
        if (_step == 1) SetTrigger(k, 0, 0); else SetAction(k, 0, 0);
        return true;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        const int WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104;
        if (msg.Msg != WM_KEYDOWN && msg.Msg != WM_SYSKEYDOWN) return base.ProcessCmdKey(ref msg, keyData);
        var key = keyData & Keys.KeyCode;
        if (key == Keys.Escape) return base.ProcessCmdKey(ref msg, keyData);
        if (key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin
            or Keys.LControlKey or Keys.RControlKey or Keys.LShiftKey or Keys.RShiftKey or Keys.LMenu or Keys.RMenu)
            return true;
        int mods = 0;
        if ((keyData & Keys.Control) != 0) mods |= 2;
        if ((keyData & Keys.Alt) != 0) mods |= 1;
        if ((keyData & Keys.Shift) != 0) mods |= 4;
        if (_step == 1) SetTrigger(ActionKind.Key, (int)key, mods); else SetAction(ActionKind.Key, (int)key, mods);
        return true;
    }

    void SetTrigger(ActionKind kind, int vk, int mods)
    {
        Result.TriggerKind = kind; Result.TriggerVk = vk; Result.TriggerMods = mods;
        _have1 = true; _step = 2;
        Render();
    }

    void SetAction(ActionKind kind, int vk, int mods)
    {
        Result.Kind = kind; Result.ActionVk = vk; Result.ActionMods = mods;
        _have2 = true;
        Render();
    }

    void Render()
    {
        _box1.Text = _have1 ? Result.TriggerText : (_step == 1 ? "waiting for a key or button…" : "");
        _box2.Text = _have2 ? Result.ActionText : (_step == 2 ? "waiting for a key or button…" : "");
        _box1.BackColor = _step == 1 ? Selection : Field;
        _box2.BackColor = _step == 2 ? Selection : Field;
        _box1.ForeColor = _step == 1 ? Amber : Fg;
        _box2.ForeColor = _step == 2 ? Amber : Fg;
        _ok.Enabled = _have1 && _have2;
    }

    protected override void OnLoad(EventArgs e) { base.OnLoad(e); Theme.ApplyDpi(this); Theme.FitToScreen(this); }
    protected override void OnDpiChanged(DpiChangedEventArgs e) { base.OnDpiChanged(e); Theme.ApplyDpi(this); }
}
