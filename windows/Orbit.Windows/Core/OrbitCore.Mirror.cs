using System.Text.Json.Nodes;
using Orbit.Core.Mirror;

namespace Orbit.Core;

public enum MirrorPhase { Idle, Connecting, Live, Lost }

/// <summary>What the mirror window shows. Mutated on the UI thread only.</summary>
public sealed class MirrorState : Observable
{
    MirrorPhase _phase;
    int _step;
    string _message = "", _stats = "", _transport = "";
    bool _viewOnly, _usbAvailable, _needsSetup;
    string? _camera;

    public MirrorPhase Phase { get => _phase; set => Set(ref _phase, value); }
    /// <summary>Connecting checklist: steps completed so far (0–3).</summary>
    public int Step { get => _step; set => Set(ref _step, value); }
    /// <summary>Why the stream is paused (shown under "Stream paused").</summary>
    public string Message { get => _message; set => Set(ref _message, value); }
    /// <summary>"1080p · 60 fps · 34 ms · USB"</summary>
    public string Stats { get => _stats; set => Set(ref _stats, value); }
    public string Transport { get => _transport; set => Set(ref _transport, value); }
    /// <summary>Set from the phone's notification: the PC must not send input.</summary>
    public bool ViewOnly { get => _viewOnly; set => Set(ref _viewOnly, value); }
    public bool UsbAvailable { get => _usbAvailable; set => Set(ref _usbAvailable, value); }
    /// <summary>No phone reachable and wireless mirroring was never set up.</summary>
    public bool NeedsSetup { get => _needsSetup; set => Set(ref _needsSetup, value); }
    /// <summary>The running stream shows this camera ("back" / "front") instead of the screen; null = the screen.</summary>
    public string? Camera { get => _camera; set => Set(ref _camera, value); }
}

public sealed partial class OrbitCore
{
    public MirrorState Mirror { get; } = new();
    public MirrorSession? MirrorSession => _mirror;
    /// <summary>Raised when the phone asks the PC to show the mirror (Quick Settings tile).</summary>
    public event Action? MirrorRequested;
    /// <summary>The phone switched between screen and camera (the stream panel shows it).</summary>
    public event Action? MirrorSourceChanged;
    /// <summary>A new stream is being set up (UI thread, before it starts); attach to its frames here.</summary>
    public event Action<MirrorSession>? MirrorSessionStarted;

    MirrorSession? _mirror;
    bool _mirrorWanted;          // the mirror window is open and wants a stream
    bool _mirrorPreferUsb;
    bool _cameraUnsupported;    // camera picked on a phone older than Android 12
    int _mirrorAttempt;
    DateTime _orbitLinkLostAt = DateTime.MaxValue;
    System.Windows.Threading.DispatcherTimer? _mirrorTimer;

    /// <summary>Starts (or restarts) mirroring. Call on the UI thread.</summary>
    public void StartMirror(bool preferUsb = false)
    {
        _mirrorWanted = true;
        _mirrorPreferUsb |= preferUsb;
        if (_mirrorTimer == null)
        {
            _mirrorTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _mirrorTimer.Tick += (_, _) => MirrorTick();
            Adb.DevicesChanged += () => Ui.Run(() =>
            {
                Mirror.UsbAvailable = UsbMirrorSerial() != null;
                if (_mirrorWanted && Mirror.Phase == MirrorPhase.Lost) _ = ConnectMirrorAsync(retry: true);
            });
        }
        _mirrorTimer.Start();
        Mirror.UsbAvailable = UsbMirrorSerial() != null;
        _ = ConnectMirrorAsync();
    }

    /// <summary>Stops mirroring and forgets that the window wanted it.</summary>
    public void StopMirror()
    {
        _mirrorWanted = false;
        _mirrorPreferUsb = false;
        _mirrorAttempt++; // abandon any connect still in flight
        _mirrorTimer?.Stop();
        CloseSession();
        Mirror.Phase = MirrorPhase.Idle;
        Mirror.ViewOnly = false;
        Mirror.Camera = null;
        _cameraUnsupported = false;
        Settings.MirrorSource = "screen"; // the camera is for one session; the next mirror shows the screen again
        SendMirrorState();
    }

