using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace Orbit.Core;

/// <summary>
/// Watches USB-connected Android devices through adb. Used for: USB link to Orbit
/// (adb forward), one-click pairing over USB, and switching the USB mode (see OrbitCore.Usb).
/// </summary>
public sealed class Adb
{
    public const string PhonePackage = "io.github.mohib76.orbit";

    public string? ExePath { get; private set; }
    public bool Available => ExePath != null;

    /// <summary>serial → state ("device", "unauthorized", "offline"…)</summary>
    public Dictionary<string, string> Devices { get; } = new();
    public event Action? DevicesChanged;
    public event Action<string>? DeviceOnline;

    readonly CancellationTokenSource _cts = new();

    public void Start()
    {
        ExePath = Find();
        Log.Write("adb: " + (ExePath ?? "not found"));
        if (ExePath == null) return;
        _ = Task.Run(TrackLoop);
    }

    public void Stop() => _cts.Cancel();

    static string? Find()
    {
        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "adb", "adb.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk", "platform-tools", "adb.exe"),
        };
        foreach (var env in new[] { "ANDROID_HOME", "ANDROID_SDK_ROOT" })
            if (Environment.GetEnvironmentVariable(env) is { Length: > 0 } root)
                candidates.Add(Path.Combine(root, "platform-tools", "adb.exe"));
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
            candidates.Add(Path.Combine(dir.Trim(), "adb.exe"));
        return candidates.FirstOrDefault(File.Exists);
    }

    public async Task<(int code, string output)> RunAsync(string args, int timeoutMs = 15000)
    {
        if (ExePath == null) return (-1, "adb not found");
        var psi = new ProcessStartInfo(ExePath, args)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        using var p = Process.Start(psi)!;
        var outTask = p.StandardOutput.ReadToEndAsync();
        var errTask = p.StandardError.ReadToEndAsync();
        using var cts = new CancellationTokenSource(timeoutMs);
        try { await p.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException) { try { p.Kill(); } catch { } return (-1, "timeout"); }
        return (p.ExitCode, ((await outTask) + (await errTask)).Trim());
    }

    public Task<(int code, string output)> ShellAsync(string serial, string cmd, int timeoutMs = 15000) =>
        RunAsync($"-s {serial} shell {cmd}", timeoutMs);

    async Task TrackLoop()
    {
        await RunAsync("start-server", 20000);
        while (!_cts.IsCancellationRequested)
        {
            Process? p = null;
            try
            {
                var psi = new ProcessStartInfo(ExePath!, "track-devices")
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
                };
                p = Process.Start(psi)!;
                var proc = p;
                using var reg = _cts.Token.Register(() => { try { proc.Kill(); } catch { } });
                // Drain stderr so a chatty adb can't block on a full pipe.
                _ = p.StandardError.ReadToEndAsync();
                var r = p.StandardOutput;
                var lenBuf = new char[4];
                while (!_cts.IsCancellationRequested)
                {
                    if (await ReadExact(r, lenBuf) == false) break;
                    // After an adb server restart the client can print text instead of a length: start over.
                    if (!int.TryParse(lenBuf, NumberStyles.AllowHexSpecifier, null, out var len))
                    {
                        Log.Write("adb track: unexpected output, restarting");
                        break;
                    }
                    var body = new char[len];
                    if (len > 0 && await ReadExact(r, body) == false) break;
                    Apply(new string(body));
                }
            }
            catch (Exception ex) { Log.Write("adb track: " + ex.Message); }
            finally
            {
                // Never leave a track-devices behind before starting the next one. Not the whole tree:
                // a client that had to start the adb server is that server's parent.
                if (p != null)
                {
                    try { if (!p.HasExited) p.Kill(); } catch { }
                    p.Dispose();
                }
            }
            lock (Devices) Devices.Clear();
            DevicesChanged?.Invoke();
            try { await Task.Delay(3000, _cts.Token); } catch { }
        }
    }

    static async Task<bool> ReadExact(StreamReader r, char[] buf)
    {
        int got = 0;
        while (got < buf.Length)
        {
            int n = await r.ReadAsync(buf, got, buf.Length - got);
            if (n <= 0) return false;
            got += n;
        }
        return true;
    }

    void Apply(string list)
    {
        var now = new Dictionary<string, string>();
        foreach (var line in list.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t');
            if (parts.Length >= 2) now[parts[0].Trim()] = parts[1].Trim();
        }
        List<string> cameOnline = new();
        lock (Devices)
        {
            foreach (var (serial, state) in now)
                if (state == "device" && (!Devices.TryGetValue(serial, out var old) || old != "device"))
                    cameOnline.Add(serial);
            Devices.Clear();
            foreach (var kv in now) Devices[kv.Key] = kv.Value;
        }
        Log.Write("adb devices: " + string.Join(", ", now.Select(kv => kv.Key + "=" + kv.Value)));
        DevicesChanged?.Invoke();
        foreach (var s in cameOnline) DeviceOnline?.Invoke(s);
    }

    public string? FirstReadySerial()
    {
        lock (Devices) return Devices.FirstOrDefault(kv => kv.Value == "device").Key;
    }

    public bool AnyUnauthorized()
    {
        lock (Devices) return Devices.Values.Any(v => v == "unauthorized");
    }

    public async Task<bool> HasOrbitAsync(string serial)
    {
        var (_, o) = await ShellAsync(serial, $"pm path {PhonePackage}");
        return o.Contains("package:");
    }
}
