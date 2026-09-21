using System.Runtime.InteropServices;

namespace KennelBridge;

/// <summary>Win32: global hotkeys (RegisterHotKey) and input injection (SendInput).</summary>
public static class Native
{
    public const int WM_HOTKEY = 0x0312;
    public const uint MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] static extern uint MapVirtualKey(uint uCode, uint uMapType);
    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public InputUnion U; }
    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }
    [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] public struct HARDWAREINPUT { public uint uMsg; public ushort wParamL, wParamH; }

    const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;
    const uint KEYEVENTF_EXTENDEDKEY = 0x0001, KEYEVENTF_KEYUP = 0x0002;
    const uint MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004;
    const uint MOUSEEVENTF_RIGHTDOWN = 0x0008, MOUSEEVENTF_RIGHTUP = 0x0010;
    const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020, MOUSEEVENTF_MIDDLEUP = 0x0040;
    const uint MOUSEEVENTF_XDOWN = 0x0080, MOUSEEVENTF_XUP = 0x0100;

    const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LWIN = 0x5B;

    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vKey);
    /// <summary>True while the physical key is held.</summary>
    public static bool IsKeyDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>Tap: press and release the binding's action on this machine.</summary>
    public static void Inject(Binding b) { Send(Sequence(b, down: true).Concat(Sequence(b, down: false)).ToList()); }
    /// <summary>Press (and keep pressed) the binding's action.</summary>
    public static void InjectDown(Binding b) => Send(Sequence(b, down: true));
    /// <summary>Release the binding's action.</summary>
    public static void InjectUp(Binding b) => Send(Sequence(b, down: false));

    static List<INPUT> Sequence(Binding b, bool down)
    {
        var seq = new List<INPUT>();
        if (b.Kind == ActionKind.Key)
        {
            if (b.ActionVk == 0) return seq;
            var modifiers = new List<int>();
            if ((b.ActionMods & 2) != 0) modifiers.Add(VK_CONTROL);
            if ((b.ActionMods & 1) != 0) modifiers.Add(VK_MENU);
            if ((b.ActionMods & 4) != 0) modifiers.Add(VK_SHIFT);
            if ((b.ActionMods & 8) != 0) modifiers.Add(VK_LWIN);
            if (down)
            {
                foreach (var m in modifiers) seq.Add(Key(m, up: false));
                seq.Add(Key(b.ActionVk, up: false));
            }
            else
            {
                seq.Add(Key(b.ActionVk, up: true));
                for (int i = modifiers.Count - 1; i >= 0; i--) seq.Add(Key(modifiers[i], up: true));
            }
            return seq;
        }

        var (dn, up, data) = b.Kind switch
        {
            ActionKind.LeftClick => (MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP, 0u),
            ActionKind.RightClick => (MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP, 0u),
            ActionKind.MiddleClick => (MOUSEEVENTF_MIDDLEDOWN, MOUSEEVENTF_MIDDLEUP, 0u),
            ActionKind.X1Click => (MOUSEEVENTF_XDOWN, MOUSEEVENTF_XUP, 1u),
            ActionKind.X2Click => (MOUSEEVENTF_XDOWN, MOUSEEVENTF_XUP, 2u),
            _ => (0u, 0u, 0u),
        };
        if (dn != 0) seq.Add(Mouse(down ? dn : up, data));
        return seq;
    }

    static INPUT Key(int vk, bool up)
    {
        uint flags = up ? KEYEVENTF_KEYUP : 0;
        if (IsExtended(vk)) flags |= KEYEVENTF_EXTENDEDKEY;
        return new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = (ushort)vk,
                    wScan = (ushort)MapVirtualKey((uint)vk, 0),
                    dwFlags = flags,
                }
            }
        };
    }

    static INPUT Mouse(uint flags, uint data) => new()
    {
        type = INPUT_MOUSE,
        U = new InputUnion { mi = new MOUSEINPUT { dwFlags = flags, mouseData = data } }
    };

    static void Send(List<INPUT> seq)
    {
        if (seq.Count == 0) return;
        var arr = seq.ToArray();
        SendInput((uint)arr.Length, arr, Marshal.SizeOf<INPUT>());
    }

    // Keys that need the extended-key flag or apps see the wrong key (e.g. numpad Enter vs Enter).
    static bool IsExtended(int vk) => vk switch
    {
        0x21 or 0x22 or 0x23 or 0x24 => true,          // PgUp PgDn End Home
        0x25 or 0x26 or 0x27 or 0x28 => true,          // arrows
        0x2C or 0x2D or 0x2E => true,                  // PrintScreen Insert Delete
        0x5B or 0x5C or 0x5D => true,                  // LWin RWin Apps
        0x6F or 0x90 => true,                          // numpad divide, NumLock
        0xA3 or 0xA5 => true,                          // RControl RAlt
        _ => false,
    };
}
