using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json.Nodes;

namespace Orbit.Core;

/// <summary>The three USB modes and how they map to Android's USB functions.</summary>
public static class UsbModes
{
    public const string Charging = "charging", Tethering = "tethering", Files = "files", Other = "other";
    public static readonly string[] All = { Charging, Tethering, Files };

    /// <summary>A valid setting value; anything unknown means the default, tethering.</summary>
    public static string Normalize(string? mode) => mode is Charging or Files ? mode : Tethering;

    /// <summary>From "svc usb getFunctions" output ("rndis", "mtp", "none", "mtp,adb"…).</summary>
    public static string FromFunctions(string functions)
    {
        var f = functions.Trim().ToLowerInvariant();
        if (f.Contains("rndis") || f.Contains("ncm")) return Tethering;
        if (f.Contains("mtp")) return Files;
        if (f.Length == 0 || f.Contains("none") || f == "adb") return Charging;
        return Other;
    }

    /// <summary>The argument for "svc usb setFunctions" (empty = charging only).</summary>
    public static string ToFunction(string mode) => mode switch { Tethering => "rndis", Files => "mtp", _ => "" };

    public static string Label(string mode) => mode switch
    {
        Charging => "Charging only", Files => "File sharing", Tethering => "USB tethering", _ => "Other"
    };
}

/// <summary>
/// USB modes: what the phone's USB port does while it is plugged into this PC (charging only, USB tethering
/// or file sharing). The PC switches it over adb; the phone reports its own view and can ask for a switch.
/// </summary>
public sealed partial class OrbitCore
{
    /// <summary>The phone's own report: is a USB cable in, and what mode its port is in.</summary>
    (bool cable, string mode)? _phoneUsb;
    /// <summary>The mode adb last read, while this PC sees the phone over USB.</summary>
    string? _adbUsbMode;
    readonly Dictionary<string, DateTime> _usbGoneAt = new();
    readonly HashSet<string> _usbPresent = new();
    readonly HashSet<string> _usbNewPlug = new();

    /// <summary>The mode being switched to, or null.</summary>
    public string? UsbSwitchingTo { get; private set; }
    /// <summary>Raised on the UI thread when anything USB-mode related changes.</summary>
    public event Action? UsbChanged;

    /// <summary>A phone cable is in: the phone says so, or this PC sees it over USB adb.</summary>
    public bool UsbCable => _phoneUsb?.cable == true || UsbSerial() != null;
    /// <summary>This PC can switch modes (USB debugging authorized over the cable).</summary>
    public bool UsbCanSwitch => UsbSerial() != null;
    public bool UsbMirroring => _mirror != null && Mirror.Phase is MirrorPhase.Connecting or MirrorPhase.Live && Mirror.Transport == "USB";
    /// <summary>Current mode: charging | tethering | files | other.</summary>
    public string UsbMode => UsbSwitchingTo ?? _adbUsbMode ?? _phoneUsb?.mode ?? UsbModes.Tethering;

    /// <summary>The adb serial of a phone plugged in by cable (wireless debugging doesn't count).</summary>
    string? UsbSerial()
    {
        lock (Adb.Devices) return Adb.Devices.FirstOrDefault(kv => kv.Value == "device" && !IsWireless(kv.Key)).Key;
    }

