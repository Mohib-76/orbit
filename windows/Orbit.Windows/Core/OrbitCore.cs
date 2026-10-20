using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows.Media.Imaging;

namespace Orbit.Core;

public sealed partial class OrbitCore : Observable
{
    public static OrbitCore Instance { get; } = new();

    public Settings Settings { get; } = Settings.Load();
    public PhoneState Phone { get; } = new();
    public Adb Adb { get; } = new();
    public ObservableCollection<ClipItem> ClipHistory { get; } = new();
    public ObservableCollection<PhoneNotification> Notifications { get; } = new();
    public ObservableCollection<Transfer> Transfers { get; } = new();

    readonly CancellationTokenSource _cts = new();
    volatile PhoneLink? _link;
    string? _pairToken;
    string? _usbSerial;          // adb device that has Orbit installed and is forwarded
    bool _usbForwardActive;

    // ---------- observable bits for the UI ----------
    string _pairQrPayload = "";
    BitmapSource? _pairQr;
    string _usbStatus = "";
    bool _canPairOverUsb;

    public string PairQrPayload { get => _pairQrPayload; private set => Set(ref _pairQrPayload, value); }
    public BitmapSource? PairQr { get => _pairQr; private set => Set(ref _pairQr, value); }
    public string UsbStatus { get => _usbStatus; private set => Set(ref _usbStatus, value); }
    public bool CanPairOverUsb { get => _canPairOverUsb; private set => Set(ref _canPairOverUsb, value); }
    public bool DoNotDisturb => Settings.DoNotDisturbUntil is { } until && until > DateTime.Now;
    public string DoNotDisturbText => DoNotDisturb ? $"Paused until {Settings.DoNotDisturbUntil:HH:mm}" : "Mirroring to this PC";
    public int UnreadCount => Notifications.Count;

    public event Action<PhoneNotification>? NotificationArrived;
    public event Action<Transfer>? TransferFinished;

    OrbitCore()
    {
        Notifications.CollectionChanged += (_, _) => Raise(nameof(UnreadCount));
    }

    public void Start()
    {
        if (Settings.Phone is { } p)
        {
            Phone.Name = p.Name;
            Phone.Model = p.Model;
            Phone.State = LinkState.Searching;
        }
        else BeginPairing();

        Adb.DevicesChanged += () => { TrackUsbPresence(); _ = RefreshUsbAsync(); };
        Adb.DeviceOnline += serial => _ = OnUsbDeviceOnlineAsync(serial);
        Adb.Start();
        UpdateUsbStatus();

        WatchSessionLock();
        _ = Task.Run(ConnectLoop);
        _ = Task.Run(TickLoop);
        StartBuds();
        NetworkChange.NetworkAddressChanged += (_, _) => _kick.Release();
    }

    public void Stop()
    {
        _cts.Cancel();
        _link?.Dispose();
        Adb.Stop();
    }

    readonly SemaphoreSlim _kick = new(0, 64);

