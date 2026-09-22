using System.Diagnostics;
using System.Net;
using KennelBridge.Audio;
using static KennelBridge.Theme;

namespace KennelBridge;

/// <summary>Audio page: game audio to the streaming PC's headphones, the streaming PC's mic back into games via VB-CABLE.</summary>
public sealed partial class MainForm
{
    readonly VbCableDevice _virtualMic = new();
    BridgeSession? _audioSession;
    bool _audioStarting;
    string _audioError = "";
    DateTime _audioRetryUtc = DateTime.MinValue;
    string _audioKey = "";   // what the running session was started with; a change means restart
    string AudioKey() => $"{S.Role}|{S.PeerHost}|{S.AudioPort}|{S.AudioLatency}|{S.AudioRenderDeviceId}|{S.AudioCaptureDeviceId}|{S.Passphrase}";

    readonly CheckBox _auEnabled = Theme.Check("Audio bridge on (starts with the app and reconnects by itself)");
    readonly Label _auRenderLabel = Theme.Label("Playback device");
    readonly ComboBox _auRender = Theme.ComboBox();
    readonly Label _auCaptureLabel = Theme.Label("Microphone to send");
    readonly ComboBox _auCapture = Theme.ComboBox();
    readonly ComboBox _auLatency = Theme.ComboBox();
    readonly Label _auStatus = Theme.Label("", muted: true);
    readonly Button _auStart = Theme.Button("Start now", primary: true);
    readonly Button _auStop = Theme.Button("Stop");
    readonly Card _cableCard = new("Virtual microphone (gaming PC)");
    readonly Label _cableStatus = Theme.Label("", muted: true);
    readonly Label _auDirection = Theme.Label("", muted: true);
    List<AudioDeviceInfo> _renderDevices = new(), _captureDevices = new();

    void WireAudio() { }

    Control BuildAudioPage()
    {
        var col = Rows(332, 252, -1);

        var main = new Card("Audio bridge") { Dock = DockStyle.Fill };
        var mT = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 7 };
        mT.ColumnStyles.Add(Cpx(150)); mT.ColumnStyles.Add(Cpct(100));
        foreach (var h in new[] { 34, 26, 36, 36, 36, 44, 30 }) mT.RowStyles.Add(Px(h));
        _auEnabled.Margin = new Padding(0, 6, 0, 0);
        _auEnabled.CheckedChanged += (_, _) => { if (_loadingUi) return; S.AudioEnabled = _auEnabled.Checked; S.Save(); ApplyAudioRuntime(); };
        mT.Controls.Add(_auEnabled, 0, 0); mT.SetColumnSpan(_auEnabled, 2);
        _auDirection.AutoSize = false; _auDirection.Dock = DockStyle.Fill; _auDirection.Font = Small; _auDirection.TextAlign = ContentAlignment.MiddleLeft;
        mT.Controls.Add(_auDirection, 0, 1); mT.SetColumnSpan(_auDirection, 2);

