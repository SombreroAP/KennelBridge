using System.Runtime.InteropServices;
using System.Text;

namespace KennelBridge;

/// <summary>
/// Watches this PC's keyboard, mouse and controllers and produces compact JSON snapshots for the overlay.
/// Keys and buttons are polled (GetAsyncKeyState); mouse motion and wheel come from Raw Input so they keep
/// working when a game locks the cursor. Controllers: XInput (Xbox pads, and anything Steam Input / DS4Windows
/// present as XInput) first, otherwise any HID gamepad through Raw Input + hid.dll (a DualSense / DualShock over
/// USB or Bluetooth, Switch Pro, 8BitDo in D-input mode, HOTAS...). Nothing is swallowed.
///
/// Snapshot: {"k":[vk,...],"m":[buttons,dx,dy,wheel],"g":[buttons,lt,rt,lx,ly,rx,ry,"xbox"|"ps"|"ps4"|"hid"]} or "g":0.
/// Pad buttons use XInput bit values (A=0x1000 ... plus 0x400 for the Guide/PS button) whatever the source.
/// </summary>
public sealed class InputMonitor : IDisposable
{
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size);
    [DllImport("user32.dll")] static extern uint GetRawInputData(IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);
    [DllImport("user32.dll")] static extern uint GetRawInputDeviceInfo(IntPtr hDevice, uint uiCommand, IntPtr pData, ref uint pcbSize);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetRawInputDeviceInfoW")] static extern uint GetRawInputDeviceInfoStr(IntPtr hDevice, uint uiCommand, StringBuilder? pData, ref uint pcbSize);

    [StructLayout(LayoutKind.Sequential)] struct RAWINPUTDEVICE { public ushort usUsagePage, usUsage; public uint dwFlags; public IntPtr hwndTarget; }
    [StructLayout(LayoutKind.Sequential)] struct RAWINPUTHEADER { public uint dwType, dwSize; public IntPtr hDevice, wParam; }
    [StructLayout(LayoutKind.Sequential)]
    struct RAWMOUSE
    {
        public ushort usFlags; ushort _pad;
        public ushort usButtonFlags, usButtonData;
        public uint ulRawButtons; public int lLastX, lLastY; public uint ulExtraInformation;
    }
    [StructLayout(LayoutKind.Sequential)] struct RAWHID { public uint dwSizeHid, dwCount; }   // followed by dwCount reports of dwSizeHid bytes
    const uint RID_INPUT = 0x10000003, RIM_TYPEMOUSE = 0, RIM_TYPEHID = 2, RIDEV_INPUTSINK = 0x100, RIDEV_DEVNOTIFY = 0x2000;
    const uint RIDI_PREPARSEDDATA = 0x20000005, RIDI_DEVICENAME = 0x20000007;
    const ushort RI_MOUSE_WHEEL = 0x0400;
    public const int WM_INPUT = 0x00FF, WM_INPUT_DEVICE_CHANGE = 0x00FE;
    const int GIDC_REMOVAL = 2;

    // ---- XInput (Xbox controllers). xinput1_4 on Windows 8+, fallback to the always-present 9.1.0. ----
    [StructLayout(LayoutKind.Sequential)] struct XINPUT_GAMEPAD { public ushort wButtons; public byte bLeftTrigger, bRightTrigger; public short sThumbLX, sThumbLY, sThumbRX, sThumbRY; }
    [StructLayout(LayoutKind.Sequential)] struct XINPUT_STATE { public uint dwPacketNumber; public XINPUT_GAMEPAD Gamepad; }
    [DllImport("xinput1_4.dll", EntryPoint = "#100")] static extern uint XInputGetStateEx14(uint idx, out XINPUT_STATE state);   // undocumented: also reports the Guide button (0x400)
    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")] static extern uint XInputGetState14(uint idx, out XINPUT_STATE state);
    [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")] static extern uint XInputGetState910(uint idx, out XINPUT_STATE state);
    [DllImport("xinput1_3.dll", EntryPoint = "XInputGetState")] static extern uint XInputGetState13(uint idx, out XINPUT_STATE state);
    static int _xinputLevel = 3;   // 3 = 1_4 ordinal 100 (Guide button), 2 = 1_4, 1 = 9_1_0, 0 = 1_3, -1 = none
    public static string XInputName => _xinputLevel switch { 3 => "xinput1_4 (+Guide)", 2 => "xinput1_4", 1 => "xinput9_1_0", 0 => "xinput1_3", _ => "none" };
    static uint XInputGetState(uint idx, out XINPUT_STATE st)
    {
        // any failure to call a variant (missing dll, missing export, marshalling) drops to the next one
        if (_xinputLevel == 3) { try { return XInputGetStateEx14(idx, out st); } catch { _xinputLevel = 2; } }
        if (_xinputLevel == 2) { try { return XInputGetState14(idx, out st); } catch { _xinputLevel = 1; } }
        if (_xinputLevel == 1) { try { return XInputGetState910(idx, out st); } catch { _xinputLevel = 0; } }
        if (_xinputLevel == 0) { try { return XInputGetState13(idx, out st); } catch { _xinputLevel = -1; } }
        st = default; return 1167;   // ERROR_DEVICE_NOT_CONNECTED
    }

    // ---- hid.dll: parse HID gamepad reports with the device's own descriptor ----
    [StructLayout(LayoutKind.Sequential)]
    struct HIDP_CAPS
    {
        public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices, NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices, NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
    }
    [StructLayout(LayoutKind.Explicit, Size = 72)]
    struct HIDP_VALUE_CAPS
    {
        [FieldOffset(0)] public ushort UsagePage;
        [FieldOffset(2)] public byte ReportID;
        [FieldOffset(12)] public byte IsRange;
        [FieldOffset(16)] public byte HasNull;
        [FieldOffset(18)] public ushort BitSize;
        [FieldOffset(40)] public int LogicalMin;
        [FieldOffset(44)] public int LogicalMax;
        [FieldOffset(56)] public ushort UsageMin;   // == Usage when !IsRange
        [FieldOffset(58)] public ushort UsageMax;
    }
    const int HIDP_STATUS_SUCCESS = 0x00110000;
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr preparsed, out HIDP_CAPS caps);
    [DllImport("hid.dll")] static extern int HidP_GetValueCaps(int reportType, [Out] HIDP_VALUE_CAPS[] caps, ref ushort len, IntPtr preparsed);
    [DllImport("hid.dll")] static extern int HidP_GetUsages(int reportType, ushort usagePage, ushort linkCollection, [Out] ushort[] usages, ref uint usageLength, IntPtr preparsed, byte[] report, uint reportLength);
    [DllImport("hid.dll")] static extern int HidP_GetUsageValue(int reportType, ushort usagePage, ushort linkCollection, ushort usage, out uint value, IntPtr preparsed, byte[] report, uint reportLength);
    [DllImport("hid.dll")] static extern uint HidP_MaxUsageListLength(int reportType, ushort usagePage, IntPtr preparsed);

    /// <summary>One HID gamepad we have seen a report from.</summary>
    sealed class HidPad
    {
        public IntPtr Preparsed;
        public string Name = "";
        public int Vid, Pid;
        public string Kind = "hid";               // "ps" DualSense, "ps4" DualShock 4, "hid" otherwise
        public bool Ignore;                       // XInput device (IG_), keyboards pretending to be pads, parse failure
        public HIDP_VALUE_CAPS[] Values = Array.Empty<HIDP_VALUE_CAPS>();
        public ushort[] UsageBuf = Array.Empty<ushort>();
        public int Buttons; public int LT, RT; public double LX, LY, RX, RY;
        public DateTime LastReport;
    }
    readonly Dictionary<IntPtr, HidPad> _hid = new();
    HidPad? _hidActive;

    readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
    readonly bool[] _down = new bool[256];
    int _dx, _dy, _wheel;                 // accumulated since the last snapshot (raw input thread = UI thread)
    int _rawButtons;                      // mouse buttons 1-5 as seen by Raw Input (GetAsyncKeyState misses middle/side buttons in some games)
    int _rawPressedSince;                 // buttons that went down since the last snapshot, so a quick click is never lost
    string _lastJson = "";
    DateTime _lastSent = DateTime.MinValue;
    readonly bool[] _padConnected = new bool[4];
    readonly uint[] _padPacket = new uint[4];
    readonly DateTime[] _padActive = new DateTime[4];
    readonly XINPUT_GAMEPAD[] _padState = new XINPUT_GAMEPAD[4];
    bool _xinputReported;
    static bool IsNeutral(XINPUT_GAMEPAD g) => g.wButtons == 0 && g.bLeftTrigger < 30 && g.bRightTrigger < 30 && Math.Abs((int)g.sThumbLX) < 8000 && Math.Abs((int)g.sThumbLY) < 8000 && Math.Abs((int)g.sThumbRX) < 8000 && Math.Abs((int)g.sThumbRY) < 8000;   // (int): Math.Abs(short.MinValue) throws
    static bool IsNeutralHid(HidPad h) => h.Buttons == 0 && h.LT < 30 && h.RT < 30 && Math.Abs(h.LX) < 0.25 && Math.Abs(h.LY) < 0.25 && Math.Abs(h.RX) < 0.25 && Math.Abs(h.RY) < 0.25;
    DateTime _padScan = DateTime.MinValue;

    /// <summary>A new snapshot to publish. UI thread. Fires on change, or every 200 ms as a heartbeat.</summary>
    public event Action<string>? Snapshot;
    /// <summary>Something worth a line in the activity log (a controller appeared / left).</summary>
    public event Action<string>? Log;
    public bool Running => _timer.Enabled;
    /// <summary>What the overlay is being fed from: "Xbox (XInput)", "DualSense (HID)", ... or "".</summary>
    public string PadSource { get; private set; } = "";

    int _tickErrors;
    public InputMonitor() { _timer.Tick += (_, _) => { try { Tick(); } catch (Exception ex) { if (_tickErrors++ < 3) Log?.Invoke("Capture error (kept running): " + ex.Message); } }; }

    public void Start(IntPtr hwnd)
    {
        var rid = new[]
        {
            new RAWINPUTDEVICE { usUsagePage = 1, usUsage = 2, dwFlags = RIDEV_INPUTSINK, hwndTarget = hwnd },                    // generic mouse, even in background
            new RAWINPUTDEVICE { usUsagePage = 1, usUsage = 4, dwFlags = RIDEV_INPUTSINK | RIDEV_DEVNOTIFY, hwndTarget = hwnd },  // joystick (HOTAS, wheels)
            new RAWINPUTDEVICE { usUsagePage = 1, usUsage = 5, dwFlags = RIDEV_INPUTSINK | RIDEV_DEVNOTIFY, hwndTarget = hwnd },  // gamepad (DualSense, DualShock, Switch Pro...)
            new RAWINPUTDEVICE { usUsagePage = 1, usUsage = 8, dwFlags = RIDEV_INPUTSINK | RIDEV_DEVNOTIFY, hwndTarget = hwnd },  // multi-axis controller
        };
        try { RegisterRawInputDevices(rid, (uint)rid.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()); } catch { }
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    /// <summary>Call from the window's WndProc for WM_INPUT.</summary>
    public void HandleRawInput(IntPtr lParam)
    {
        if (!_timer.Enabled) return;
        uint size = 0;
        uint headerSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
        GetRawInputData(lParam, RID_INPUT, IntPtr.Zero, ref size, headerSize);
        if (size == 0 || size > 4096) return;
        var buf = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(lParam, RID_INPUT, buf, ref size, headerSize) != size) return;
            var header = Marshal.PtrToStructure<RAWINPUTHEADER>(buf);
            if (header.dwType == RIM_TYPEMOUSE)
            {
                var mouse = Marshal.PtrToStructure<RAWMOUSE>(buf + (int)headerSize);
                if ((mouse.usFlags & 1) == 0) { _dx += mouse.lLastX; _dy += mouse.lLastY; }   // relative motion only
                if ((mouse.usButtonFlags & RI_MOUSE_WHEEL) != 0) _wheel += (short)mouse.usButtonData / 120;
                // RI_MOUSE_BUTTON_n_DOWN = 1 << (2n-2), _UP = 1 << (2n-1); our mask bit for button n is 1 << (n-1)
                for (int b = 0; b < 5; b++)
                {
                    if ((mouse.usButtonFlags & (1 << (2 * b))) != 0) { _rawButtons |= 1 << b; _rawPressedSince |= 1 << b; }
                    if ((mouse.usButtonFlags & (1 << (2 * b + 1))) != 0) _rawButtons &= ~(1 << b);
                }
            }
            else if (header.dwType == RIM_TYPEHID)
            {
                var hid = Marshal.PtrToStructure<RAWHID>(buf + (int)headerSize);
                if (hid.dwSizeHid == 0 || hid.dwCount == 0) return;
                var pad = GetHidPad(header.hDevice);
                if (pad == null || pad.Ignore) return;
                int dataOff = (int)headerSize + Marshal.SizeOf<RAWHID>();
                int last = (int)((hid.dwCount - 1) * hid.dwSizeHid);            // only the newest report matters
                if (dataOff + last + hid.dwSizeHid > size) return;
                var report = new byte[hid.dwSizeHid];
                Marshal.Copy(buf + dataOff + last, report, 0, report.Length);
                ParseHidReport(pad, report);
            }
        }
        catch { }
        finally { Marshal.FreeHGlobal(buf); }
    }

    /// <summary>Call from the window's WndProc for WM_INPUT_DEVICE_CHANGE.</summary>
    public void HandleDeviceChange(IntPtr wParam, IntPtr lParam)
    {
        if ((int)wParam != GIDC_REMOVAL) return;
        if (_hid.Remove(lParam, out var pad))
        {
            if (pad.Preparsed != IntPtr.Zero) Marshal.FreeHGlobal(pad.Preparsed);
            if (_hidActive == pad) { _hidActive = null; Log?.Invoke($"Controller disconnected: {pad.Name}."); }
        }
    }

    HidPad? GetHidPad(IntPtr hDevice)
    {
        if (_hid.TryGetValue(hDevice, out var known)) return known;
        var pad = new HidPad();
        _hid[hDevice] = pad;
        try
        {
            uint len = 0;
            GetRawInputDeviceInfoStr(hDevice, RIDI_DEVICENAME, null, ref len);
            var sb = new StringBuilder((int)len + 1);
            if (len > 0 && GetRawInputDeviceInfoStr(hDevice, RIDI_DEVICENAME, sb, ref len) > 0) pad.Name = sb.ToString();
            var path = pad.Name.ToUpperInvariant();
            if (path.Contains("IG_")) { pad.Ignore = true; return pad; }              // an XInput pad: XInput handles it (and reports it properly)
            int vi = path.IndexOf("VID_", StringComparison.Ordinal), pi = path.IndexOf("PID_", StringComparison.Ordinal);
            if (vi >= 0) int.TryParse(path.AsSpan(vi + 4, Math.Min(4, path.Length - vi - 4)), System.Globalization.NumberStyles.HexNumber, null, out pad.Vid);
            if (pi >= 0) int.TryParse(path.AsSpan(pi + 4, Math.Min(4, path.Length - pi - 4)), System.Globalization.NumberStyles.HexNumber, null, out pad.Pid);
            if (pad.Vid == 0x054C) pad.Kind = pad.Pid is 0x05C4 or 0x09CC or 0x0BA0 ? "ps4" : "ps";   // DualShock 4 (and its USB adapter) vs DualSense

            uint psize = 0;
            GetRawInputDeviceInfo(hDevice, RIDI_PREPARSEDDATA, IntPtr.Zero, ref psize);
            if (psize == 0) { pad.Ignore = true; return pad; }
            pad.Preparsed = Marshal.AllocHGlobal((int)psize);
            if (GetRawInputDeviceInfo(hDevice, RIDI_PREPARSEDDATA, pad.Preparsed, ref psize) == unchecked((uint)-1)) { pad.Ignore = true; return pad; }
            if (HidP_GetCaps(pad.Preparsed, out var caps) != HIDP_STATUS_SUCCESS) { pad.Ignore = true; return pad; }
            if (caps.UsagePage != 1 || (caps.Usage != 4 && caps.Usage != 5 && caps.Usage != 8)) { pad.Ignore = true; return pad; }
            ushort n = caps.NumberInputValueCaps;
            if (n > 0)
            {
                var vc = new HIDP_VALUE_CAPS[n];
                if (HidP_GetValueCaps(0, vc, ref n, pad.Preparsed) == HIDP_STATUS_SUCCESS) pad.Values = vc.Take(n).ToArray();
            }
            pad.UsageBuf = new ushort[Math.Max(HidP_MaxUsageListLength(0, 9, pad.Preparsed), 1)];
            pad.Name = FriendlyName(pad);
        }
        catch { pad.Ignore = true; }
        return pad;
    }

    static string FriendlyName(HidPad p) => (p.Vid, p.Pid) switch
    {
        (0x054C, 0x0CE6) or (0x054C, 0x0DF2) => "DualSense",
        (0x054C, 0x05C4) or (0x054C, 0x09CC) => "DualShock 4",
        (0x054C, _) => "PlayStation controller",
        (0x057E, 0x2009) => "Switch Pro Controller",
        (0x057E, _) => "Nintendo controller",
        (0x2DC8, _) => "8BitDo controller",
        (0x044F, _) => "Thrustmaster device",
        (0x046D, _) => "Logitech device",
        _ => $"HID controller {p.Vid:X4}:{p.Pid:X4}",
    };

    // Button usage (1-based) -> XInput bit. Sony order is fixed; "generic" is the common D-input order (A B X Y LB RB LT RT Back Start LS RS Guide).
    static readonly int[] SonyMap = { 0, 0x4000, 0x1000, 0x2000, 0x8000, 0x100, 0x200, 0, 0, 0x20, 0x10, 0x40, 0x80, 0x400, 0 };   // 1 Square 2 Cross 3 Circle 4 Triangle 5 L1 6 R1 7 L2 8 R2 9 Create 10 Options 11 L3 12 R3 13 PS 14 Touchpad
    static readonly int[] NintendoMap = { 0, 0x2000, 0x1000, 0x8000, 0x4000, 0x100, 0x200, 0, 0, 0x20, 0x10, 0x40, 0x80, 0x400, 0 };   // 1 B 2 A 3 Y 4 X 5 L 6 R 7 ZL 8 ZR 9 - 10 + 11 LS 12 RS 13 Home 14 Capture
    static readonly int[] GenericMap = { 0, 0x1000, 0x2000, 0x4000, 0x8000, 0x100, 0x200, 0, 0, 0x20, 0x10, 0x40, 0x80, 0x400 };
    static readonly int[] HatBits = { 1, 1 | 8, 8, 8 | 2, 2, 2 | 4, 4, 4 | 1 };   // up, up-right, right, ... clockwise

    void ParseHidReport(HidPad pad, byte[] report)
    {
        var map = pad.Vid == 0x054C ? SonyMap : pad.Vid == 0x057E ? NintendoMap : GenericMap;
        // a report id the descriptor does not describe with axes (e.g. the DualSense's extended 0x31 Bluetooth report,
        // which only appears once another app has asked for it) carries nothing we can read: leave the last state alone
        if (pad.Values.Length > 0 && !pad.Values.Any(v => v.ReportID == 0 || v.ReportID == report[0])) return;
        int buttons = 0, lt = -1, rt = -1;
        uint count = (uint)pad.UsageBuf.Length;
        if (HidP_GetUsages(0, 9, 0, pad.UsageBuf, ref count, pad.Preparsed, report, (uint)report.Length) == HIDP_STATUS_SUCCESS)
        {
            for (int i = 0; i < count; i++)
            {
                int u = pad.UsageBuf[i];
                if (u > 0 && u < map.Length) buttons |= map[u];
                if (u == 7) lt = 255; else if (u == 8) rt = 255;    // digital triggers when the pad has no analog ones
            }
        }
        double lx = 0, ly = 0, rx = 0, ry = 0;
        foreach (var v in pad.Values)
        {
            if (v.UsagePage != 1 || v.IsRange != 0) continue;
            if (v.ReportID != 0 && v.ReportID != report[0]) continue;        // value not in this report (e.g. DualSense sends 0x01 or 0x31)
            if (HidP_GetUsageValue(0, 1, 0, v.UsageMin, out uint raw, pad.Preparsed, report, (uint)report.Length) != HIDP_STATUS_SUCCESS) continue;
            long min = v.LogicalMin, max = v.LogicalMax;
            if (max <= min) { max = (1L << v.BitSize) - 1; min = 0; }
            long val = raw;
            if (v.LogicalMin < 0 && v.BitSize < 32) { long sign = 1L << (v.BitSize - 1); if (val >= sign) val -= sign << 1; }   // sign-extend
            switch (v.UsageMin)
            {
                case 0x30: lx = Axis(val, min, max); break;
                case 0x31: ly = -Axis(val, min, max); break;             // HID Y grows downward; XInput upward
                case 0x32: rx = Axis(val, min, max); break;
                case 0x35: ry = -Axis(val, min, max); break;
                case 0x33: lt = Trigger(val, min, max); break;
                case 0x34: rt = Trigger(val, min, max); break;
                case 0x39:
                    if (val >= min && val <= max) { long span = max - min + 1; int dir = (int)((val - min) * 8 / span); if (dir >= 0 && dir < 8) buttons |= HatBits[dir]; }
                    break;
            }
        }
        pad.Buttons = buttons; pad.LT = Math.Max(lt, 0); pad.RT = Math.Max(rt, 0);
        pad.LX = lx; pad.LY = ly; pad.RX = rx; pad.RY = ry;
        pad.LastReport = DateTime.UtcNow;
        if (_hidActive != pad) { _hidActive = pad; Log?.Invoke($"Controller: {pad.Name} (HID{(pad.Kind == "ps" ? ", PlayStation layout" : "")})."); }
    }

    static double Axis(long v, long min, long max) { double n = (v - min) / (double)(max - min) * 2 - 1; return Math.Clamp(n, -1, 1); }
    static int Trigger(long v, long min, long max) => (int)Math.Clamp((v - min) * 255 / Math.Max(max - min, 1), 0, 255);

    void Tick()
    {
        // keyboard + mouse buttons
        var keys = new StringBuilder();
        for (int vk = 1; vk < 256; vk++)
        {
            if (vk == 3 || vk == 7) continue;   // VK_CANCEL, reserved
            bool d = (GetAsyncKeyState(vk) & 0x8000) != 0;
            _down[vk] = d;
            if (d && vk > 6) { if (keys.Length > 0) keys.Append(','); keys.Append(vk); }
        }
        int mb = (_down[1] ? 1 : 0) | (_down[2] ? 2 : 0) | (_down[4] ? 4 : 0) | (_down[5] ? 8 : 0) | (_down[6] ? 16 : 0);
        mb |= _rawButtons | _rawPressedSince;
        _rawPressedSince = 0;
        int dx = _dx, dy = _dy, wheel = _wheel;
        _dx = _dy = _wheel = 0;

        // controllers: poll all four XInput slots (a Wooting keyboard or a Steam virtual pad can occupy slot 1 while the real
        // pad sits in slot 2) and follow whichever pad changed most recently; a HID pad (DualSense etc.) competes on the same rule
        var pad = "0";
        var source = "";
        var now0 = DateTime.UtcNow;
        bool scan = (now0 - _padScan).TotalSeconds > 2;
        if (scan) _padScan = now0;
        for (uint i = 0; i < 4; i++)
        {
            if (!_padConnected[i] && !scan) continue;
            if (XInputGetState(i, out var st) != 0)
            {
                if (_padConnected[i]) { _padConnected[i] = false; Log?.Invoke($"XInput pad {i + 1} disconnected."); }
                continue;
            }
            if (!_padConnected[i]) { _padConnected[i] = true; _padPacket[i] = st.dwPacketNumber; _padActive[i] = DateTime.MinValue; Log?.Invoke($"Controller: XInput pad {i + 1} via {XInputName}{(i > 0 ? " (slot 1 is taken by something else, e.g. a Wooting keyboard - the pad that moves last is shown)" : "")}."); }
            _padState[i] = st.Gamepad;
            if (st.dwPacketNumber != _padPacket[i] && !IsNeutral(st.Gamepad)) { _padPacket[i] = st.dwPacketNumber; _padActive[i] = now0; }
            else if (st.dwPacketNumber != _padPacket[i]) _padPacket[i] = st.dwPacketNumber;
        }
        if (!_xinputReported) { _xinputReported = true; Log?.Invoke($"Controller check: {XInputName}; {(_padConnected.Any(c => c) ? "pad found" : "no XInput pad yet - re-checking every 2 s; HID pads (PlayStation etc.) are picked up on first input")}."); }
        // pick: the most recently active pad; if none has moved yet, the first connected XInput slot
        int best = -1; DateTime bestT = DateTime.MinValue;
        for (int i = 0; i < 4; i++) if (_padConnected[i] && (best < 0 || _padActive[i] > bestT)) { best = i; bestT = _padActive[i]; }
        bool useHid = _hidActive != null && (best < 0 || _hidActive.LastReport > bestT && !IsNeutralHid(_hidActive));
        if (useHid)
        {
            var h = _hidActive!;
            pad = string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"[{h.Buttons},{h.LT},{h.RT},{h.LX:0.00},{h.LY:0.00},{h.RX:0.00},{h.RY:0.00},\"{h.Kind}\"]");
            source = h.Name + " (HID)";
        }
        else if (best >= 0)
        {
            var g = _padState[best];
            pad = string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"[{g.wButtons & 0xF7FF},{g.bLeftTrigger},{g.bRightTrigger},{Axis(g.sThumbLX)},{Axis(g.sThumbLY)},{Axis(g.sThumbRX)},{Axis(g.sThumbRY)},\"xbox\"]");
            source = $"XInput pad {best + 1}";
        }
        PadSource = source;

        var json = $"{{\"k\":[{keys}],\"m\":[{mb},{dx},{dy},{wheel}],\"g\":{pad}}}";
        bool moved = dx != 0 || dy != 0 || wheel != 0;
        var now = DateTime.UtcNow;
        if (json != _lastJson || moved || (now - _lastSent).TotalMilliseconds > 200)
        {
            _lastJson = json;
            _lastSent = now;
            Snapshot?.Invoke(json);
        }
    }

    static string Axis(short v) => (v / 32767.0).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

    public void Dispose()
    {
        _timer.Stop(); _timer.Dispose();
        foreach (var p in _hid.Values) if (p.Preparsed != IntPtr.Zero) Marshal.FreeHGlobal(p.Preparsed);
        _hid.Clear();
    }
}
