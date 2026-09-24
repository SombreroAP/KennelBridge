using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using KennelBridge.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace KennelBridge;

/// <summary>One sound on the board (or in search results). Id is the catalogue's own id, so both PCs agree on it.</summary>
public sealed class SoundInfo
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public string License { get; set; } = "";
    public string Attribution { get; set; } = "";
    public int DurationMs { get; set; }
    public string Source { get; set; } = "";
    /// <summary>Key held on the other PC while this sound plays: null = the board's default, "" = none, else a hotkey row's ActionKey.</summary>
    public string? HoldAction { get; set; }
    /// <summary>Set only on the message to the other PC: this press went into the microphone, so the gaming PC should not also play it into CABLE.</summary>
    public bool ViaMic { get; set; }
    [JsonIgnore] public string LengthText => DurationMs <= 0 ? "" : DurationMs < 60000 ? $"{DurationMs / 1000.0:0.0} s" : $"{DurationMs / 60000}:{DurationMs / 1000 % 60:00}";
    [JsonIgnore] public string ShortTitle => Soundboard.Tidy(Title);
}

/// <summary>
/// The soundboard's library and player.
///
/// Catalogue: Openverse (api.openverse.org), an open search over Creative Commons audio from Freesound,
/// Wikimedia and others; no account or key. Only licences that allow use on a monetised stream are asked
/// for (CC0, CC BY, CC BY-SA). Each sound is downloaded once into %APPDATA%\KennelBridge\sounds and played
/// from there after that.
///
/// Playing opens a shared-mode WASAPI stream on the chosen output. Windows mixes shared streams, so playing
/// into CABLE Input on the gaming PC adds the sound to the microphone the audio bridge already plays there.
/// </summary>
public static class Soundboard
{
    public static string Dir => Path.Combine(Settings.Dir, "sounds");
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    static readonly object Gate = new();
    static readonly List<IWavePlayer> Playing = new();

    static Soundboard() => Http.DefaultRequestHeaders.UserAgent.ParseAdd($"KennelBridge/{UpdateCheck.CurrentText} (+https://kennel.gg/bridge/)");

    public static readonly string[] Categories =
        { "air horn", "applause", "laugh", "drum roll", "fail", "victory", "explosion", "whoosh", "ding", "buzzer", "crickets", "gasp", "boing", "cash register", "record scratch", "bell" };

    /// <summary>
    /// The popular list shown before any search: the classic streamer soundboard staples, one short clean
    /// version of each, picked from the same catalogue and built into the app (sounds/popular.json), so it
    /// appears instantly with no network. Files still download on first use.
    /// </summary>
    public static List<SoundInfo> Popular()
    {
        try
        {
            using var rs = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("sounds/popular.json");
            if (rs != null) return JsonSerializer.Deserialize<List<SoundInfo>>(rs) ?? new();
        }
        catch { }
        return new();
    }

    /// <summary>Search the catalogue. Short sounds first; nothing longer than a minute.</summary>
    public enum Length { Short, Long, Music }

    public static async Task<List<SoundInfo>> SearchAsync(string query, int page = 1, CancellationToken ct = default, Length length = Length.Short)
    {
        // anonymous requests are capped at 20 per page: take two pages
        var list = new List<SoundInfo>();
        for (int pg = page * 2 - 1; pg <= page * 2; pg++)
        {
            var url = $"https://api.openverse.org/v1/audio/?q={Uri.EscapeDataString(query)}&page_size=20&page={pg}&license=cc0,by,by-sa&mature=false"
                + (length == Length.Music ? "&source=jamendo" : "");
            JsonDocument doc;
            try
            {
                using var res = await Http.GetAsync(url, ct);
                if (!res.IsSuccessStatusCode) { if (pg == page * 2 - 1) res.EnsureSuccessStatusCode(); break; }   // a missing 2nd page is fine
                doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            }
            catch when (pg != page * 2 - 1) { break; }
            using var _ = doc;
            ReadResults(doc, list, length == Length.Short ? 60000 : 600000);
            if (!doc.RootElement.TryGetProperty("page_count", out var pc) || pc.ValueKind != JsonValueKind.Number || pc.GetInt32() <= pg) break;
        }
        var all = list.GroupBy(x => x.Id).Select(g => g.First());
        return length == Length.Short ? all.OrderBy(x => x.DurationMs <= 0 ? int.MaxValue : x.DurationMs).ToList() : all.ToList();   // long / music keep the catalogue's relevance order
    }

