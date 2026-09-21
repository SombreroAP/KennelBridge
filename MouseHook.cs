using System.Runtime.InteropServices;

namespace KennelBridge;

/// <summary>
/// Low-level mouse hook so Middle / Mouse 4 / Mouse 5 can be used as triggers (RegisterHotKey is keyboard-only).
/// Left and right are deliberately not hookable - swallowing them would make the PC unusable.
/// </summary>
public static class MouseHook
{
    const int WH_MOUSE_LL = 14;
    const int WM_MBUTTONDOWN = 0x0207, WM_MBUTTONUP = 0x0208, WM_XBUTTONDOWN = 0x020B, WM_XBUTTONUP = 0x020C;
    const uint LLMHF_INJECTED = 0x01;

    delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll", SetLastError = true)] static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string? lpModuleName);

    [StructLayout(LayoutKind.Sequential)] struct POINT { public int x, y; }
    [StructLayout(LayoutKind.Sequential)] struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData, flags, time; public IntPtr dwExtraInfo; }

    static HookProc? _proc;          // kept alive so the GC doesn't collect the callback
    static IntPtr _hook;
    static readonly HashSet<ActionKind> _swallowUp = new();
    static readonly HashSet<ActionKind> _watchUp = new();

    /// <summary>Raised on a physical (not injected) Middle/X1/X2 button-down. Return true to swallow the press.</summary>
    public static Func<ActionKind, bool>? ButtonDown;
    /// <summary>Set to also get ButtonUp for a button that was NOT swallowed (pass-through triggers).</summary>
    public static Func<ActionKind, bool>? Watch;
    /// <summary>Raised when a swallowed or watched button is released.</summary>
    public static Action<ActionKind>? ButtonUp;

    public static bool Install()
    {
        if (_hook != IntPtr.Zero) return true;
        _proc = Callback;
        _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
        return _hook != IntPtr.Zero;
    }

    public static void Uninstall()
    {
        if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    static IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            if ((info.flags & LLMHF_INJECTED) == 0)          // ignore presses we injected ourselves (no ping-pong)
            {
                int msg = wParam.ToInt32();
                ActionKind? kind = msg switch
                {
                    WM_MBUTTONDOWN or WM_MBUTTONUP => ActionKind.MiddleClick,
                    WM_XBUTTONDOWN or WM_XBUTTONUP => ((info.mouseData >> 16) & 0xFFFF) == 1 ? ActionKind.X1Click : ActionKind.X2Click,
                    _ => null,
                };
                if (kind is ActionKind k)
                {
                    bool down = msg is WM_MBUTTONDOWN or WM_XBUTTONDOWN;
                    if (down)
                    {
                        if (ButtonDown?.Invoke(k) == true) { _swallowUp.Add(k); return (IntPtr)1; }
                        if (Watch?.Invoke(k) == true) _watchUp.Add(k);
                    }
                    else
                    {
                        if (_swallowUp.Remove(k)) { ButtonUp?.Invoke(k); return (IntPtr)1; }   // swallow the matching release too
                        if (_watchUp.Remove(k)) ButtonUp?.Invoke(k);
                    }
                }
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }
}
