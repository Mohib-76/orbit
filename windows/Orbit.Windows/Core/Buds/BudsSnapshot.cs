using System.Text.Json.Nodes;

namespace Orbit.Core.Buds;

/// <summary>A noise-control mode the buds offer. <c>Id</c> is what <c>orbit.buds.cmd {op: "anc"}</c> carries.</summary>
public sealed record NoiseMode(string Id, string Label);

/// <summary>An on/off setting the buds reported.</summary>
public sealed record BudsToggle(string Id, string Title, string Subtitle, bool On);

/// <summary>
/// Everything one device knows about the buds at one moment. Immutable; replaced on every change.
/// The driver fills in the controls; without one only the connection and the OS battery level are known.
/// </summary>
public sealed record BudsSnapshot
{
    /// <summary>Windows has the buds connected to this PC.</summary>
    public bool Connected { get; init; }
    /// <summary>A driver holds the buds' control channel here, so the controls are live.</summary>
    public bool Control { get; init; }
    /// <summary>The driver that matches these buds ("heymelody"), or null: battery and playback only.</summary>
    public string? Driver { get; init; }
    public string Name { get; init; } = "Earbuds";
    public string? Mac { get; init; }
    /// <summary>Current noise mode id; one of <see cref="NoiseModes"/>.</summary>
    public string? Noise { get; init; }
    /// <summary>Only the modes the buds answered for, so buds without noise control show none.</summary>
    public IReadOnlyList<NoiseMode> NoiseModes { get; init; } = [];
    /// <summary>HeyMelody: bitmask of AncMode codes cycled by the noise-control touch action; 0 = unknown.</summary>
    public int CycleMask { get; init; }
    /// <summary>Left, right, case from the driver; -1 = unknown.</summary>
    public int[] Battery { get; init; } = { -1, -1, -1 };
    public bool[] Charging { get; init; } = new bool[3];
    /// <summary>One overall level from Windows (the headset's standard battery report); -1 = unknown.</summary>
    public int BatteryMain { get; init; } = -1;
    /// <summary>Only toggles the buds reported, so unsupported ones never show up.</summary>
    public IReadOnlyList<BudsToggle> Toggles { get; init; } = [];
    /// <summary>HeyMelody touch settings.</summary>
    public Dictionary<(int side, int gesture), int> Touch { get; init; } = new();

    public bool HasSideBattery => Battery.Any(b => b >= 0);
    public string? NoiseLabel => NoiseModes.FirstOrDefault(m => m.Id == Noise)?.Label;

    public string BatterySummary
    {
        get
        {
            var parts = new List<string>();
            if (Battery[0] >= 0) parts.Add($"L {Battery[0]}");
            if (Battery[1] >= 0) parts.Add($"R {Battery[1]}");
            if (parts.Count == 0 && BatteryMain >= 0) parts.Add($"{BatteryMain}%");
            return string.Join(" · ", parts);
        }
    }

    /// <summary>The mode after the current one, for one-key cycling.</summary>
    public string? NextNoise()
    {
        if (NoiseModes.Count == 0) return null;
        int i = NoiseModes.ToList().FindIndex(m => m.Id == Noise);
        return NoiseModes[(i + 1) % NoiseModes.Count].Id;
    }

    public BudsSnapshot WithToggle(string id, bool on) =>
        this with { Toggles = Toggles.Select(t => t.Id == id ? t with { On = on } : t).ToList() };

    /// <summary>Adds or updates toggles, keeping <paramref name="order"/> (the driver's catalog order).</summary>
    public BudsSnapshot MergeToggles(IEnumerable<BudsToggle> added, IReadOnlyList<string> order)
    {
        var byId = Toggles.ToDictionary(t => t.Id);
        foreach (var t in added) byId[t.Id] = t;
        return this with { Toggles = byId.Values.OrderBy(t => order.ToList().IndexOf(t.Id)).ToList() };
    }

    /// <summary>What an <c>orbit.buds.cmd</c> op will change, shown before the buds confirm it.</summary>
    public BudsSnapshot Apply(JsonObject op) => op["op"]?.GetValue<string>() switch
    {
        "anc" when op["mode"]?.GetValue<string>() is { } m && NoiseModes.Any(x => x.Id == m) => this with { Noise = m },
        "feature" when op["id"]?.ToString() is { } id => WithToggle(id, op["on"]?.GetValue<bool>() ?? false),
        "cycle" => this with { CycleMask = op["mask"]?.GetValue<int>() ?? CycleMask },
        "touch" => this with { Touch = new(Touch) { [(op["side"]!.GetValue<int>(), op["gesture"]!.GetValue<int>())] = op["action"]!.GetValue<int>() } },
        _ => this
    };

