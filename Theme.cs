using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;

namespace KennelBridge;

/// <summary>The Kennel palette: graphite, amber, olive, bone. One place for every colour, font and control style.</summary>
public static class Theme
{
    // ground → rail → surface → raised; one warm accent. Values match the Claude Design mockup.
    public static readonly Color Bg = Color.FromArgb(12, 15, 17);
    public static readonly Color Rail = Color.FromArgb(16, 20, 23);
    public static readonly Color CardBg = Color.FromArgb(21, 26, 30);
    public static readonly Color CardBorder = Color.FromArgb(35, 42, 48);
    public static readonly Color Line2 = Color.FromArgb(46, 54, 61);
    public static readonly Color Field = Color.FromArgb(28, 34, 39);
    public static readonly Color FieldHover = Color.FromArgb(36, 43, 49);
    public static readonly Color Input = Color.FromArgb(15, 19, 22);
    public static readonly Color Fg = Color.FromArgb(236, 233, 226);
    public static readonly Color Muted = Color.FromArgb(167, 174, 181);
    public static readonly Color Amber = Color.FromArgb(210, 160, 74);
    public static readonly Color AmberHover = Color.FromArgb(226, 180, 100);
    public static readonly Color Ink = Color.FromArgb(23, 18, 10);
    public static readonly Color Olive = Color.FromArgb(140, 154, 91);
    public static readonly Color Green = Color.FromArgb(92, 194, 122);
    public static readonly Color Red = Color.FromArgb(224, 113, 95);
    public static readonly Color Selection = Color.FromArgb(34, 29, 18);

    public static readonly Font Body = new("Segoe UI", 10.5f);
    public static readonly Font Small = new("Segoe UI", 9.5f);
    public static readonly Font Semibold = new("Segoe UI Semibold", 10.5f);
    public static readonly Font Title = new("Segoe UI Semibold", 11f);
    public static readonly Font Big = new("Segoe UI Semibold", 18f);
    public static readonly Font Huge = new("Segoe UI Semibold", 14f);
    public static readonly Font Mono = new("Consolas", 10f);
    /// <summary>Windows' own icon font: Segoe Fluent Icons on Windows 11, Segoe MDL2 Assets on 10.</summary>
    public static readonly Font Icons = MakeIconFont();

