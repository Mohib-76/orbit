using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Orbit.Core.Mirror;

/// <summary>Records the mirror stream to Videos\Orbit as MP4. The phone's H.264 is stored as is (no re-encoding).</summary>
public sealed class MirrorRecorder
{
    readonly object _gate = new();
    MirrorSession? _session;
    Mp4Writer? _mp4;
    string? _path;

    public bool IsRecording { get { lock (_gate) return _session != null; } }

    public string? Start(MirrorSession session)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Orbit");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"Orbit mirror {DateTime.Now:yyyy-MM-dd HH.mm.ss}.mp4");
            lock (_gate)
            {
                _mp4 = new Mp4Writer(path, session.VideoWidth, session.VideoHeight);
                _path = path;
                _session = session;
                session.Packet += OnPacket;
                session.VideoSizeChanged += OnSizeChanged;
            }
            session.Send(Msg.ResetVideo()); // start on a fresh key frame
            Log.Write("recording to " + path);
            return path;
        }
        catch (Exception ex) { Log.Write("record start: " + ex.Message); return null; }
    }

    void OnPacket(ReadOnlySpan<byte> data, long ptsUs, bool keyFrame)
    {
        lock (_gate)
        {
            try { _mp4?.Add(data, ptsUs, keyFrame, _session?.LastConfig); }
            catch (Exception ex) { Log.Write("record: " + ex.Message); _mp4 = null; }
        }
    }

    // One MP4 track has one picture size; stop collecting when the phone rotates.
    void OnSizeChanged(int w, int h) { lock (_gate) _mp4?.Seal(); }

    /// <summary>Finishes the file. Returns its path, or null if nothing was recorded.</summary>
    public string? Stop()
    {
        lock (_gate)
        {
            if (_session != null) { _session.Packet -= OnPacket; _session.VideoSizeChanged -= OnSizeChanged; }
            _session = null;
            var mp4 = _mp4; var path = _path;
            _mp4 = null; _path = null;
            if (mp4 == null || path == null) return null;
            try
            {
                if (mp4.Finish()) return path;
                File.Delete(path);
            }
            catch (Exception ex) { Log.Write("record finish: " + ex.Message); }
            return null;
        }
    }
}

/// <summary>
/// Minimal MP4 (ISO BMFF) writer for one H.264 track: ftyp, mdat written as samples arrive, moov at the end.
/// </summary>
sealed class Mp4Writer
{
    const int Timescale = 90000;
    readonly FileStream _f;
    readonly long _mdatStart;
    readonly int _width, _height;
    readonly List<(long offset, int size, long ticks, bool key)> _samples = new();
    byte[]? _sps, _pps;
    bool _started, _sealed;
    long _firstPts = -1;

