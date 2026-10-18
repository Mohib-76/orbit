using System.IO;
using System.Runtime.InteropServices;

namespace Orbit.Core.Mirror;

/// <summary>
/// The handful of FFmpeg calls the mirror needs, bound by hand against the DLLs scrcpy ships
/// (ThirdParty/scrcpy). Only the head of AVFrame and AVOption are read directly; both are stable.
/// </summary>
static unsafe class FFmpeg
{
    const string AvCodec = "avcodec-63", AvUtil = "avutil-61";
    public const int CodecH264 = 27;
    public const int EAgain = -11;        // AVERROR(EAGAIN) on Windows
    public const int PixFmtYuv420p = 0, PixFmtYuvj420p = 12;
    public const int InputPadding = 64;   // AV_INPUT_BUFFER_PADDING_SIZE

    public static string Dir => Path.Combine(AppContext.BaseDirectory, "scrcpy");
    static bool _loaded;

    /// <summary>Loads the DLLs from the scrcpy folder (they depend on each other by bare name).</summary>
    public static void Load()
    {
        lock (typeof(FFmpeg)) LoadCore();
    }

    static void LoadCore()
    {
        if (_loaded) return;
        var handles = new Dictionary<string, IntPtr>();
        foreach (var name in new[] { "avutil-61", "swresample-7", "avcodec-63" })
            handles[name] = NativeLibrary.Load(Path.Combine(Dir, name + ".dll"));
        NativeLibrary.SetDllImportResolver(typeof(FFmpeg).Assembly, (lib, _, _) =>
            handles.TryGetValue(lib, out var h) ? h : IntPtr.Zero);
        _loaded = true;
    }

    [DllImport(AvCodec)] public static extern IntPtr avcodec_find_decoder(int id);
    [DllImport(AvCodec)] public static extern IntPtr avcodec_alloc_context3(IntPtr codec);
    [DllImport(AvCodec)] public static extern int avcodec_open2(IntPtr ctx, IntPtr codec, IntPtr options);
    [DllImport(AvCodec)] public static extern void avcodec_free_context(ref IntPtr ctx);
    [DllImport(AvCodec)] public static extern int avcodec_send_packet(IntPtr ctx, IntPtr pkt);
    [DllImport(AvCodec)] public static extern int avcodec_receive_frame(IntPtr ctx, IntPtr frame);
    [DllImport(AvCodec)] public static extern IntPtr av_packet_alloc();
    [DllImport(AvCodec)] public static extern void av_packet_free(ref IntPtr pkt);
    [DllImport(AvCodec)] public static extern int av_packet_from_data(IntPtr pkt, IntPtr data, int size);
    [DllImport(AvCodec)] public static extern void av_packet_unref(IntPtr pkt);
    [DllImport(AvUtil)] public static extern IntPtr av_frame_alloc();
    [DllImport(AvUtil)] public static extern void av_frame_free(ref IntPtr frame);
    [DllImport(AvUtil)] public static extern void av_frame_unref(IntPtr frame);
    [DllImport(AvUtil)] public static extern IntPtr av_malloc(nuint size);
    [DllImport(AvUtil)] public static extern void av_free(IntPtr p);
    [DllImport(AvUtil, CharSet = CharSet.Ansi)] public static extern int av_opt_set(IntPtr obj, string name, string val, int searchFlags);
    [DllImport(AvUtil, CharSet = CharSet.Ansi)] public static extern IntPtr av_opt_find(IntPtr obj, string name, string? unit, int optFlags, int searchFlags);
    [DllImport(AvUtil, CharSet = CharSet.Ansi)] public static extern int av_get_pix_fmt(string name);
    [DllImport(AvUtil, CharSet = CharSet.Ansi)] public static extern int av_hwdevice_find_type_by_name(string name);
    [DllImport(AvUtil, CharSet = CharSet.Ansi)] public static extern int av_hwdevice_ctx_create(out IntPtr deviceCtx, int type, string? device, IntPtr opts, int flags);
    [DllImport(AvUtil)] public static extern int av_hwframe_transfer_data(IntPtr dst, IntPtr src, int flags);
    [DllImport(AvUtil)] public static extern IntPtr av_buffer_ref(IntPtr buf);
    [DllImport(AvUtil)] public static extern void av_buffer_unref(ref IntPtr buf);
    [DllImport(AvUtil)] public static extern IntPtr av_frame_clone(IntPtr src);