    /// <summary>Applies stream-panel changes that need a new session (quality, frame rate, audio…).</summary>
    public void RestartMirror()
    {
        Settings.Save();
        _cameraUnsupported = false;
        if (_mirrorWanted) _ = ConnectMirrorAsync();
    }

    public void SetMirrorScreenOff(bool off)
    {
        Settings.MirrorScreenOff = off;
        Settings.Save();
        _mirror?.Send(Msg.SetDisplayPower(!off));
    }

    public void SetMirrorViewOnly(bool on)
    {
        Ui.Run(() => { Mirror.ViewOnly = on; SendMirrorState(); });
    }

    void CloseSession()
    {
        var s = _mirror;
        _mirror = null;
        s?.Stop();
    }

    /// <param name="retry">A background retry: leave the "paused" screen alone unless a phone is found.</param>
    async Task ConnectMirrorAsync(bool retry = false)
    {
        if (retry && _connecting) return;
        int attempt = ++_mirrorAttempt;
        _connecting = true;
        try { await ConnectMirrorCoreAsync(attempt, retry); }
        finally { if (attempt == _mirrorAttempt) _connecting = false; }
    }

    bool _connecting;

    async Task ConnectMirrorCoreAsync(int attempt, bool retry)
    {
        if (!retry)
        {
            CloseSession();
            Mirror.Phase = MirrorPhase.Connecting;
            Mirror.Step = 0;
        }

        if (Phone.State != LinkState.Connected)
        {
            // The phone must be able to show its "being mirrored" notice.
            if (!retry) Lost($"{PhoneName} isn't connected to Orbit. Mirroring starts when it's back.");
            return;
        }
        var dev = await FindMirrorDeviceAsync(_mirrorPreferUsb);
        if (dev == null && !_mirrorPreferUsb && Settings.MirrorWirelessPaired)
        {
            // Wireless debugging tends to be off after a reboot or a Wi-Fi change; the phone turns it
            // back on if it can, then adb finds it over mDNS.
            _link?.Post("orbit.mirror.prepare");
            if (await ConnectWirelessAsync(null, CancellationToken.None)) dev = await FindMirrorDeviceAsync(false);
        }
        if (attempt != _mirrorAttempt || !_mirrorWanted) return;
        Mirror.NeedsSetup = dev == null && !Settings.MirrorWirelessPaired;
        if (dev == null)
        {
            if (!retry || Mirror.Phase != MirrorPhase.Lost)
                Lost(Adb.AnyUnauthorized() ? "Tap \"Allow\" on the USB debugging prompt on your phone."
                    : Settings.MirrorWirelessPaired && !_mirrorPreferUsb
                        ? $"Couldn't reach {PhoneName} over Wi-Fi. Keep it on the same Wi-Fi as this PC, or plug in USB."
                        : $"Plug {PhoneName} in with USB (debugging on), or set up wireless mirroring.");
            return;
        }

        CloseSession();
        Mirror.Phase = MirrorPhase.Connecting;
        Mirror.Step = 0;
        var (serial, transport) = dev.Value;
        var options = await BuildMirrorOptionsAsync(serial, transport);
        if (attempt != _mirrorAttempt || !_mirrorWanted) return;
        if (options == null)
        {
            _cameraUnsupported = true; // don't retry every few seconds: it can't work until Show is back on Screen
            Lost($"Showing the camera needs Android 12 or later on {PhoneName}. Switch Show back to Screen.");
            return;
        }
        if (attempt != _mirrorAttempt || !_mirrorWanted) return;
        var session = new MirrorSession(Adb, serial, options);
        session.Progress += step => Ui.Run(() => { if (_mirror == session) Mirror.Step = Math.Max(Mirror.Step, step + 1); });
        session.Ended += reason => Ui.Run(() =>
        {
            if (_mirror != session) return;
            _mirror = null;
            Lost($"The stream from {PhoneName} stopped. The last frame stays until it's back.");
        });
        session.PhoneClipboard += text => { if (Settings.ClipboardAutoSync) OnRemoteClipboard(text); };
        session.Progress += step =>
        {
            if (step == 2) Ui.Run(() =>
            {
                if (_mirror != session) return;
                Mirror.Phase = MirrorPhase.Live;
                if (Settings.MirrorScreenOff && options.Camera == null) session.Send(Msg.SetDisplayPower(false));
            });
        };
        _mirror = session;
        MirrorSessionStarted?.Invoke(session);
        Mirror.Transport = transport;
        Mirror.Camera = options.Camera;
        if (options.Camera != null) Mirror.ViewOnly = false; // nothing to tap on a camera picture
        Log.Write($"mirror: starting on {serial} ({transport}) size={options.MaxSize} fps={options.MaxFps} br={options.BitRate} audio={options.Audio} source={options.Camera ?? "screen"}");
        try
        {
            await session.StartAsync();
            if (_mirror == session) SendMirrorState();
            _ = GrantSecureSettingsAsync(serial);
        }
        catch (Exception ex)
        {
            Log.Write("mirror start: " + ex.Message);
            session.Stop();
            if (_mirror != session) return;
            _mirror = null;
            Lost("Couldn't start the stream: " + ex.Message);
        }
    }

