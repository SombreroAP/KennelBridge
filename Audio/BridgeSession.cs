using System.Net;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace KennelBridge.Audio;

public sealed record BridgeSettings
{
    public PcRole Role { get; init; } = PcRole.Unset;

    /// <summary>Playback device to capture from on the gaming PC (loopback), or to play to on
    /// the streaming PC. Null means the Windows default.</summary>
    public string? RenderDeviceId { get; init; }

    /// <summary>Microphone to capture on the streaming PC. Null means the Windows default.</summary>
    public string? CaptureDeviceId { get; init; }

    /// <summary>Packets held back before playback starts. The stability-vs-latency knob.</summary>
    public int JitterDepth { get; init; } = 3;

    /// <summary>WASAPI playback buffer, in milliseconds.</summary>
    public int RenderLatencyMs { get; init; } = 25;

    /// <summary>Audio capture block size. Kept small enough to fit one datagram.</summary>
    public double BlockMilliseconds { get; init; } = 5;

    /// <summary>Ceiling on queued playback. Sender and receiver clocks are never exactly
    /// equal, so without trimming, latency creeps upward across a long session.</summary>
    public int MaxPlaybackBufferMs { get; init; } = 75;

    public ushort AudioPort { get; init; } = 47810;

    /// <summary>Audio kept queued for the sound card; absorbs network jitter.</summary>
    public int TargetBufferMs { get; init; } = 35;
}

/// <summary>
/// One running link to the other PC. Owns the capture, the sockets, and the playback, and
/// keeps them fed.
///
/// Both roles run the same machinery pointed at different devices — the only asymmetry is
/// which stream each side sends and where the received audio is played.
/// </summary>
public sealed class BridgeSession : IAsyncDisposable
{
    private readonly BridgeSettings _settings;
    private readonly AudioFormat _format = AudioFormat.Default;
    private readonly VbCableDevice _virtualMic;

    private AudioReceiver? _receiver;
    private AudioSender? _sender;
    private WasapiCaptureSource? _capture;
    private IAudioRenderSink? _render;
    private Thread? _pump;
    private volatile bool _running;
    private byte[] _silence = [];

    public BridgeSession(BridgeSettings settings, VbCableDevice virtualMic)
    {
        _settings = settings;
        _virtualMic = virtualMic;
    }

    /// <summary>Which stream this PC sends.</summary>
    public StreamId OutboundStream => _settings.Role == PcRole.Gaming ? StreamId.GameAudio : StreamId.Microphone;

    /// <summary>Which stream this PC plays.</summary>
    public StreamId InboundStream => _settings.Role == PcRole.Gaming ? StreamId.Microphone : StreamId.GameAudio;

    public bool IsRunning => _running;
    public IPEndPoint? Peer { get; private set; }

    /// <summary>Blocks dropped to stop playback latency drifting upward.</summary>
    public long TrimmedBlocks { get; private set; }

    public event Action<string>? Failed;

    /// <summary>Soundboard "mute my mic": the microphone stream is silenced (sent or played as zeros, timing kept).</summary>
    public volatile bool MuteMic;
    private byte[] _zeros = new byte[4096];

    public async Task StartAsync(IPEndPoint peer, CancellationToken cancellationToken = default)
    {
        if (_running) throw new InvalidOperationException("Session already running.");
        if (_settings.Role == PcRole.Unset)
            throw new InvalidOperationException("Pick which PC this is before starting.");

        Peer = peer;
        _silence = new byte[_format.BytesForDuration(_settings.BlockMilliseconds)];

        _receiver = new AudioReceiver(_settings.AudioPort, _settings.JitterDepth);
        _receiver.Start();
        _sender = new AudioSender(peer, _format);

        _render = OpenRenderSink();
        await _render.StartAsync(cancellationToken);

        _capture = OpenCapture();
        _capture.DataAvailable += OnCaptured;
        await _capture.StartAsync(cancellationToken);

        _running = true;
        // A dedicated thread, not the thread pool: this loop must keep the sound card fed on
        // a steady cadence, and pool threads can be delayed by unrelated work.
        _pump = new Thread(PumpLoop) { IsBackground = true, Name = "KennelBridge render pump", Priority = ThreadPriority.AboveNormal };
        _pump.Start();
    }

    private IAudioRenderSink OpenRenderSink()
    {
        // The gaming PC plays the incoming microphone into VB-CABLE so games see it as a mic.
        // The streaming PC plays incoming game audio out of the user's headphones.
        if (_settings.Role == PcRole.Gaming)
        {
            if (!_virtualMic.IsAvailable)
                throw new InvalidOperationException(
                    "VB-CABLE is not installed on this PC, so the microphone cannot be received. " +
                    "Install it from vb-cable.com, or run this PC in send-only mode.");
            return _virtualMic.OpenSink(_format);
        }

        var device = WindowsAudioDevices.Resolve(_settings.RenderDeviceId, DataFlow.Render)
            ?? throw new InvalidOperationException("No playback device available.");
        return new WasapiRenderSink(device, _format, _settings.RenderLatencyMs);
    }

