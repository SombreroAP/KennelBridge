using System.Diagnostics;
using System.IO.Compression;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using NAudio.CoreAudioApi;

namespace KennelBridge;

/// <summary>
/// "Collect diagnostics": one zip on the Desktop with everything needed to look into a problem —
/// the log (with the 10-second audio stats), settings with the passphrase removed, and a system
/// report: Windows and app version, display scaling, network adapters (Wi-Fi or cable, link speed)
/// and every audio device with the format Windows runs it at. Nothing is sent anywhere.
/// </summary>
public static class Diagnostics
{
    public static string Collect(Settings s, string liveState, int dpi)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmm");
        var name = $"KennelBridge-diagnostics-{Environment.MachineName}-{stamp}.zip";
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var zipPath = Path.Combine(Directory.Exists(desktop) ? desktop : Settings.Dir, name);
        if (File.Exists(zipPath)) File.Delete(zipPath);

        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var f in new[] { Settings.LogPath + ".old", Settings.LogPath })
            if (File.Exists(f)) AddFile(zip, f, Path.GetFileName(f));
        Add(zip, "settings.json", RedactedSettings(s));
        Add(zip, "system.txt", SystemReport(dpi));
        Add(zip, "live-state.txt", liveState);
        return zipPath;
    }

    /// <summary>Open Explorer with the zip selected, so it can be dragged into Drive, Discord or an email.</summary>
    public static void Reveal(string path)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }); } catch { }
    }

    static void AddFile(ZipArchive zip, string path, string entry)
    {
        // the log is open for appending: read it shared rather than through ZipFile's exclusive open
        using var src = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var dst = zip.CreateEntry(entry).Open();
        src.CopyTo(dst);
    }

    static void Add(ZipArchive zip, string entry, string text)
    {
        using var w = new StreamWriter(zip.CreateEntry(entry).Open(), new UTF8Encoding(false));
        w.Write(text);
    }

    static string RedactedSettings(Settings s)
    {
        var json = JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true });
        using var doc = JsonDocument.Parse(json);
        var d = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
        var o = new Dictionary<string, object?>();
        foreach (var (k, v) in d)
            o[k] = k == "Passphrase" ? (s.Passphrase.Length == 0 ? "(empty)" : $"(set, {s.Passphrase.Length} characters)") : v;
        return JsonSerializer.Serialize(o, new JsonSerializerOptions { WriteIndented = true });
    }

    static string SystemReport(int dpi)
    {
        var b = new StringBuilder();
        void L(string t) => b.AppendLine(t);
        L($"KennelBridge {UpdateCheck.CurrentText}   collected {DateTime.Now:yyyy-MM-dd HH:mm:ss zzz}");
        L($"Windows: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})   .NET {RuntimeInformation.FrameworkDescription}");
        L($"Machine: {Environment.MachineName}   CPUs: {Environment.ProcessorCount}   64-bit process: {Environment.Is64BitProcess}");
        L($"Display scaling: {dpi * 100 / 96} % ({dpi} dpi)   screens: {string.Join(", ", Screen.AllScreens.Select(sc => $"{sc.Bounds.Width}x{sc.Bounds.Height}{(sc.Primary ? " primary" : "")}"))}");
        try { using var p = Process.GetCurrentProcess(); L($"Process: {p.WorkingSet64 / 1048576} MB, {p.Threads.Count} threads, up {DateTime.Now - p.StartTime:hh\\:mm\\:ss}, priority {p.PriorityClass}"); } catch { }
        L("");
        L("== Network adapters (up) ==");
        try
        {
            foreach (var n in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback))
            {
                var ips = string.Join(", ", n.GetIPProperties().UnicastAddresses.Where(u => u.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork).Select(u => u.Address));
                L($"{n.Name}  [{n.NetworkInterfaceType}]  {n.Speed / 1_000_000} Mbit/s  {n.Description}  {ips}");
            }
        }
        catch (Exception ex) { L("(could not list: " + ex.Message + ")"); }
        L("");
        L("== Audio devices (format Windows mixes at) ==");
        try
        {
            using var en = new MMDeviceEnumerator();
            foreach (var flow in new[] { DataFlow.Render, DataFlow.Capture })
            {
                string? def = null;
                try { if (en.HasDefaultAudioEndpoint(flow, Role.Multimedia)) { using var dd = en.GetDefaultAudioEndpoint(flow, Role.Multimedia); def = dd.ID; } } catch { }
                L(flow == DataFlow.Render ? "Playback:" : "Recording:");
                foreach (var dev in en.EnumerateAudioEndPoints(flow, DeviceState.Active))
                {
                    string fmt;
                    try { fmt = dev.AudioClient.MixFormat.ToString(); } catch (Exception ex) { fmt = "format unknown: " + ex.Message; }
                    string nm; try { nm = dev.FriendlyName; } catch { nm = "(no name)"; }
                    L($"  {(dev.ID == def ? "* " : "  ")}{nm}  —  {fmt}");
                    dev.Dispose();
                }
            }
        }
        catch (Exception ex) { L("(could not list: " + ex.Message + ")"); }
        return b.ToString();
    }
}
