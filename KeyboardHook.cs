using System.Runtime.InteropServices;

namespace KennelBridge;

/// <summary>
/// Low-level keyboard hook used for "pass-through" triggers: we watch the key go down and up but never
/// swallow it, so the game on this PC still sees it. Swallowing triggers keep using RegisterHotKey.
/// </summary>
public static class KeyboardHook
{
    const int WH_KEYBOARD_LL = 13;
    const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
    const uint LLKHF_INJECTED = 0x10;

    delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll", SetLastError = true)] static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string? lpModuleName);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);

    [StructLayout(LayoutKind.Sequential)] struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }

    static HookProc? _proc;
    static IntPtr _hook;
    static readonly HashSet<int> _down = new();

    /// <summary>Physical key went down (first event only, no auto-repeat). Args: vk, modifier bits (MOD_ALT=1, MOD_CONTROL=2, MOD_SHIFT=4, MOD_WIN=8).</summary>
    public static Action<int, int>? KeyDown;
    /// <summary>Physical key released.</summary>
    public static Action<int>? KeyUp;

    public static bool Install()
    {
        if (_hook != IntPtr.Zero) return true;
        _proc = Callback;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        return _hook != IntPtr.Zero;
    }

    public static void Uninstall()
    {
        if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    static IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var k = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if ((k.flags & LLKHF_INJECTED) == 0)               // only real keyboards, not our own SendInput
            {
                int msg = wParam.ToInt32(), vk = (int)k.vkCode;
                if (msg is WM_KEYDOWN or WM_SYSKEYDOWN)
                {
                    if (_down.Add(vk))
                    {
                        int mods = (Down(0x11) ? 2 : 0) | (Down(0x12) ? 1 : 0) | (Down(0x10) ? 4 : 0) | ((Down(0x5B) || Down(0x5C)) ? 8 : 0);
                        KeyDown?.Invoke(vk, mods);
                    }
                }
                else if (msg is WM_KEYUP or WM_SYSKEYUP)
                {
                    if (_down.Remove(vk)) KeyUp?.Invoke(vk);
                }
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);   // never swallow
    }
}
