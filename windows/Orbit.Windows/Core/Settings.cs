using System.IO;
using System.Text.Json;

namespace Orbit.Core;

public static class Paths
{
    public static readonly string Data = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Orbit");
    public static readonly string Icons = Path.Combine(Data, "icons");
    public static string SettingsFile => Path.Combine(Data, "settings.json");
    public static string CertFile => Path.Combine(Data, "identity.pfx");
    public static string LogFile => Path.Combine(Data, "orbit.log");

    static Paths()
    {
        Directory.CreateDirectory(Data);
        Directory.CreateDirectory(Icons);
    }
}

public sealed class PairedPhone
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Model { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public string? LastIp { get; set; }
    public DateTime PairedAt { get; set; } = DateTime.Now;
}

public sealed class Settings
{
    public string DeviceId { get; set; } = Guid.NewGuid().ToString("N");
    public string DeviceName { get; set; } = Environment.MachineName;
    public PairedPhone? Phone { get; set; }

    public bool StartWithWindows { get; set; } = true;
    public string Theme { get; set; } = "System"; // System | Light | Dark

    public bool ClipboardAutoSync { get; set; } = true;
    public bool ClipboardKeepHistory { get; set; } = true;

    public bool NotificationsEnabled { get; set; } = true;
    public DateTime? DoNotDisturbUntil { get; set; }

    public string SaveFolder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Orbit");
    public bool AutoAcceptFiles { get; set; } = true;

    /// <summary>Turn on the phone's USB tethering whenever it is plugged in (needs USB debugging).</summary>
    public bool AutoUsbTethering { get; set; } = true;
    /// <summary>Earbuds feature (kept in sync with the phone).</summary>
    public bool BudsEnabled { get; set; } = true;
    public string? BudsMac { get; set; }
    public string? BudsName { get; set; }

    /// <summary>Phone address to try directly ("192.168.1.20" or "host:port"), for networks where discovery fails.</summary>
    public string? ManualAddress { get; set; }

    /// <summary>Show "New notification from App" instead of the text (for screens other people can see).</summary>
    public bool NotificationsHideContent { get; set; }
    /// <summary>Show phone notifications as toasts while this PC is locked.</summary>
    public bool NotificationsWhenLocked { get; set; } = true;
    /// <summary>Warn when the phone battery drops to this level (0 = off).</summary>
    public int LowBatteryThreshold { get; set; } = 15;

    /// <summary>Don't sync clips that password managers mark as private.</summary>
    public bool ClipboardSkipSensitive { get; set; } = true;

    /// <summary>Global hotkeys, e.g. "Ctrl+Alt+C". Empty = off.</summary>
    public string HotkeyClipboard { get; set; } = "Ctrl+Alt+C";
    public string HotkeyNoise { get; set; } = "Ctrl+Alt+B";
    public string HotkeyMirror { get; set; } = "Ctrl+Alt+M";

    /// <summary>Use the ADB link over USB for Orbit itself (fast pairing, works without Wi-Fi).</summary>
    public bool UseUsbLink { get; set; } = true;

    /// <summary>Screen mirroring stream panel. Quality: Auto | 720p | 1080p | Max.</summary>
    public string MirrorQuality { get; set; } = "Auto";
    public int MirrorFps { get; set; } = 60;
    public bool MirrorAudio { get; set; } = true;
    public bool MirrorScreenOff { get; set; } = true;
    public bool MirrorKeepAwake { get; set; } = true;
    public bool MirrorShowTouches { get; set; }
    public bool MirrorAutoResize { get; set; } = true;
    public bool MirrorOnTop { get; set; }
    public bool MirrorPanel { get; set; } = true;
    /// <summary>
    /// Decode mirror video on the GPU (D3D11VA). No UI. Off by default: decoding is ~1 ms on the GPU, but
    /// copying each frame back costs ~12 ms at 1080p, so software (~7.5 ms, no copy) is smoother.
    /// It becomes the better choice once frames are drawn straight from the GPU texture.
    /// </summary>
    public bool MirrorHardwareDecode { get; set; }
    /// <summary>Wireless debugging has been paired with this PC (adb keeps the key), so mirroring can skip setup.</summary>
    public bool MirrorWirelessPaired { get; set; }

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static Settings Load()
    {
        try
        {
            if (File.Exists(Paths.SettingsFile))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(Paths.SettingsFile)) ?? new Settings();
        }
        catch (Exception ex) { Log.Write("settings load failed: " + ex.Message); }
        var s = new Settings();
        s.Save();
        return s;
    }

    readonly object _saveLock = new();
    public void Save()
    {
        lock (_saveLock)
        {
            var tmp = Paths.SettingsFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, Paths.SettingsFile, true);
        }
    }
}

public static class Log
{
    static readonly object Gate = new();
    public static void Write(string msg)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} {msg}";
        System.Diagnostics.Debug.WriteLine(line);
        lock (Gate)
        {
            try
            {
                var fi = new FileInfo(Paths.LogFile);
                if (fi.Exists && fi.Length > 2_000_000) fi.Delete();
                File.AppendAllText(Paths.LogFile, line + Environment.NewLine);
            }
            catch { }
        }
    }
}
