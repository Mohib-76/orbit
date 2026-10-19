using System.Text.Json.Nodes;
using Windows.Devices.Bluetooth;

namespace Orbit.Core.Buds;

/// <summary>
/// One brand's control protocol (noise modes, settings, touch). Everything else (connection, Windows'
/// battery level, playback, syncing with the phone) is the same for all buds and lives in
/// <see cref="BudsController"/>.
/// <para>To add a brand: implement this, add it to <see cref="BudsDrivers.ForDeviceAsync"/>, and port it
/// to the Android app (<c>buds/BudsDriver.kt</c>). A driver on only one side still works on both: the other
/// side relays its commands (<c>orbit.buds.cmd</c>).</para>
/// </summary>
public interface IBudsDriver
{
    /// <summary>Stable id, sent to the phone in <c>orbit.buds.state</c> ("heymelody").</summary>
    string Id { get; }
    bool IsOpen { get; }

    /// <summary>Opens the control channel. Only called while Windows has the buds connected.</summary>
    Task<bool> OpenAsync(BluetoothDevice device);

    /// <summary>Asks the buds for everything (battery, modes, settings) after <see cref="OpenAsync"/>.</summary>
    Task RefreshAsync();

    void Close();

    /// <summary>Runs one <c>orbit.buds.cmd</c> op (fire and forget).</summary>
    void Execute(JsonObject op);
}

/// <summary>What a driver reports to.</summary>
public interface IBudsHost
{
    void Update(Func<BudsSnapshot, BudsSnapshot> change);

    /// <summary>The control channel dropped.</summary>
    void Closed(IBudsDriver driver);
}

public static class BudsDrivers
{
    /// <summary>The driver for these buds, or null if Orbit has none for them (battery and playback only).</summary>
    public static async Task<IBudsDriver?> ForDeviceAsync(BluetoothDevice device, IBudsHost host) =>
        await HeyMelodyDriver.MatchesAsync(device) ? new HeyMelodyDriver(host) : null;
}