    private WasapiCaptureSource OpenCapture()
    {
        if (_settings.Role == PcRole.Gaming)
        {
            // Loopback-record the device the games are playing through.
            var device = WindowsAudioDevices.Resolve(_settings.RenderDeviceId, DataFlow.Render)
                ?? throw new InvalidOperationException("No playback device to capture game audio from.");

            // Installing VB-CABLE often makes CABLE Input the default playback device, so
            // this is the setup a user lands on by accident. Capturing the device we render
            // the incoming microphone into feeds it straight back to the other PC, which is
            // heard as your own voice returning on a delay.
            if (_virtualMic.InputEndpoint is not null && device.ID == _virtualMic.InputEndpoint.Id)
            {
                var name = device.FriendlyName;
                device.Dispose();
                throw new InvalidOperationException(
                    $"\"{name}\" cannot be the device game audio is captured from -- it is the " +
                    "virtual microphone KennelBridge plays into, so the audio would loop straight " +
                    "back. Pick the speakers or headset your games actually play through.");
            }

            return WasapiCaptureSource.Loopback(device, _format, _settings.BlockMilliseconds);
        }

        var mic = WindowsAudioDevices.Resolve(_settings.CaptureDeviceId, DataFlow.Capture)
            ?? throw new InvalidOperationException("No microphone available.");
        return WasapiCaptureSource.Microphone(mic, _format, _settings.BlockMilliseconds);
    }

    // ---- soundboard: sounds mixed into the microphone before it is sent ----
    private readonly List<(ISampleProvider src, Action? done)> _mix = new();
    private readonly object _mixGate = new();
    private byte[] _work = new byte[4096];
    private float[] _mixBuf = new float[2048];

    /// <summary>Add a sound to the outgoing microphone (48 kHz stereo float). <paramref name="done"/> runs when it ends.</summary>
    public void MixIntoMic(ISampleProvider source, Action? done)
    {
        if (source.WaveFormat.SampleRate != _format.SampleRate || source.WaveFormat.Channels != _format.Channels)
            throw new ArgumentException("Mic mix needs 48 kHz stereo.");
        lock (_mixGate) _mix.Add((source, done));
    }

    public void ClearMix()
    {
        List<Action?> ended;
        lock (_mixGate) { ended = _mix.Select(m => m.done).ToList(); _mix.Clear(); }
        foreach (var d in ended) if (d != null) ThreadPool.QueueUserWorkItem(_ => d());
    }

    public bool CanMixIntoMic => _running && OutboundStream == StreamId.Microphone;

    private void OnCaptured(ReadOnlyMemory<byte> block)
    {
        try
        {
            bool mic = OutboundStream == StreamId.Microphone;
            int mixing; lock (_mixGate) mixing = _mix.Count;
            if (!mic || (!MuteMic && mixing == 0)) { _sender?.Send(OutboundStream, block.Span); return; }

            if (_work.Length < block.Length) _work = new byte[block.Length];
            var pcm = _work.AsSpan(0, block.Length);
            if (MuteMic) pcm.Clear(); else block.Span.CopyTo(pcm);
            if (mixing > 0) MixInto(pcm);
            _sender?.Send(OutboundStream, pcm);
        }
        catch (Exception ex)
        {
            // Never let an exception escape into the WASAPI capture thread; it would kill
            // capture for the rest of the session with no way to recover.
            Failed?.Invoke(ex.Message);
        }
    }

    [System.Runtime.InteropServices.DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint period);
    [System.Runtime.InteropServices.DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint period);

    /// <summary>Times the playback buffer ran empty (each one is an audible gap).</summary>
    public long Underruns { get; private set; }
    /// <summary>Lost packets filled in by repeating the previous block quietly.</summary>
    public long Concealed { get; private set; }
    /// <summary>Single frames dropped (+) or repeated (-) to follow the other PC's clock without a click.</summary>
    public long DriftDropped { get; private set; }
    public long DriftRepeated { get; private set; }

    /// <summary>
    /// Feeds the sound card at a steady level. Three things here prevent crackle:
    /// 1 ms timer resolution (Windows' default 15.6 ms tick starves a 25 ms device buffer);
    /// a cushion of TargetBufferMs kept in the playback buffer, re-established after any underrun;
    /// and clock drift followed one frame at a time (inaudible) instead of dropping whole 5 ms blocks.
    /// </summary>
    /// <summary>Add every playing sound into a block of 16-bit PCM, clipping instead of wrapping.</summary>
    private void MixInto(Span<byte> pcm)
    {
        int samples = pcm.Length / 2;
        if (_mixBuf.Length < samples) _mixBuf = new float[samples];
        var acc = new float[samples];
        List<Action?>? ended = null;
        lock (_mixGate)
        {
            for (int m = _mix.Count - 1; m >= 0; m--)
            {
                int n = _mix[m].src.Read(_mixBuf, 0, samples);
                for (int i = 0; i < n; i++) acc[i] += _mixBuf[i];
                if (n < samples) { (ended ??= new()).Add(_mix[m].done); _mix.RemoveAt(m); }
            }
        }
        for (int i = 0; i < samples; i++)
        {
            int v = (short)(pcm[2 * i] | (pcm[2 * i + 1] << 8)) + (int)(acc[i] * 32767f);
            v = Math.Clamp(v, short.MinValue, short.MaxValue);
            pcm[2 * i] = (byte)v; pcm[2 * i + 1] = (byte)(v >> 8);
        }
        if (ended != null) foreach (var d in ended) if (d != null) ThreadPool.QueueUserWorkItem(_ => d());
    }