    static Font MakeIconFont()
    {
        using var fonts = new System.Drawing.Text.InstalledFontCollection();
        var name = fonts.Families.Any(f => f.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
        return new Font(name, 12f);
    }

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Dark background, bone text, dark title bar (and the title bar tinted to the ground on Windows 11).</summary>
    public static void ApplyWindow(Form f)
    {
        f.BackColor = Bg;
        f.ForeColor = Fg;
        f.Font = Body;
        f.HandleCreated += (_, _) => DarkTitleBar(f.Handle);
        if (f.IsHandleCreated) DarkTitleBar(f.Handle);
    }

    static void DarkTitleBar(IntPtr h)
    {
        int on = 1;
        try { if (DwmSetWindowAttribute(h, 20, ref on, 4) != 0) DwmSetWindowAttribute(h, 19, ref on, 4); } catch { }
        try { int cap = Rail.R | (Rail.G << 8) | (Rail.B << 16); DwmSetWindowAttribute(h, 35, ref cap, 4); } catch { }   // DWMWA_CAPTION_COLOR, Windows 11
    }

    public static GraphicsPath Rounded(Rectangle r, int radius)
    {
        int d = radius * 2;
        var p = new GraphicsPath();
        if (d <= 0) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    /// <summary>The colour an owner-drawn control should paint behind its rounded shape.</summary>
    public static Color BackOf(Control c)
    {
        for (var p = c.Parent; p != null; p = p.Parent)
            if (p.BackColor.A == 255) return p is Card ? CardBg : p.BackColor;
        return Bg;
    }

    // ----- controls -----

    public static Button Button(string text, bool primary = false, int minWidth = 88)
        => new KButton
        {
            Text = text,
            Kind = primary ? KButton.Style.Primary : KButton.Style.Secondary,
            AutoSize = true,
            MinimumSize = new Size(minWidth, 34),
            Font = primary ? Semibold : Body,
            Padding = new Padding(12, 0, 12, 0),
            Margin = new Padding(0, 0, 8, 0),
        };

    /// <summary>A button that behaves as one segment of a segmented control.</summary>
    public static void SetSegment(Button b, bool selected)
    {
        if (b is KButton k) { k.Kind = selected ? KButton.Style.SegmentOn : KButton.Style.SegmentOff; k.Font = selected ? Semibold : Body; k.Invalidate(); return; }
        b.BackColor = selected ? Field : Bg; b.ForeColor = selected ? Fg : Muted;
    }

    public static TextBox TextBox(string placeholder = "")
        => new()
        {
            BackColor = Input, ForeColor = Fg, BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = placeholder, Font = Body, Margin = new Padding(0, 4, 0, 4),
        };

    public static ComboBox ComboBox()
        => new()
        {
            BackColor = Input, ForeColor = Fg, FlatStyle = FlatStyle.Flat, Font = Body,
            DropDownStyle = ComboBoxStyle.DropDown, Margin = new Padding(0, 4, 0, 4),
        };

    /// <summary>An on/off switch (a CheckBox underneath, so Checked / CheckedChanged work as before).</summary>
    public static CheckBox Check(string text, bool isChecked = false)
        => new KennelCheck { Text = text, Checked = isChecked };

    public static Label Label(string text, bool muted = false, Font? font = null)
        => new() { Text = text, AutoSize = true, ForeColor = muted ? Muted : Fg, Font = font ?? Body, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 8, 0) };

    public static Label Wrapped(string text, bool muted = true)
        => new() { Text = text, AutoSize = false, Dock = DockStyle.Fill, ForeColor = muted ? Muted : Fg, Font = Body };

    /// <summary>Owner-drawn ListView that sits flush in a card: roomy rows, hairline dividers, warm selection.</summary>
    public static void StyleList(ListView lv)
    {
        lv.BackColor = CardBg;
        lv.ForeColor = Fg;
        lv.BorderStyle = BorderStyle.None;
        lv.View = View.Details;
        lv.FullRowSelect = true;
        lv.MultiSelect = false;
        lv.HideSelection = false;
        lv.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        lv.OwnerDraw = true;
        lv.Font = Body;
        lv.SmallImageList = new ImageList { ImageSize = new Size(1, 36) };   // the only way to set a ListView's row height
        lv.DrawColumnHeader += (_, e) =>
        {
            using var b = new SolidBrush(CardBg);
            e.Graphics.FillRectangle(b, e.Bounds);
            using var p = new Pen(CardBorder);
            e.Graphics.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            var r = e.Bounds; r.Inflate(-10, 0);
            TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? "", Small, r, Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        };
        lv.DrawItem += (_, _) => { };
        lv.DrawSubItem += (_, e) =>
        {
            bool sel = e.Item?.Selected == true;
            using var b = new SolidBrush(sel ? Selection : CardBg);
            e.Graphics.FillRectangle(b, e.Bounds);
            using (var p = new Pen(CardBorder)) e.Graphics.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            if (sel && e.ColumnIndex == 0) { using var a = new SolidBrush(Amber); e.Graphics.FillRectangle(a, e.Bounds.Left, e.Bounds.Top + 9, 3, e.Bounds.Height - 18); }
            var r = e.Bounds; r.Inflate(-10, 0);
            var color = e.Item == null || e.Item.ForeColor == SystemColors.WindowText || e.Item.ForeColor.IsEmpty ? Fg : e.Item.ForeColor;
            if (e.ColumnIndex > 0 && color == Fg) color = Muted;   // first column is the name; the rest are details
            TextRenderer.DrawText(e.Graphics, e.SubItem?.Text ?? "", lv.Font, r, color, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        };
    }

    public static RowStyle Px(int h) => new(SizeType.Absolute, h);
    public static RowStyle Pct(float p) => new(SizeType.Percent, p);
    public static ColumnStyle Cpx(int w) => new(SizeType.Absolute, w);
    public static ColumnStyle Cpct(float p) => new(SizeType.Percent, p);
}

/// <summary>Rounded owner-drawn button: primary (amber), secondary (raised surface) or a segment of a segmented control.</summary>
public sealed class KButton : Button
{
    public enum Style { Primary, Secondary, SegmentOn, SegmentOff }
    public Style Kind { get; set; } = Style.Secondary;
    bool _hover, _down;

    public KButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        UseVisualStyleBackColor = false;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.BackOf(this));
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Theme.Rounded(r, Kind is Style.SegmentOn or Style.SegmentOff ? 6 : 7);
        Color fill, border, text;
        switch (Kind)
        {
            case Style.Primary:
                fill = !Enabled ? Theme.Field : _down ? Theme.Amber : _hover ? Theme.AmberHover : Theme.Amber;
                border = fill; text = Enabled ? Theme.Ink : Theme.Muted; break;
            case Style.SegmentOn:
                fill = Theme.Field; border = Theme.Line2; text = Theme.Fg; break;
            case Style.SegmentOff:
                fill = _hover ? Theme.CardBg : Theme.Input; border = Theme.CardBorder; text = Theme.Muted; break;
            default:
                fill = _down ? Theme.CardBorder : _hover ? Theme.FieldHover : Theme.Field;
                border = Theme.Line2; text = Enabled ? Theme.Fg : Theme.Muted; break;
        }
        using (var b = new SolidBrush(fill)) g.FillPath(b, path);
        using (var p = new Pen(border)) g.DrawPath(p, path);
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        if (Focused && ShowFocusCues) { using var fp = new Pen(Color.FromArgb(160, Theme.Amber)); g.DrawPath(fp, path); }
    }
}