    /// <summary>True when this PC's internet comes through the phone's USB tethering right now.</summary>
    public static bool OnlineThroughUsbTethering()
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || InterfaceKind(ni) != "USB tethering") continue;
                if (ni.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork)) return true;
            }
        }
        catch { }
        return false;
    }

    void RaiseUsb() => Ui.Run(() => UsbChanged?.Invoke());

    /// <summary>Notes plug-ins: a phone counts as newly plugged in after being gone from adb for 15 s.</summary>
    void TrackUsbPresence()
    {
        List<string> ready;
        lock (Adb.Devices) ready = Adb.Devices.Where(kv => kv.Value == "device" && !IsWireless(kv.Key)).Select(kv => kv.Key).ToList();
        lock (_usbPresent)
        {
            var now = DateTime.Now;
            foreach (var s in ready.Where(s => !_usbPresent.Contains(s)))
            {
                if (!_usbGoneAt.TryGetValue(s, out var gone) || now - gone > TimeSpan.FromSeconds(15)) _usbNewPlug.Add(s);
                _usbPresent.Add(s);
            }
            foreach (var s in _usbPresent.Where(s => !ready.Contains(s)).ToList())
            {
                _usbPresent.Remove(s);
                _usbGoneAt[s] = now;
            }
            if (ready.Count == 0) _adbUsbMode = null;
        }
        SendUsbControl();
        RaiseUsb();
    }

    /// <summary>Called when an authorized phone appears on USB: read its mode, and apply the default once per plug-in.</summary>
    async Task OnUsbOnlineAsync(string serial)
    {
        if (IsWireless(serial)) return;
        bool newPlug;
        lock (_usbPresent) newPlug = _usbNewPlug.Remove(serial);
        var target = UsbModes.Normalize(Settings.UsbMode);
        var current = await ReadUsbModeAsync(serial);
        // Never switch blind: if the mode can't be read, leave the port alone.
        if (!newPlug || current == null || UsbSwitchingTo != null || current == target) return;
        Log.Write($"usb: plugged in, applying {target} (was {current})");
        await SwitchUsbAsync(serial, target, announce: true);
    }

    async Task<string?> ReadUsbModeAsync(string serial)
    {
        var (code, o) = await Adb.ShellAsync(serial, "svc usb getFunctions", 5000);
        if (code != 0 || o.Contains("error", StringComparison.OrdinalIgnoreCase) || o.Contains("not found", StringComparison.OrdinalIgnoreCase)) return null;
        _adbUsbMode = UsbModes.FromFunctions(o);
        SendUsbControl();
        RaiseUsb();
        return _adbUsbMode;
    }

    /// <summary>Why the user can't switch right now: "" | "mirroring" | "debugOff" | "switching".</summary>
    public string UsbBlockedReason =>
        UsbSwitchingTo != null ? "switching" : UsbMirroring ? "mirroring" : !UsbCanSwitch ? "debugOff" : "";

    /// <summary>The user picked a mode (Home, tray or the phone). Returns false if it couldn't start.</summary>
    public async Task<bool> SetUsbModeAsync(string mode, bool fromPhone = false)
    {
        mode = UsbModes.Normalize(mode);
        var serial = UsbSerial();
        if (UsbSwitchingTo != null || mode == UsbMode && UsbSwitchingTo == null) return false;
        if (UsbMirroring) { RaiseUsb(); return false; }
        if (serial == null)
        {
            Toasts.UsbSwitchFailed();
            RaiseUsb();
            return false;
        }
        await SwitchUsbAsync(serial, mode, announce: fromPhone);
        return true;
    }

    async Task SwitchUsbAsync(string serial, string mode, bool announce)
    {
        UsbSwitchingTo = mode;
        SendUsbControl();
        RaiseUsb();
        string? result = null;
        try
        {
            var (code, o) = await Adb.ShellAsync(serial, ("svc usb setFunctions " + UsbModes.ToFunction(mode)).TrimEnd(), 8000);
            Log.Write($"usb: setFunctions {UsbModes.ToFunction(mode)} -> {code} {o}");
            // USB reconnects after a switch, so adb drops for a moment. Wait for it to report the new mode.
            var until = DateTime.Now.AddSeconds(12);
            while (DateTime.Now < until)
            {
                await Task.Delay(600);
                var s = UsbSerial();
                if (s == null) continue;
                var (c2, f) = await Adb.ShellAsync(s, "svc usb getFunctions", 4000);
                if (c2 != 0) continue;
                result = UsbModes.FromFunctions(f);
                if (result == mode) break;
            }
        }
        catch (Exception ex) { Log.Write("usb switch: " + ex.Message); }
        finally
        {
            UsbSwitchingTo = null;
            if (result != null) _adbUsbMode = result;
            SendUsbControl();
            RaiseUsb();
        }
        if (result != mode)
        {
            Log.Write($"usb: switch to {mode} didn't take (now {result ?? "unknown"})");
            Toasts.UsbSwitchFailed();
        }
        else if (mode == UsbModes.Files && announce) Toasts.FileSharingOn(Phone.Name);
        else if (mode == UsbModes.Tethering && announce) Toasts.Simple("USB tethering on", "Your laptop is now using your phone's internet.");
    }

    void SendUsbControl() => _link?.Post("orbit.usb.control", new JsonObject
    {
        ["adb"] = UsbCanSwitch,
        ["switching"] = UsbSwitchingTo ?? "",
        ["mirroring"] = UsbMirroring
    });

    void OnPhoneUsbState(JsonObject b)
    {
        _phoneUsb = (b["cable"]?.GetValue<bool>() ?? false, b["mode"]?.GetValue<string>() ?? UsbModes.Other);
        // The phone's view is the freshest (it also sees switches made from Android's own USB menu).
        if (_phoneUsb.Value.cable && UsbSerial() != null) _adbUsbMode = _phoneUsb.Value.mode;
        RaiseUsb();
    }

    void OnUsbPeerGone()
    {
        _phoneUsb = null;
        RaiseUsb();
    }

    public void SetUsbDefault(string mode)
    {
        Settings.UsbMode = UsbModes.Normalize(mode);
        Settings.Save();
        SendSettings();
        RaiseUsb();
    }

    /// <summary>Opens the phone in File Explorer (falls back to This PC).</summary>
    public void OpenPhoneInExplorer()
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType != null)
            {
                dynamic shell = Activator.CreateInstance(shellType)!;
                dynamic thisPc = shell.NameSpace(17);
                var names = new[] { Phone.Name, Phone.Model }.Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
                foreach (dynamic item in thisPc.Items())
                {
                    string name = item.Name;
                    if (names.Any(n => name.Contains(n, StringComparison.OrdinalIgnoreCase) || n.Contains(name, StringComparison.OrdinalIgnoreCase)))
                    {
                        item.InvokeVerb();
                        return;
                    }
                }
            }
        }
        catch (Exception ex) { Log.Write("open phone folder: " + ex.Message); }
        System.Diagnostics.Process.Start("explorer.exe", "shell:MyComputerFolder");
    }
}
