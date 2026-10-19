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

    /// <summary>Ours if we hold the buds, else the phone's if it does.</summary>
    public BudsSnapshot Buds
    {
        get
        {
            var l = BudsLocal.State;
            return l.Connected || BudsPeer is not { state.Connected: true } p ? l : p.state;
        }
    }

    public bool BudsViaPhone => !BudsLocal.State.Connected && BudsPeer is { state.Connected: true };

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
            // Prefer what the phone uses, else the connected "…Buds…" device, else the only paired buds.
            var paired = await BudsController.PairedDevicesAsync();
            var phoneMac = BudsPeer?.state.Mac;
            var pick = paired.FirstOrDefault(d => phoneMac != null && d.mac.Equals(phoneMac, StringComparison.OrdinalIgnoreCase));
            if (pick.mac == null)
            {
                var buds = paired.Where(d => d.name.Contains("buds", StringComparison.OrdinalIgnoreCase)).ToList();
                pick = buds.FirstOrDefault(d => d.connected);
                if (pick.mac == null && buds.Count == 1) pick = buds[0];
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

    public void BudsSetAnc(AncMode m) => Route(new JsonObject { ["op"] = "anc", ["mode"] = m.WireName() }, () => BudsLocal.SetAnc(m));
    public void BudsCycleAnc() => BudsSetAnc((Buds.Anc ?? AncMode.Off).Next());
    public void BudsSetFeature(int id, bool on) => Route(new JsonObject { ["op"] = "feature", ["id"] = id, ["on"] = on }, () => BudsLocal.SetFeature(id, on));
    public void BudsSetCycle(int mask) => Route(new JsonObject { ["op"] = "cycle", ["mask"] = mask }, () => BudsLocal.SetCycle(mask));
    public void BudsSetTouch(int side, int gesture, int action) =>
        Route(new JsonObject { ["op"] = "touch", ["side"] = side, ["gesture"] = gesture, ["action"] = action }, () => BudsLocal.SetTouch(side, gesture, action));

    void Route(JsonObject op, Action local)
    {
        if (!Settings.BudsEnabled) return;
        if (BudsLocal.State.Connected || _link == null || BudsPeer is not { state.Connected: true } p) { local(); return; }
        BudsPeer = (p.enabled, Optimistic(p.state, op), p.playing);
        _link.Post("orbit.buds.cmd", op);
        Ui.Run(() => BudsChanged?.Invoke());
    }

    static BudsSnapshot Optimistic(BudsSnapshot s, JsonObject op) => op["op"]?.GetValue<string>() switch
    {
        "anc" => s with { Anc = AncModes.FromWireName(op["mode"]?.GetValue<string>()) ?? s.Anc },
        "feature" => s with { Features = new(s.Features) { [op["id"]!.GetValue<int>()] = op["on"]!.GetValue<bool>() } },
        "cycle" => s with { CycleMask = op["mask"]!.GetValue<int>() },
        "touch" => s with { Touch = new(s.Touch) { [(op["side"]!.GetValue<int>(), op["gesture"]!.GetValue<int>())] = op["action"]!.GetValue<int>() } },
        _ => s
    };

    void OnBudsCommand(JsonObject op)
    {
        if (!BudsLocal.State.Connected) return;
        switch (op["op"]?.GetValue<string>())
        {
            case "anc": if (AncModes.FromWireName(op["mode"]?.GetValue<string>()) is { } m) BudsLocal.SetAnc(m); break;
            case "feature": BudsLocal.SetFeature(op["id"]!.GetValue<int>(), op["on"]!.GetValue<bool>()); break;
            case "cycle": BudsLocal.SetCycle(op["mask"]!.GetValue<int>()); break;
            case "touch": BudsLocal.SetTouch(op["side"]!.GetValue<int>(), op["gesture"]!.GetValue<int>(), op["action"]!.GetValue<int>()); break;
        }
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
