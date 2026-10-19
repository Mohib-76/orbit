using System.IO;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Networking.Sockets;

namespace Orbit.Core.Buds;

/// <summary>
/// realme / OnePlus / OPPO buds (the realme Link and HeyMelody apps' protocol) over RFCOMM.
/// Framing and commands are in <see cref="BudsProtocol"/>.
/// </summary>
public sealed class HeyMelodyDriver : IBudsDriver
{
    public const string DriverId = "heymelody";
    const int ResponseTimeoutMs = 1000;
    static readonly Regex NameHint = new("realme|oneplus|oppo|enco", RegexOptions.IgnoreCase);

    public static readonly IReadOnlyList<NoiseMode> NoiseModes =
        AncModes.All.Select(m => new NoiseMode(m.WireName(), m.Label())).ToList();
    static readonly IReadOnlyList<string> ToggleOrder = Feature.All.Select(f => f.Id.ToString()).ToList();

    readonly IBudsHost _host;
    StreamSocket? _socket;
    Stream? _out;
    int _seq;
    readonly SemaphoreSlim _sendLock = new(1, 1);
    TaskCompletionSource<bool>? _awaitTcs;
    int _awaiting = -1;

    public HeyMelodyDriver(IBudsHost host) => _host = host;

    public string Id => DriverId;
    public bool IsOpen => _socket != null;

    /// <summary>The buds look like these brands, or offer the vendor service.</summary>
    public static async Task<bool> MatchesAsync(BluetoothDevice d)
    {
        if (NameHint.IsMatch(d.Name ?? "")) return true;
        foreach (var mode in new[] { BluetoothCacheMode.Cached, BluetoothCacheMode.Uncached })
            if ((await d.GetRfcommServicesForIdAsync(RfcommServiceId.FromUuid(BudsProtocol.UuidVendor), mode)).Services.Count > 0)
                return true;
        return false;
    }

    public async Task<bool> OpenAsync(BluetoothDevice device)
    {
        if (IsOpen) return true;
        // Plain SPP only for buds that look like these brands, so other devices never get our packets.
        var uuids = NameHint.IsMatch(device.Name ?? "") ? new[] { BudsProtocol.UuidVendor, BudsProtocol.UuidSpp } : new[] { BudsProtocol.UuidVendor };
        // Try the cached service record first (fast); if it's missing or stale (the buds can move their
        // RFCOMM channel, e.g. after a firmware update or re-pairing), ask the buds again (SDP).
        var attempts = uuids.SelectMany(u => new[] { (uuid: u, mode: BluetoothCacheMode.Cached), (uuid: u, mode: BluetoothCacheMode.Uncached) });
        foreach (var (uuid, mode) in attempts)
        {
            var res = await device.GetRfcommServicesForIdAsync(RfcommServiceId.FromUuid(uuid), mode);
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
            Log.Write($"buds: connected to {device.Name} via {uuid}");
            _ = Task.Run(() => ReadLoop(sock));
            return true;
        }
        return false;
    }

    public async Task RefreshAsync()
    {
        await Send(BudsProtocol.AncReq, BudsProtocol.AncRequest(BudsProtocol.AncTypeMode));
        await Send(BudsProtocol.BatteryReq, Array.Empty<byte>());
        await Send(BudsProtocol.SubscriptionSet, BudsProtocol.Subscribe());
        await Send(BudsProtocol.TouchReq, BudsProtocol.TouchRequest());
        await Send(BudsProtocol.AncReq, BudsProtocol.AncRequest(BudsProtocol.AncTypeCycle));
        // One request per feature: unsupported ids get no answer and simply stay hidden.
        foreach (var f in Feature.All) await Send(BudsProtocol.MiscReq, BudsProtocol.MiscRequest(f.Id));
    }

    public void Close()
    {
        var s = _socket;
        if (s == null) return;
        _socket = null; _out = null;
        try { s.Dispose(); } catch { }
        _awaitTcs?.TrySetResult(false);
        Log.Write("buds: disconnected");
        _host.Closed(this);
    }