    static void ReadResults(JsonDocument doc, List<SoundInfo> list, int maxMs)
    {
        foreach (var r in doc.RootElement.GetProperty("results").EnumerateArray())
        {
            string S(string k) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            int dur = r.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetInt32() : 0;
            var u = S("url");
            if (u.Length == 0 || dur > maxMs) continue;
            var lic = S("license"); var ver = S("license_version");
            list.Add(new SoundInfo
            {
                Id = S("id"), Title = S("title"), Url = u, DurationMs = dur, Source = S("source"),
                License = lic == "cc0" ? "CC0" : $"CC {lic.ToUpperInvariant()} {ver}".Trim(),
                Attribution = S("attribution"),
            });
        }
    }

    public static string PathFor(SoundInfo s)
    {
        var ext = Path.GetExtension(new Uri(s.Url).AbsolutePath);
        if (ext.Length is 0 or > 5) ext = ".mp3";
        var safe = string.Concat(s.Id.Where(c => char.IsLetterOrDigit(c) || c == '-'));
        return Path.Combine(Dir, (safe.Length > 0 ? safe : Math.Abs(s.Url.GetHashCode()).ToString()) + ext);
    }

    public static bool IsCached(SoundInfo s) => File.Exists(PathFor(s));

    /// <summary>Long sounds are not worth waiting for: stream them from the web the first time (and save in the background).</summary>
    public static bool StreamFirst(SoundInfo s) => !IsCached(s) && !IsBrowserOnly(s) && (s.DurationMs == 0 || s.DurationMs > 20000);

    /// <summary>Sounds whose files only a real browser session can fetch (MyInstants): added through the in-app browser, sent to the other PC directly.</summary>
    public static bool IsBrowserOnly(SoundInfo s) => s.Source == "myinstants";

    /// <summary>The local file, downloading it the first time.</summary>
    public static async Task<string> EnsureAsync(SoundInfo s, CancellationToken ct = default)
    {
        var path = PathFor(s);
        if (File.Exists(path)) return path;
        if (IsBrowserOnly(s)) throw new InvalidOperationException("this MyInstants sound is not on this PC yet - add it with Browse MyInstants, or play it once on the other PC");
        Directory.CreateDirectory(Dir);
        var tmp = path + ".part";
        using (var res = await Http.GetAsync(s.Url, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            res.EnsureSuccessStatusCode();
            await using var src = await res.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(tmp);
            await src.CopyToAsync(dst, ct);
        }
        File.Move(tmp, path, overwrite: true);
        return path;
    }

    /// <summary>Play a file on an output device (null = Windows default). Several can overlap.</summary>
    public static void Play(string path, string? deviceId, float volume, Action? ended = null)
    {
        var device = WindowsAudioDevices.Resolve(deviceId, DataFlow.Render) ?? throw new InvalidOperationException("No playback device.");
        var reader = new MediaFoundationReader(path);
        var vol = new VolumeSampleProvider(reader.ToSampleProvider()) { Volume = Math.Clamp(volume, 0f, 1f) };
        var output = new WasapiOut(device, AudioClientShareMode.Shared, true, 60);
        output.Init(vol);
        output.PlaybackStopped += (_, _) =>
        {
            lock (Gate) Playing.Remove(output);
            try { output.Dispose(); } catch { }
            reader.Dispose(); device.Dispose();
            try { ended?.Invoke(); } catch { }
        };
        lock (Gate) Playing.Add(output);
        output.Play();
    }

    /// <summary>A sound as 48 kHz stereo samples at the given volume, for mixing into the microphone.</summary>
    public static ISampleProvider OpenForMix(string path, float volume, out IDisposable reader)
    {
        var r = new MediaFoundationReader(path);
        reader = r;
        ISampleProvider sp = r.ToSampleProvider();
        if (sp.WaveFormat.Channels == 1) sp = new MonoToStereoSampleProvider(sp);
        else if (sp.WaveFormat.Channels > 2) sp = new MultiplexingSampleProvider(new[] { sp }, 2);
        if (sp.WaveFormat.SampleRate != 48000) sp = new WdlResamplingSampleProvider(sp, 48000);
        return new VolumeSampleProvider(sp) { Volume = Math.Clamp(volume, 0f, 1f) };
    }

    public static void StopAll()
    {
        IWavePlayer[] all;
        lock (Gate) all = Playing.ToArray();
        foreach (var p in all) { try { p.Stop(); } catch { } }
    }

    /// <summary>"Air_Horn_01.wav" → "Air Horn 01": catalogue titles are often file names.</summary>
    public static string Tidy(string title)
    {
        var t = Path.GetExtension(title) is ".wav" or ".mp3" or ".ogg" or ".flac" or ".aif" or ".aiff" ? Path.GetFileNameWithoutExtension(title) : title;
        t = t.Replace('_', ' ').Replace("  ", " ").Trim();
        return t.Length > 40 ? t[..38].TrimEnd() + "…" : t;
    }
}
