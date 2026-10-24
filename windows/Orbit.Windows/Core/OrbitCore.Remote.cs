using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace Orbit.Core;

/// <summary>
/// Phone remote: links both ways, this PC's volume, the power buttons and the user's own commands. The phone
/// only gets the buttons Settings › Phone remote allows (<c>orbit.remote.caps</c>), and this side checks every
/// request against the same settings, so a phone can't do more than what's switched on here.
/// </summary>
public sealed partial class OrbitCore
{
    public PcVolume Volume { get; } = new();
    // Settings.RemoteCommands is replaced, never changed in place (see RemoteSettingsPage), so readers on the
    // session thread can enumerate whatever list they picked up.
    CancellationTokenSource? _volumeDebounce;

    string? _copiedLink;
    /// <summary>A link copied on this PC that hasn't gone to the phone yet (the tray flyout's "Link copied" card).</summary>
    public string? CopiedLink { get => _copiedLink; private set => Set(ref _copiedLink, value); }

    public static readonly string[] PowerOps = { "lock", "sleep", "shutdown", "restart" };

    void StartRemote()
    {
        Volume.Changed += (_, _) => ScheduleVolumeState();
        Volume.Start();
    }

    /// <summary>Tells the phone which remote buttons to show. Sent on connect and after every settings change.</summary>
    public void SendRemoteCaps() => _link?.Post("orbit.remote.caps", RemoteCaps());

    JsonObject RemoteCaps()
    {
        var power = new JsonArray();
        foreach (var op in PowerOps.Where(PowerAllowed)) power.Add(op);
        var commands = new JsonArray();
        foreach (var c in Settings.RemoteCommands)
            commands.Add(new JsonObject { ["id"] = c.Id, ["name"] = c.Name, ["ask"] = c.Ask });
        return new JsonObject
        {
            ["volume"] = Settings.RemoteVolume, ["power"] = power, ["commands"] = commands,
            ["linksAsk"] = Settings.LinksFromPhone == "ask"
        };
    }

    bool PowerAllowed(string op) => op switch
    {
        "lock" => Settings.RemoteLock,
        "sleep" => Settings.RemoteSleep,
        "shutdown" => Settings.RemoteShutdown,
        "restart" => Settings.RemoteRestart,
        _ => false
    };

    /// <summary>Saves Settings › Phone remote and brings the phone's buttons up to date.</summary>
    public void SaveRemoteSettings()
    {
        Settings.Save();
        SendRemoteCaps();
        SendVolumeState();
    }

    // ---------------------------------------------------------------- volume

    void ScheduleVolumeState()
    {
        _volumeDebounce?.Cancel();
        var cts = _volumeDebounce = new CancellationTokenSource();
        _ = Task.Delay(80, cts.Token).ContinueWith(t => { if (!t.IsCanceled) SendVolumeState(); }, TaskScheduler.Default);
    }

    void SendVolumeState()
    {
        if (_link is not { } link || !Settings.RemoteVolume) return;
        var v = Volume.Read();
        link.Post("orbit.volume.state", new JsonObject
        {
            ["level"] = v?.level ?? -1, ["muted"] = v?.muted ?? false
        });
    }

    void OnVolumeSet(JsonObject b)
    {
        if (!Settings.RemoteVolume) return;
        int? level = b["level"] is { } l ? l.GetValue<int>() : null;
        bool? muted = b["muted"] is { } m ? m.GetValue<bool>() : null;
        Volume.Set(level, muted);
    }

    // ---------------------------------------------------------------- links

    void OnPhoneUrl(JsonObject b)
    {
        // Only http(s): file paths and custom schemes would let a link start programs here.
        if (Links.Normalize(b["url"]?.GetValue<string>()) is not { } url) return;
        var phone = Phone.Name;
        Ui.Run(() =>
        {
            if (Settings.LinksFromPhone == "ask") { Toasts.LinkAsk(phone, url); return; }
            OpenLink(url);
            Toasts.LinkOpened(phone, url);
        });
    }

