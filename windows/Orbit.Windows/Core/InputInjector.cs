using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Orbit.Core;

/// <summary>
/// Mouse and keyboard input from the phone's touchpad, keyboard and slides remote, through SendInput. Windows
/// doesn't let it reach elevated windows or the lock screen (UIPI), which is deliberate.
/// </summary>
public static class InputInjector
{
    const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;
    const uint MOUSEEVENTF_MOVE = 0x0001, MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004,
        MOUSEEVENTF_RIGHTDOWN = 0x0008, MOUSEEVENTF_RIGHTUP = 0x0010, MOUSEEVENTF_MIDDLEDOWN = 0x0020,
        MOUSEEVENTF_MIDDLEUP = 0x0040, MOUSEEVENTF_WHEEL = 0x0800, MOUSEEVENTF_HWHEEL = 0x1000;
    const uint KEYEVENTF_EXTENDEDKEY = 0x0001, KEYEVENTF_KEYUP = 0x0002, KEYEVENTF_UNICODE = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit)]
    struct INPUTUNION { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public uint type; public INPUTUNION u; }

    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    static void Send(params INPUT[] inputs)
    {
        if (inputs.Length == 0) return;
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) != inputs.Length)
            Log.Write("input: SendInput blocked (" + Marshal.GetLastWin32Error() + ")");
    }

    static INPUT Mouse(uint flags, int dx = 0, int dy = 0, int data = 0) =>
        new() { type = INPUT_MOUSE, u = new INPUTUNION { mi = new MOUSEINPUT { dx = dx, dy = dy, mouseData = unchecked((uint)data), dwFlags = flags } } };

    static INPUT Key(ushort vk, bool up) =>
        new() { type = INPUT_KEYBOARD, u = new INPUTUNION { ki = new KEYBDINPUT { wVk = vk, dwFlags = (up ? KEYEVENTF_KEYUP : 0) | (IsExtended(vk) ? KEYEVENTF_EXTENDEDKEY : 0) } } };

    static INPUT Unicode(char c, bool up) =>
        new() { type = INPUT_KEYBOARD, u = new INPUTUNION { ki = new KEYBDINPUT { wScan = c, dwFlags = KEYEVENTF_UNICODE | (up ? KEYEVENTF_KEYUP : 0) } } };

    static bool IsExtended(ushort vk) => vk is 0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 or 0x2D or 0x2E or 0x5B;

    // ---------------------------------------------------------------- mouse

    public static void Move(int dx, int dy) { if (dx != 0 || dy != 0) Send(Mouse(MOUSEEVENTF_MOVE, dx, dy)); }

    /// <summary>"left" | "right" | "middle"; action "click" (default), "down" or "up".</summary>
    public static void Button(string button, string action)
    {
        var (down, up) = button switch
        {
            "right" => (MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP),
            "middle" => (MOUSEEVENTF_MIDDLEDOWN, MOUSEEVENTF_MIDDLEUP),
            _ => (MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP)
        };
        lock (Held)
        {
            switch (action)
            {
                case "down": Send(Mouse(down)); Held[down] = up; break;
                case "up": Send(Mouse(up)); Held.Remove(down); break;
                default: Send(Mouse(down), Mouse(up)); break;
            }
        }
    }

    /// <summary>Buttons the phone pressed and hasn't released yet (down flag → up flag).</summary>
    static readonly Dictionary<uint, uint> Held = new();

    /// <summary>Lets go of any button the phone left held (link dropped mid-drag), so the real mouse isn't stuck dragging.</summary>
    public static void ReleaseAll()
    {
        lock (Held)
        {
            if (Held.Count == 0) return;
            Send(Held.Values.Select(up => Mouse(up)).ToArray());
            Held.Clear();
        }
    }

    /// <summary>Wheel in notches (120 = one click of a wheel); fractions scroll smoothly where apps allow.</summary>
    public static void Scroll(int vertical, int horizontal)
    {
        var list = new List<INPUT>();
        if (vertical != 0) list.Add(Mouse(MOUSEEVENTF_WHEEL, data: vertical));
        if (horizontal != 0) list.Add(Mouse(MOUSEEVENTF_HWHEEL, data: horizontal));
        Send(list.ToArray());
    }

    // ---------------------------------------------------------------- keyboard

    /// <summary>Types text as Unicode characters, so it doesn't depend on this PC's keyboard layout. "\n" presses Enter.</summary>
    public static void Text(string text)
    {
        var list = new List<INPUT>();
        foreach (var c in text.Replace("\r\n", "\n"))
        {
            if (c == '\n') { list.Add(Key(0x0D, false)); list.Add(Key(0x0D, true)); continue; }
            if (c == '\t') { list.Add(Key(0x09, false)); list.Add(Key(0x09, true)); continue; }
            if (char.IsControl(c)) continue;
            list.Add(Unicode(c, false)); list.Add(Unicode(c, true));
        }
        Send(list.ToArray());
    }

    /// <summary>Virtual-key codes for the names the phone sends. Letters and digits map to themselves.</summary>
    public static ushort? KeyCode(string name) => name.ToLowerInvariant() switch
    {
        "esc" => 0x1B, "tab" => 0x09, "enter" => 0x0D, "backspace" => 0x08, "delete" => 0x2E, "space" => 0x20,
        "left" => 0x25, "up" => 0x26, "right" => 0x27, "down" => 0x28,
        "home" => 0x24, "end" => 0x23, "pageup" => 0x21, "pagedown" => 0x22, "insert" => 0x2D,
        "f1" => 0x70, "f2" => 0x71, "f3" => 0x72, "f4" => 0x73, "f5" => 0x74, "f6" => 0x75,
        "f7" => 0x76, "f8" => 0x77, "f9" => 0x78, "f10" => 0x79, "f11" => 0x7A, "f12" => 0x7B,
        "win" => 0x5B,
        var s when s.Length == 1 && char.IsAsciiLetterOrDigit(s[0]) => char.ToUpperInvariant(s[0]),
        _ => null
    };

    static ushort? ModifierCode(string name) => name.ToLowerInvariant() switch
    {
        "ctrl" => 0x11, "alt" => 0x12, "shift" => 0x10, "win" => 0x5B, _ => null
    };

    /// <summary>Presses <paramref name="key"/> (null = only the modifiers, e.g. Win alone) with modifiers held.</summary>
    public static bool Press(string? key, IEnumerable<string> modifiers)
    {
        ushort? vk = key == null ? null : KeyCode(key);
        if (key != null && vk == null) return false;
        var mods = modifiers.Select(ModifierCode).OfType<ushort>().Distinct().ToList();
        var list = new List<INPUT>();
        foreach (var m in mods) list.Add(Key(m, false));
        if (vk is { } k) { list.Add(Key(k, false)); list.Add(Key(k, true)); }
        for (int i = mods.Count - 1; i >= 0; i--) list.Add(Key(mods[i], true));
        Send(list.ToArray());
        return true;
    }

    // ---------------------------------------------------------------- focused app

    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc proc, IntPtr lParam);

    static IntPtr _lastWindow;
    static string _lastName = "";

    /// <summary>
    /// The foreground app's friendly name ("Google Chrome"), never the window title (titles hold document names).
    /// Only looked up again when the foreground window changes.
    /// </summary>
    public static string ForegroundApp()
    {
        try
        {
            var h = GetForegroundWindow();
            if (h == IntPtr.Zero) return "";
            if (h == _lastWindow) return _lastName;
            GetWindowThreadProcessId(h, out var pid);
            using var p = Process.GetProcessById((int)pid);
            var proc = p;
            Process? inner = null;
            // Store apps (Settings, Calculator…) sit inside ApplicationFrameHost; name the app, not the frame.
            if (p.ProcessName.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))
            {
                uint innerPid = 0;
                EnumChildWindows(h, (child, _) =>
                {
                    GetWindowThreadProcessId(child, out var cp);
                    if (cp != pid) { innerPid = cp; return false; }
                    return true;
                }, IntPtr.Zero);
                if (innerPid != 0) proc = inner = Process.GetProcessById((int)innerPid);
            }
            string? desc = null;
            try { desc = proc.MainModule?.FileVersionInfo.FileDescription; } catch { }
            var name = string.IsNullOrWhiteSpace(desc) ? proc.ProcessName : desc.Trim();
            inner?.Dispose();
            _lastWindow = h;
            _lastName = name.Length > 60 ? name[..60] : name;
            return _lastName;
        }
        catch { return ""; }
    }

    static readonly string[] Browsers = { "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "arc" };

    /// <summary>The window in front belongs to a web browser (where F5 reloads the page).</summary>
    public static bool ForegroundIsBrowser()
    {
        try
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
            using var p = Process.GetProcessById((int)pid);
            return Browsers.Contains(p.ProcessName.ToLowerInvariant());
        }
        catch { return false; }
    }
}
