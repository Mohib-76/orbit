using System.Text.Json.Nodes;
using Orbit.Core.Buds;

namespace Orbit.Core;

/// <summary>
/// Earbuds. This PC talks to the buds directly when Windows has them connected; otherwise it shows the
/// phone's view and relays commands through the phone. Both sides publish their own view over Orbit.
/// </summary>
public sealed partial class OrbitCore
{
    public BudsController BudsLocal { get; } = new();
    /// <summary>The phone's report: is the feature on there, its own view of the buds, and is it playing.</summary>
    public (bool enabled, BudsSnapshot state, bool playing)? BudsPeer { get; private set; }
    public bool PlayingHere { get; private set; }

    /// <summary>Raised (on the UI thread) whenever anything buds-related changes.</summary>
    public event Action? BudsChanged;

    public bool BudsEnabled => Settings.BudsEnabled;

    /// <summary>Whoever holds the buds' controls, else whoever has them connected (ours first).</summary>
    public BudsSnapshot Buds
    {
        get
        {
            var l = BudsLocal.State;
            var p = BudsPeer?.state;
            if (l.Control) return l;
            if (p is { Control: true }) return p;
            if (l.Connected || p is not { Connected: true }) return l;
            return p;
        }
    }

    /// <summary>The phone holds the buds' controls and we don't, so commands go through it.</summary>
    public bool BudsViaPhone => !BudsLocal.State.Control && BudsPeer is { state.Control: true };

    void StartBuds()
    {
        BudsLocal.Changed += () => { Ui.Run(() => BudsChanged?.Invoke()); SchedulePublishBuds(); };
        StartMedia();
        if (Settings.BudsEnabled) _ = EnableBudsAsync();
    }

    public async Task EnableBudsAsync()
    {
        if (Settings.BudsMac == null)
        {
            // Prefer what the phone uses, else the connected earbuds, else the only paired ones.
            var paired = await BudsController.PairedDevicesAsync();
            var phoneMac = BudsPeer?.state.Mac;
            var pick = paired.FirstOrDefault(d => phoneMac != null && d.mac.Equals(phoneMac, StringComparison.OrdinalIgnoreCase));
            if (pick.mac == null)
            {
                var buds = paired.Where(d => d.headset).ToList();
                var connected = buds.Where(d => d.connected).ToList();
                if (connected.Count == 1) pick = connected[0];
                else if (buds.Count == 1) pick = buds[0];
            }
            if (pick.mac == null) { Ui.Run(() => BudsChanged?.Invoke()); return; }
            Settings.BudsMac = pick.mac; Settings.BudsName = pick.name; Settings.Save();
        }
        BudsLocal.Start(Settings.BudsMac, Settings.BudsName);
    }

    public void SetBudsEnabled(bool on, bool fromPeer = false)
    {
        Settings.BudsEnabled = on;
        Settings.Save();
        if (on) _ = EnableBudsAsync(); else BudsLocal.Stop();
        if (!fromPeer) SendSettings();
        SchedulePublishBuds();
        Ui.Run(() => { Raise(nameof(BudsEnabled)); BudsChanged?.Invoke(); });
    }

    public void SelectBuds(string mac, string name)
    {
        Settings.BudsMac = mac; Settings.BudsName = name; Settings.Save();
        if (Settings.BudsEnabled) BudsLocal.Start(mac, name);
    }

    // ---------------------------------------------------------------- commands (routed)

    public void BudsSetAnc(string mode) => Route(new JsonObject { ["op"] = "anc", ["mode"] = mode });
    public void BudsCycleAnc() { if (Buds.NextNoise() is { } m) BudsSetAnc(m); }
    // Numeric ids go as numbers: older phones read them with getInt().
    public void BudsSetFeature(string id, bool on) =>
        Route(new JsonObject { ["op"] = "feature", ["id"] = int.TryParse(id, out var n) ? JsonValue.Create(n) : JsonValue.Create(id), ["on"] = on });
    public void BudsSetCycle(int mask) => Route(new JsonObject { ["op"] = "cycle", ["mask"] = mask });
    public void BudsSetTouch(int side, int gesture, int action) =>
        Route(new JsonObject { ["op"] = "touch", ["side"] = side, ["gesture"] = gesture, ["action"] = action });

    /// <summary>Run it here if we hold the controls; else ask the phone if it does.</summary>
    void Route(JsonObject op)
    {
        if (!Settings.BudsEnabled) return;
        if (BudsLocal.State.Control || _link == null || BudsPeer is not { state.Control: true } p) { BudsLocal.Execute(op); return; }
        BudsPeer = (p.enabled, p.state.Apply(op), p.playing);
        _link.Post("orbit.buds.cmd", op);
        Ui.Run(() => BudsChanged?.Invoke());
    }

    void OnBudsCommand(JsonObject op)
    {
        if (BudsLocal.State.Control) BudsLocal.Execute(op);
    }

    void OnBudsPeerState(JsonObject b)
    {
        BudsPeer = (b["enabled"]?.GetValue<bool>() ?? false, BudsSnapshot.FromJson(b), b["playing"]?.GetValue<bool>() ?? false);
        if (Settings.BudsEnabled && Settings.BudsMac == null && BudsPeer.Value.state.Mac != null) _ = EnableBudsAsync();
        Ui.Run(() => BudsChanged?.Invoke());
    }

    void OnBudsPeerGone()
    {
        BudsPeer = null;
        Ui.Run(() => BudsChanged?.Invoke());
    }

    // ---------------------------------------------------------------- publishing our view to the phone

    CancellationTokenSource? _publishDebounce;

    void SchedulePublishBuds()
    {
        _publishDebounce?.Cancel();
        var cts = _publishDebounce = new CancellationTokenSource();
        _ = Task.Delay(150, cts.Token).ContinueWith(t => { if (!t.IsCanceled) PublishBuds(); }, TaskScheduler.Default);
    }

    void PublishBuds()
    {
        var j = BudsLocal.State.ToJson();
        j["enabled"] = Settings.BudsEnabled;
        j["playing"] = PlayingHere;
        _link?.Post("orbit.buds.state", j);
    }

    // ---------------------------------------------------------------- media ("Play here")

    Task MediaAsync(bool play) => LocalMedia.CommandAsync(null, play ? "play" : "pause");

    /// <summary>Moves playback to this PC (true) or the phone (false). The buds follow whichever device plays.</summary>
    public void PlayOn(bool pc)
    {
        _link?.Post("orbit.media", new JsonObject { ["action"] = pc ? "pause" : "play" });
        _ = MediaAsync(pc);
    }
}
