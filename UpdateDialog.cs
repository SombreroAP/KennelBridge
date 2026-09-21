using System.Diagnostics;
using static KennelBridge.Theme;

namespace KennelBridge;

/// <summary>"KennelBridge 1.2.0 is available": what changed, and buttons to download it or read the changelog.</summary>
public sealed class UpdateDialog : Form
{
    public UpdateDialog(UpdateInfo u)
    {
        Text = "Update available";
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96f, 96f);
        ApplyWindow(this);
        ClientSize = new Size(640, 520);
        MinimumSize = new Size(520, 400);

        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(22, 18, 22, 16) };
        t.ColumnStyles.Add(Cpct(100));
        t.RowStyles.Add(Px(44)); t.RowStyles.Add(Px(30)); t.RowStyles.Add(Pct(100)); t.RowStyles.Add(Px(52));
        Controls.Add(t);
        t.Controls.Add(Theme.Label($"KennelBridge {u.Version} is available", font: Big), 0, 0);
        t.Controls.Add(Theme.Label($"You have {UpdateCheck.CurrentText}.{(u.Published > DateTime.MinValue ? $"  Published {u.Published:d MMM yyyy}." : "")}  Download the new exe, close KennelBridge and replace the old one on both PCs; your settings are kept.", muted: true, Small), 0, 1);

        var notes = new TextBox
        {
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill,
            BackColor = Field, ForeColor = Fg, BorderStyle = BorderStyle.FixedSingle, Font = Body, Margin = new Padding(0, 6, 0, 6),
            Text = Plain(u.Notes.Length > 0 ? u.Notes : "See the changelog for what changed."),
        };
        t.Controls.Add(notes, 0, 2);

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = new Padding(0), Padding = new Padding(0, 8, 0, 0) };
        var later = Theme.Button("Later"); later.DialogResult = DialogResult.Cancel; later.Margin = new Padding(8, 0, 0, 0);
        var cl = Theme.Button("Changelog"); cl.Margin = new Padding(8, 0, 0, 0);
        cl.Click += (_, _) => Open(UpdateCheck.ChangelogPage);
        var dl = Theme.Button("Download", primary: true); dl.Margin = new Padding(8, 0, 0, 0);
        dl.Click += (_, _) => { Open(u.DownloadUrl.Length > 0 ? u.DownloadUrl : u.ReleaseUrl.Length > 0 ? u.ReleaseUrl : UpdateCheck.SitePage); DialogResult = DialogResult.OK; Close(); };
        bottom.Controls.Add(later); bottom.Controls.Add(cl); bottom.Controls.Add(dl);
        t.Controls.Add(bottom, 0, 3);
        CancelButton = later; AcceptButton = dl;
    }

    static void Open(string url) { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { } }

    /// <summary>Release notes are Markdown; strip the bits that read badly in a plain text box.</summary>
    static string Plain(string md)
    {
        var lines = md.Replace("\r\n", "\n").Split('\n').Select(l =>
        {
            l = l.TrimEnd();
            if (l.StartsWith("#")) l = l.TrimStart('#').Trim().ToUpperInvariant();
            else if (l.StartsWith("- ") || l.StartsWith("* ")) l = "•  " + l[2..];
            return l.Replace("**", "").Replace("`", "");
        });
        return string.Join(Environment.NewLine, lines).Trim();
    }
}
