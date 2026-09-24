using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using static KennelBridge.Theme;

namespace KennelBridge;

/// <summary>
/// myinstants.com in a real browser window (WebView2, the Edge engine in Windows). MyInstants only serves
/// its pages and files to a real browser session, so this is how its sounds reach the board: the person
/// browses the site themselves, and each sound gets an "+ Board" button. The file is fetched by the page
/// itself, inside that session, and handed to the app.
/// </summary>
public sealed class MyInstantsForm : Form
{
    readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    readonly Label _status = Theme.Label("", muted: true, Small);
    /// <summary>A sound came back from the page: title, source URL, file bytes.</summary>
    public event Action<string, string, byte[]>? SoundAdded;
    public Func<string, bool>? IsOnBoard { get; set; }

    const string Home = "https://www.myinstants.com/en/index/us/";

    // Adds "+ Board" beside every play button (including ones added later by search and paging), and does
    // the fetch inside the page's own session when clicked.
    const string Inject = @"
(() => {
  if (window.__kb) return; window.__kb = true;
  const css = document.createElement('style');
  css.textContent = '.kb-add{display:block;margin:6px auto 0;padding:3px 10px;border:0;border-radius:6px;background:#D2A04A;color:#17120A;font:600 12px Segoe UI,sans-serif;cursor:pointer}.kb-add[disabled]{background:#3a3f44;color:#cfd3d6;cursor:default}';
  document.documentElement.appendChild(css);
  const soundOf = el => { const m = (el.getAttribute('onclick') || '').match(/play\(\s*'([^']+\.(?:mp3|wav|ogg))'/i); return m ? m[1] : null; };
  const titleOf = box => { const a = box.querySelector('.instant-link, a[href*=""/instant/""]'); return (a ? a.textContent : box.textContent || '').trim().slice(0, 80); };
  const decorate = () => {
    document.querySelectorAll('[onclick*=""play(""]').forEach(btn => {
      if (btn.dataset.kb) return; btn.dataset.kb = '1';
      const path = soundOf(btn); if (!path) return;
      const box = btn.closest('.instant') || btn.parentElement;
      const url = new URL(path, location.href).href;
      const add = document.createElement('button');
      add.className = 'kb-add'; add.type = 'button'; add.textContent = '+ Board';
      add.onclick = async e => {
        e.preventDefault(); e.stopPropagation();
        add.disabled = true; add.textContent = 'Adding…';
        try {
          const r = await fetch(url, { credentials: 'include' });
          if (!r.ok) throw new Error('HTTP ' + r.status);
          const b = new Uint8Array(await r.arrayBuffer());
          let s = ''; for (let i = 0; i < b.length; i += 0x8000) s += String.fromCharCode.apply(null, b.subarray(i, i + 0x8000));
          window.chrome.webview.postMessage({ type: 'sound', url, title: titleOf(box), data: btoa(s) });
          add.textContent = 'On board';
        } catch (err) { add.disabled = false; add.textContent = 'Retry'; window.chrome.webview.postMessage({ type: 'error', url, error: String(err) }); }
      };
      (box || btn).appendChild(add);
    });
  };
  decorate();
  new MutationObserver(decorate).observe(document.documentElement, { childList: true, subtree: true });
})();";

    public MyInstantsForm()
    {
        Text = "MyInstants";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96f, 96f);
        ApplyWindow(this);
        ClientSize = new Size(1000, 760);
        MinimumSize = new Size(640, 480);

        var bar = new TableLayoutPanel { Dock = DockStyle.Top, Height = 48, ColumnCount = 4, Padding = new Padding(10, 7, 10, 7), BackColor = Rail };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(Cpct(100)); bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var back = Theme.Button("Back", minWidth: 64); back.Click += (_, _) => { if (_web.CanGoBack) _web.GoBack(); };
        var home = Theme.Button("Home", minWidth: 64); home.Click += (_, _) => _web.CoreWebView2?.Navigate(Home);
        _status.AutoSize = false; _status.Dock = DockStyle.Fill; _status.TextAlign = ContentAlignment.MiddleLeft; _status.AutoEllipsis = true; _status.BackColor = Rail;
        _status.Text = "Press + Board under any sound to add it.";
        var close = Theme.Button("Done", primary: true, minWidth: 72); close.Click += (_, _) => Close();
        bar.Controls.Add(back, 0, 0); bar.Controls.Add(home, 1, 0); bar.Controls.Add(_status, 2, 0); bar.Controls.Add(close, 3, 0);

        var note = new Label
        {
            Dock = DockStyle.Bottom, Height = 30, BackColor = Rail, ForeColor = Muted, Font = Small, TextAlign = ContentAlignment.MiddleCenter,
            Text = "MyInstants sounds are uploaded by its users. You are responsible for having the right to play them on your stream.",
        };
        Controls.Add(_web); Controls.Add(bar); Controls.Add(note);
        Shown += async (_, _) => await Init();
    }

    async Task Init()
    {
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Settings.Dir, "webview"));
            await _web.EnsureCoreWebView2Async(env);
            var core = _web.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            await core.AddScriptToExecuteOnDocumentCreatedAsync(
                "document.addEventListener('DOMContentLoaded', () => { if (location.hostname.endsWith('myinstants.com')) {" + Inject + "} });");
            core.WebMessageReceived += OnMessage;
            // stay on MyInstants: anything else opens in the normal browser
            core.NewWindowRequested += (_, e) => { e.Handled = true; OpenOutside(e.Uri); };
            core.NavigationStarting += (_, e) =>
            {
                if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var u) && !u.Host.EndsWith("myinstants.com") && u.Scheme.StartsWith("http")) { e.Cancel = true; OpenOutside(e.Uri); }
            };
            core.Navigate(Home);
        }
        catch (Exception ex)
        {
            _status.Text = "The browser could not start: " + ex.Message + "  (it needs the Microsoft Edge WebView2 Runtime, which Windows 10 and 11 normally have).";
            _status.ForeColor = Red;
        }
    }

    static void OpenOutside(string url) { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch { } }

    void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var r = doc.RootElement;
            var type = r.GetProperty("type").GetString();
            if (type == "error") { _status.Text = "Could not fetch that sound: " + r.GetProperty("error").GetString(); return; }
            if (type != "sound") return;
            var url = r.GetProperty("url").GetString() ?? "";
            var title = r.GetProperty("title").GetString() ?? "";
            var bytes = Convert.FromBase64String(r.GetProperty("data").GetString() ?? "");
            if (bytes.Length == 0 || bytes.Length > 20 * 1024 * 1024) { _status.Text = "That file is empty or too large."; return; }
            SoundAdded?.Invoke(title, url, bytes);
            _status.Text = $"Added {Soundboard.Tidy(title)} to the board.";
        }
        catch (Exception ex) { _status.Text = "Could not add that sound: " + ex.Message; }
    }
}
