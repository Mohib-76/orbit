using System.IO;
using System.Text.Json.Nodes;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;
using Windows.Networking.Sockets;

namespace Orbit.Core.Buds;

/// <summary>Everything we know about the buds at one moment. Immutable; replaced on every change.</summary>
public sealed record BudsSnapshot
{
    public bool Connected { get; init; }
    public string Name { get; init; } = "Earbuds";
    public string? Mac { get; init; }
    public AncMode? Anc { get; init; }
    public int CycleMask { get; init; }
    public int[] Battery { get; init; } = { -1, -1, -1 };
    public bool[] Charging { get; init; } = new bool[3];
    public Dictionary<int, bool> Features { get; init; } = new();
    public Dictionary<(int side, int gesture), int> Touch { get; init; } = new();

    public string BatterySummary
    {
        get
        {
            var parts = new List<string>();
            if (Battery[0] >= 0) parts.Add($"L {Battery[0]}");
            if (Battery[1] >= 0) parts.Add($"R {Battery[1]}");
            return string.Join(" · ", parts);
        }
    }

    public JsonObject ToJson()
    {
        var feats = new JsonObject();
        foreach (var (id, on) in Features) feats[id.ToString()] = on;
        var touch = new JsonArray();
        foreach (var ((side, gesture), action) in Touch) touch.Add(new JsonArray(side, gesture, action));
        return new JsonObject
        {
            ["connected"] = Connected, ["name"] = Name, ["mac"] = Mac,
            ["anc"] = Anc?.WireName(), ["cycle"] = CycleMask,
            ["battery"] = new JsonArray(Battery.Select(b => (JsonNode?)b).ToArray()),
            ["charging"] = new JsonArray(Charging.Select(b => (JsonNode?)b).ToArray()),
            ["features"] = feats, ["touch"] = touch
        };
    }

    public static BudsSnapshot FromJson(JsonObject o)
    {
        var feats = new Dictionary<int, bool>();
        if (o["features"] is JsonObject fo)
            foreach (var (k, v) in fo) if (int.TryParse(k, out var id) && v != null) feats[id] = v.GetValue<bool>();
        var touch = new Dictionary<(int, int), int>();
        if (o["touch"] is JsonArray ta)
            foreach (var t in ta.OfType<JsonArray>())
                touch[(t[0]!.GetValue<int>(), t[1]!.GetValue<int>())] = t[2]!.GetValue<int>();
        int[] bat = { -1, -1, -1 };
        bool[] chg = new bool[3];
        if (o["battery"] is JsonArray ba) for (int i = 0; i < 3 && i < ba.Count; i++) bat[i] = ba[i]?.GetValue<int>() ?? -1;
        if (o["charging"] is JsonArray ca) for (int i = 0; i < 3 && i < ca.Count; i++) chg[i] = ca[i]?.GetValue<bool>() ?? false;
        return new BudsSnapshot
        {
            Connected = o["connected"]?.GetValue<bool>() ?? false,
            Name = o["name"]?.GetValue<string>() ?? "Earbuds",
            Mac = o["mac"]?.GetValue<string>(),
            Anc = AncModes.FromWireName(o["anc"]?.GetValue<string>()),
            CycleMask = o["cycle"]?.GetValue<int>() ?? 0,
            Battery = bat, Charging = chg, Features = feats, Touch = touch
        };
    }
}

/// <summary>
/// This PC's own RFCOMM link to the buds (Windows.Devices.Bluetooth). Opens the control channel whenever
/// Windows reports the buds connected, and never pages the buds itself, so it can't steal them from the phone.
/// </summary>
public sealed class BudsController
{
    const int ResponseTimeoutMs = 1000;

    public BudsSnapshot State { get; private set; } = new();
    public event Action? Changed;

    BluetoothDevice? _device;
    StreamSocket? _socket;
    Stream? _out;
    int _seq;
    readonly SemaphoreSlim _sendLock = new(1, 1);
    TaskCompletionSource<bool>? _awaitTcs;
    int _awaiting = -1;
    bool _enabled;
    string? _mac;
    CancellationTokenSource? _cts;

