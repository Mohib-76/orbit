using System.IO;

namespace Orbit.Core.Buds;

// Protocol facts from Gadgetbridge's Oppo/realme support (see THIRD_PARTY_NOTICES.md).
// realme / OnePlus / OPPO "HeyMelody" earbuds, classic Bluetooth RFCOMM; the driver is HeyMelodyDriver.cs.

public enum AncMode { Off = 0x01, Transparency = 0x02, Anc = 0x08 }

public static class AncModes
{
    public static readonly AncMode[] All = { AncMode.Off, AncMode.Transparency, AncMode.Anc };

    public static string Label(this AncMode m) => m switch
    {
        AncMode.Off => "Off", AncMode.Transparency => "Transparency", _ => "ANC"
    };

    /// <summary>Wire name shared with the phone ("OFF", "TRANSPARENCY", "ANC").</summary>
    public static string WireName(this AncMode m) => m switch
    {
        AncMode.Off => "OFF", AncMode.Transparency => "TRANSPARENCY", _ => "ANC"
    };

    public static AncMode? FromWireName(string? n) => n switch
    {
        "OFF" => AncMode.Off, "TRANSPARENCY" => AncMode.Transparency, "ANC" => AncMode.Anc, _ => null
    };

    // 0x04 / 0x10 / 0x20 are ANC light / medium / smart on other models; show them as ANC.
    public static AncMode? FromCode(int code) => code switch
    {
        0x01 => AncMode.Off, 0x02 => AncMode.Transparency, 0x04 or 0x08 or 0x10 or 0x20 => AncMode.Anc, _ => null
    };

    public static AncMode Next(this AncMode m) => m switch
    {
        AncMode.Off => AncMode.Transparency, AncMode.Transparency => AncMode.Anc, _ => AncMode.Off
    };
}

public sealed record TouchAction(int Code, string Label)
{
    public static readonly TouchAction None = new(0x00, "None");
    public static readonly TouchAction PlayPause = new(0x01, "Play / pause");
    public static readonly TouchAction Previous = new(0x05, "Previous track");
    public static readonly TouchAction Next = new(0x06, "Next track");
    public static readonly TouchAction VolumeUp = new(0x0B, "Volume up");
    public static readonly TouchAction VolumeDown = new(0x0C, "Volume down");
    public static readonly TouchAction Assistant = new(0x04, "Voice assistant");
    public static readonly TouchAction NoiseControl = new(0x08, "Noise control");
    public static readonly TouchAction GameMode = new(0x11, "Game mode");

    public static readonly TouchAction[] SingleBud = { PlayPause, Previous, Next, VolumeUp, VolumeDown, Assistant, NoiseControl, None };
    public static readonly TouchAction[] BothBuds = { None, GameMode };
    static readonly TouchAction[] All = { None, PlayPause, Previous, Next, VolumeUp, VolumeDown, Assistant, NoiseControl, GameMode };

    public static string LabelOf(int code) => All.FirstOrDefault(a => a.Code == code)?.Label ?? "Custom";
    public override string ToString() => Label;
}

public sealed record Feature(int Id, string Title, string Subtitle)
{
    public static readonly Feature[] All =
    {
        new(6, "Game mode", "Lower latency for games and video"),
        new(27, "360° spatial audio", "Surround-style sound"),
        new(29, "Bass boost", "Dynamic low-frequency enhancement"),
        new(12, "Voice enhancement", "Clearer voices in calls and videos"),
        new(26, "Wind-noise reduction", "Cuts wind noise on the mics"),
        new(17, "Dual-device connection", "Stay connected to your phone and this PC"),
        new(24, "High-quality audio", "Hi-res codec when supported"),
    };
    public const int GameModeId = 6;

    public BudsToggle Toggle(bool on) => new(Id.ToString(), Title, Subtitle, on);
}

/// <summary>OPPOv1 framing: AA | varint(len) | 00 00 | cmd:u16le | seq | plen:u16le | payload</summary>
public static class BudsProtocol
{
    public static readonly Guid UuidVendor = Guid.Parse("0000079a-d102-11e1-9b23-00025b00a5a5");
    public static readonly Guid UuidSpp = Guid.Parse("00001101-0000-1000-8000-00805f9b34fb");

    public const int ResponseBit = 0x8000;
    public const int BatteryReq = 0x0106, BatteryRet = 0x8106;
    public const int SubscriptionSet = 0x0205, SubscriptionRet = 0x0204;
    public const int TouchReq = 0x0108, TouchRet = 0x8108, TouchSet = 0x0401;
    public const int MiscReq = 0x010D, MiscRet = 0x810D, MiscSet = 0x0403;
    public const int AncReq = 0x010C, AncRet = 0x810C, AncSet = 0x0404, AncAck = 0x8404;

