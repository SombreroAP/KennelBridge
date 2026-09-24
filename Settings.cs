using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KennelBridge;

/// <summary>Which of the two PCs this is. Every bridge derives its direction from this.</summary>
public enum PcRole { Unset = 0, Gaming = 1, Streaming = 2 }

/// <summary>Hotkeys only: transmit this PC's presses, perform incoming ones, or both.</summary>
public enum BridgeMode { Both = 0, SendOnly = 1, ReceiveOnly = 2 }

public enum ActionKind { Key = 0, LeftClick = 1, RightClick = 2, MiddleClick = 3, X1Click = 4, X2Click = 5 }

/// <summary>Tap = press and release at once. Down/Up = the other PC mirrors how long the trigger is held.</summary>
public enum PressState { Tap = 0, Down = 1, Up = 2 }

/// <summary>One hotkey rule: a press on this PC, and what the other PC does in response.</summary>
public sealed class Binding
{
    // Modifier bits match RegisterHotKey: MOD_ALT=1, MOD_CONTROL=2, MOD_SHIFT=4, MOD_WIN=8
    public ActionKind TriggerKind { get; set; } = ActionKind.Key;
    public int TriggerVk { get; set; }
    public int TriggerMods { get; set; }
    public ActionKind Kind { get; set; } = ActionKind.Key;
    public int ActionVk { get; set; }
    public int ActionMods { get; set; }
    /// <summary>true: the other PC holds the action for as long as the trigger is held. false: single tap.</summary>
    public bool Hold { get; set; } = true;
    /// <summary>true: this PC still sees the press too. false: swallowed here, only the other PC reacts.</summary>
    public bool PassThrough { get; set; } = true;
    /// <summary>Optional friendly name, e.g. "Discord: Push to mute".</summary>
    public string Label { get; set; } = "";

    [JsonIgnore] public string ActionKey => $"{(int)Kind}:{ActionVk}:{ActionMods}";
    [JsonIgnore] public string DisplayAction => Label.Length > 0 ? $"{Label}  ({ActionText})" : ActionText;
    [JsonIgnore] public string TriggerText => TriggerKind == ActionKind.Key ? KeyNames.Describe(TriggerMods, TriggerVk) : KeyNames.MouseName(TriggerKind);
    [JsonIgnore] public bool IsKeyTrigger => TriggerKind == ActionKind.Key;
    public bool SameTriggerAs(Binding o) => TriggerKind == o.TriggerKind && (!IsKeyTrigger || (TriggerVk == o.TriggerVk && TriggerMods == o.TriggerMods));
    [JsonIgnore] public string ActionText => Kind == ActionKind.Key ? KeyNames.Describe(ActionMods, ActionVk) : KeyNames.MouseName(Kind);

    public Binding Clone() => (Binding)MemberwiseClone();
}

