using System.Text.Json.Nodes;

namespace Orbit.Core.Media;

/// <summary>One media player as it travels in orbit.media.state. Positions are in milliseconds.</summary>
public sealed record PlayerInfo(
    string Id, string App, string Title, string Artist, string Album,
    bool Playing, long PosMs, long LenMs,
    bool CanPlay, bool CanPause, bool CanNext, bool CanPrev, bool CanSeek, string Art)
{
    public bool IsLive => LenMs <= 0;

    public JsonObject ToJson() => new()
    {
        ["id"] = Id, ["app"] = App, ["title"] = Title, ["artist"] = Artist, ["album"] = Album,
        ["playing"] = Playing, ["pos"] = PosMs, ["len"] = LenMs,
        ["canPlay"] = CanPlay, ["canPause"] = CanPause, ["canNext"] = CanNext, ["canPrev"] = CanPrev, ["canSeek"] = CanSeek,
        ["art"] = Art
    };

    public static PlayerInfo? FromJson(JsonNode? n)
    {
        if (n is not JsonObject o || o["id"]?.GetValue<string>() is not { Length: > 0 } id) return null;
        string S(string k) => o[k]?.GetValue<string>() ?? "";
        bool B(string k, bool def = false) => o[k]?.GetValue<bool>() ?? def;
        long L(string k) => o[k] is JsonValue v && v.TryGetValue<long>(out var x) ? x : 0;
        return new PlayerInfo(id, S("app"), S("title"), S("artist"), S("album"), B("playing"), Math.Max(0, L("pos")), Math.Max(0, L("len")),
            B("canPlay", true), B("canPause", true), B("canNext"), B("canPrev"), B("canSeek"), S("art"));
    }
}

/// <summary>What one side reported: its players, which one it considers current, and when we received it.</summary>
public sealed record MediaSnapshot(IReadOnlyList<PlayerInfo> Players, string? Active, bool Controllable, DateTime ReceivedUtc)
{
    public static readonly MediaSnapshot Empty = new(Array.Empty<PlayerInfo>(), null, true, DateTime.MinValue);

    public bool HasPlayers => Players.Count > 0;

    /// <summary>The player to show: the user's pick if it still exists, else the sender's current one, else a playing one.</summary>
    public PlayerInfo? Pick(string? userPick) =>
        Players.FirstOrDefault(p => p.Id == userPick)
        ?? Players.FirstOrDefault(p => p.Id == Active)
        ?? Players.FirstOrDefault(p => p.Playing)
        ?? Players.FirstOrDefault();

    /// <summary>Position now: while playing, advance from the moment the packet arrived. Clamped to the length.</summary>
    public long PositionNow(PlayerInfo p, DateTime nowUtc)
    {
        long pos = p.PosMs;
        if (p.Playing && ReceivedUtc != DateTime.MinValue) pos += (long)(nowUtc - ReceivedUtc).TotalMilliseconds;
        return p.LenMs > 0 ? Math.Clamp(pos, 0, p.LenMs) : Math.Max(0, pos);
    }

    public JsonObject ToJson() => new()
    {
        ["players"] = new JsonArray(Players.Select(p => (JsonNode)p.ToJson()).ToArray()),
        ["active"] = Active,
        ["controllable"] = Controllable
    };

    public static MediaSnapshot FromJson(JsonObject b, DateTime receivedUtc)
    {
        var players = (b["players"] as JsonArray)?.Select(PlayerInfo.FromJson).OfType<PlayerInfo>().ToList() ?? new();
        return new MediaSnapshot(players, b["active"]?.GetValue<string>(), b["controllable"]?.GetValue<bool>() ?? true, receivedUtc);
    }

    /// <summary>"1:42", "1:12:05".</summary>
    public static string Clock(long ms)
    {
        var t = TimeSpan.FromMilliseconds(Math.Max(0, ms));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }
}
