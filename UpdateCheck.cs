using System.Net.Http;
using System.Text.Json;

namespace KennelBridge;

/// <summary>What a newer release on GitHub looks like to the app.</summary>
public sealed record UpdateInfo(string Version, string Notes, string DownloadUrl, string ReleaseUrl, DateTime Published);

/// <summary>
/// Asks GitHub for the newest published release and compares it with the running version. One
/// small request, no token, once at start-up and every six hours; the result is only ever shown,
/// never installed - the user downloads the exe themselves from the release page or kennel.gg.
/// </summary>
public static class UpdateCheck
{
    public const string Repo = "SombreroAP/KennelBridge";
    public const string SitePage = "https://kennel.gg/bridge/";
    public const string ChangelogPage = "https://kennel.gg/bridge/changelog/";
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public static Version Current => typeof(UpdateCheck).Assembly.GetName().Version ?? new Version(0, 0, 0);
    public static string CurrentText => $"{Current.Major}.{Current.Minor}.{Current.Build}";

    /// <summary>The newest release if it is newer than this build, otherwise null. Throws on network trouble.</summary>
    public static async Task<UpdateInfo?> FetchAsync(CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repo}/releases/latest");
        req.Headers.UserAgent.ParseAdd($"KennelBridge/{CurrentText} (+{SitePage})");
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var res = await Http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var v)) return null;
        var mine = new Version(Current.Major, Current.Minor, Math.Max(Current.Build, 0));
        if (v <= mine) return null;
        string download = "";
        if (root.TryGetProperty("assets", out var assets))
            foreach (var a in assets.EnumerateArray())
                if ((a.GetProperty("name").GetString() ?? "").EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) { download = a.GetProperty("browser_download_url").GetString() ?? ""; break; }
        var notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
        // the SHA-256 block at the end of the notes is for the website, not for reading
        int cut = notes.IndexOf("### Checksums", StringComparison.OrdinalIgnoreCase);
        if (cut > 0) notes = notes[..cut].TrimEnd();
        var published = root.TryGetProperty("published_at", out var p) && DateTime.TryParse(p.GetString(), out var dt) ? dt : DateTime.MinValue;
        var url = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "";
        return new UpdateInfo(tag.TrimStart('v', 'V'), notes, download, url, published);
    }
}
