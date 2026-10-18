using System.Windows.Input;

namespace Orbit.Core;

/// <summary>A global hotkey such as "Ctrl+Alt+C": parsing, formatting and the Win32 values.</summary>
public readonly record struct Hotkey(ModifierKeys Modifiers, Key Key)
{
    public const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_WIN = 8, MOD_NOREPEAT = 0x4000;

    public uint Win32Modifiers =>
        (Modifiers.HasFlag(ModifierKeys.Alt) ? MOD_ALT : 0) |
        (Modifiers.HasFlag(ModifierKeys.Control) ? MOD_CONTROL : 0) |
        (Modifiers.HasFlag(ModifierKeys.Shift) ? MOD_SHIFT : 0) |
        (Modifiers.HasFlag(ModifierKeys.Windows) ? MOD_WIN : 0) | MOD_NOREPEAT;

    public uint VirtualKey => (uint)KeyInterop.VirtualKeyFromKey(Key);

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(KeyName(Key));
        return string.Join("+", parts);
    }

    static string KeyName(Key k) => k switch
    {
        >= Key.D0 and <= Key.D9 => ((int)(k - Key.D0)).ToString(),
        _ => k.ToString()
    };

    /// <summary>Parses "Ctrl+Alt+C". Needs at least one of Ctrl/Alt/Win and one non-modifier key.</summary>
    public static Hotkey? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var mods = ModifierKeys.None;
        Key? key = null;
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= ModifierKeys.Control; break;
                case "alt": mods |= ModifierKeys.Alt; break;
                case "shift": mods |= ModifierKeys.Shift; break;
                case "win" or "windows": mods |= ModifierKeys.Windows; break;
                default:
                    var name = raw.Length == 1 && char.IsDigit(raw[0]) ? "D" + raw : raw;
                    if (!Enum.TryParse<Key>(name, true, out var k)) return null;
                    key = k;
                    break;
            }
        }
        if (key is not { } kk || IsModifier(kk)) return null;
        if ((mods & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) == 0) return null;
        return new Hotkey(mods, kk);
    }

    public static bool IsModifier(Key k) => k is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System;
}
