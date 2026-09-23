using System.Diagnostics;
using static KennelBridge.Theme;

namespace KennelBridge;

/// <summary>Files page: finished recordings from one PC's folder land in the other PC's folder.</summary>
public sealed partial class MainForm
{
    readonly FileSender _fileSender = new();
    readonly FileReceiver _fileReceiver = new();

    readonly CheckBox _fsEnabled = Theme.Check("Send new recordings from this PC");
    readonly TextBox _fsFolder = Theme.TextBox(@"the folder OBS records into, e.g. C:\Users\you\Videos");
    readonly TextBox _fsExt = Theme.TextBox("mp4,mkv,mov");
    readonly CheckBox _fsDelete = Theme.Check("Delete here after the other PC has it");
    readonly Label _fsStatus = Theme.Label("", muted: true);
    readonly CheckBox _frEnabled = Theme.Check("Receive recordings on this PC");
    readonly TextBox _frFolder = Theme.TextBox(@"where they should land, e.g. D:\Recordings");
    readonly TextBox _frPort = Theme.TextBox();
    readonly Label _frStatus = Theme.Label("", muted: true);
    readonly ListView _transfers = new();
    readonly Dictionary<string, ListViewItem> _transferRows = new();

    void WireFiles()
    {
        _fileSender.Log += msg => Activity(msg, flash: false);
        _fileReceiver.Log += msg => Activity(msg, flash: false);
        _fileSender.Progress += OnTransfer;
        _fileReceiver.Progress += OnTransfer;
        _fileSender.Target = () => (S.PeerHost, S.FilePort);
    }