    private void PumpLoop()
    {
        timeBeginPeriod(1);
        try
        {
            var sink = _render as WasapiRenderSink;
            int frame = _format.BytesPerFrame;
            double target = _settings.TargetBufferMs;
            bool primed = false;
            var last = new byte[_silence.Length];
            int lastLen = 0;
            while (_running)
            {
                var wroteSomething = false;
                while (_receiver!.TryRead(InboundStream, out var packet))
                {
                    wroteSomething = true;
                    double buffered = sink?.BufferedDuration.TotalMilliseconds ?? target;

                    if (sink != null && !primed)
                    {
                        // start (or restart after an underrun) with a cushion so network jitter cannot empty the card
                        sink.Write(new byte[_format.BytesForDuration(Math.Max(0, target - buffered))]);
                        primed = true;
                        buffered = target;
                    }
                    else if (sink != null && buffered < 1)
                    {
                        Underruns++;
                        sink.Write(new byte[_format.BytesForDuration(target / 2)]);
                        buffered = target / 2;
                    }

                    if (buffered > _settings.MaxPlaybackBufferMs) { TrimmedBlocks++; continue; }   // far behind: hard limit (rare now)

                    ReadOnlySpan<byte> pcm;
                    if (packet.Payload.IsEmpty)
                    {
                        // lost packet: repeat the previous block at half level rather than a hole of silence
                        Concealed++;
                        if (lastLen > 0) { for (int i = 0; i + 1 < lastLen; i += 2) { short v = (short)(last[i] | (last[i + 1] << 8)); v = (short)(v / 2); last[i] = (byte)v; last[i + 1] = (byte)(v >> 8); } pcm = last.AsSpan(0, lastLen); }
                        else pcm = _silence;
                    }
                    else
                    {
                        pcm = packet.Payload.Span;
                        lastLen = Math.Min(pcm.Length, last.Length); pcm[..lastLen].CopyTo(last);
                    }

                    if (MuteMic && InboundStream == StreamId.Microphone)
                    {
                        if (_zeros.Length < pcm.Length) _zeros = new byte[pcm.Length];
                        pcm = _zeros.AsSpan(0, pcm.Length);
                    }

                    if (sink != null && buffered > target * 1.6 && pcm.Length >= frame * 2)
                    { sink.Write(pcm[..^frame]); DriftDropped++; }
                    else if (sink != null && buffered < target * 0.6 && pcm.Length >= frame)
                    { sink.Write(pcm); sink.Write(pcm[^frame..]); DriftRepeated++; }
                    else
                        _render!.Write(pcm);
                }
                if (!wroteSomething) Thread.Sleep(1);
            }
        }
        finally { timeEndPeriod(1); }
    }

    /// <summary>One line for the log: formats, devices and buffer sizes actually in use.</summary>
    public string Describe() =>
        $"stream {OutboundStream} out / {InboundStream} in, 48 kHz 16-bit stereo, block {_settings.BlockMilliseconds} ms, jitter depth {_settings.JitterDepth}, " +
        $"target buffer {_settings.TargetBufferMs} ms, max {_settings.MaxPlaybackBufferMs} ms, device buffer {(_render as WasapiRenderSink)?.ActualLatencyMs ?? 0} ms" +
        (_capture != null ? $", capture device format {_capture.SourceFormat}" : "");

    public BridgeStatus GetStatus()
    {
        var stats = _receiver?.GetStats(InboundStream) ?? default;
        return new BridgeStatus(
            _running,
            Peer,
            _sender?.PacketsSent ?? 0,
            _receiver?.PacketsReceived ?? 0,
            stats,
            (_render as WasapiRenderSink)?.BufferedDuration ?? TimeSpan.Zero,
            TrimmedBlocks, Underruns, Concealed, DriftDropped, DriftRepeated);
    }

    public async Task StopAsync()
    {
        _running = false;
        _pump?.Join(TimeSpan.FromSeconds(1));
        _pump = null;

        if (_capture is not null)
        {
            _capture.DataAvailable -= OnCaptured;
            await _capture.DisposeAsync();
            _capture = null;
        }

        if (_render is not null)
        {
            await _render.StopAsync();
            await _render.DisposeAsync();
            _render = null;
        }

        _sender?.Dispose();
        _sender = null;

        if (_receiver is not null)
        {
            await _receiver.DisposeAsync();
            _receiver = null;
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}

public readonly record struct BridgeStatus(
    bool IsRunning,
    IPEndPoint? Peer,
    long PacketsSent,
    long PacketsReceived,
    JitterBufferStats Jitter,
    TimeSpan PlaybackBuffered,
    long TrimmedBlocks,
    long Underruns = 0,
    long Concealed = 0,
    long DriftDropped = 0,
    long DriftRepeated = 0);