    public const int AncTypeMode = 0x01, AncTypeCycle = 0x02;
    public const int SubBattery = 0x01, SubAnc = 0x03, SubGameMode = 0x05;
    public const int SideLeft = 0x01, SideRight = 0x02, SideBoth = 0x04;
    public const int Tap2 = 0x0201, Tap3 = 0x0301, Hold = 0x0401;

    public static byte[] AncRequest(int type) => new[] { (byte)type, (byte)1 };
    public static byte[] AncSetting(int type, int value) => new[] { (byte)type, (byte)1, (byte)value };
    public static byte[] Subscribe() => new byte[] { 0x09, SubBattery, SubAnc, SubGameMode };
    public static byte[] MiscRequest(int id) => new[] { (byte)1, (byte)id };
    public static byte[] MiscSetting(int id, bool on) => new[] { (byte)id, (byte)(on ? 1 : 0) };
    public static byte[] TouchRequest() => new byte[] { 0x02, 0x03, 0x01 };
    public static byte[] TouchSetting(int side, int gesture, int action) =>
        new[] { (byte)1, (byte)side, (byte)(gesture & 0xFF), (byte)(gesture >> 8), (byte)action };

    public static byte[] Encode(int cmd, int seq, byte[] payload)
    {
        var ms = new MemoryStream();
        ms.WriteByte(0xAA);
        int len = 7 + payload.Length;
        while (true)
        {
            int b = len & 0x7F;
            len >>= 7;
            if (len == 0) { ms.WriteByte((byte)b); break; }
            ms.WriteByte((byte)(b | 0x80));
        }
        ms.WriteByte(0); ms.WriteByte(0);
        ms.WriteByte((byte)(cmd & 0xFF)); ms.WriteByte((byte)((cmd >> 8) & 0xFF));
        ms.WriteByte((byte)seq);
        ms.WriteByte((byte)(payload.Length & 0xFF)); ms.WriteByte((byte)((payload.Length >> 8) & 0xFF));
        ms.Write(payload);
        return ms.ToArray();
    }

    public static (int type, int value)? ParseAnc(byte[] p) => p.Length < 3 ? null : (p[1], p[^1]);

    public static (int[] levels, bool[] charging) ParseBattery(byte[] p)
    {
        var levels = new[] { -1, -1, -1 };
        var charging = new bool[3];
        for (int i = 2; i + 1 < p.Length; i += 2)
        {
            int idx = p[i] - 1, level = p[i + 1] & 0x7F;
            if (idx is >= 0 and <= 2 && !(idx == 2 && level == 0))
            {
                levels[idx] = level;
                charging[idx] = (p[i + 1] & 0x80) != 0;
            }
        }
        return (levels, charging);
    }

    public static Dictionary<int, bool> ParseMisc(byte[] p)
    {
        var o = new Dictionary<int, bool>();
        if (p.Length < 2 || p[0] != 0) return o;
        int i = 2;
        for (int n = 0; n < p[1] && i + 1 < p.Length; n++, i += 2) o[p[i]] = p[i + 1] == 1;
        return o;
    }

    public static Dictionary<(int side, int gesture), int> ParseTouch(byte[] p)
    {
        var o = new Dictionary<(int, int), int>();
        if (p.Length < 2 || (p.Length - 2) % 4 != 0) return o;
        for (int i = 2; i < p.Length; i += 4) o[(p[i], p[i + 1] | (p[i + 2] << 8))] = p[i + 3];
        return o;
    }

    /// <summary>Reassembles packets from the RFCOMM stream.</summary>
    public sealed class Parser
    {
        readonly List<byte> _buf = new();

        public List<(int cmd, byte[] payload)> Feed(ReadOnlySpan<byte> data)
        {
            foreach (var b in data) _buf.Add(b);
            var outp = new List<(int, byte[])>();
            int pos = 0;
            while (pos < _buf.Count)
            {
                if (_buf[pos] != 0xAA) { pos++; continue; }
                int p = pos + 1, len = 0, shift = 0;
                bool complete = false;
                while (p < _buf.Count)
                {
                    int b = _buf[p++];
                    len |= (b & 0x7F) << shift;
                    shift += 7;
                    if ((b & 0x80) == 0) { complete = true; break; }
                }
                if (!complete || _buf.Count - p < len) break;
                var pkt = _buf.GetRange(p, len).ToArray();
                pos = p + len;
                if (pkt.Length < 7) continue;
                int cmd = pkt[2] | (pkt[3] << 8);
                int plen = pkt[5] | (pkt[6] << 8);
                if (7 + plen <= pkt.Length) outp.Add((cmd, pkt[7..(7 + plen)]));
            }
            _buf.RemoveRange(0, pos);
            return outp;
        }
    }
}