    /// <summary>Head of struct AVFrame: data[8], linesize[8], extended_data, width, height, nb_samples, format.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct FrameHead
    {
        public fixed long Data[8];
        public fixed int Linesize[8];
        public IntPtr ExtendedData;
        public int Width, Height, NbSamples, Format;
    }

    /// <summary>The byte offset of an option's field in its struct (AVOption: name, help, offset…).</summary>
    public static int OptionOffset(IntPtr obj, string name)
    {
        var o = av_opt_find(obj, name, null, 0, 0);
        return o == IntPtr.Zero ? -1 : *(int*)((byte*)o + 16);
    }
}

/// <summary>
/// A decoded 4:2:0 picture: planar (I420: Y, U, V) or semi-planar (NV12: Y, interleaved UV in U).
/// The plane pointers stay valid until the next Decode call.
/// </summary>
public readonly unsafe struct YuvFrame
{
    public readonly byte* Y, U, V;
    public readonly int StrideY, StrideU, StrideV, Width, Height;
    public readonly bool Nv12;
    public YuvFrame(byte* y, byte* u, byte* v, int sy, int su, int sv, int w, int h, bool nv12 = false)
    { Y = y; U = u; V = v; StrideY = sy; StrideU = su; StrideV = sv; Width = w; Height = h; Nv12 = nv12; }
}

/// <summary>
/// One decoded picture, held by reference (no pixel copy): a software I420 frame or a D3D11 GPU surface.
/// Whoever ends up with it disposes it.
/// </summary>
public sealed unsafe class DecodedFrame : IDisposable
{
    IntPtr _frame;
    public DecodedFrame(IntPtr frame) => _frame = frame;
    internal FFmpeg.FrameHead* Head => (FFmpeg.FrameHead*)_frame;
    internal IntPtr Ptr => _frame;
    public void Dispose() { if (_frame != IntPtr.Zero) FFmpeg.av_frame_free(ref _frame); }
}

/// <summary>
/// Gives the planes of a <see cref="DecodedFrame"/>, copying GPU surfaces back to memory first (NV12).
/// Used on the conversion thread, so that copy never holds up decoding.
/// </summary>
public sealed unsafe class FrameReader : IDisposable
{
    IntPtr _sw;
    public long TransferTicks;

    public FrameReader()
    {
        FFmpeg.Load();
        _sw = FFmpeg.av_frame_alloc();
    }

    public int TransferErrors;

    /// <summary>The frame's planes, valid until the next call (and while <paramref name="d"/> lives).</summary>
    public bool TryRead(DecodedFrame d, out YuvFrame yuv)
    {
        yuv = default;
        FFmpeg.av_frame_unref(_sw);
        var f = d.Head;
        if (f->Format == H264Decoder.PixD3D11)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            int r = FFmpeg.av_hwframe_transfer_data(_sw, d.Ptr, 0);
            TransferTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
            var s = (FFmpeg.FrameHead*)_sw;
            if (r < 0 || s->Format != H264Decoder.PixNv12) { TransferErrors++; return false; }
            yuv = new YuvFrame((byte*)s->Data[0], (byte*)s->Data[1], null, s->Linesize[0], s->Linesize[1], 0, f->Width, f->Height, nv12: true);
            return true;
        }
        if (f->Format is FFmpeg.PixFmtYuv420p or FFmpeg.PixFmtYuvj420p)
        {
            yuv = new YuvFrame((byte*)f->Data[0], (byte*)f->Data[1], (byte*)f->Data[2],
                f->Linesize[0], f->Linesize[1], f->Linesize[2], f->Width, f->Height);
            return true;
        }
        return false;
    }

    public void Dispose() { if (_sw != IntPtr.Zero) FFmpeg.av_frame_free(ref _sw); }
}

/// <summary>
/// H.264 decoder set up for low latency like scrcpy's. Decodes on the GPU through D3D11VA when it can,
/// otherwise in software. Pictures come out as <see cref="DecodedFrame"/> references.
/// </summary>
public sealed unsafe class H264Decoder : IDisposable
{
    IntPtr _ctx, _pkt, _frame, _device;
    int _packets, _frames;
    static readonly Lazy<(int d3d11, int nv12)> Formats = new(() => (FFmpeg.av_get_pix_fmt("d3d11"), FFmpeg.av_get_pix_fmt("nv12")));
    internal static int PixD3D11 => Formats.Value.d3d11;
    internal static int PixNv12 => Formats.Value.nv12;

    /// <summary>Frames are decoded by the GPU.</summary>
    public bool Hardware { get; }
    /// <summary>The GPU path produced nothing; the caller should switch to a software decoder.</summary>
    public bool HardwareFailed => Hardware && _frames == 0 && _packets > 60;

