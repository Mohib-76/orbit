using System.Text.Json.Nodes;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace Orbit.Core.Buds;

/// <summary>
/// This PC's side of the earbuds, for any brand: whether Windows has them connected, the battery level
/// Windows reports, and (through an <see cref="IBudsDriver"/>, for buds Orbit knows) the controls.
/// Opens the control channel only while Windows has the buds connected, and never pages the buds itself,
/// so it can't steal them from the phone.
/// </summary>
public sealed class BudsController : IBudsHost
{
    // DEVPKEY_Bluetooth_Battery: set by Windows on the buds' Hands-Free node from the headset's battery reports.
    const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";

    public BudsSnapshot State { get; private set; } = new();
    public event Action? Changed;

    readonly object _stateLock = new();
    readonly SemaphoreSlim _connectLock = new(1, 1);
    BluetoothDevice? _device;
    IBudsDriver? _driver;
    bool _matched; // looked for a driver since the buds last connected
    bool _enabled;
    string? _mac;
    CancellationTokenSource? _cts;

    public static async Task<List<(string name, string mac, bool connected, bool headset)>> PairedDevicesAsync()
    {
        var list = new List<(string, string, bool, bool)>();
        try
        {
            var infos = await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true));
            foreach (var info in infos)
            {
                using var d = await BluetoothDevice.FromIdAsync(info.Id);
                if (d == null) continue;
                list.Add((d.Name, MacOf(d.BluetoothAddress), d.ConnectionStatus == BluetoothConnectionStatus.Connected, IsHeadset(d)));
            }
        }
        catch (Exception ex) { Log.Write("buds: list paired: " + ex.Message); }
        return list.OrderByDescending(x => x.Item3).ThenByDescending(x => x.Item4).ToList();
    }

    /// <summary>Earbuds or headphones by their Bluetooth class (or, failing that, their name).</summary>
    static bool IsHeadset(BluetoothDevice d) =>
        // Minor class values repeat across major classes (AudioVideoWearableHeadset == ComputerDesktop), so check both.
        d.ClassOfDevice.MajorClass == BluetoothMajorClass.AudioVideo
            && d.ClassOfDevice.MinorClass is BluetoothMinorClass.AudioVideoWearableHeadset or BluetoothMinorClass.AudioVideoHeadphones
        || d.Name.Contains("buds", StringComparison.OrdinalIgnoreCase);

    public static string MacOf(ulong a) => string.Join(":", Enumerable.Range(0, 6).Select(i => ((a >> (8 * (5 - i))) & 0xFF).ToString("X2")));
    static ulong AddressOf(string mac) => Convert.ToUInt64(mac.Replace(":", ""), 16);

    /// <summary>Start (or restart) following the given buds.</summary>
    public void Start(string mac, string? name)
    {
        Stop();
        _enabled = true;
        _mac = mac;
        _matched = false;
        Update(_ => new BudsSnapshot { Mac = mac, Name = name ?? "Earbuds" });
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => WatchLoop(_cts.Token));
    }

    public void Stop()
    {
        _enabled = false;
        _cts?.Cancel();
        if (_device != null) { _device.ConnectionStatusChanged -= OnStatus; _device.Dispose(); _device = null; }
        _driver?.Close();
        _driver = null;
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
                        Update(s => s with { Name = _device.Name });
                    }
                }
                await RefreshOsAsync();
                if (_device?.ConnectionStatus == BluetoothConnectionStatus.Connected && _driver?.IsOpen != true)
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
            _ = Task.Delay(2000).ContinueWith(async _ => { await RefreshOsAsync(); await ConnectAsync(); });
        else
        {
            _driver?.Close();
            _matched = false; // the buds may offer other services next time (e.g. after a firmware update)
            Update(s => s with { Connected = false, BatteryMain = -1 });
        }
    }

    /// <summary>Connection and battery as Windows sees them.</summary>
    async Task RefreshOsAsync()
    {
        bool connected = _device?.ConnectionStatus == BluetoothConnectionStatus.Connected;
        int level = connected && _mac != null ? await OsBatteryAsync(_mac) : -1;
        Update(s => s with { Connected = connected || s.Control, BatteryMain = level });
    }

    static async Task<int> OsBatteryAsync(string mac)
    {
        try
        {
            // The buds' service nodes carry the address in their instance id; the Hands-Free one has the level.
            var aqs = $"System.Devices.DeviceInstanceId:~~\"{mac.Replace(":", "")}\"";
            foreach (var d in await DeviceInformation.FindAllAsync(aqs, new[] { BatteryKey }, DeviceInformationKind.Device))
                if (d.Properties.TryGetValue(BatteryKey, out var v) && v is byte b && b <= 100) return b;
        }
        catch (Exception ex) { Log.Write("buds: battery level: " + ex.Message); }
        return -1;
    }

    async Task ConnectAsync()
    {
        if (!_enabled || _device == null || _driver?.IsOpen == true) return;
        if (!await _connectLock.WaitAsync(0)) return;
        try
        {
            if (!_matched)
            {
                _driver = await BudsDrivers.ForDeviceAsync(_device, this);
                _matched = true;
                Log.Write(_driver != null ? $"buds: {_driver.Id} driver for {_device.Name}" : $"buds: no driver for {_device.Name}; battery and playback only");
                Update(s => s with { Driver = _driver?.Id });
            }
            if (_driver == null || !await _driver.OpenAsync(_device)) return;
            Update(s => s with { Control = true, Connected = true, Name = _device.Name, Mac = MacOf(_device.BluetoothAddress) });
            await _driver.RefreshAsync();
        }
        catch (Exception ex) { Log.Write("buds connect: " + ex.Message); }
        finally { _connectLock.Release(); }
    }

    /// <summary>Runs an <c>orbit.buds.cmd</c> op on the buds, if we hold their controls.</summary>
    public void Execute(JsonObject op)
    {
        if (_driver is not { IsOpen: true } d) return;
        Update(s => s.Apply(op)); // optimistic; the buds' answer corrects it
        d.Execute(op);
    }

    // ---------------------------------------------------------------- IBudsHost

    public void Update(Func<BudsSnapshot, BudsSnapshot> change)
    {
        lock (_stateLock)
        {
            var next = change(State);
            if (next == State) return; // e.g. the periodic battery read found nothing new
            State = next;
        }
        Changed?.Invoke();
    }

    public void Closed(IBudsDriver driver)
    {
        if (driver == _driver) Update(s => s with { Control = false });
    }
}
