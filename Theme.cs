using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace KennelBridge;

/// <summary>The Kennel palette: graphite, amber, olive, bone. One place for every colour, font and control style.</summary>
public static class Theme
{
    public static readonly Color Bg = Color.FromArgb(11, 14, 16);
    public static readonly Color CardBg = Color.FromArgb(21, 26, 30);
    public static readonly Color CardBorder = Color.FromArgb(40, 47, 53);
    public static readonly Color Field = Color.FromArgb(34, 40, 46);
    public static readonly Color FieldHover = Color.FromArgb(40, 47, 53);
    public static readonly Color Fg = Color.FromArgb(242, 239, 232);
    public static readonly Color Muted = Color.FromArgb(176, 181, 187);
    public static readonly Color Amber = Color.FromArgb(201, 154, 59);
    public static readonly Color AmberHover = Color.FromArgb(220, 175, 84);
    public static readonly Color Olive = Color.FromArgb(140, 154, 91);
    public static readonly Color Green = Color.FromArgb(76, 190, 90);
    public static readonly Color Red = Color.FromArgb(206, 96, 80);
    public static readonly Color Selection = Color.FromArgb(58, 47, 24);

    public static readonly Font Body = new("Segoe UI", 11f);
    public static readonly Font Small = new("Segoe UI", 10f);
    public static readonly Font Semibold = new("Segoe UI Semibold", 11f);
    public static readonly Font Title = new("Segoe UI Semibold", 13f);
    public static readonly Font Big = new("Segoe UI Semibold", 20f);
    public static readonly Font Huge = new("Segoe UI Semibold", 17f);

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Dark background, bone text, dark title bar.</summary>
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
    }

    public static GraphicsPath Rounded(Rectangle r, int radius)
    {
        int d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    // ----- controls -----

    public static Button Button(string text, bool primary = false, int minWidth = 96)
    {
        var b = new Button
        {
            Text = text,
            AutoSize = true,
            MinimumSize = new Size(minWidth, 36),
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Amber : Field,
            ForeColor = primary ? Bg : Fg,
            Font = primary ? Semibold : Body,
            Padding = new Padding(10, 2, 10, 2),
            Margin = new Padding(0, 0, 8, 0),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = primary ? AmberHover : FieldHover;
        b.FlatAppearance.MouseDownBackColor = primary ? Amber : CardBorder;
        return b;
    }

    /// <summary>A button that behaves as one segment of a segmented control.</summary>
    public static void SetSegment(Button b, bool selected)
    {
        b.BackColor = selected ? Amber : Field;
        b.ForeColor = selected ? Bg : Fg;
        b.Font = selected ? Semibold : Body;
        b.FlatAppearance.MouseOverBackColor = selected ? AmberHover : FieldHover;
    }

    public static TextBox TextBox(string placeholder = "")
    {
        var t = new TextBox
        {
            BackColor = Field, ForeColor = Fg, BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = placeholder, Font = Body, Margin = new Padding(0, 4, 0, 4),
        };
        return t;
    }

    public static ComboBox ComboBox()
    {
        return new ComboBox
        {
            BackColor = Field, ForeColor = Fg, FlatStyle = FlatStyle.Flat, Font = Body,
            DropDownStyle = ComboBoxStyle.DropDown, Margin = new Padding(0, 4, 0, 4),
        };
    }

    public static CheckBox Check(string text, bool isChecked = false)
        => new KennelCheck { Text = text, Checked = isChecked };

    public static Label Label(string text, bool muted = false, Font? font = null)
        => new() { Text = text, AutoSize = true, ForeColor = muted ? Muted : Fg, Font = font ?? Body, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 8, 0) };

    public static Label Wrapped(string text, bool muted = true)
        => new() { Text = text, AutoSize = false, Dock = DockStyle.Fill, ForeColor = muted ? Muted : Fg, Font = Body };

    /// <summary>Dark owner-drawn ListView with a flat header and amber-tinted selection.</summary>
    public static void StyleList(ListView lv)
    {
        lv.BackColor = Field;
        lv.ForeColor = Fg;
        lv.BorderStyle = BorderStyle.None;
        lv.View = View.Details;
        lv.FullRowSelect = true;
        lv.MultiSelect = false;
        lv.HideSelection = false;
        lv.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        lv.OwnerDraw = true;
        lv.Font = Body;
        lv.DrawColumnHeader += (_, e) =>
        {
            using var b = new SolidBrush(CardBg);
            e.Graphics.FillRectangle(b, e.Bounds);
            using var p = new Pen(CardBorder);
            e.Graphics.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            var r = e.Bounds; r.Inflate(-8, 0);
            TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? "", Small, r, Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        };
        lv.DrawItem += (_, _) => { };
        lv.DrawSubItem += (_, e) =>
        {
            bool sel = e.Item?.Selected == true;
            using var b = new SolidBrush(sel ? Selection : Field);
            e.Graphics.FillRectangle(b, e.Bounds);
            var r = e.Bounds; r.Inflate(-8, 0);
            var color = e.Item == null || e.Item.ForeColor == SystemColors.WindowText || e.Item.ForeColor.IsEmpty ? Fg : e.Item.ForeColor;
            TextRenderer.DrawText(e.Graphics, e.SubItem?.Text ?? "", lv.Font, r, color, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        };
    }

    public static RowStyle Px(int h) => new(SizeType.Absolute, h);
    public static RowStyle Pct(float p) => new(SizeType.Percent, p);
    public static ColumnStyle Cpx(int w) => new(SizeType.Absolute, w);
    public static ColumnStyle Cpct(float p) => new(SizeType.Percent, p);
}

/// <summary>Owner-drawn checkbox: amber box with a black tick when checked, so the state is obvious at a glance.</summary>
public sealed class KennelCheck : CheckBox
{
    const int Box = 20;

    public KennelCheck()
    {
        AutoSize = true;
        ForeColor = Theme.Fg;
        Font = Theme.Body;
        Cursor = Cursors.Hand;
        Margin = new Padding(0, 6, 18, 0);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    public override Size GetPreferredSize(Size proposed)
    {
        var t = TextRenderer.MeasureText(Text, Font);
        return new Size(Box + 10 + t.Width + 4, Math.Max(Box + 4, t.Height + 4));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        // parent background (cards are CardBg, pages are Bg)
        using (var bg = new SolidBrush(Parent?.BackColor is { } pc && pc != Color.Transparent ? pc : Theme.CardBg)) g.FillRectangle(bg, ClientRectangle);
        int y = (Height - Box) / 2;
        var box = new Rectangle(1, y, Box - 1, Box - 1);
        using var path = Theme.Rounded(box, 5);
        bool hover = ClientRectangle.Contains(PointToClient(MousePosition));
        if (Checked)
        {
            using var fill = new SolidBrush(Enabled ? (hover ? Theme.AmberHover : Theme.Amber) : Theme.Muted); g.FillPath(fill, path);
            using var pen = new Pen(Color.FromArgb(11, 14, 16), 2.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            g.DrawLines(pen, new[] { new PointF(box.Left + 4.5f, box.Top + 10f), new PointF(box.Left + 8.5f, box.Top + 14f), new PointF(box.Right - 4f, box.Top + 5.5f) });
        }
        else
        {
            using var fill = new SolidBrush(hover ? Theme.FieldHover : Theme.Field); g.FillPath(fill, path);
            using var pen = new Pen(Enabled ? Theme.Muted : Theme.CardBorder, 1.5f); g.DrawPath(pen, path);
        }
        var tr = new Rectangle(Box + 10, 0, Width - Box - 10, Height);
        TextRenderer.DrawText(g, Text, Font, tr, Enabled ? ForeColor : Theme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        if (Focused) { using var fp = new Pen(Theme.Amber) { DashStyle = DashStyle.Dot }; g.DrawRectangle(fp, 0, 0, Width - 1, Height - 1); }
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Invalidate(); }
}

/// <summary>Rounded panel with an optional title drawn in the top-left and a hint in the top-right.</summary>
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
        Padding = title.Length > 0 ? new Padding(16, 50, 16, 14) : new Padding(16, 14, 16, 14);
        Margin = new Padding(6);
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
            TextRenderer.DrawText(g, Title, Theme.Title, new Point(16, 12), Theme.Fg, TextFormatFlags.NoPrefix);
        if (_hint.Length > 0)
        {
            var r = new Rectangle(Width / 2, 12, Width / 2 - 16, 24);
            TextRenderer.DrawText(g, _hint, Theme.Small, r, Theme.Muted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
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
        Font = Theme.Semibold;
        BackColor = Theme.Bg;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var bg = new SolidBrush(Theme.Bg)) g.FillRectangle(bg, ClientRectangle);
        using var path = Theme.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), Height / 2);
        using (var fill = new SolidBrush(_fill)) g.FillPath(fill, path);
        using (var dot = new SolidBrush(_dot)) g.FillEllipse(dot, 10, Height / 2 - 4, 8, 8);
        var r = new Rectangle(Padding.Left, 0, Width - Padding.Left - Padding.Right, Height);
        TextRenderer.DrawText(g, Text, Font, r, ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
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
