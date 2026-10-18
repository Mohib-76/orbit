using System.Buffers.Binary;
using System.Text;

namespace Orbit.Core.Mirror;

/// <summary>Android key codes and meta flags the mirror uses.</summary>
public static class Key
{
    public const int Home = 3, Back = 4, Num0 = 7, A = 29, Tab = 61, Space = 62, Enter = 66, Del = 67,
        VolumeUp = 24, VolumeDown = 25, Power = 26, Escape = 111, ForwardDel = 112, AppSwitch = 187,
        DpadUp = 19, DpadDown = 20, DpadLeft = 21, DpadRight = 22, PageUp = 92, PageDown = 93, MoveHome = 122, MoveEnd = 123;
    public const int MetaShift = 0x1 | 0x40, MetaAlt = 0x2 | 0x10, MetaCtrl = 0x1000 | 0x2000;
}

/// <summary>Serializes scrcpy control messages (see scrcpy app/src/control_msg.c).</summary>
public static class Msg
{
    enum T : byte
    {
        InjectKeycode = 0, InjectText = 1, InjectTouch = 2, InjectScroll = 3, BackOrScreenOn = 4,
        ExpandNotifications = 5, ExpandSettings = 6, CollapsePanels = 7, GetClipboard = 8, SetClipboard = 9,
        SetDisplayPower = 10, RotateDevice = 11, ResetVideo = 17,
    }

    public const int ActionDown = 0, ActionUp = 1, ActionMove = 2;
    /// <summary>Pointer id scrcpy maps to a finger (so "Show touches" draws it).</summary>
    public const ulong FingerPointer = unchecked((ulong)-2L);

    public static byte[] Keycode(int action, int keycode, int meta = 0, int repeat = 0)
    {
        var b = new byte[14];
        b[0] = (byte)T.InjectKeycode; b[1] = (byte)action;
        BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(2), keycode);
        BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(6), repeat);
        BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(10), meta);
        return b;
    }

    public static byte[] Text(string text)
    {
        var raw = Encoding.UTF8.GetBytes(text);
        if (raw.Length > 300) raw = raw[..300];
        var b = new byte[5 + raw.Length];
        b[0] = (byte)T.InjectText;
        BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(1), raw.Length);
        raw.CopyTo(b, 5);
        return b;
    }

    public static byte[] Touch(int action, int x, int y, int w, int h, float pressure)
    {
        var b = new byte[32];
        b[0] = (byte)T.InjectTouch; b[1] = (byte)action;
        BinaryPrimitives.WriteUInt64BigEndian(b.AsSpan(2), FingerPointer);
        Position(b.AsSpan(10), x, y, w, h);
        BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(22), (ushort)Math.Min(0xffff, (int)(pressure * 65536)));
        // action_button and buttons stay 0 for a finger
        return b;
    }

    public static byte[] Scroll(int x, int y, int w, int h, float hscroll, float vscroll)
    {
        var b = new byte[21];
        b[0] = (byte)T.InjectScroll;
        Position(b.AsSpan(1), x, y, w, h);
        BinaryPrimitives.WriteInt16BigEndian(b.AsSpan(13), Fixed16(hscroll / 16));
        BinaryPrimitives.WriteInt16BigEndian(b.AsSpan(15), Fixed16(vscroll / 16));
        return b;
    }

    public static byte[] BackOrScreenOn(int action) => new[] { (byte)T.BackOrScreenOn, (byte)action };
    public static byte[] ExpandNotifications() => new[] { (byte)T.ExpandNotifications };
    public static byte[] CollapsePanels() => new[] { (byte)T.CollapsePanels };
    public static byte[] SetDisplayPower(bool on) => new[] { (byte)T.SetDisplayPower, (byte)(on ? 1 : 0) };
    public static byte[] RotateDevice() => new[] { (byte)T.RotateDevice };
    public static byte[] ResetVideo() => new[] { (byte)T.ResetVideo };

    public static byte[] SetClipboard(long sequence, string text, bool paste)
    {
        var raw = Encoding.UTF8.GetBytes(text);
        var b = new byte[14 + raw.Length];
        b[0] = (byte)T.SetClipboard;
        BinaryPrimitives.WriteInt64BigEndian(b.AsSpan(1), sequence);
        b[9] = (byte)(paste ? 1 : 0);
        BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(10), raw.Length);
        raw.CopyTo(b, 14);
        return b;
    }

    static void Position(Span<byte> b, int x, int y, int w, int h)
    {
        BinaryPrimitives.WriteInt32BigEndian(b, x);
        BinaryPrimitives.WriteInt32BigEndian(b[4..], y);
        BinaryPrimitives.WriteUInt16BigEndian(b[8..], (ushort)w);
        BinaryPrimitives.WriteUInt16BigEndian(b[10..], (ushort)h);
    }

    static short Fixed16(float f)
    {
        f = Math.Clamp(f, -1f, 1f);
        int i = (int)(f * 32768);
        return (short)Math.Min(i, 0x7fff);
    }
}
