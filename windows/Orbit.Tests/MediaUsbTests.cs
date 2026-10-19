using System.Text.Json.Nodes;
using Orbit.Core;
using Orbit.Core.Media;
using Xunit;

namespace Orbit.Tests;

public class MediaTests
{
    static PlayerInfo Song(bool playing = true, long pos = 10_000, long len = 200_000) =>
        new("com.spotify.music", "Spotify", "Midnight City", "M83", "Hurry Up", playing, pos, len, true, true, true, true, true, "abc123");

    [Fact]
    public void StateRoundTrips()
    {
        var snap = new MediaSnapshot(new[] { Song() }, "com.spotify.music", false, DateTime.UtcNow);
        var line = Proto.Encode("orbit.media.state", snap.ToJson());
        var (type, body) = Proto.Decode(line.TrimEnd('\n'));
        var back = MediaSnapshot.FromJson(body, DateTime.UtcNow);
        Assert.Equal("orbit.media.state", type);
        Assert.Equal(Song(), back.Players.Single());
        Assert.Equal("com.spotify.music", back.Active);
        Assert.False(back.Controllable);
    }

    [Fact]
    public void MissingFieldsHaveSafeDefaults()
    {
        var b = JsonNode.Parse("""{"players":[{"id":"x"},{"app":"no id"}]}""")!.AsObject();
        var snap = MediaSnapshot.FromJson(b, DateTime.UtcNow);
        var p = Assert.Single(snap.Players);
        Assert.True(snap.Controllable);
        Assert.True(p.IsLive);
        Assert.False(p.CanSeek);
        Assert.Equal("", p.Art);
    }

    [Fact]
    public void PositionAdvancesOnlyWhilePlaying()
    {
        var t0 = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
        var playing = new MediaSnapshot(new[] { Song() }, null, true, t0);
        Assert.Equal(13_000, playing.PositionNow(playing.Players[0], t0.AddSeconds(3)));
        Assert.Equal(200_000, playing.PositionNow(playing.Players[0], t0.AddHours(1))); // clamped to the length

        var paused = new MediaSnapshot(new[] { Song(playing: false) }, null, true, t0);
        Assert.Equal(10_000, paused.PositionNow(paused.Players[0], t0.AddSeconds(3)));
    }

    [Fact]
    public void PickPrefersUserThenActiveThenPlaying()
    {
        var a = Song(playing: false) with { Id = "a" };
        var b = Song(playing: true) with { Id = "b" };
        var c = Song(playing: false) with { Id = "c" };
        var snap = new MediaSnapshot(new[] { a, b, c }, "c", true, DateTime.UtcNow);
        Assert.Equal("a", snap.Pick("a")!.Id);
        Assert.Equal("c", snap.Pick("gone")!.Id);
        Assert.Equal("b", (snap with { Active = null }).Pick(null)!.Id);
        Assert.Null(MediaSnapshot.Empty.Pick(null));
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(102_000, "1:42")]
    [InlineData(4_325_000, "1:12:05")]
    public void ClockFormats(long ms, string text) => Assert.Equal(text, MediaSnapshot.Clock(ms));
}

public class UsbModeTests
{
    [Theory]
    [InlineData("rndis", "tethering")]
    [InlineData("ncm", "tethering")]
    [InlineData("mtp", "files")]
    [InlineData("mtp,adb", "files")]
    [InlineData("none", "charging")]
    [InlineData("", "charging")]
    [InlineData("ptp", "other")]
    public void ReadsFunctions(string functions, string mode) => Assert.Equal(mode, UsbModes.FromFunctions(functions));

    [Fact]
    public void MapsModesToFunctions()
    {
        Assert.Equal("rndis", UsbModes.ToFunction(UsbModes.Tethering));
        Assert.Equal("mtp", UsbModes.ToFunction(UsbModes.Files));
        Assert.Equal("", UsbModes.ToFunction(UsbModes.Charging));
    }

    [Theory]
    [InlineData("files", "files")]
    [InlineData("charging", "charging")]
    [InlineData("tethering", "tethering")]
    [InlineData("bogus", "tethering")]
    [InlineData(null, "tethering")]
    public void NormalizesSettings(string? value, string expected) => Assert.Equal(expected, UsbModes.Normalize(value));
}