    public Mp4Writer(string path, int width, int height)
    {
        _width = width; _height = height;
        _f = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16);
        var ftyp = new Box("ftyp");
        ftyp.Ascii("isom").U32(0x200).Ascii("isomiso2avc1mp41");
        ftyp.WriteTo(_f);
        _mdatStart = _f.Position;
        // 64-bit "largesize" mdat header; the size is patched in Finish().
        _f.Write(new byte[] { 0, 0, 0, 1, (byte)'m', (byte)'d', (byte)'a', (byte)'t', 0, 0, 0, 0, 0, 0, 0, 0 });
    }

    public void Seal() => _sealed = true;

    public void Add(ReadOnlySpan<byte> annexB, long ptsUs, bool keyFrame, byte[]? lastConfig)
    {
        if (_sealed) return;
        var nals = Nals(annexB);
        foreach (var (o, l) in nals)
        {
            int type = annexB[o] & 0x1f;
            if (type == 7) _sps = annexB.Slice(o, l).ToArray();
            else if (type == 8) _pps = annexB.Slice(o, l).ToArray();
        }
        if (!_started)
        {
            if (!keyFrame) return;
            if ((_sps == null || _pps == null) && lastConfig != null)
                foreach (var (o, l) in Nals(lastConfig))
                {
                    int type = lastConfig[o] & 0x1f;
                    if (type == 7) _sps = lastConfig.AsSpan(o, l).ToArray();
                    else if (type == 8) _pps = lastConfig.AsSpan(o, l).ToArray();
                }
            if (_sps == null || _pps == null) return;
            _started = true;
            _firstPts = ptsUs;
        }

        long start = _f.Position;
        Span<byte> len = stackalloc byte[4];
        foreach (var (o, l) in nals)
        {
            int type = annexB[o] & 0x1f;
            if (type is 7 or 8 or 9) continue; // parameter sets live in avcC; drop access unit delimiters
            BinaryPrimitives.WriteInt32BigEndian(len, l);
            _f.Write(len);
            _f.Write(annexB.Slice(o, l));
        }
        int size = (int)(_f.Position - start);
        if (size > 0) _samples.Add((start, size, (ptsUs - _firstPts) * Timescale / 1_000_000, keyFrame));
    }

    static List<(int offset, int length)> Nals(ReadOnlySpan<byte> b)
    {
        var list = new List<(int, int)>();
        int i = 0, nalStart = -1;
        while (i + 3 <= b.Length)
        {
            if (b[i] == 0 && b[i + 1] == 0 && b[i + 2] == 1)
            {
                if (nalStart >= 0)
                {
                    int end = i;
                    while (end > nalStart && b[end - 1] == 0) end--; // the extra zero of a 4-byte start code
                    list.Add((nalStart, end - nalStart));
                }
                i += 3;
                nalStart = i;
            }
            else i++;
        }
        if (nalStart >= 0 && nalStart < b.Length) list.Add((nalStart, b.Length - nalStart));
        return list;
    }

    /// <summary>Writes the index and closes the file. False if no sample was written.</summary>
    public bool Finish()
    {
        try
        {
            if (_samples.Count == 0) return false;
            long end = _f.Position;
            _f.Position = _mdatStart + 8;
            Span<byte> big = stackalloc byte[8];
            BinaryPrimitives.WriteInt64BigEndian(big, end - _mdatStart);
            _f.Write(big);
            _f.Position = end;
            BuildMoov().WriteTo(_f);
            return true;
        }
        finally { _f.Dispose(); }
    }

    Box BuildMoov()
    {
        int n = _samples.Count;
        var deltas = new uint[n];
        for (int i = 0; i < n - 1; i++) deltas[i] = (uint)Math.Max(1, _samples[i + 1].ticks - _samples[i].ticks);
        deltas[n - 1] = n > 1 ? deltas[n - 2] : Timescale / 30;
        long mediaDuration = 0;
        foreach (var d in deltas) mediaDuration += d;
        uint movieDuration = (uint)(mediaDuration * 1000 / Timescale);
        int[] matrix = { 0x10000, 0, 0, 0, 0x10000, 0, 0, 0, 0x40000000 };

        var mvhd = new Box("mvhd").U32(0).U32(0).U32(0).U32(1000).U32(movieDuration).U32(0x10000).U16(0x100).Zero(10);
        foreach (var m in matrix) mvhd.U32((uint)m);
        mvhd.Zero(24).U32(2);

        var tkhd = new Box("tkhd").U32(3).U32(0).U32(0).U32(1).U32(0).U32(movieDuration).Zero(8).U16(0).U16(0).U16(0).U16(0);
        foreach (var m in matrix) tkhd.U32((uint)m);
        tkhd.U32((uint)_width << 16).U32((uint)_height << 16);

        var mdhd = new Box("mdhd").U32(0).U32(0).U32(0).U32(Timescale).U32((uint)mediaDuration).U16(0x55C4).U16(0);
        var hdlr = new Box("hdlr").U32(0).U32(0).Ascii("vide").Zero(12).Ascii("VideoHandler\0");

        var avcC = new Box("avcC").U8(1).U8(_sps![1]).U8(_sps[2]).U8(_sps[3]).U8(0xFF).U8(0xE1)
            .U16((ushort)_sps.Length).Bytes(_sps).U8(1).U16((ushort)_pps!.Length).Bytes(_pps);
        var avc1 = new Box("avc1").Zero(6).U16(1).Zero(16).U16((ushort)_width).U16((ushort)_height)
            .U32(0x480000).U32(0x480000).U32(0).U16(1).Zero(32).U16(0x18).U16(0xFFFF).Child(avcC);
        var stsd = new Box("stsd").U32(0).U32(1).Child(avc1);

        var runs = new List<(uint count, uint delta)>();
        foreach (var d in deltas)
            if (runs.Count > 0 && runs[^1].delta == d) runs[^1] = (runs[^1].count + 1, d); else runs.Add((1, d));
        var stts = new Box("stts").U32(0).U32((uint)runs.Count);
        foreach (var (c, d) in runs) stts.U32(c).U32(d);

        var keys = Enumerable.Range(0, n).Where(i => _samples[i].key).ToList();
        var stss = new Box("stss").U32(0).U32((uint)keys.Count);
        foreach (var k in keys) stss.U32((uint)k + 1);

        var stsz = new Box("stsz").U32(0).U32(0).U32((uint)n);
        foreach (var s in _samples) stsz.U32((uint)s.size);
        var stsc = new Box("stsc").U32(0).U32(1).U32(1).U32(1).U32(1);
        var co64 = new Box("co64").U32(0).U32((uint)n);
        foreach (var s in _samples) co64.U64((ulong)s.offset);

        var stbl = new Box("stbl").Child(stsd).Child(stts).Child(stss).Child(stsz).Child(stsc).Child(co64);
        var dinf = new Box("dinf").Child(new Box("dref").U32(0).U32(1).Child(new Box("url ").U32(1)));
        var minf = new Box("minf").Child(new Box("vmhd").U32(1).Zero(8)).Child(dinf).Child(stbl);
        var mdia = new Box("mdia").Child(mdhd).Child(hdlr).Child(minf);
        var trak = new Box("trak").Child(tkhd).Child(mdia);
        return new Box("moov").Child(mvhd).Child(trak);
    }

    /// <summary>A box being assembled in memory (big-endian fields).</summary>
    sealed class Box
    {
        readonly string _type;
        readonly MemoryStream _body = new();
        public Box(string type) => _type = type;

        public Box U8(byte v) { _body.WriteByte(v); return this; }
        public Box U16(ushort v) { Span<byte> b = stackalloc byte[2]; BinaryPrimitives.WriteUInt16BigEndian(b, v); _body.Write(b); return this; }
        public Box U32(uint v) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, v); _body.Write(b); return this; }
        public Box U64(ulong v) { Span<byte> b = stackalloc byte[8]; BinaryPrimitives.WriteUInt64BigEndian(b, v); _body.Write(b); return this; }
        public Box Zero(int n) { _body.Write(new byte[n]); return this; }
        public Box Ascii(string s) { _body.Write(Encoding.ASCII.GetBytes(s)); return this; }
        public Box Bytes(byte[] b) { _body.Write(b); return this; }
        public Box Child(Box c) { c.WriteTo(_body); return this; }

        public void WriteTo(Stream s)
        {
            Span<byte> h = stackalloc byte[8];
            BinaryPrimitives.WriteUInt32BigEndian(h, (uint)(_body.Length + 8));
            Encoding.ASCII.GetBytes(_type, h[4..]);
            s.Write(h);
            _body.Position = 0;
            _body.CopyTo(s);
        }
    }
}