    public void Execute(JsonObject op) => _ = Task.Run(async () =>
    {
        switch (op["op"]?.GetValue<string>())
        {
            case "anc":
                if (AncModes.FromWireName(op["mode"]?.GetValue<string>()) is { } m)
                    await Send(BudsProtocol.AncSet, BudsProtocol.AncSetting(BudsProtocol.AncTypeMode, (int)m));
                break;
            case "cycle":
                await Send(BudsProtocol.AncSet, BudsProtocol.AncSetting(BudsProtocol.AncTypeCycle, op["mask"]!.GetValue<int>()));
                await Send(BudsProtocol.AncReq, BudsProtocol.AncRequest(BudsProtocol.AncTypeCycle));
                break;
            case "feature":
                if (int.TryParse(op["id"]?.ToString(), out var id))
                {
                    await Send(BudsProtocol.MiscSet, BudsProtocol.MiscSetting(id, op["on"]?.GetValue<bool>() ?? false));
                    await Send(BudsProtocol.MiscReq, BudsProtocol.MiscRequest(id));
                }
                break;
            case "touch":
                await Send(BudsProtocol.TouchSet, BudsProtocol.TouchSetting(op["side"]!.GetValue<int>(), op["gesture"]!.GetValue<int>(), op["action"]!.GetValue<int>()));
                await Send(BudsProtocol.TouchReq, BudsProtocol.TouchRequest());
                break;
        }
    });

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
        if (_socket == sock) Close();
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
            catch (Exception ex) { Log.Write("buds write: " + ex.Message); Close(); return; }
            await Task.WhenAny(tcs.Task, Task.Delay(ResponseTimeoutMs));
            _awaiting = -1;
        }
        finally { _sendLock.Release(); }
    }

    void Handle(int cmd, byte[] p)
    {
        switch (cmd)
        {
            case BudsProtocol.AncRet: ApplyAnc(p); break;
            case BudsProtocol.BatteryRet: ApplyBattery(p); break;
            case BudsProtocol.MiscRet:
                var known = BudsProtocol.ParseMisc(p)
                    .Select(kv => Feature.All.FirstOrDefault(f => f.Id == kv.Key)?.Toggle(kv.Value)).OfType<BudsToggle>().ToList();
                if (known.Count > 0) _host.Update(s => s.MergeToggles(known, ToggleOrder));
                break;
            case BudsProtocol.TouchRet:
                Log.Write("buds: touch config " + Convert.ToHexString(p));
                var t = BudsProtocol.ParseTouch(p);
                if (t.Count > 0)
                    _host.Update(s =>
                    {
                        var touch = new Dictionary<(int, int), int>(s.Touch);
                        foreach (var kv in t) touch[kv.Key] = kv.Value;
                        return s with { Touch = touch };
                    });
                break;
            case BudsProtocol.SubscriptionRet:
                switch (p.Length > 0 ? p[0] : -1)
                {
                    case BudsProtocol.SubAnc: ApplyAnc(p); break;
                    case BudsProtocol.SubBattery: ApplyBattery(p); break;
                    case BudsProtocol.SubGameMode when p.Length >= 2:
                        var game = Feature.All.First(f => f.Id == Feature.GameModeId).Toggle(p[1] == 1);
                        _host.Update(s => s.MergeToggles(new[] { game }, ToggleOrder));
                        break;
                }
                break;
            case BudsProtocol.AncAck:
                // A refused change: ask what the mode really is.
                if (p.Length > 0 && p[0] != 0)
                    _ = Send(BudsProtocol.AncReq, BudsProtocol.AncRequest(BudsProtocol.AncTypeMode));
                break;
        }
    }

    void ApplyAnc(byte[] p)
    {
        if (BudsProtocol.ParseAnc(p) is not { } r) return;
        var (type, value) = r;
        // Buds that answer have noise control; the others never show the modes.
        if (type == BudsProtocol.AncTypeMode && AncModes.FromCode(value) is { } m)
            _host.Update(s => s with { Noise = m.WireName(), NoiseModes = NoiseModes });
        else if (type == BudsProtocol.AncTypeCycle) _host.Update(s => s with { CycleMask = value });
    }

    void ApplyBattery(byte[] p)
    {
        var (levels, charging) = BudsProtocol.ParseBattery(p);
        _host.Update(s => s with { Battery = levels, Charging = charging });
    }
}