        mT.Controls.Add(_auRenderLabel, 0, 2);
        _auRender.Dock = DockStyle.Fill; _auRender.DropDownStyle = ComboBoxStyle.DropDownList;
        _auRender.SelectedIndexChanged += (_, _) => { if (_loadingUi) return; S.AudioRenderDeviceId = (_auRender.SelectedItem as AudioDeviceInfo)?.Id; S.Save(); };
        mT.Controls.Add(_auRender, 1, 2);
        mT.Controls.Add(_auCaptureLabel, 0, 3);
        _auCapture.Dock = DockStyle.Fill; _auCapture.DropDownStyle = ComboBoxStyle.DropDownList;
        _auCapture.SelectedIndexChanged += (_, _) => { if (_loadingUi) return; S.AudioCaptureDeviceId = (_auCapture.SelectedItem as AudioDeviceInfo)?.Id; S.Save(); };
        mT.Controls.Add(_auCapture, 1, 3);
        mT.Controls.Add(Theme.Label("Latency"), 0, 4);
        var lRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0) };
        lRow.ColumnStyles.Add(Cpx(220)); lRow.ColumnStyles.Add(Cpct(100));
        _auLatency.Dock = DockStyle.Fill; _auLatency.DropDownStyle = ComboBoxStyle.DropDownList;
        foreach (var p in LatencyProfile.All) _auLatency.Items.Add(p);
        _auLatency.SelectedIndexChanged += (_, _) => { if (_loadingUi) return; S.AudioLatency = (_auLatency.SelectedItem as LatencyProfile)?.Name ?? "Balanced"; S.Save(); };
        lRow.Controls.Add(_auLatency, 0, 0);
        var lHint = Theme.Label("Lower = more responsive, more likely to crackle. Use the same on both PCs.", muted: true, Small);
        lHint.AutoSize = false; lHint.Dock = DockStyle.Fill; lHint.AutoEllipsis = true; lHint.TextAlign = ContentAlignment.MiddleLeft; lHint.Margin = new Padding(10, 0, 0, 0);
        lRow.Controls.Add(lHint, 1, 0);
        mT.Controls.Add(lRow, 1, 4);

        var bb = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0), Padding = new Padding(0, 6, 0, 0) };
        _auStart.Click += (_, _) => _ = StartAudio(manual: true);
        _auStop.Click += (_, _) => _ = StopAudio("stopped");
        bb.Controls.Add(_auStart); bb.Controls.Add(_auStop);
        bb.Controls.Add(On(Theme.Button("Refresh devices"), RefreshAudioDevices));
        mT.Controls.Add(bb, 0, 5); mT.SetColumnSpan(bb, 2);
        _auStatus.AutoSize = false; _auStatus.Dock = DockStyle.Fill; _auStatus.AutoEllipsis = true; _auStatus.TextAlign = ContentAlignment.MiddleLeft;
        mT.Controls.Add(_auStatus, 0, 6); mT.SetColumnSpan(_auStatus, 2);
        main.Controls.Add(mT);
        col.Controls.Add(main, 0, 0);

        _cableCard.Dock = DockStyle.Fill;
        var cT = Rows(40, -1, 40);
        _cableStatus.AutoSize = false; _cableStatus.Dock = DockStyle.Fill; _cableStatus.TextAlign = ContentAlignment.MiddleLeft;
        cT.Controls.Add(_cableStatus, 0, 0);
        cT.Controls.Add(Wrapped("To make the streaming PC's microphone show up in games on this PC, Windows needs a virtual audio device. KennelBridge uses VB-CABLE: " +
            "the incoming microphone is played into \"CABLE Input\" and you pick \"CABLE Output\" as your microphone in the game or Discord. " +
            _virtualMic.Attribution + " Restart Windows after installing it."), 0, 1);
        var cb = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0), Padding = new Padding(0, 4, 0, 0) };
        cb.Controls.Add(On(Theme.Button("Open vb-cable.com"), () => { try { Process.Start(new ProcessStartInfo(_virtualMic.InstallUri.ToString()) { UseShellExecute = true }); } catch { } }));
        cb.Controls.Add(On(Theme.Button("I've installed it - check again"), RefreshAudioDevices));
        cT.Controls.Add(cb, 0, 2);
        _cableCard.Controls.Add(cT);
        col.Controls.Add(_cableCard, 0, 1);

        var how = new Card("How it works") { Dock = DockStyle.Fill };
        how.Controls.Add(Wrapped("Gaming PC: everything it plays (game, Discord, music) is loopback-recorded from the playback device chosen above and sent to the streaming PC, where it plays out of the headphones. " +
            "Streaming PC: the microphone chosen above is sent to the gaming PC, where it comes out of CABLE Output as a normal mic. " +
            "Both directions run at once over UDP on the audio port, uncompressed 48 kHz stereo 16-bit, so keep the two PCs on a wired LAN."));
        col.Controls.Add(how, 0, 2);
        return col;
    }

    void LoadAudioUi()
    {
        _auEnabled.Checked = S.AudioEnabled;
        RefreshAudioDevices();
        var lat = LatencyProfile.ByName(S.AudioLatency);
        _auLatency.SelectedItem = _auLatency.Items.Cast<LatencyProfile>().FirstOrDefault(p => p.Name == lat.Name);
        bool gaming = S.Role == PcRole.Gaming;
        _auRenderLabel.Text = gaming ? "Capture game audio from" : "Play game audio to";
        _auCaptureLabel.Visible = _auCapture.Visible = !gaming;
        _cableCard.Visible = gaming;
        _auDirection.Text = S.Role switch
        {
            PcRole.Gaming => "This PC sends its game audio and receives the microphone (into VB-CABLE).",
            PcRole.Streaming => "This PC receives game audio into the headphones and sends the microphone.",
            _ => "Pick a role on the Connection page first.",
        };
        UpdateAudioStatus();
    }

    void RefreshAudioDevices()
    {
        bool loading = _loadingUi; _loadingUi = true;
        try
        {
            _virtualMic.Refresh();
            try
            {
                _renderDevices = WindowsAudioDevices.GetRenderDevices().Where(d => !(S.Role == PcRole.Gaming && _virtualMic.InputEndpoint != null && d.Id == _virtualMic.InputEndpoint.Id)).ToList();
                _captureDevices = WindowsAudioDevices.GetCaptureDevices().ToList();
            }
            catch (Exception ex) { _audioError = "Could not list audio devices: " + ex.Message; }
            _auRender.Items.Clear(); foreach (var d in _renderDevices) _auRender.Items.Add(d);
            _auCapture.Items.Clear(); foreach (var d in _captureDevices) _auCapture.Items.Add(d);
            _auRender.DisplayMember = _auCapture.DisplayMember = "Name";
            _auRender.SelectedItem = _renderDevices.FirstOrDefault(d => d.Id == S.AudioRenderDeviceId) ?? _renderDevices.FirstOrDefault(d => d.IsDefault) ?? _renderDevices.FirstOrDefault();
            _auCapture.SelectedItem = _captureDevices.FirstOrDefault(d => d.Id == S.AudioCaptureDeviceId) ?? _captureDevices.FirstOrDefault(d => d.IsDefault) ?? _captureDevices.FirstOrDefault();
            _cableStatus.Text = _virtualMic.IsAvailable
                ? $"✓  Found {_virtualMic.InputEndpoint!.Name}. In your game or Discord, choose \"{_virtualMic.OutputEndpoint!.Name}\" as the microphone."
                : "✗  VB-CABLE is not installed on this PC, so the microphone cannot be received here.";
            _cableStatus.ForeColor = _virtualMic.IsAvailable ? Green : Red;
        }
        finally { _loadingUi = loading; }
    }

    BridgeSettings AudioSettings()
    {
        var lat = LatencyProfile.ByName(S.AudioLatency);
        return new BridgeSettings
        {
            Role = S.Role, RenderDeviceId = S.AudioRenderDeviceId, CaptureDeviceId = S.AudioCaptureDeviceId,
            JitterDepth = lat.JitterDepth, RenderLatencyMs = lat.RenderLatencyMs, BlockMilliseconds = lat.BlockMilliseconds, MaxPlaybackBufferMs = lat.MaxPlaybackBufferMs,
            AudioPort = (ushort)S.AudioPort,
        };
    }

    IPEndPoint? AudioDestination()
    {
        if (S.PeerHost.Length == 0) return null;
        if (IPAddress.TryParse(S.PeerHost.Trim(), out var ip)) return new IPEndPoint(ip, S.AudioPort);
        try { var a = System.Net.Dns.GetHostAddresses(S.PeerHost.Trim()).FirstOrDefault(x => x.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork); return a == null ? null : new IPEndPoint(a, S.AudioPort); }
        catch { return null; }
    }

    /// <summary>Auto mode: keep the session matching the settings. Called on every Apply and once a second from AudioTick.</summary>
    internal void ApplyAudioRuntime()
    {
        bool want = S.Enabled && S.AudioEnabled && S.Role != PcRole.Unset && S.PeerHost.Length > 0;
        if (!want) { if (_audioSession != null) _ = StopAudio(S.Enabled ? "audio bridge off" : "paused"); UpdateAudioStatus(); return; }
        if (_audioSession != null && _audioKey != AudioKey()) { _ = RestartAudio(); return; }   // role, peer or device changed
        if (_audioSession == null && !_audioStarting) { _audioRetryUtc = DateTime.MinValue; _ = StartAudio(manual: false); }
        else UpdateAudioStatus();
    }

    // One gate for start, stop and restart: the receiver binds the audio port, so a stop must have
    // finished disposing the old socket before a start binds a new one, or Windows answers
    // "only one usage of each socket address".
    readonly SemaphoreSlim _audioGate = new(1, 1);

    async Task RestartAudio()
    {
        await StopAudio("settings changed, restarting");
        await StartAudio(manual: false);
    }

    async Task StartAudio(bool manual)
    {
        if (_audioSession != null || _audioStarting) return;
        if (S.Role == PcRole.Unset) { _audioError = "Pick a role on the Connection page first."; UpdateAudioStatus(); return; }
        var dest = AudioDestination();
        if (dest == null) { _audioError = "No other PC set - pick it on the Connection page."; UpdateAudioStatus(); return; }
        _audioStarting = true;
        UpdateAudioStatus();
        await _audioGate.WaitAsync();
        BridgeSession? session = null;
        try
        {
            if (_audioSession != null) return;
            session = new BridgeSession(AudioSettings(), _virtualMic);
            session.Failed += msg => { if (IsHandleCreated) BeginInvoke(() => { _audioError = msg; Activity("Audio: " + msg, flash: false); }); };
            await session.StartAsync(dest);
            _audioSession = session;
            _audioKey = AudioKey();
            _audioError = "";
            Activity($"Audio: started - {(S.Role == PcRole.Gaming ? "sending game audio, receiving the microphone" : "receiving game audio, sending the microphone")} ({PeerLabel()}).", flash: false);
        }
        catch (Exception ex)
        {
            // Same as AudioBridge's MainViewModel: a session that failed part-way still owns the
            // receiver socket (and maybe a device), so it must be disposed, not dropped.
            if (session != null) { try { await session.DisposeAsync(); } catch { } }
            _audioError = ex.Message;
            _audioRetryUtc = DateTime.UtcNow.AddSeconds(manual ? 3600 : 30);   // auto mode retries later (device unplugged, peer asleep); a manual failure waits for the user
            Activity("Audio: could not start - " + ex.Message, flash: false);
        }
        finally { _audioGate.Release(); _audioStarting = false; UpdateAudioStatus(); }
    }

    async Task StopAudio(string why)
    {
        await _audioGate.WaitAsync();
        try
        {
            var s = _audioSession;
            _audioSession = null;
            if (s != null)
            {
                try { await s.DisposeAsync(); } catch { }
                Activity($"Audio: {why}.", flash: false);
            }
        }
        finally { _audioGate.Release(); UpdateAudioStatus(); }
    }

    /// <summary>Once a second: live stats, and a retry when auto mode failed earlier.</summary>
    void AudioTick()
    {
        if (_audioSession == null && !_audioStarting && S.Enabled && S.AudioEnabled && S.Role != PcRole.Unset && S.PeerHost.Length > 0 && DateTime.UtcNow > _audioRetryUtc)
        {
            _audioRetryUtc = DateTime.UtcNow.AddSeconds(30);
            _ = StartAudio(manual: false);
        }
        if (_currentPage == PageAudio) UpdateAudioStatus();
    }

    void UpdateAudioStatus()
    {
        _auStart.Enabled = _audioSession == null && !_audioStarting;
        _auStop.Enabled = _audioSession != null;
        _auRender.Enabled = _auCapture.Enabled = _auLatency.Enabled = _audioSession == null;
        if (_audioSession != null)
        {
            var st = _audioSession.GetStatus();
            _auStatus.Text = $"● Running  ·  latency ~{st.PlaybackBuffered.TotalMilliseconds:F0} ms  ·  sent {st.PacketsSent:N0}  ·  received {st.PacketsReceived:N0}  ·  {st.Jitter.ConcealedPackets:N0} dropouts  ·  {st.TrimmedBlocks:N0} trimmed" + (_audioError.Length > 0 ? "  ·  " + _audioError : "");
            _auStatus.ForeColor = st.PacketsReceived > 0 ? Green : Muted;
        }
        else if (_audioStarting) { _auStatus.Text = "Starting…"; _auStatus.ForeColor = Muted; }
        else if (_audioError.Length > 0) { _auStatus.Text = "✗  " + _audioError + (S.AudioEnabled ? "  (will retry)" : ""); _auStatus.ForeColor = Red; }
        else { _auStatus.Text = S.AudioEnabled ? (S.Enabled ? "Waiting for the other PC…" : "Paused.") : "Off. Tick the box to start the audio link automatically, or press Start now."; _auStatus.ForeColor = Muted; }
    }

    void ShutdownAudio()
    {
        var s = _audioSession; _audioSession = null;
        if (s != null) { try { s.DisposeAsync().AsTask().Wait(3000); } catch { } }
    }
}