/// <summary>Owner-drawn on/off switch with its label to the right. Amber track and a dark knob when on.</summary>
public sealed class KennelCheck : CheckBox
{
    const int TrackW = 40, TrackH = 22;

    public KennelCheck()
    {
        AutoSize = true;
        ForeColor = Theme.Fg;
        Font = Theme.Body;
        Cursor = Cursors.Hand;
        Margin = new Padding(0, 6, 18, 0);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    public override Size GetPreferredSize(Size proposed)
    {
        var t = TextRenderer.MeasureText(Text, Font);
        return new Size(TrackW + (Text.Length > 0 ? 12 + t.Width + 4 : 2), Math.Max(TrackH + 6, t.Height + 6));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.BackOf(this));
        int y = (Height - TrackH) / 2;
        var track = new Rectangle(1, y, TrackW - 2, TrackH - 1);
        using var path = Theme.Rounded(track, (TrackH - 1) / 2);
        bool hover = ClientRectangle.Contains(PointToClient(MousePosition));
        if (Checked)
        {
            using (var fill = new SolidBrush(!Enabled ? Theme.Line2 : hover ? Theme.AmberHover : Theme.Amber)) g.FillPath(fill, path);
            using var knob = new SolidBrush(Theme.Ink);
            g.FillEllipse(knob, track.Right - 19, y + 3, 16, 16);
        }
        else
        {
            using (var fill = new SolidBrush(hover && Enabled ? Theme.FieldHover : Theme.Field)) g.FillPath(fill, path);
            using (var pen = new Pen(Theme.Line2)) g.DrawPath(pen, path);
            using var knob = new SolidBrush(Enabled ? Theme.Muted : Theme.Line2);
            g.FillEllipse(knob, track.Left + 3, y + 3, 16, 16);
        }
        if (Text.Length > 0)
        {
            var tr = new Rectangle(TrackW + 12, 0, Width - TrackW - 12, Height);
            TextRenderer.DrawText(g, Text, Font, tr, Enabled ? ForeColor : Theme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
        if (Focused && ShowFocusCues) { using var fp = new Pen(Color.FromArgb(160, Theme.Amber)); g.DrawPath(fp, path); }
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Invalidate(); }
}

/// <summary>A grouped settings surface: rounded, hairline border, a short heading in the top-left and an optional note on the right.</summary>
public sealed class Card : Panel
{
    public string Title { get; set; } = "";
    string _hint = "";
    public string Hint { get => _hint; set { _hint = value; Invalidate(); } }

    public Card(string title = "")
    {
        Title = title;
        BackColor = Theme.CardBg;
        DoubleBuffered = true;
        ResizeRedraw = true;
        Padding = title.Length > 0 ? new Padding(18, 46, 18, 14) : new Padding(18, 14, 18, 14);
        Margin = new Padding(0, 0, 0, 16);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var bg = new SolidBrush(Theme.Bg)) g.FillRectangle(bg, ClientRectangle);
        using var path = Theme.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 10);
        using (var fill = new SolidBrush(Theme.CardBg)) g.FillPath(fill, path);
        using (var pen = new Pen(Theme.CardBorder)) g.DrawPath(pen, path);
        if (Title.Length > 0)
        {
            TextRenderer.DrawText(g, Title, Theme.Title, new Point(18, 14), Theme.Fg, TextFormatFlags.NoPrefix);
            using var pen = new Pen(Theme.CardBorder);
            g.DrawLine(pen, 1, 40, Width - 2, 40);
        }
        if (_hint.Length > 0)
        {
            var r = new Rectangle(Width / 3, 12, Width * 2 / 3 - 18, 24);
            TextRenderer.DrawText(g, _hint, Theme.Small, r, Theme.Muted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }
}

/// <summary>One entry in the left rail: an icon, a name, and an optional status dot (green = that bridge is on).</summary>
public sealed class NavItem : Control
{
    public string Glyph { get; }
    public bool Active { get; set; }
    public Func<bool?>? State { get; set; }
    bool _hover;

    public NavItem(string text, string glyph)
    {
        Text = text; Glyph = glyph;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
        Cursor = Cursors.Hand;
        Size = new Size(206, 40);
        Margin = new Padding(0, 0, 0, 2);
        TabStop = true;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode is Keys.Enter or Keys.Space) OnClick(EventArgs.Empty); base.OnKeyDown(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Rail);
        using var path = Theme.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 8);
        if (Active || _hover) { using var b = new SolidBrush(Active ? Theme.Field : Theme.CardBg); g.FillPath(b, path); }
        if (Focused && ShowFocusCues) { using var fp = new Pen(Color.FromArgb(140, Theme.Amber)); g.DrawPath(fp, path); }
        TextRenderer.DrawText(g, Glyph, Theme.Icons, new Rectangle(12, 0, 22, Height), Active ? Theme.Amber : Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, Text, Active ? Theme.Semibold : Theme.Body, new Rectangle(44, 0, Width - 70, Height), Active ? Theme.Fg : Theme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        if (State?.Invoke() is bool on) { using var d = new SolidBrush(on ? Theme.Green : Theme.Line2); g.FillEllipse(d, Width - 20, Height / 2 - 4, 8, 8); }
    }
}

/// <summary>Small rounded status badge: "● Active · sends + receives".</summary>
public sealed class Pill : Label
{
    Color _fill = Theme.Field;
    public Color Fill { get => _fill; set { _fill = value; Invalidate(); } }
    Color _dot = Theme.Green;
    public Color Dot { get => _dot; set { _dot = value; Invalidate(); } }

