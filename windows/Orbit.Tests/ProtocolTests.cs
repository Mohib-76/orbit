using System.Text.Json.Nodes;
using System.Windows.Input;
using Orbit.Core;
using Orbit.Core.Buds;
using Xunit;

namespace Orbit.Tests;

public class ProtoTests
{
    [Fact]
    public void EncodeDecodeRoundTrip()
    {
        var line = Proto.Encode("orbit.clipboard", new JsonObject { ["text"] = "hello\nworld" });
        Assert.EndsWith("\n", line);
        Assert.Equal(1, line.Count(c => c == '\n')); // payload newlines must be escaped
        var (type, body) = Proto.Decode(line.TrimEnd('\n'));
        Assert.Equal("orbit.clipboard", type);
        Assert.Equal("hello\nworld", body["text"]!.GetValue<string>());
    }

    [Fact]
    public void HmacMatchesBothSidesFormula()
    {
        // HMAC-SHA256(token, "pc|nonce|phoneFp|pcFp"), lowercase hex — must equal the Kotlin side.
        var mac = Proto.Hmac("token", "pc", "n", "a", "b");
        Assert.Equal(64, mac.Length);
        Assert.Equal(mac.ToLowerInvariant(), mac);
        Assert.Equal(mac, Proto.Hmac("token", "pc", "n", "a", "b"));
        Assert.NotEqual(mac, Proto.Hmac("token", "phone", "n", "a", "b"));
    }

    [Fact]
    public void VersionIsAdded()
    {
        var b = Proto.WithVersion(new JsonObject());
        Assert.Equal(Proto.Version, b["proto"]!.GetValue<int>());
        Assert.False(string.IsNullOrEmpty(b["app"]!.GetValue<string>()));
    }

    [Theory]
    [InlineData("192.168.1.20", "192.168.1.20", 47800)]
    [InlineData(" 10.0.0.5:5000 ", "10.0.0.5", 5000)]
    [InlineData("phone.local", "phone.local", 47800)]
    public void ManualAddressParses(string input, string host, int port)
    {
        var r = OrbitCore.ParseManualAddress(input);
        Assert.NotNull(r);
        Assert.Equal(host, r!.Value.host);
        Assert.Equal(port, r.Value.port);
    }

    [Theory]
    [InlineData("")]
    [InlineData("10.0.0.5:99999")]
    [InlineData("not an address")]
    public void ManualAddressRejectsJunk(string input) => Assert.Null(OrbitCore.ParseManualAddress(input));
}

public class HotkeyTests
{
    [Fact]
    public void ParsesAndFormats()
    {
        var hk = Hotkey.Parse("ctrl+alt+c");
        Assert.NotNull(hk);
        Assert.Equal(ModifierKeys.Control | ModifierKeys.Alt, hk!.Value.Modifiers);
        Assert.Equal(Key.C, hk.Value.Key);
        Assert.Equal("Ctrl+Alt+C", hk.Value.ToString());
        Assert.Equal("Ctrl+Shift+5", Hotkey.Parse("Ctrl+Shift+5")!.Value.ToString());
    }

    [Theory]
    [InlineData("C")]            // no modifier
    [InlineData("Shift+C")]      // Shift alone clashes with typing
    [InlineData("Ctrl+Alt")]     // no key
    [InlineData("Ctrl+Nope")]
    [InlineData("")]
    public void RejectsInvalid(string text) => Assert.Null(Hotkey.Parse(text));
}

public class BudsProtocolTests
{
    [Fact]
    public void EncodesAncSetLikeTheReference()
    {
        // From PROTOCOL notes: set ANC on, seq 0 → AA 0A 00 00 04 04 00 03 00 01 01 08
        var pkt = BudsProtocol.Encode(BudsProtocol.AncSet, 0, BudsProtocol.AncSetting(BudsProtocol.AncTypeMode, (int)AncMode.Anc));
        Assert.Equal("AA0A0000040400030001010" + "8", Convert.ToHexString(pkt));
    }

    [Fact]
    public void ParserReassemblesSplitPackets()
    {
        var a = BudsProtocol.Encode(BudsProtocol.BatteryRet, 1, new byte[] { 0, 3, 1, 90, 2, 0x80 | 85, 3, 40 });
        var b = BudsProtocol.Encode(BudsProtocol.AncRet, 2, new byte[] { 0, 1, 1, 2 });
        var stream = a.Concat(b).ToArray();
        var parser = new BudsProtocol.Parser();
        var first = parser.Feed(stream.AsSpan(0, 5));
        Assert.Empty(first);
        var rest = parser.Feed(stream.AsSpan(5));
        Assert.Equal(2, rest.Count);
        Assert.Equal(BudsProtocol.BatteryRet, rest[0].cmd);
        Assert.Equal(BudsProtocol.AncRet, rest[1].cmd);
    }

    [Fact]
    public void ParsesBattery()
    {
        var (levels, charging) = BudsProtocol.ParseBattery(new byte[] { 0, 3, 1, 90, 2, 0x80 | 85, 3, 0 });
        Assert.Equal(new[] { 90, 85, -1 }, levels); // case reports 0 = unknown
        Assert.False(charging[0]);
        Assert.True(charging[1]);
    }

    [Fact]
    public void ParsesTouchConfig()
    {
        // Real T310 answer: every gesture reports action 0.
        var t = BudsProtocol.ParseTouch(Convert.FromHexString("0006010102000101030001010400020102000201030002010400"));
        Assert.Equal(6, t.Count);
        Assert.Equal(0, t[(BudsProtocol.SideLeft, BudsProtocol.Tap2)]);
        Assert.Equal(0, t[(BudsProtocol.SideRight, BudsProtocol.Hold)]);
    }

    [Fact]
    public void AncCodesMapToModes()
    {
        Assert.Equal(AncMode.Off, AncModes.FromCode(0x01));
        Assert.Equal(AncMode.Transparency, AncModes.FromCode(0x02));
        Assert.Equal(AncMode.Anc, AncModes.FromCode(0x10));
        Assert.Null(AncModes.FromCode(0x40));
        Assert.Equal(AncMode.Off, AncMode.Anc.Next());
    }

    [Fact]
    public void SnapshotJsonRoundTrip()
    {
        var s = new BudsSnapshot
        {
            Connected = true, Name = "Buds", Mac = "00:11:22:33:44:55", Anc = AncMode.Transparency, CycleMask = 0x0B,
            Battery = new[] { 50, 60, -1 }, Charging = new[] { false, true, false },
            Features = new() { [29] = true }, Touch = new() { [(1, 0x0201)] = 1 }
        };
        var r = BudsSnapshot.FromJson(s.ToJson());
        Assert.Equal(s.Anc, r.Anc);
        Assert.Equal(s.Battery, r.Battery);
        Assert.Equal(s.Charging, r.Charging);
        Assert.True(r.Features[29]);
        Assert.Equal(1, r.Touch[(1, 0x0201)]);
    }
}
