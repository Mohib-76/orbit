using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Windows.Media.Imaging;

namespace Orbit.Core;

/// <summary>The wireless-mirroring setup window's state. Mutated on the UI thread only.</summary>
public sealed class MirrorSetupState : Observable
{
    bool _known, _devOptions, _wirelessDebugging, _wifi, _paired;
    string _status = "";
    BitmapSource? _qr;

    /// <summary>The phone has reported its developer settings (orbit.mirror.info).</summary>
    public bool Known { get => _known; set => Set(ref _known, value); }
    public bool DevOptions { get => _devOptions; set => Set(ref _devOptions, value); }
    public bool WirelessDebugging { get => _wirelessDebugging; set => Set(ref _wirelessDebugging, value); }
    public bool Wifi { get => _wifi; set => Set(ref _wifi, value); }
    public bool Paired { get => _paired; set => Set(ref _paired, value); }
    public string Status { get => _status; set => Set(ref _status, value); }
    public BitmapSource? Qr { get => _qr; set => Set(ref _qr, value); }
}

/// <summary>
/// Wireless debugging pairing, the way Android Studio does it: the PC shows a QR code
/// (WIFI:T:ADB;S:name;P:password;;), the phone scans it in "Pair device with QR code" and advertises
/// _adb-tls-pairing._tcp over mDNS under that name, then the PC runs "adb pair" against it.
/// </summary>
public sealed partial class OrbitCore
{
    public MirrorSetupState MirrorSetup { get; } = new();
    /// <summary>Wireless pairing finished; the setup window hands over to the mirror window.</summary>
    public event Action? MirrorSetupDone;
    CancellationTokenSource? _pairCts;

    public bool HasUsbMirrorDevice => UsbMirrorSerial() != null;

    public void BeginMirrorSetup()
    {
        _pairCts?.Cancel();
        _pairCts = new CancellationTokenSource();
        var name = "orbit-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(3)).ToLowerInvariant();
        var password = RandomNumberGenerator.GetInt32(100_000_000, 999_999_999).ToString();
        MirrorSetup.Paired = false;
        MirrorSetup.Qr = Qr.Render($"WIFI:T:ADB;S:{name};P:{password};;");
        MirrorSetup.Status = $"Waiting for {PhoneName}…";
        _link?.Post("orbit.mirror.prepare");
        var ct = _pairCts.Token;
        _ = Task.Run(() => PairLoopAsync(name, password, ct));
    }

    public void EndMirrorSetup() => _pairCts?.Cancel();

    /// <summary>"Open on phone": the phone shows its setup screen (or a notification to open it).</summary>
    public void OpenMirrorSetupOnPhone() => _link?.Post("orbit.mirror.setup");

    void OnMirrorInfo(JsonObject b) => Ui.Run(() =>
    {
        MirrorSetup.Known = true;
        MirrorSetup.DevOptions = b["devOptions"]?.GetValue<bool>() ?? false;
        MirrorSetup.WirelessDebugging = b["wirelessDebugging"]?.GetValue<bool>() ?? false;
        MirrorSetup.Wifi = b["wifi"]?.GetValue<bool>() ?? false;
    });

    async Task PairLoopAsync(string name, string password, CancellationToken ct)
    {
        try
        {
            var (_, check) = await Adb.RunAsync("mdns check", 8000);
            Log.Write("mirror setup: " + check);
            while (!ct.IsCancellationRequested)
            {
                var pairing = (await MdnsServicesAsync()).FirstOrDefault(s => s.type.StartsWith("_adb-tls-pairing") && s.name == name);
                if (pairing.endpoint != null)
                {
                    Ui.Run(() => MirrorSetup.Status = "Pairing…");
                    var (_, o) = await Adb.RunAsync($"pair {pairing.endpoint} {password}", 20000);
                    Log.Write("mirror setup: adb pair -> " + o);
                    if (o.Contains("Successfully paired"))
                    {
                        Settings.MirrorWirelessPaired = true;
                        Settings.Save();
                        Ui.Run(() => MirrorSetup.Status = "Paired. Connecting…");
                        await ConnectWirelessAsync(pairing.endpoint.Split(':')[0], ct);
                        Ui.Run(() =>
                        {
                            MirrorSetup.Paired = true;
                            MirrorSetup.Status = "Paired";
                            MirrorSetupDone?.Invoke();
                        });
                        return;
                    }
                    Ui.Run(() => MirrorSetup.Status = "Pairing didn't work. Scan the code again.");
                    await Task.Delay(4000, ct);
                }
                await Task.Delay(1000, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log.Write("mirror setup: " + ex.Message); }
    }

    /// <summary>Connects adb to the phone's wireless debugging (advertised as _adb-tls-connect._tcp).</summary>
    async Task<bool> ConnectWirelessAsync(string? host, CancellationToken ct)
    {
        for (int i = 0; i < 8 && !ct.IsCancellationRequested; i++)
        {
            foreach (var s in await MdnsServicesAsync())
            {
                if (!s.type.StartsWith("_adb-tls-connect") || s.endpoint == null) continue;
                if (host != null && !s.endpoint.StartsWith(host + ":")) continue;
                var (_, o) = await Adb.RunAsync($"connect {s.endpoint}", 10000);
                Log.Write($"mirror: adb connect {s.endpoint} -> {o}");
                if (o.Contains("connected to")) return true;
            }
            await Task.Delay(1000, ct);
        }
        return false;
    }

    /// <summary>Parses "adb mdns services": name, type and ip:port per line.</summary>
    async Task<List<(string name, string type, string? endpoint)>> MdnsServicesAsync()
    {
        var (_, o) = await Adb.RunAsync("mdns services", 8000);
        var list = new List<(string, string, string?)>();
        foreach (var line in o.Split('\n'))
        {
            var parts = line.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3 && parts[1].StartsWith("_adb")) list.Add((parts[0], parts[1].TrimEnd('.'), parts[^1]));
        }
        return list;
    }

    /// <summary>Lets the phone app turn wireless debugging back on after a reboot or Wi-Fi change.</summary>
    async Task GrantSecureSettingsAsync(string serial)
    {
        var (_, o) = await Adb.ShellAsync(serial, $"pm grant {Adb.PhonePackage} android.permission.WRITE_SECURE_SETTINGS");
        if (o.Length > 0) Log.Write("mirror: pm grant -> " + o);
    }
}