    public Pill()
    {
        AutoSize = true;
        Padding = new Padding(24, 7, 12, 7);
        ForeColor = Theme.Fg;
        Font = Theme.Body;
        BackColor = Theme.Rail;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.BackOf(this));
        using var path = Theme.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 8);
        using (var fill = new SolidBrush(_fill)) g.FillPath(fill, path);
        using (var dot = new SolidBrush(_dot)) g.FillEllipse(dot, 10, Height / 2 - 4, 8, 8);
        var r = new Rectangle(Padding.Left, 0, Width - Padding.Left - Padding.Right, Height);
        TextRenderer.DrawText(g, Text, Font, r, ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>A big selectable option (used by the setup wizard for the role choice).</summary>
public sealed class OptionCard : Panel
{
    public string Title { get; }
    public string Description { get; }
    bool _selected;
    public bool Selected { get => _selected; set { _selected = value; Invalidate(); } }

    public OptionCard(string title, string description)
    {
        Title = title; Description = description;
        BackColor = Theme.Bg;
        Cursor = Cursors.Hand;
        DoubleBuffered = true;
        ResizeRedraw = true;
        Margin = new Padding(0, 0, 0, 10);
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var bg = new SolidBrush(Theme.Bg)) g.FillRectangle(bg, ClientRectangle);
        using var path = Theme.Rounded(new Rectangle(1, 1, Width - 3, Height - 3), 10);
        bool hover = ClientRectangle.Contains(PointToClient(MousePosition));
        using (var fill = new SolidBrush(_selected ? Theme.Selection : hover ? Theme.FieldHover : Theme.CardBg)) g.FillPath(fill, path);
        using (var pen = new Pen(_selected ? Theme.Amber : Theme.CardBorder, _selected ? 2f : 1f)) g.DrawPath(pen, path);

        // radio dot
        var dotRect = new Rectangle(18, Height / 2 - 9, 18, 18);
        using (var ring = new Pen(_selected ? Theme.Amber : Theme.Muted, 2f)) g.DrawEllipse(ring, dotRect);
        if (_selected) { using var d = new SolidBrush(Theme.Amber); g.FillEllipse(d, Rectangle.Inflate(dotRect, -5, -5)); }

        var textRect = new Rectangle(52, 12, Width - 66, Height - 24);
        TextRenderer.DrawText(g, Title, Theme.Huge, textRect, Theme.Fg, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPrefix);
        var descRect = new Rectangle(52, 40, Width - 66, Height - 48);
        TextRenderer.DrawText(g, Description, Theme.Body, descRect, Theme.Muted, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
    }
}