/// <summary>Everything the app remembers, in one file: the shared connection plus a section per bridge.</summary>
public sealed class Settings
{
    // ---- connection (shared by every bridge) ----
    public PcRole Role { get; set; } = PcRole.Unset;
    public string PeerHost { get; set; } = "";
    public int Port { get; set; } = 47850;            // UDP: hotkeys, input snapshots, link test
    public string Passphrase { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public bool StartMinimized { get; set; }
    public bool StreamerMode { get; set; } = true;    // hide IPs on screen unless the user turns it off
    public bool SetupDone { get; set; }
    public int WindowWidth { get; set; }
    public int WindowHeight { get; set; }
    public string UpdateNotifiedVersion { get; set; } = "";   // the tray balloon is shown once per new version

    // ---- hotkeys ----
    public bool HotkeysEnabled { get; set; } = true;
    public BridgeMode HotkeyMode { get; set; } = BridgeMode.Both;
    public List<Binding> Bindings { get; set; } = new();

    // ---- input overlay ----
    public bool OverlayServer { get; set; }            // serve overlay pages for OBS from this PC
    public int OverlayPort { get; set; } = 47790;
    public bool OverlayCapture { get; set; } = true;   // capture keyboard/mouse/controller here and send it
    public string OverlayTheme { get; set; } = "amber";
    public string OverlayModel { get; set; } = "auto"; // "" = built-in pad, "auto" = bundled model by pad type, or a file name
    public string OverlayId { get; set; } = "wasd?look=classic";
    public string OverlayStyle { get; set; } = "solid";
    public string OverlayScale { get; set; } = "1";
    public bool OverlayPlate { get; set; }
    public bool OverlayHistory { get; set; }

    // ---- audio ----
    public bool AudioEnabled { get; set; }              // start the audio link automatically
    public int AudioPort { get; set; } = 47852;          // UDP, raw PCM
    public string? AudioRenderDeviceId { get; set; }
    public string? AudioCaptureDeviceId { get; set; }
    public string AudioLatency { get; set; } = "Balanced";

    // ---- files ----
    public bool FileSendEnabled { get; set; }
    public string FileSendFolder { get; set; } = "";
    public bool FileDeleteAfterSend { get; set; }
    public string FileExtensions { get; set; } = "mp4,mkv,mov,flv,ts,m4v,mp3,wav,m4a";
    public int FileSettleSeconds { get; set; } = 10;     // a recording counts as finished once it stops growing for this long
    public bool FileReceiveEnabled { get; set; }
    public string FileReceiveFolder { get; set; } = "";
    public int FilePort { get; set; } = 47853;           // TCP

    // ---- soundboard ----
    public bool SoundboardEnabled { get; set; } = true;
    public bool SoundBothPcs { get; set; } = true;        // a press plays on this PC and the other one
    public string? SoundDeviceId { get; set; }            // null = Windows default; gaming PC defaults to CABLE Input
    public bool SoundDeviceSet { get; set; }              // the user picked a device (stop applying the default)
    public int SoundVolume { get; set; } = 75;
    public string SoundHoldAction { get; set; } = "";   // a hotkey row held on the other PC while a sound plays ("" = none)
    public List<SoundInfo> Sounds { get; set; } = new();

    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KennelBridge");
    public static string FilePath => Path.Combine(Dir, "settings.json");
    public static string LogPath => Path.Combine(Dir, "kennelbridge.log");
    public static string ModelsDir => Path.Combine(Dir, "models");
    public static string SentManifestPath => Path.Combine(Dir, "sent-files.json");

    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public static Settings Load()
    {
        try { if (File.Exists(FilePath)) return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Opts) ?? new Settings(); }
        catch { /* corrupt file: start fresh */ }
        return new Settings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Opts));
    }

    /// <summary>Sensible per-bridge directions for a role. Called by the wizard when the role is chosen.</summary>
    public void ApplyRoleDefaults()
    {
        if (Role == PcRole.Gaming)
        {
            OverlayCapture = true; OverlayServer = false;
            FileReceiveEnabled = true; FileSendEnabled = false;
            if (FileReceiveFolder.Length == 0) FileReceiveFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "KennelBridge");
        }
        else if (Role == PcRole.Streaming)
        {
            OverlayCapture = false; OverlayServer = true;
            FileSendEnabled = true; FileReceiveEnabled = false;
            if (FileSendFolder.Length == 0) FileSendFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        }
    }

    public string RoleText => Role switch { PcRole.Gaming => "Gaming PC", PcRole.Streaming => "Streaming PC", _ => "(role not set)" };
}

public static class KeyNames
{
    public static string Describe(int mods, int vk)
    {
        var sb = new StringBuilder();
        if ((mods & 2) != 0) sb.Append("Ctrl+");
        if ((mods & 1) != 0) sb.Append("Alt+");
        if ((mods & 4) != 0) sb.Append("Shift+");
        if ((mods & 8) != 0) sb.Append("Win+");
        sb.Append(KeyName(vk));
        return sb.ToString();
    }

    public static string KeyName(int vk)
    {
        if (vk == 0) return "(none)";
        if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();   // 0-9
        var k = (Keys)vk;
        return k switch
        {
            Keys.Oemtilde => "`",
            Keys.OemMinus => "-",
            Keys.Oemplus => "=",
            Keys.OemOpenBrackets => "[",
            Keys.OemCloseBrackets => "]",
            Keys.OemPipe => "\\",
            Keys.OemSemicolon => ";",
            Keys.OemQuotes => "'",
            Keys.Oemcomma => ",",
            Keys.OemPeriod => ".",
            Keys.OemQuestion => "/",
            Keys.Prior => "PageUp",
            Keys.Next => "PageDown",
            Keys.Return => "Enter",
            Keys.Capital => "CapsLock",
            Keys.Scroll => "ScrollLock",
            Keys.Snapshot => "PrintScreen",
            Keys.Back => "Backspace",
            _ => k.ToString(),
        };
    }

    public static string MouseName(ActionKind k) => k switch
    {
        ActionKind.LeftClick => "Left click",
        ActionKind.RightClick => "Right click",
        ActionKind.MiddleClick => "Middle click",
        ActionKind.X1Click => "Mouse button 4",
        ActionKind.X2Click => "Mouse button 5",
        _ => "Key press",
    };
}
