namespace KennelBridge;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        using var mutex = new Mutex(true, @"Local\KennelBridge.SingleInstance", out bool first);
        if (!first)
        {
            MessageBox.Show("KennelBridge is already running - look for its icon in the system tray.",
                "KennelBridge", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        ApplicationConfiguration.Initialize();
        // never show the .NET crash dialog for a UI-thread exception: log it and keep the tray app alive
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => { try { Directory.CreateDirectory(Settings.Dir); File.AppendAllText(Settings.LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} UNHANDLED {e.Exception}\n"); } catch { } };
        Application.Run(new MainForm(startHidden: args.Contains("--minimized")));
    }
}