    void Lost(string message)
    {
        Mirror.Phase = MirrorPhase.Lost;
        Mirror.Message = message;
        Mirror.Stats = "";
        SendMirrorState();
    }

    string PhoneName => string.IsNullOrEmpty(Phone.Name) ? "Your phone" : Phone.Name;

    void MirrorTick()
    {
        var s = _mirror;
        if (s != null && Mirror.Phase == MirrorPhase.Live)
        {
            int shortSide = Math.Min(s.VideoWidth, s.VideoHeight);
            Mirror.Stats = $"{shortSide}p · {s.Fps:0} fps · {s.DelayMs} ms · {Mirror.Transport}";
        }

        // Never keep mirroring when the phone can't show that it's being mirrored.
        if (Phone.State == LinkState.Connected) _orbitLinkLostAt = DateTime.MaxValue;
        else if (_orbitLinkLostAt == DateTime.MaxValue) _orbitLinkLostAt = DateTime.Now;
        if (s != null && DateTime.Now - _orbitLinkLostAt > TimeSpan.FromSeconds(20))
        {
            Log.Write("mirror: Orbit link gone for 20 s, stopping the stream");
            CloseSession();
            Lost($"{PhoneName} disconnected from Orbit, so mirroring stopped.");
        }
        // Retry a lost stream every few seconds.
        if (_mirrorWanted && s == null && Mirror.Phase == MirrorPhase.Lost && Phone.State == LinkState.Connected && !_cameraUnsupported
            && DateTime.Now.Second % 4 == 0)
            _ = ConnectMirrorAsync(retry: true);
    }

    // =====================================================================
    // Which adb device, which stream settings
    // =====================================================================

    static bool IsWireless(string serial) => serial.Contains(':') || serial.Contains("._adb-tls-connect.");

    string? UsbMirrorSerial()
    {
        lock (Adb.Devices)
            return Adb.Devices.FirstOrDefault(kv => kv.Value == "device" && !IsWireless(kv.Key)).Key;
    }

    /// <summary>An adb device that is the paired phone (has Orbit, same model). USB first.</summary>
    async Task<(string serial, string transport)?> FindMirrorDeviceAsync(bool usbOnly)
    {
        List<string> ready;
        lock (Adb.Devices) ready = Adb.Devices.Where(kv => kv.Value == "device").Select(kv => kv.Key).ToList();
        foreach (var serial in ready.OrderBy(IsWireless))
        {
            if (usbOnly && IsWireless(serial)) continue;
            if (!await Adb.HasOrbitAsync(serial)) continue;
            if (!string.IsNullOrEmpty(Phone.Model))
            {
                var (_, model) = await Adb.ShellAsync(serial, "getprop ro.product.model");
                if (!string.Equals(model.Trim(), Phone.Model, StringComparison.OrdinalIgnoreCase)) continue;
            }
            return (serial, IsWireless(serial) ? "Wi-Fi" : "USB");
        }
        return null;
    }