    async Task TickLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(20));
            Ui.Run(() =>
            {
                Phone.Tick();
                foreach (var n in Notifications) n.Tick();
                Raise(nameof(DoNotDisturb)); Raise(nameof(DoNotDisturbText));
            });
            if (_link is { } l) l.Post("orbit.ping");
        }
    }

    // =====================================================================
    // Pairing
    // =====================================================================

    public void BeginPairing()
    {
        _pairToken = Proto.Nonce();
        var payload = new JsonObject
        {
            ["v"] = 1, ["id"] = Settings.DeviceId, ["name"] = Settings.DeviceName,
            ["fp"] = Identity.Fingerprint, ["token"] = _pairToken
        }.ToJsonString();
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        PairQrPayload = "orbit://pair?d=" + b64;
        PairQr = Qr.Render(PairQrPayload);
        Ui.Run(() => { if (Settings.Phone == null) Phone.State = LinkState.Pairing; });
        _kick.Release();
    }

    /// <summary>Pair without the camera: hand the QR payload straight to the phone app over adb.</summary>
    public async Task<string> PairOverUsbAsync()
    {
        var serial = Adb.FirstReadySerial();
        if (serial == null) return "Connect your phone with USB debugging on.";
        if (!await Adb.HasOrbitAsync(serial)) return "Install Orbit on your phone first.";
        if (_pairToken == null) BeginPairing();
        await Adb.RunAsync($"-s {serial} forward tcp:{Proto.UsbForwardPort} tcp:{Proto.PhonePort}");
        _usbSerial = serial; _usbForwardActive = true;
        var (code, o) = await Adb.ShellAsync(serial,
            $"am start -n {Adb.PhonePackage}/.MainActivity -a {Adb.PhonePackage}.PAIR --es payload \"{PairQrPayload}\"");
        _kick.Release();
        return code == 0 && !o.Contains("Error") ? "Confirm on your phone…" : "Could not open Orbit on the phone: " + o;
    }

    public void Unpair()
    {
        _link?.Post("orbit.unpair");
        _link?.Dispose();
        Settings.Phone = null;
        Settings.Save();
        Ui.Run(() =>
        {
            Phone.Name = ""; Phone.Model = ""; Phone.Battery = -1; Phone.Link = "";
            Notifications.Clear();
        });
        BeginPairing();
    }

    public void RenamePhone(string name)
    {
        if (Settings.Phone == null || string.IsNullOrWhiteSpace(name)) return;
        Settings.Phone.Name = name.Trim();
        Settings.Save();
        Phone.Name = Settings.Phone.Name;
    }

    // =====================================================================
    // Finding and connecting to the phone
    // =====================================================================

    record Candidate(string Host, int Port, string Kind);

    async Task ConnectLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                if (_link == null && (Settings.Phone != null || _pairToken != null))
                {
                    var link = await FindPhoneAsync(_cts.Token);
                    if (link != null) { await RunSessionAsync(link); continue; }
                }
            }
            catch (Exception ex) { Log.Write("connect loop: " + ex.Message); }
            await _kick.WaitAsync(1500).ConfigureAwait(false);
        }
    }

    async Task<PhoneLink?> FindPhoneAsync(CancellationToken ct)
    {
        var candidates = new List<Candidate>();
        if (_usbForwardActive) candidates.Add(new("127.0.0.1", Proto.UsbForwardPort, "USB cable"));

        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            var kind = InterfaceKind(ni);
            foreach (var gw in ni.GetIPProperties().GatewayAddresses)
                if (gw.Address.AddressFamily == AddressFamily.InterNetwork && !gw.Address.Equals(IPAddress.Any))
                    candidates.Add(new(gw.Address.ToString(), Proto.PhonePort, kind));
        }
        // A manual address for networks where neither the gateway trick nor broadcast works (VPNs, guest Wi-Fi…).
        if (ParseManualAddress(Settings.ManualAddress) is { } manual && candidates.All(c => c.Host != manual.host))
            candidates.Add(new(manual.host, manual.port, "Wi-Fi"));
        if (Settings.Phone?.LastIp is { } last && candidates.All(c => c.Host != last))
            candidates.Add(new(last, Proto.PhonePort, "Wi-Fi"));

        using var race = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var tasks = candidates.Select(c => TryCandidateAsync(c, race.Token)).ToList();
        tasks.Add(DiscoverAsync(race.Token, candidates));

        PhoneLink? winner = null;
        while (tasks.Count > 0)
        {
            var t = await Task.WhenAny(tasks);
            tasks.Remove(t);
            var link = await t;
            if (link == null) continue;
            if (winner == null) { winner = link; race.Cancel(); }
            else link.Dispose();
        }
        return winner;
    }

    /// <summary>UDP broadcast for when phone and PC share a router (neither is the other's gateway).</summary>
    async Task<PhoneLink?> DiscoverAsync(CancellationToken ct, List<Candidate> tried)
    {
        try
        {
            using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0)) { EnableBroadcast = true };
            var probe = Encoding.UTF8.GetBytes(Proto.Encode("orbit.discover", new JsonObject { ["id"] = Settings.DeviceId }));
            foreach (var bc in BroadcastAddresses())
                try { await udp.SendAsync(probe, new IPEndPoint(bc, Proto.DiscoveryPort)); } catch { }

            using var window = CancellationTokenSource.CreateLinkedTokenSource(ct);
            window.CancelAfter(900);
            while (true)
            {
                var r = await udp.ReceiveAsync(window.Token);
                var (type, body) = Proto.Decode(Encoding.UTF8.GetString(r.Buffer));
                if (type != "orbit.identity") continue;
                var id = body["id"]?.GetValue<string>();
                if (Settings.Phone != null && id != Settings.Phone.Id) continue;
                var host = r.RemoteEndPoint.Address.ToString();
                if (tried.Any(c => c.Host == host)) continue;
                var port = body["port"]?.GetValue<int>() ?? Proto.PhonePort;
                return await TryCandidateAsync(new(host, port, "Wi-Fi"), ct);
            }
        }
        catch { return null; }
    }

    static IEnumerable<IPAddress> BroadcastAddresses()
    {
        yield return IPAddress.Broadcast;
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            foreach (var ua in ni.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork || ua.IPv4Mask == null) continue;
                var ip = ua.Address.GetAddressBytes();
                var mask = ua.IPv4Mask.GetAddressBytes();
                if (ip[0] == 169 && ip[1] == 254) continue;
                var bc = new byte[4];
                for (int i = 0; i < 4; i++) bc[i] = (byte)(ip[i] | ~mask[i]);
                yield return new IPAddress(bc);
            }
        }
    }

    /// <summary>"192.168.1.20" or "192.168.1.20:47800" (also host names). Null when empty or invalid.</summary>
    public static (string host, int port)? ParseManualAddress(string? s)
    {
        s = s?.Trim();
        if (string.IsNullOrEmpty(s)) return null;
        int port = Proto.PhonePort;
        var colon = s.LastIndexOf(':');
        if (colon > 0 && s.IndexOf(':') == colon)
        {
            if (!int.TryParse(s[(colon + 1)..], out port) || port is < 1 or > 65535) return null;
            s = s[..colon];
        }
        return Uri.CheckHostName(s) == UriHostNameType.Unknown ? null : (s, port);
    }

    static string InterfaceKind(NetworkInterface ni)
    {
        if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) return "Phone hotspot";
        var d = (ni.Description + " " + ni.Name).ToUpperInvariant();
        if (d.Contains("NDIS") || d.Contains("NCM") || d.Contains("USB") || d.Contains("ANDROID")) return "USB tethering";
        return "Network";
    }

    async Task<PhoneLink?> TryCandidateAsync(Candidate c, CancellationToken ct)
    {
        var paired = Settings.Phone;
        var token = _pairToken;
        if (paired == null && token == null) return null;
        PhoneLink? link = null;
        try
        {
            var (tcp, ssl, fp) = await Proto.DialAsync(c.Host, c.Port,
                fp => paired != null ? fp == paired.Fingerprint : token != null, 1500, ct);
            link = new PhoneLink(tcp, ssl, c.Host, c.Port, c.Kind);
            using var hs = CancellationTokenSource.CreateLinkedTokenSource(ct);
            hs.CancelAfter(8000);

            if (paired != null)
            {
                await link.SendAsync("orbit.hello", Proto.WithVersion(new JsonObject { ["id"] = Settings.DeviceId, ["name"] = Settings.DeviceName }));
                var r = await link.ReadAsync(hs.Token);
                if (r == null || r.Value.type != "orbit.hello" || r.Value.body["id"]?.GetValue<string>() != paired.Id)
                    throw new Exception("bad hello");
                var model = r.Value.body["model"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(model)) paired.Model = model;
                CheckPeerVersion(r.Value.body);
            }
            else
            {
                var nonce = Proto.Nonce();
                await link.SendAsync("orbit.pair", Proto.WithVersion(new JsonObject
                {
                    ["id"] = Settings.DeviceId, ["name"] = Settings.DeviceName, ["nonce"] = nonce
                }));
                var r = await link.ReadAsync(hs.Token);
                if (r == null || r.Value.type != "orbit.pair.proof") throw new Exception("pair rejected");
                var b = r.Value.body;
                CheckPeerVersion(b);
                var expected = Proto.Hmac(token!, "phone", nonce, fp, Identity.Fingerprint);
                if (b["proof"]?.GetValue<string>() != expected) throw new Exception("bad pairing proof");
                var proof = Proto.Hmac(token!, "pc", b["nonce"]!.GetValue<string>(), fp, Identity.Fingerprint);
                await link.SendAsync("orbit.pair.ok", new JsonObject { ["proof"] = proof });

                Settings.Phone = new PairedPhone
                {
                    Id = b["id"]!.GetValue<string>(),
                    Name = b["name"]?.GetValue<string>() ?? "Phone",
                    Model = b["model"]?.GetValue<string>() ?? "",
                    Fingerprint = fp
                };
                _pairToken = null;
                Log.Write($"paired with {Settings.Phone.Name} via {c.Kind}");
            }
            return link;
        }
        catch (Exception ex)
        {
            // Refused / nothing listening / EOF are the normal "phone isn't there" cases.
            if (ex is not OperationCanceledException and not SocketException and not IOException)
                Log.Write($"candidate {c.Host}:{c.Port} ({c.Kind}): {ex.Message}");
            link?.Dispose();
            return null;
        }
    }

    string _versionWarning = "";
    /// <summary>Empty when both apps speak the same protocol; otherwise what the user should update.</summary>
    public string VersionWarning { get => _versionWarning; private set => Set(ref _versionWarning, value); }
    public string PeerAppVersion { get; private set; } = "";

    void CheckPeerVersion(JsonObject b)
    {
        // Phones from before the version handshake send nothing: treat them as protocol 1.
        int proto = b["proto"]?.GetValue<int>() ?? 1;
        PeerAppVersion = b["app"]?.GetValue<string>() ?? "";
        var warning = proto < Proto.Version ? "Orbit on your phone is older than on this PC. Update it so every feature works."
            : proto > Proto.Version ? "Orbit on this PC is older than on your phone. Update it so every feature works."
            : "";
        Ui.Run(() =>
        {
            if (warning.Length > 0 && warning != VersionWarning) Toasts.Simple("Update Orbit", warning);
            VersionWarning = warning;
        });
    }

    // =====================================================================
    // A live session
    // =====================================================================

    async Task RunSessionAsync(PhoneLink link)
    {
        var phone = Settings.Phone!;
        if (link.Host != "127.0.0.1") phone.LastIp = link.Host;
        Settings.Save();
        _link = link;
        Log.Write($"connected to {phone.Name} at {link.Host} ({link.Kind})");
        Ui.Run(() =>
        {
            Phone.Name = phone.Name; Phone.Model = phone.Model;
            Phone.Link = link.Kind; Phone.ConnectedSince = DateTime.Now;
            Phone.State = LinkState.Connected;
        });

        SendSettings();
        ResumeTransfers();
        PublishBuds();
        SendMirrorState();
        OnMediaConnected();

        try
        {
            while (!_cts.IsCancellationRequested)
            {
                using var idle = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                idle.CancelAfter(TimeSpan.FromSeconds(45));
                var p = await link.ReadAsync(idle.Token);
                if (p == null) break;
                try { Handle(link, p.Value.type, p.Value.body); }
                catch (Exception ex) { Log.Write($"handle {p.Value.type}: {ex.Message}"); }
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
        catch (Exception ex) { Log.Write("session: " + ex.Message); }
        finally
        {
            link.Dispose();
            _link = null;
            OnBudsPeerGone();
            OnMediaPeerGone();
            OnUsbPeerGone();
            Log.Write("disconnected");
            Ui.Run(() =>
            {
                Phone.State = Settings.Phone != null ? LinkState.Searching : LinkState.Pairing;
                foreach (var t in Transfers.Where(t => t.State == TransferState.Running)) t.State = TransferState.Paused;
            });
        }
    }

    void Handle(PhoneLink link, string type, JsonObject b)
    {
        switch (type)
        {
            case "orbit.ping": link.Post("orbit.pong"); break;
            case "orbit.pong": case "": break;
            case "orbit.battery":
                Ui.Run(() => OnBattery(b["level"]?.GetValue<int>() ?? -1, b["charging"]?.GetValue<bool>() ?? false,
                    b["chargeTimeMs"]?.GetValue<long>() ?? -1));
                break;
            case "orbit.clipboard":
                OnRemoteClipboard(b["text"]?.GetValue<string>() ?? "");
                break;
            case "orbit.notification": OnNotification(b); break;
            case "orbit.notification.removed":
                var key = b["key"]?.GetValue<string>() ?? "";
                Ui.Run(() => RemoveNotificationLocal(key));
                break;
            case "orbit.file.offer": OnFileOffer(b); break;
            case "orbit.file.accept": OnFileAccept(b); break;
            case "orbit.file.done": OnFileDone(b); break;
            case "orbit.file.cancel": OnFileCancel(b); break;
            case "orbit.buds.state": OnBudsPeerState(b); break;
            case "orbit.buds.cmd": OnBudsCommand(b); break;
            case "orbit.media": _ = MediaAsync(b["action"]?.GetValue<string>() == "play"); break;
            case "orbit.media.state": OnPhoneMedia(b); break;
            case "orbit.media.art": OnPhoneMediaArt(b); break;
            case "orbit.media.cmd": OnMediaCommand(b); break;
            case "orbit.usb.state": OnPhoneUsbState(b); break;
            case "orbit.usb.set": _ = SetUsbModeAsync(b["mode"]?.GetValue<string>() ?? "", fromPhone: true); break;
            case "orbit.mirror.cmd": OnMirrorCommand(b); break;
            case "orbit.mirror.info": OnMirrorInfo(b); break;
            case "orbit.settings":
                if (b["budsEnabled"] is { } be && be.GetValue<bool>() != Settings.BudsEnabled)
                    SetBudsEnabled(be.GetValue<bool>(), fromPeer: true);
                // Older phones only send autoUsbTethering.
                var usbMode = b["usbMode"]?.GetValue<string>()
                    ?? (b["autoUsbTethering"] is { } aut ? (aut.GetValue<bool>() ? UsbModes.Tethering : UsbModes.Charging) : null);
                if (usbMode != null && UsbModes.Normalize(usbMode) != Settings.UsbMode)
                {
                    Settings.UsbMode = UsbModes.Normalize(usbMode);
                    Settings.Save();
                    Ui.Run(() => Raise(nameof(Settings)));
                    RaiseUsb();
                }
                if (b["mediaWanted"] is { } mw) OnPeerMediaWanted(mw.GetValue<bool>());
                break;
            case "orbit.unpair":
                Settings.Phone = null; Settings.Save();
                link.Dispose();
                Ui.Run(() => { Phone.Name = ""; Notifications.Clear(); });
                BeginPairing();
                break;
            default: Log.Write("unknown packet " + type); break;
        }
    }

    public void SendSettings() => _link?.Post("orbit.settings", new JsonObject
    {
        ["autoUsbTethering"] = Settings.UsbMode == UsbModes.Tethering,
        ["usbMode"] = Settings.UsbMode,
        ["pcName"] = Settings.DeviceName,
        ["clipboardAutoSync"] = Settings.ClipboardAutoSync,
        ["budsEnabled"] = Settings.BudsEnabled,
        ["mediaWanted"] = Settings.MediaShowPhone
    });

    // =====================================================================
    // Battery
    // =====================================================================

    bool _lowWarned;
    void OnBattery(int level, bool charging, long chargeMs)
    {
        Phone.Battery = level;
        Phone.Charging = charging;
        Phone.ChargeInfo = charging && chargeMs > 0
            ? (chargeMs < 3_600_000 ? $"full in {chargeMs / 60000} min" : $"full in {chargeMs / 3_600_000} h {(chargeMs / 60000) % 60} min")
            : "";
        int threshold = Settings.LowBatteryThreshold;
        if (threshold > 0 && !charging && level >= 0 && level <= threshold && !_lowWarned)
        {
            _lowWarned = true;
            Toasts.LowBattery(Phone.Name, level);
        }
        if (charging || level > threshold + 5) _lowWarned = false;
    }

    // =====================================================================
    // USB / adb
    // =====================================================================

    async Task RefreshUsbAsync()
    {
        var serial = Adb.FirstReadySerial();
        if (serial == null)
        {
            _usbSerial = null; _usbForwardActive = false;
            await RefreshPhoneAppAsync(null);
        }
        Ui.Run(UpdateUsbStatus);
        if (serial != null && _usbSerial == null) await OnUsbDeviceOnlineAsync(serial);
    }

    async Task OnUsbDeviceOnlineAsync(string serial)
    {
        try
        {
            var hasOrbit = await Adb.HasOrbitAsync(serial);
            if (hasOrbit && Settings.UseUsbLink)
            {
                var (code, _) = await Adb.RunAsync($"-s {serial} forward tcp:{Proto.UsbForwardPort} tcp:{Proto.PhonePort}");
                if (code == 0) { _usbSerial = serial; _usbForwardActive = true; _kick.Release(); }
            }
            Ui.Run(() => { CanPairOverUsb = hasOrbit; UpdateUsbStatus(); });
            await RefreshPhoneAppAsync(serial);
            await OnUsbOnlineAsync(serial);
        }
        catch (Exception ex) { Log.Write("usb online: " + ex.Message); }
    }

    void UpdateUsbStatus()
    {
        if (!Adb.Available) { UsbStatus = "adb not found — USB features off"; CanPairOverUsb = false; return; }
        var serial = Adb.FirstReadySerial();
        if (serial != null) UsbStatus = "Phone on USB · debugging authorized";
        else if (Adb.AnyUnauthorized()) UsbStatus = "Phone on USB · tap Allow on the debugging prompt";
        else UsbStatus = "No phone on USB";
        if (serial == null) CanPairOverUsb = false;
        Raise(nameof(CanInstallPhoneApp));
    }

    // =====================================================================
    // Clipboard
    // =====================================================================

    string? _lastRemoteClip;

    public void OnLocalClipboard(string text, bool sensitive)
    {
        if (string.IsNullOrEmpty(text) || text == _lastRemoteClip) return;
        if (!Settings.ClipboardAutoSync || _link == null) return;
        // Password managers mark their copies private; huge clips are almost never meant for the phone.
        if (sensitive && Settings.ClipboardSkipSensitive) return;
        if (text.Length > ClipboardHost.MaxClipChars) return;
        SendClipboardText(text);
    }

    // =====================================================================
    // Session lock (notification privacy)
    // =====================================================================

    volatile bool _sessionLocked;

    void WatchSessionLock() =>
        Microsoft.Win32.SystemEvents.SessionSwitch += (_, e) =>
        {
            if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionLock) _sessionLocked = true;
            else if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionUnlock) _sessionLocked = false;
        };

    public bool SendClipboardText(string text)
    {
        if (_link == null || string.IsNullOrEmpty(text)) return false;
        _lastRemoteClip = text;
        _link.Post("orbit.clipboard", new JsonObject { ["text"] = text });
        Ui.Run(() => AddClip(new ClipItem { Text = text, FromPhone = false }));
        return true;
    }

    void OnRemoteClipboard(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        _lastRemoteClip = text;
        Ui.Run(() =>
        {
            ClipboardHost.SetText(text);
            AddClip(new ClipItem { Text = text, FromPhone = true });
        });
    }

    void AddClip(ClipItem c)
    {
        var dup = ClipHistory.FirstOrDefault(x => x.Text == c.Text);
        if (dup != null) ClipHistory.Remove(dup);
        ClipHistory.Insert(0, c);
        int max = Settings.ClipboardKeepHistory ? 10 : 1;
        while (ClipHistory.Count > max) ClipHistory.RemoveAt(ClipHistory.Count - 1);
        Raise(nameof(ClipHistory));
    }

    // =====================================================================
    // Notifications
    // =====================================================================

    void OnNotification(JsonObject b)
    {
        var pkg = b["pkg"]?.GetValue<string>() ?? "";
        string? iconPath = null;
        if (b["icon"]?.GetValue<string>() is { Length: > 0 } icon)
        {
            iconPath = Path.Combine(Paths.Icons, string.Concat(pkg.Split(Path.GetInvalidFileNameChars())) + ".png");
            try { File.WriteAllBytes(iconPath, Convert.FromBase64String(icon)); } catch { iconPath = null; }
        }
        else
        {
            var p = Path.Combine(Paths.Icons, pkg + ".png");
            if (File.Exists(p)) iconPath = p;
        }

        Ui.Run(() =>
        {
            BitmapImage? img = null;
            if (iconPath != null)
            {
                try
                {
                    img = new BitmapImage();
                    img.BeginInit(); img.CacheOption = BitmapCacheOption.OnLoad;
                    img.UriSource = new Uri(iconPath); img.EndInit(); img.Freeze();
                }
                catch { img = null; }
            }
            var n = new PhoneNotification
            {
                Key = b["key"]?.GetValue<string>() ?? "",
                App = b["app"]?.GetValue<string>() ?? pkg,
                Package = pkg,
                Title = b["title"]?.GetValue<string>() ?? "",
                Text = b["text"]?.GetValue<string>() ?? "",
                Time = b["time"]?.GetValue<long>() is long ms and > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime : DateTime.Now,
                IconPath = iconPath, Icon = img
            };
            var existing = Notifications.FirstOrDefault(x => x.Key == n.Key);
            if (existing != null) Notifications.Remove(existing);
            Notifications.Insert(0, n);
            while (Notifications.Count > 100) Notifications.RemoveAt(Notifications.Count - 1);

            bool silent = b["silent"]?.GetValue<bool>() ?? false;
            if (Settings.NotificationsEnabled && !DoNotDisturb && !silent && (Settings.NotificationsWhenLocked || !_sessionLocked))
                Toasts.Show(n, Phone.Name, Settings.NotificationsHideContent);
            NotificationArrived?.Invoke(n);
        });
    }

    public void DismissNotification(string key)
    {
        _link?.Post("orbit.notification.dismiss", new JsonObject { ["key"] = key });
        Ui.Run(() => RemoveNotificationLocal(key));
    }

    public void ClearNotifications()
    {
        foreach (var n in Notifications.ToList())
            _link?.Post("orbit.notification.dismiss", new JsonObject { ["key"] = n.Key });
        Notifications.Clear();
        Toasts.ClearAll();
    }

    void RemoveNotificationLocal(string key)
    {
        var n = Notifications.FirstOrDefault(x => x.Key == key);
        if (n != null) Notifications.Remove(n);
        Toasts.Remove(key);
    }

    public void PauseNotifications(TimeSpan? span)
    {
        Settings.DoNotDisturbUntil = span is { } s ? DateTime.Now + s : null;
        Settings.Save();
        Raise(nameof(DoNotDisturb)); Raise(nameof(DoNotDisturbText));
    }
}