    public static void OpenLink(string url)
    {
        if (Links.Normalize(url) is not { } safe) return;
        try { Process.Start(new ProcessStartInfo(safe) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Write("open link: " + ex.Message); }
    }

    /// <summary>Sends a link to the phone, which shows it as a notification. False when the phone isn't connected.</summary>
    public bool SendLink(string url)
    {
        if (_link is not { } link || Links.Normalize(url) is not { } safe) return false;
        link.Post("orbit.url", new JsonObject { ["url"] = safe });
        if (CopiedLink == url) Ui.Run(() => CopiedLink = null);
        return true;
    }

    /// <summary>Remembers a copied link for the tray's "Open on phone"; anything else (images, files: "") clears it.</summary>
    void NoteCopiedLink(string? text, bool sensitive) =>
        Ui.Run(() => CopiedLink = sensitive ? null : Links.FromClipboard(text));

    // ---------------------------------------------------------------- power and commands

    [DllImport("user32.dll")] static extern bool LockWorkStation();
    [DllImport("powrprof.dll", SetLastError = true)] static extern bool SetSuspendState(bool hibernate, bool force, bool disableWakeEvent);

    void OnRemoteRun(JsonObject b)
    {
        var op = b["op"]?.GetValue<string>() ?? "";
        var id = b["id"]?.GetValue<string>();
        var phone = Phone.Name;
        var result = new JsonObject { ["op"] = op };
        if (id != null) result["id"] = id;

        if (op == "command")
        {
            // Off the packet thread: opening a file through the shell can wait on a dialog.
            var cmd = Settings.RemoteCommands.FirstOrDefault(c => c.Id == id);
            _ = Task.Run(() =>
            {
                var error = cmd == null ? "That command was removed on the PC." : RunCommand(cmd);
                if (cmd != null && error == null) Ui.Run(() => Toasts.RemoteAction($"Ran “{cmd.Name}”", phone));
                Log.Write(error == null ? "ran a remote command" : "remote command failed: " + error);
                Answer(result, error);
            });
        }
        else if (PowerOps.Contains(op))
        {
            var error = PowerAllowed(op) ? null : "Not allowed in Orbit's settings on the PC.";
            Log.Write($"remote {op}" + (error == null ? "" : " refused"));
            Answer(result, error);
            if (error == null) _ = PowerAsync(op, phone);
        }
    }

    void Answer(JsonObject result, string? error)
    {
        result["ok"] = error == null;
        if (error != null) result["error"] = error;
        _link?.Post("orbit.remote.result", result);
    }

    async Task PowerAsync(string op, string phone)
    {
        var what = op switch { "lock" => "Locked", "sleep" => "Going to sleep", "shutdown" => "Shutting down", _ => "Restarting" };
        Ui.Run(() => Toasts.RemoteAction(what, phone));
        // Let the result and the toast go out before the PC locks or stops.
        await Task.Delay(op == "lock" ? 200 : 1500);
        try
        {
            switch (op)
            {
                case "lock": LockWorkStation(); break;
                case "sleep":
                    // Fails where sleep is blocked by policy or not supported (some Modern Standby laptops).
                    if (!SetSuspendState(false, false, false))
                    {
                        Log.Write("remote sleep: SetSuspendState failed " + Marshal.GetLastWin32Error());
                        Ui.Run(() => Toasts.RemoteAction("Couldn't put this PC to sleep", phone));
                    }
                    break;
                case "shutdown": Process.Start(new ProcessStartInfo("shutdown.exe", "/s /t 0") { CreateNoWindow = true, UseShellExecute = false }); break;
                case "restart": Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 0") { CreateNoWindow = true, UseShellExecute = false }); break;
            }
        }
        catch (Exception ex) { Log.Write($"remote {op}: {ex.Message}"); }
    }

    /// <summary>Starts the command; null on success, else what went wrong (shown on the phone).</summary>
    static string? RunCommand(RemoteCommand cmd)
    {
        try
        {
            using var p = Process.Start(CommandStartInfo(cmd.Command));
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    /// <summary>
    /// How a command runs: a PowerShell script through PowerShell (opening it would only show it in Notepad), any
    /// other existing file the way Explorer opens it, and anything else as a cmd.exe command line, hidden.
    /// </summary>
    public static ProcessStartInfo CommandStartInfo(string command)
    {
        var text = command.Trim();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var path = Environment.ExpandEnvironmentVariables(text.Trim('"'));
        if (File.Exists(path))
        {
            if (Path.GetExtension(path).Equals(".ps1", StringComparison.OrdinalIgnoreCase))
                return new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -File \"{path}\"")
                    { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(path) ?? home };
            return new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path) ?? home };
        }
        // /s with outer quotes: cmd strips exactly those, so commands with several quoted parts survive.
        return new ProcessStartInfo("cmd.exe", $"/d /s /c \"{text}\"") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = home };
    }
}
