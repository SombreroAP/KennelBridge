using static KennelBridge.Theme;

namespace KennelBridge;

/// <summary>Single-purpose prompt: "press the key or mouse button you want to use". Returns the trigger or null.</summary>
public sealed class PressPrompt : Form
{
    public ActionKind Kind { get; private set; } = ActionKind.Key;
    public int Vk { get; private set; }
    public int Mods { get; private set; }

    readonly Label _box = new();

    public PressPrompt(string title, string prompt)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96f, 96f);
        ApplyWindow(this);
        ClientSize = new Size(520, 236);
        KeyPreview = true;

        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(20, 16, 20, 14) };
        t.ColumnStyles.Add(Cpct(100));
        foreach (var h in new[] { 52, 74, 30, 44 }) t.RowStyles.Add(Px(h));
        Controls.Add(t);
        t.Controls.Add(Wrapped(prompt, muted: false), 0, 0);
        _box.Dock = DockStyle.Fill; _box.TextAlign = ContentAlignment.MiddleCenter; _box.BorderStyle = BorderStyle.FixedSingle;
        _box.Font = new Font("Segoe UI Semibold", 15f); _box.BackColor = Selection; _box.ForeColor = Amber;
        _box.Text = "waiting for a key or mouse button…";
        _box.Margin = new Padding(0, 2, 0, 6);
        t.Controls.Add(_box, 0, 1);
        t.Controls.Add(Theme.Label("Keys, Middle, Mouse 4 and Mouse 5 all work. Esc cancels.", muted: true, Small), 0, 2);
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = new Padding(0), Padding = new Padding(0, 8, 0, 0) };
        var cancel = Theme.Button("Cancel"); cancel.DialogResult = DialogResult.Cancel; cancel.Margin = new Padding(0);
        bottom.Controls.Add(cancel);
        t.Controls.Add(bottom, 0, 3);
        CancelButton = cancel;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        MouseHook.ButtonDown = k => { Done(k, 0, 0); return true; };
        Focus();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        MouseHook.ButtonDown = null;
        base.OnFormClosed(e);
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
        Done(ActionKind.Key, (int)key, mods);
        return true;
    }

    void Done(ActionKind kind, int vk, int mods)
    {
        Kind = kind; Vk = vk; Mods = mods;
        _box.Text = kind == ActionKind.Key ? KeyNames.Describe(mods, vk) : KeyNames.MouseName(kind);
        BeginInvoke(() => { DialogResult = DialogResult.OK; Close(); });
    }

    protected override void OnLoad(EventArgs e) { base.OnLoad(e); Theme.ApplyDpi(this); Theme.FitToScreen(this); }
    protected override void OnDpiChanged(DpiChangedEventArgs e) { base.OnDpiChanged(e); Theme.ApplyDpi(this); }
}