    public H264Decoder(bool tryHardware)
    {
        FFmpeg.Load();
        var codec = FFmpeg.avcodec_find_decoder(FFmpeg.CodecH264);
        if (codec == IntPtr.Zero) throw new InvalidOperationException("H.264 decoder missing");
        _ctx = FFmpeg.avcodec_alloc_context3(codec);
        FFmpeg.av_opt_set(_ctx, "flags", "+low_delay", 0);
        if (tryHardware)
        {
            Hardware = AttachD3D11();
            // The conversion thread holds on to a surface or two while the decoder carries on.
            if (Hardware) FFmpeg.av_opt_set(_ctx, "extra_hw_frames", "4", 0);
        }
        if (FFmpeg.avcodec_open2(_ctx, codec, IntPtr.Zero) < 0) throw new InvalidOperationException("Could not open the H.264 decoder");
        _pkt = FFmpeg.av_packet_alloc();
        _frame = FFmpeg.av_frame_alloc();
    }

    /// <summary>
    /// Sets AVCodecContext.hw_device_ctx to a D3D11VA device; the default get_format then picks the GPU
    /// format. hw_device_ctx has no AVOption, so its offset comes from its neighbours, which do:
    /// "AVBufferRef *hw_frames_ctx; AVBufferRef *hw_device_ctx; int hwaccel_flags; int extra_hw_frames;".
    /// </summary>
    bool AttachD3D11()
    {
        try
        {
            int flagsOff = FFmpeg.OptionOffset(_ctx, "hwaccel_flags");
            int extraOff = FFmpeg.OptionOffset(_ctx, "extra_hw_frames");
            if (flagsOff < 16 || extraOff != flagsOff + 4) { Log.Write("mirror decoder: unexpected AVCodecContext layout, using software"); return false; }
            var slot = (IntPtr*)((byte*)_ctx + flagsOff - 8);
            if (*slot != IntPtr.Zero || *(slot - 1) != IntPtr.Zero) { Log.Write("mirror decoder: hw_device_ctx slot not empty, using software"); return false; }

            int type = FFmpeg.av_hwdevice_find_type_by_name("d3d11va");
            if (type <= 0 || PixD3D11 < 0 || PixNv12 < 0) return false;
            int r = FFmpeg.av_hwdevice_ctx_create(out _device, type, null, IntPtr.Zero, 0);
            if (r < 0) { Log.Write($"mirror decoder: no D3D11VA device ({r}), using software"); return false; }
            *slot = FFmpeg.av_buffer_ref(_device);
            return true;
        }
        catch (Exception ex) { Log.Write("mirror decoder: " + ex.Message); return false; }
    }

    /// <summary>Feeds one access unit; hands each picture that comes out to <paramref name="onFrame"/>, which owns it.</summary>
    public void Decode(ReadOnlySpan<byte> data, Action<DecodedFrame> onFrame)
    {
        _packets++;
        var buf = FFmpeg.av_malloc((nuint)(data.Length + FFmpeg.InputPadding));
        if (buf == IntPtr.Zero) throw new OutOfMemoryException();
        var dst = new Span<byte>((void*)buf, data.Length + FFmpeg.InputPadding);
        data.CopyTo(dst);
        dst[data.Length..].Clear();
        if (FFmpeg.av_packet_from_data(_pkt, buf, data.Length) < 0) { FFmpeg.av_free(buf); return; }
        int r = FFmpeg.avcodec_send_packet(_ctx, _pkt);
        FFmpeg.av_packet_unref(_pkt);
        if (r < 0 && r != FFmpeg.EAgain) return; // a corrupt packet; the next key frame recovers

        while (FFmpeg.avcodec_receive_frame(_ctx, _frame) == 0)
        {
            _frames++;
            var clone = FFmpeg.av_frame_clone(_frame); // a new reference, not a pixel copy
            FFmpeg.av_frame_unref(_frame);
            if (clone != IntPtr.Zero) onFrame(new DecodedFrame(clone));
        }
    }

    public void Dispose()
    {
        if (_frame != IntPtr.Zero) FFmpeg.av_frame_free(ref _frame);
        if (_pkt != IntPtr.Zero) FFmpeg.av_packet_free(ref _pkt);
        if (_ctx != IntPtr.Zero) FFmpeg.avcodec_free_context(ref _ctx); // also unrefs hw_device_ctx
        if (_device != IntPtr.Zero) FFmpeg.av_buffer_unref(ref _device);
    }
}
