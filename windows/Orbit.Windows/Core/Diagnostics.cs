using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Orbit.Core;

/// <summary>
/// "Export diagnostics" for bug reports, with personal details removed (device names, addresses, serials,
/// user paths), and "Reset Orbit". Logs never contain notification text or clipboard contents to begin with.
/// </summary>
public static class Diagnostics
{
    /// <summary>Writes a redacted zip to the Desktop and returns its path.</summary>
    public static string Export(OrbitCore core)
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            $"Orbit-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        var redact = Redactor(core);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);

        void Add(string name, string text)
        {
            using var w = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
            w.Write(text);
        }

        string log = "";
        try
        {
            using var fs = new FileStream(Paths.LogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            log = new StreamReader(fs).ReadToEnd();
        }
        catch { }
        Add("orbit.log", redact(log));
        Add("settings.json", redact(SettingsSummary(core.Settings)));
        Add("system.txt", redact(SystemInfo(core)));
        return path;
    }

    static string SettingsSummary(Settings s)
    {
        var o = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(s))!.AsObject();
        // Identities and addresses are never needed to debug.
        foreach (var k in new[] { "DeviceId", "DeviceName", "ManualAddress", "BudsMac", "BudsName", "SaveFolder" }) o.Remove(k);
        if (o["Phone"] is JsonObject p) { p.Remove("Id"); p.Remove("Name"); p.Remove("Fingerprint"); p.Remove("LastIp"); }
        return o.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }

    static string SystemInfo(OrbitCore core)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Orbit {Proto.AppVersion} (protocol {Proto.Version})");
        sb.AppendLine($"Phone app: {(core.PeerAppVersion.Length > 0 ? core.PeerAppVersion : "unknown")}");
        sb.AppendLine($"Windows: {Environment.OSVersion.VersionString} ({(Environment.Is64BitOperatingSystem ? "64" : "32")}-bit, {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture})");
        sb.AppendLine($".NET: {Environment.Version}");
        sb.AppendLine($"Phone: {core.Phone.Model} · {core.Phone.State} · link {core.Phone.Link}");
        sb.AppendLine($"adb: {(core.Adb.Available ? "found" : "not found")} · {core.UsbStatus}");
        sb.AppendLine($"Buds: {(core.BudsEnabled ? "on" : "off")} · connected here: {core.BudsLocal.State.Connected}");
        sb.AppendLine($"Version warning: {core.VersionWarning}");
        return sb.ToString();
    }

    static Func<string, string> Redactor(OrbitCore core)
    {
        var words = new List<string>();
        void W(string? s) { if (!string.IsNullOrWhiteSpace(s) && s.Length >= 3) words.Add(s); }
        W(core.Settings.DeviceName); W(Environment.MachineName); W(core.Phone.Name); W(core.Settings.Phone?.Name);
        W(core.Settings.BudsName); W(core.Settings.SaveFolder); W(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        W(Environment.UserName);
        lock (core.Adb.Devices) foreach (var serial in core.Adb.Devices.Keys) W(serial);
        var ordered = words.Distinct().OrderByDescending(w => w.Length).ToList();

        return text =>
        {
            foreach (var w in ordered) text = text.Replace(w, "<redacted>", StringComparison.OrdinalIgnoreCase);
            text = Regex.Replace(text, @"\b([0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}\b", "<mac>");
            text = Regex.Replace(text, @"\b(?!127\.0\.0\.1\b)\d{1,3}(\.\d{1,3}){3}\b", "<ip>");
            text = Regex.Replace(text, @"\b[0-9a-f]{64}\b", "<fingerprint>");
            return text;
        };
    }

    /// <summary>Deletes all Orbit data (pairing, keys, settings, logs) and restarts the app.</summary>
    public static void ResetAndRestart()
    {
        AutoStart.Apply(false);
        var exe = Environment.ProcessPath!;
        // Delete after we exit, so files in use are released, then start fresh.
        var cmd = $"/c timeout /t 2 /nobreak >nul & rmdir /s /q \"{Paths.Data}\" & start \"\" \"{exe}\"";
        Process.Start(new ProcessStartInfo("cmd.exe", cmd) { CreateNoWindow = true, UseShellExecute = false });
        App.Current.Quit();
    }
}