    public JsonObject ToJson()
    {
        var feats = new JsonObject();
        foreach (var t in Toggles) feats[t.Id] = t.On;
        var touch = new JsonArray();
        foreach (var ((side, gesture), action) in Touch) touch.Add(new JsonArray(side, gesture, action));
        return new JsonObject
        {
            ["connected"] = Connected, ["control"] = Control, ["driver"] = Driver,
            ["name"] = Name, ["mac"] = Mac,
            ["anc"] = Noise,
            ["noiseModes"] = new JsonArray(NoiseModes.Select(m => (JsonNode)new JsonObject { ["id"] = m.Id, ["label"] = m.Label }).ToArray()),
            ["cycle"] = CycleMask,
            ["battery"] = new JsonArray(Battery.Select(b => (JsonNode?)b).ToArray()),
            ["charging"] = new JsonArray(Charging.Select(b => (JsonNode?)b).ToArray()),
            ["batteryMain"] = BatteryMain,
            ["toggles"] = new JsonArray(Toggles.Select(t => (JsonNode)new JsonObject
                { ["id"] = t.Id, ["title"] = t.Title, ["subtitle"] = t.Subtitle, ["on"] = t.On }).ToArray()),
            ["features"] = feats, // older Orbit versions read these instead of "toggles"
            ["touch"] = touch
        };
    }

    public static BudsSnapshot FromJson(JsonObject o)
    {
        bool connected = o["connected"]?.GetValue<bool>() ?? false;
        // Older versions only sent "connected", meaning the control channel (always HeyMelody).
        bool legacy = !o.ContainsKey("control");
        string? noise = o["anc"]?.GetValue<string>();

        IReadOnlyList<NoiseMode> modes = o["noiseModes"] is JsonArray ma
            ? ma.OfType<JsonObject>().Select(m => new NoiseMode(m["id"]!.GetValue<string>(), m["label"]?.GetValue<string>() ?? m["id"]!.GetValue<string>())).ToList()
            : noise != null ? HeyMelodyDriver.NoiseModes : [];

        IReadOnlyList<BudsToggle> toggles = [];
        if (o["toggles"] is JsonArray ta)
            toggles = ta.OfType<JsonObject>().Select(t => new BudsToggle(t["id"]!.GetValue<string>(), t["title"]?.GetValue<string>() ?? "",
                t["subtitle"]?.GetValue<string>() ?? "", t["on"]?.GetValue<bool>() ?? false)).ToList();
        else if (o["features"] is JsonObject fo)
            toggles = Feature.All.Where(f => fo[f.Id.ToString()] != null).Select(f => f.Toggle(fo[f.Id.ToString()]!.GetValue<bool>())).ToList();

        var touch = new Dictionary<(int, int), int>();
        if (o["touch"] is JsonArray tou)
            foreach (var t in tou.OfType<JsonArray>())
                touch[(t[0]!.GetValue<int>(), t[1]!.GetValue<int>())] = t[2]!.GetValue<int>();
        int[] bat = { -1, -1, -1 };
        bool[] chg = new bool[3];
        if (o["battery"] is JsonArray ba) for (int i = 0; i < 3 && i < ba.Count; i++) bat[i] = ba[i]?.GetValue<int>() ?? -1;
        if (o["charging"] is JsonArray ca) for (int i = 0; i < 3 && i < ca.Count; i++) chg[i] = ca[i]?.GetValue<bool>() ?? false;

        return new BudsSnapshot
        {
            Connected = connected,
            Control = legacy ? connected : o["control"]?.GetValue<bool>() ?? false,
            Driver = o["driver"]?.GetValue<string>() ?? (legacy && connected ? HeyMelodyDriver.DriverId : null),
            Name = o["name"]?.GetValue<string>() ?? "Earbuds",
            Mac = o["mac"]?.GetValue<string>(),
            Noise = noise, NoiseModes = modes,
            CycleMask = o["cycle"]?.GetValue<int>() ?? 0,
            Battery = bat, Charging = chg,
            BatteryMain = o["batteryMain"]?.GetValue<int>() ?? -1,
            Toggles = toggles, Touch = touch
        };
    }
}