    Control BuildFilesPage()
    {
        var col = Rows(256, 218, -1);

        var send = new Card("Send recordings") { Dock = DockStyle.Fill, Hint = "usually the streaming PC" };
        var sT = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 5 };
        sT.ColumnStyles.Add(Cpx(110)); sT.ColumnStyles.Add(Cpct(100)); sT.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        foreach (var h in new[] { 34, 36, 36, 36, 30 }) sT.RowStyles.Add(Px(h));
        _fsEnabled.Margin = new Padding(0, 6, 0, 0);
        _fsEnabled.CheckedChanged += (_, _) => { if (_loadingUi) return; SaveFilesFromUi(); ApplyFileRuntime(); };
        sT.Controls.Add(_fsEnabled, 0, 0); sT.SetColumnSpan(_fsEnabled, 3);
        sT.Controls.Add(Theme.Label("Watch folder"), 0, 1);
        _fsFolder.Dock = DockStyle.Fill;
        _fsFolder.Leave += (_, _) => { if (_loadingUi) return; if (_fsFolder.Text.Trim() != S.FileSendFolder) { SaveFilesFromUi(); ApplyFileRuntime(); } };
        sT.Controls.Add(_fsFolder, 1, 1);
        var sb = Theme.Button("Browse…", minWidth: 90); sb.Margin = new Padding(6, 4, 0, 0); sb.MinimumSize = new Size(90, 30);
        sb.Click += (_, _) => { if (PickFolder(_fsFolder)) { SaveFilesFromUi(); ApplyFileRuntime(); } };
        sT.Controls.Add(sb, 2, 1);
        sT.Controls.Add(Theme.Label("File types"), 0, 2);
        var eRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0) };
        eRow.ColumnStyles.Add(Cpx(260)); eRow.ColumnStyles.Add(Cpct(100));
        _fsExt.Dock = DockStyle.Fill;
        _fsExt.Leave += (_, _) => { if (_loadingUi) return; SaveFilesFromUi(); ApplyFileRuntime(); };
        eRow.Controls.Add(_fsExt, 0, 0);
        _fsDelete.Margin = new Padding(12, 8, 0, 0);
        _fsDelete.CheckedChanged += (_, _) => { if (_loadingUi) return; SaveFilesFromUi(); ApplyFileRuntime(); };
        eRow.Controls.Add(_fsDelete, 1, 0);
        sT.Controls.Add(eRow, 1, 2); sT.SetColumnSpan(eRow, 2);
        var bb = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0), Padding = new Padding(0, 4, 0, 0) };
        bb.Controls.Add(On(Theme.Button("Sync now", primary: true), () => { SaveFilesFromUi(); ApplyFileRuntime(); _fileSender.Scan(); SetStatus("Scanning the watch folder for finished recordings that have not been sent yet…"); }));
        bb.Controls.Add(On(Theme.Button("Send everything again"), () => { if (MessageBox.Show(this, "Forget which files were already sent? Every recording in the folder will be offered to the other PC again (it skips files it already has with the same size).", "KennelBridge", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK) { _fileSender.ForgetSent(); _fileSender.Scan(); } }));
        bb.Controls.Add(On(Theme.Button("Open folder"), () => OpenFolder(S.FileSendFolder)));
        sT.Controls.Add(bb, 0, 3); sT.SetColumnSpan(bb, 3);
        _fsStatus.AutoSize = false; _fsStatus.Dock = DockStyle.Fill; _fsStatus.AutoEllipsis = true; _fsStatus.TextAlign = ContentAlignment.MiddleLeft; _fsStatus.Font = Small;
        sT.Controls.Add(_fsStatus, 0, 4); sT.SetColumnSpan(_fsStatus, 3);
        send.Controls.Add(sT);
        col.Controls.Add(send, 0, 0);

        var recv = new Card("Receive recordings") { Dock = DockStyle.Fill, Hint = "usually the gaming PC" };
        var rT = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 4 };
        rT.ColumnStyles.Add(Cpx(110)); rT.ColumnStyles.Add(Cpct(100)); rT.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        foreach (var h in new[] { 34, 36, 36, 30 }) rT.RowStyles.Add(Px(h));
        _frEnabled.Margin = new Padding(0, 6, 0, 0);
        _frEnabled.CheckedChanged += (_, _) => { if (_loadingUi) return; SaveFilesFromUi(); ApplyFileRuntime(); };
        rT.Controls.Add(_frEnabled, 0, 0); rT.SetColumnSpan(_frEnabled, 3);
        rT.Controls.Add(Theme.Label("Save into"), 0, 1);
        _frFolder.Dock = DockStyle.Fill;
        _frFolder.Leave += (_, _) => { if (_loadingUi) return; if (_frFolder.Text.Trim() != S.FileReceiveFolder) { SaveFilesFromUi(); ApplyFileRuntime(); } };
        rT.Controls.Add(_frFolder, 1, 1);
        var rb = Theme.Button("Browse…", minWidth: 90); rb.Margin = new Padding(6, 4, 0, 0); rb.MinimumSize = new Size(90, 30);
        rb.Click += (_, _) => { if (PickFolder(_frFolder)) { SaveFilesFromUi(); ApplyFileRuntime(); } };
        rT.Controls.Add(rb, 2, 1);
        rT.Controls.Add(Theme.Label("TCP port"), 0, 2);
        var pRow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
        _frPort.Width = 90; _frPort.TextAlign = HorizontalAlignment.Center;
        _frPort.Leave += (_, _) => { if (_loadingUi) return; SaveFilesFromUi(); ApplyFileRuntime(); };
        pRow.Controls.Add(_frPort);
        pRow.Controls.Add(On(Theme.Button("Open folder"), () => OpenFolder(S.FileReceiveFolder)));
        var ph = Theme.Label("Same on both PCs. The Firewall button on the Connection page opens it.", muted: true, Small); ph.Margin = new Padding(6, 10, 0, 0);
        pRow.Controls.Add(ph);
        rT.Controls.Add(pRow, 1, 2); rT.SetColumnSpan(pRow, 2);
        _frStatus.AutoSize = false; _frStatus.Dock = DockStyle.Fill; _frStatus.AutoEllipsis = true; _frStatus.TextAlign = ContentAlignment.MiddleLeft; _frStatus.Font = Small;
        rT.Controls.Add(_frStatus, 0, 3); rT.SetColumnSpan(_frStatus, 3);
        recv.Controls.Add(rT);
        col.Controls.Add(recv, 0, 1);

        var list = new Card("Transfers") { Dock = DockStyle.Fill };
        var lT = Rows(-1, 40);
        StyleList(_transfers);
        _transfers.Dock = DockStyle.Fill;
        _transfers.Columns.Add("File", 260); _transfers.Columns.Add("Status", 200); _transfers.Columns.Add("Size", 90); _transfers.Columns.Add("Progress", 90); _transfers.Columns.Add("When", 90);
        _transfers.Resize += (_, _) => FitColumns(_transfers);
        lT.Controls.Add(_transfers, 0, 0);
        var lb = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0), Padding = new Padding(0, 6, 0, 0) };
        lb.Controls.Add(On(Theme.Button("Clear finished", minWidth: 110), () => { foreach (var kv in _transferRows.ToList()) if (kv.Value.Tag is FileTransfer t && t.Status is "done" or "skipped" or "failed") { _transfers.Items.Remove(kv.Value); _transferRows.Remove(kv.Key); } }));
        lb.Controls.Add(Wrapped("Sent files are remembered, so nothing goes twice."));
        lT.Controls.Add(lb, 0, 1);
        list.Controls.Add(lT);
        col.Controls.Add(list, 0, 2);
        return col;
    }

    void LoadFilesUi()
    {
        _fsEnabled.Checked = S.FileSendEnabled;
        _fsFolder.Text = S.FileSendFolder;
        _fsExt.Text = S.FileExtensions;
        _fsDelete.Checked = S.FileDeleteAfterSend;
        _frEnabled.Checked = S.FileReceiveEnabled;
        _frFolder.Text = S.FileReceiveFolder;
        _frPort.Text = S.FilePort.ToString();
        UpdateFileStatus();
    }

    void SaveFilesFromUi()
    {
        S.FileSendEnabled = _fsEnabled.Checked;
        S.FileSendFolder = _fsFolder.Text.Trim();
        S.FileExtensions = _fsExt.Text.Trim();
        S.FileDeleteAfterSend = _fsDelete.Checked;
        S.FileReceiveEnabled = _frEnabled.Checked;
        S.FileReceiveFolder = _frFolder.Text.Trim();
        if (int.TryParse(_frPort.Text.Trim(), out var p) && p is >= 1024 and <= 65535) S.FilePort = p;
        _frPort.Text = S.FilePort.ToString();
        S.Save();
    }

    static bool PickFolder(TextBox box)
    {
        using var d = new FolderBrowserDialog { ShowNewFolderButton = true, UseDescriptionForTitle = true, Description = "Choose a folder" };
        if (box.Text.Length > 0 && Directory.Exists(box.Text)) d.SelectedPath = box.Text;
        if (d.ShowDialog() != DialogResult.OK) return false;
        box.Text = d.SelectedPath;
        return true;
    }

    void OpenFolder(string path)
    {
        try { if (path.Length > 0) { Directory.CreateDirectory(path); Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } }
        catch (Exception ex) { SetStatus("Could not open the folder: " + ex.Message); }
    }

    internal void ApplyFileRuntime()
    {
        _fileSender.Passphrase = _fileReceiver.Passphrase = S.Passphrase;
        _fileSender.Folder = S.FileSendFolder; _fileSender.Extensions = S.FileExtensions; _fileSender.DeleteAfterSend = S.FileDeleteAfterSend; _fileSender.SettleSeconds = S.FileSettleSeconds;
        _fileReceiver.Folder = S.FileReceiveFolder;

        bool send = S.Enabled && S.FileSendEnabled && S.FileSendFolder.Length > 0;
        if (send) { if (!_fileSender.Running || _fileSender.RunningKey != S.FileSendFolder + "|" + S.FileExtensions) _fileSender.Start(); }
        else _fileSender.Stop();

        bool recv = S.Enabled && S.FileReceiveEnabled && S.FileReceiveFolder.Length > 0;
        if (recv) { if (!_fileReceiver.Running || _fileReceiver.Port != S.FilePort) _fileReceiver.Start(S.FilePort); }
        else _fileReceiver.Stop();
        UpdateFileStatus();
    }

    void UpdateFileStatus()
    {
        _fsStatus.Text = !S.FileSendEnabled ? "Off."
            : !S.Enabled ? "Paused."
            : S.FileSendFolder.Length == 0 ? "Pick the folder OBS records into."
            : !Directory.Exists(S.FileSendFolder) ? "That folder does not exist."
            : S.PeerHost.Length == 0 ? "Watching, but no other PC is set yet - see the Connection page."
            : $"Watching {S.FileSendFolder} - finished recordings go to {PeerLabel()}.";
        _frStatus.Text = !S.FileReceiveEnabled ? "Off."
            : !S.Enabled ? "Paused."
            : S.FileReceiveFolder.Length == 0 ? "Pick where recordings should be saved."
            : _fileReceiver.Running ? $"Listening on TCP {S.FilePort}; files land in {S.FileReceiveFolder}."
            : $"Could not listen on TCP {S.FilePort} - pick another port or check the Activity page.";
    }

    void OnTransfer(FileTransfer t)
    {
        if (IsHandleCreated && InvokeRequired) { BeginInvoke(() => OnTransfer(t)); return; }
        var key = (t.Outgoing ? "out:" : "in:") + t.Name;
        var status = t.Status switch
        {
            "sending" => $"→ sending to {PeerName()}", "receiving" => "← receiving", "done" => t.Outgoing ? "✓ sent" : "✓ received",
            "skipped" => "already there", "failed" => "✗ failed", _ => t.Status,
        } + (t.Detail.Length > 0 ? $"  ({t.Detail})" : "");
        var cells = new[] { t.Name, status, FileSender.Human(t.Size), t.Percent + " %", DateTime.Now.ToString("HH:mm:ss") };
        if (_transferRows.TryGetValue(key, out var row))
        {
            for (int i = 0; i < cells.Length; i++) row.SubItems[i].Text = cells[i];
            row.Tag = t;
        }
        else
        {
            row = new ListViewItem(cells) { Tag = t };
            _transfers.Items.Insert(0, row);
            _transferRows[key] = row;
            while (_transfers.Items.Count > 200) { var last = _transfers.Items[^1]; _transfers.Items.Remove(last); var k = _transferRows.FirstOrDefault(kv => kv.Value == last).Key; if (k != null) _transferRows.Remove(k); }
        }
        row.ForeColor = t.Status switch { "done" => Green, "failed" => Red, "skipped" => Muted, _ => Fg };
        if (t.Status is "done" or "failed") { _lamp.BackColor = Green; _lamp.Invalidate(); _tray.Icon = _iconFlash; _flashTimer.Stop(); _flashTimer.Start(); }
    }

    void ShutdownFiles()
    {
        _fileSender.Dispose();
        _fileReceiver.Dispose();
    }
}