    public static async Task<List<(string name, string mac, bool connected)>> PairedDevicesAsync()
    {
        var list = new List<(string, string, bool)>();
        try
        {
            var infos = await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true));
            foreach (var info in infos)
            {
                using var d = await BluetoothDevice.FromIdAsync(info.Id);
                if (d == null) continue;
                list.Add((d.Name, MacOf(d.BluetoothAddress), d.ConnectionStatus == BluetoothConnectionStatus.Connected));
            }
        }
        catch (Exception ex) { Log.Write("buds: list paired: " + ex.Message); }
        return list.OrderByDescending(x => x.Item3).ThenByDescending(x => x.Item1.Contains("buds", StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public static string MacOf(ulong a) => string.Join(":", Enumerable.Range(0, 6).Select(i => ((a >> (8 * (5 - i))) & 0xFF).ToString("X2")));
    static ulong AddressOf(string mac) => Convert.ToUInt64(mac.Replace(":", ""), 16);

    /// <summary>Start (or restart) following the given buds.</summary>
    public void Start(string mac, string? name)
    {
        Stop();
        _enabled = true;
        _mac = mac;
        Set(new BudsSnapshot { Mac = mac, Name = name ?? "Earbuds" });
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => WatchLoop(_cts.Token));
    }

    public void Stop()
    {
        _enabled = false;
        _cts?.Cancel();
        if (_device != null) { _device.ConnectionStatusChanged -= OnStatus; _device.Dispose(); _device = null; }
        CloseSocket();
    }

    async Task WatchLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_device == null && _mac != null)
                {
                    _device = await BluetoothDevice.FromBluetoothAddressAsync(AddressOf(_mac));
                    if (_device != null)
                    {
                        _device.ConnectionStatusChanged += OnStatus;
                        Set(State with { Name = _device.Name });
                    }
                }
                if (_device?.ConnectionStatus == BluetoothConnectionStatus.Connected && _socket == null)
                    await ConnectAsync();
            }
            catch (Exception ex) { Log.Write("buds watch: " + ex.Message); }
            try { await Task.Delay(TimeSpan.FromSeconds(10), ct); } catch { }
        }
    }

    void OnStatus(BluetoothDevice d, object _)
    {
        if (d.ConnectionStatus == BluetoothConnectionStatus.Connected)
            // Give the buds a moment to bring up their profiles before opening RFCOMM.
            _ = Task.Delay(2000).ContinueWith(_ => ConnectAsync());
        else CloseSocket();
    }

    readonly SemaphoreSlim _connectLock = new(1, 1);

    async Task ConnectAsync()
    {
        if (!_enabled || _device == null || _socket != null) return;
        if (!await _connectLock.WaitAsync(0)) return;
        try
        {
            // Try the cached service record first (fast); if it's missing or stale (the buds can move their
            // RFCOMM channel, e.g. after a firmware update or re-pairing), ask the buds again (SDP).
            var attempts = new[] { BudsProtocol.UuidVendor, BudsProtocol.UuidSpp }
                .SelectMany(u => new[] { (uuid: u, mode: BluetoothCacheMode.Cached), (uuid: u, mode: BluetoothCacheMode.Uncached) });
            foreach (var (uuid, mode) in attempts)
            {
                var res = await _device.GetRfcommServicesForIdAsync(RfcommServiceId.FromUuid(uuid), mode);
                if (res.Services.Count == 0)
                {
                    if (mode == BluetoothCacheMode.Uncached) Log.Write($"buds: no {uuid} service on the buds ({res.Error})");
                    continue;
                }
                var svc = res.Services[0];
                var sock = new StreamSocket();
                try
                {
                    await sock.ConnectAsync(svc.ConnectionHostName, svc.ConnectionServiceName,
                        SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication);
                }
                catch (Exception ex)
                {
                    Log.Write($"buds: rfcomm {uuid} ({mode}) failed: 0x{ex.HResult:X8} {ex.Message}".TrimEnd());
                    sock.Dispose();
                    continue;
                }
                _socket = sock;
                _out = sock.OutputStream.AsStreamForWrite(0);
                _seq = 0;
                Log.Write($"buds: connected to {_device.Name} via {uuid}");
                Set(State with { Connected = true, Name = _device.Name, Mac = MacOf(_device.BluetoothAddress) });
                _ = Task.Run(() => ReadLoop(sock));
                await Probe();
                return;
            }
        }
        catch (Exception ex) { Log.Write("buds connect: " + ex.Message); }
        finally { _connectLock.Release(); }
    }

    async Task Probe()
    {
        await Send(BudsProtocol.AncReq, BudsProtocol.AncRequest(BudsProtocol.AncTypeMode));
        await Send(BudsProtocol.BatteryReq, Array.Empty<byte>());
        await Send(BudsProtocol.SubscriptionSet, BudsProtocol.Subscribe());
        await Send(BudsProtocol.TouchReq, BudsProtocol.TouchRequest());
        await Send(BudsProtocol.AncReq, BudsProtocol.AncRequest(BudsProtocol.AncTypeCycle));
        // One request per feature: unsupported ids get no answer and simply stay hidden.
        foreach (var f in Feature.All) await Send(BudsProtocol.MiscReq, BudsProtocol.MiscRequest(f.Id));
    }

    async Task ReadLoop(StreamSocket sock)
    {
        var parser = new BudsProtocol.Parser();
        var buf = new byte[1024];
        try
        {
            using var input = sock.InputStream.AsStreamForRead(0);
            while (true)
            {
                int n = await input.ReadAsync(buf);
                if (n <= 0) break;
                foreach (var (cmd, payload) in parser.Feed(buf.AsSpan(0, n)))
                {
                    if (cmd == _awaiting) _awaitTcs?.TrySetResult(true);
                    Handle(cmd, payload);
                }
            }
        }
        catch (Exception ex) { Log.Write("buds read: " + ex.Message); }
        if (_socket == sock) CloseSocket();
    }

    void CloseSocket()
    {
        var s = _socket;
        if (s == null) return;
        _socket = null; _out = null;
        try { s.Dispose(); } catch { }
        _awaitTcs?.TrySetResult(false);
        Set(State with { Connected = false });
        Log.Write("buds: disconnected");
    }

    /// <summary>One command at a time; wait (bounded) for its response.</summary>
    async Task Send(int cmd, byte[] payload)
    {
        await _sendLock.WaitAsync();
        try
        {
            var o = _out;
            if (o == null) return;
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _awaitTcs = tcs;
            _awaiting = cmd | BudsProtocol.ResponseBit;
            try
            {
                await o.WriteAsync(BudsProtocol.Encode(cmd, _seq++, payload));
                await o.FlushAsync();
            }
            catch (Exception ex) { Log.Write("buds write: " + ex.Message); CloseSocket(); return; }
            await Task.WhenAny(tcs.Task, Task.Delay(ResponseTimeoutMs));
            _awaiting = -1;
        }
        finally { _sendLock.Release(); }
    }

    // ---------------------------------------------------------------- commands

    public void SetAnc(AncMode m)
    {
        if (State.Connected) Set(State with { Anc = m }); // optimistic; the ACK / push corrects it
        _ = Send(BudsProtocol.AncSet, BudsProtocol.AncSetting(BudsProtocol.AncTypeMode, (int)m));
    }

    public void SetCycle(int mask)
    {
        Set(State with { CycleMask = mask });
        _ = Task.Run(async () =>
        {
            await Send(BudsProtocol.AncSet, BudsProtocol.AncSetting(BudsProtocol.AncTypeCycle, mask));
            await Send(BudsProtocol.AncReq, BudsProtocol.AncRequest(BudsProtocol.AncTypeCycle));
        });
    }

    public void SetFeature(int id, bool on)
    {
        Set(State with { Features = new(State.Features) { [id] = on } });
        _ = Task.Run(async () =>
        {
            await Send(BudsProtocol.MiscSet, BudsProtocol.MiscSetting(id, on));
            await Send(BudsProtocol.MiscReq, BudsProtocol.MiscRequest(id));
        });
    }

    public void SetTouch(int side, int gesture, int action)
    {
        Set(State with { Touch = new(State.Touch) { [(side, gesture)] = action } });
        _ = Task.Run(async () =>
        {
            await Send(BudsProtocol.TouchSet, BudsProtocol.TouchSetting(side, gesture, action));
            await Send(BudsProtocol.TouchReq, BudsProtocol.TouchRequest());
        });
    }

    // ---------------------------------------------------------------- incoming

    void Handle(int cmd, byte[] p)
    {
        switch (cmd)
        {
            case BudsProtocol.AncRet: ApplyAnc(p); break;
            case BudsProtocol.BatteryRet: ApplyBattery(p); break;
            case BudsProtocol.MiscRet:
                var known = BudsProtocol.ParseMisc(p).Where(kv => Feature.All.Any(f => f.Id == kv.Key)).ToList();
                if (known.Count > 0)
                {
                    var feats = new Dictionary<int, bool>(State.Features);
                    foreach (var (id, on) in known) feats[id] = on;
                    Set(State with { Features = feats });
                }
                break;
            case BudsProtocol.TouchRet:
                Log.Write("buds: touch config " + Convert.ToHexString(p));
                var t = BudsProtocol.ParseTouch(p);
                if (t.Count > 0)
                {
                    var touch = new Dictionary<(int, int), int>(State.Touch);
                    foreach (var kv in t) touch[kv.Key] = kv.Value;
                    Set(State with { Touch = touch });
                }
                break;
            case BudsProtocol.SubscriptionRet:
                switch (p.Length > 0 ? p[0] : -1)
                {
                    case BudsProtocol.SubAnc: ApplyAnc(p); break;
                    case BudsProtocol.SubBattery: ApplyBattery(p); break;
                    case BudsProtocol.SubGameMode when p.Length >= 2:
                        Set(State with { Features = new(State.Features) { [Feature.GameModeId] = p[1] == 1 } });
                        break;
                }
                break;
            case BudsProtocol.AncAck:
                if (p.Length > 0 && p[0] != 0)
                    _ = Send(BudsProtocol.AncReq, BudsProtocol.AncRequest(BudsProtocol.AncTypeMode));
                break;
        }
    }

    void ApplyAnc(byte[] p)
    {
        if (BudsProtocol.ParseAnc(p) is not { } r) return;
        var (type, value) = r;
        if (type == BudsProtocol.AncTypeMode && AncModes.FromCode(value) is { } m) Set(State with { Anc = m });
        else if (type == BudsProtocol.AncTypeCycle) Set(State with { CycleMask = value });
    }

    void ApplyBattery(byte[] p)
    {
        var (levels, charging) = BudsProtocol.ParseBattery(p);
        Set(State with { Battery = levels, Charging = charging });
    }

    void Set(BudsSnapshot s)
    {
        State = s;
        Changed?.Invoke();
    }
}