    /// <summary>Null when the camera is wanted but the phone is older than Android 12 (scrcpy needs Camera2 there).</summary>
    async Task<MirrorOptions?> BuildMirrorOptionsAsync(string serial, string transport)
    {
        bool usb = transport == "USB";
        string? camera = Settings.MirrorSource == "camera" ? (Settings.MirrorCamera == "front" ? "front" : "back") : null;
        if (camera != null)
        {
            var (_, sdk) = await Adb.ShellAsync(serial, "getprop ro.build.version.sdk");
            if (!int.TryParse(sdk.Trim(), out var level) || level < 31) return null;
            // Cameras pick their own picture size; this caps it.
            return new MirrorOptions(usb ? 1920 : 1280, Math.Min(Settings.MirrorFps, 30), usb ? 12_000_000 : 6_000_000, false, false, false,
                Settings.MirrorHardwareDecode, camera);
        }
        int shortTarget = Settings.MirrorQuality switch { "720p" => 720, "1080p" => 1080, "Max" => 0, _ => usb ? 1080 : 720 };
        int maxSize = 0;
        if (shortTarget > 0)
        {
            // max_size limits the long side; derive it from the short side we want.
            var (_, o) = await Adb.ShellAsync(serial, "wm size");
            var m = System.Text.RegularExpressions.Regex.Match(o, @"Physical size: (\d+)x(\d+)");
            if (m.Success)
            {
                int a = int.Parse(m.Groups[1].Value), b = int.Parse(m.Groups[2].Value);
                int shortSide = Math.Min(a, b), longSide = Math.Max(a, b);
                if (shortSide > shortTarget) maxSize = (int)Math.Ceiling(longSide * (double)shortTarget / shortSide / 8) * 8;
            }
        }
        int bitRate = usb ? 16_000_000 : shortTarget is > 0 and <= 720 ? 5_000_000 : 8_000_000;
        return new MirrorOptions(maxSize, Settings.MirrorFps, bitRate, Settings.MirrorAudio, Settings.MirrorKeepAwake, Settings.MirrorShowTouches,
            Settings.MirrorHardwareDecode);
    }

    // =====================================================================
    // Telling the phone (it shows the LIVE notice and tile)
    // =====================================================================

    void SendMirrorState()
    {
        bool active = _mirror != null && Mirror.Phase is MirrorPhase.Connecting or MirrorPhase.Live;
        _link?.Post("orbit.mirror.state", new JsonObject
        {
            ["active"] = active,
            ["viewOnly"] = Mirror.ViewOnly,
            ["transport"] = Mirror.Transport,
            ["source"] = Mirror.Camera != null ? "camera" : "screen",
            ["camera"] = Mirror.Camera ?? "",
        });
        // USB mirroring blocks switching the USB mode.
        SendUsbControl();
        RaiseUsb();
    }

    void OnMirrorCommand(JsonObject b)
    {
        switch (b["op"]?.GetValue<string>())
        {
            case "stop":
                Log.Write("mirror: stopped from the phone");
                Ui.Run(() => { StopMirror(); MirrorStoppedByPhone?.Invoke(); });
                break;
            case "viewOnly":
                SetMirrorViewOnly(b["on"]?.GetValue<bool>() ?? true);
                break;
            case "start":
                // "Camera on PC" asks for the camera; the Mirror tile for the screen. Camera only when allowed.
                var camera = b["source"]?.GetValue<string>() == "camera";
                if (camera && !Settings.RemoteCamera)
                {
                    _link?.Post("orbit.remote.result", new JsonObject
                    {
                        ["op"] = "camera", ["ok"] = false, ["error"] = "Showing the phone's camera is switched off in Orbit on the PC."
                    });
                    break;
                }
                Ui.Run(() =>
                {
                    var source = camera ? "camera" : "screen";
                    if (Settings.MirrorSource != source)
                    {
                        Settings.MirrorSource = source;
                        MirrorSourceChanged?.Invoke();
                        if (_mirrorWanted) RestartMirror();
                    }
                    MirrorRequested?.Invoke();
                });
                break;
        }
    }

    /// <summary>Raised when the phone's notification stopped mirroring (the window closes).</summary>
    public event Action? MirrorStoppedByPhone;
}
