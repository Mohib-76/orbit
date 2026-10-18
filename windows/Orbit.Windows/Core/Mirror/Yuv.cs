using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Orbit.Core.Mirror;

/// <summary>
/// YUV 4:2:0 (limited range, BT.709) to BGRA32, in parallel bands of rows. SSE4.1 handles 8 pixels a
/// step in 16-bit fixed point (×64, saturating); a scalar loop does the rest.
/// </summary>
static unsafe class Yuv
{
    // ×64 coefficients: V→R 1.793, U→G 0.213, V→G 0.533, U→B 2.112. Luma (1.164) is ×128 = 149 then
    // halved: 239 × 149 overflows a short, so it's done unsigned. +32 rounds the final >> 6.
    const short CY2 = 149, CVR = 115, CUG = 14, CVG = 34, CUB = 135, Round = 32;

    public static void ToBgra(in YuvFrame f, byte* dst, int dstStride)
    {
        int rowPairs = (f.Height + 1) / 2;
        int bands = Math.Clamp(Environment.ProcessorCount, 1, 8);
        int per = (rowPairs + bands - 1) / bands;
        var frame = f;
        var d = (IntPtr)dst;
        Parallel.For(0, bands, b =>
        {
            int from = b * per, to = Math.Min(rowPairs, from + per);
            for (int pair = from; pair < to; pair++)
            {
                int y0 = pair * 2;
                ConvertRow(frame, y0, (byte*)d + (long)y0 * dstStride);
                if (y0 + 1 < frame.Height) ConvertRow(frame, y0 + 1, (byte*)d + (long)(y0 + 1) * dstStride);
            }
        });
    }

    static void ConvertRow(in YuvFrame f, int y, byte* o)
    {
        if (f.Nv12) { ConvertRowNv12(f, y, o); return; }
        byte* py = f.Y + (long)y * f.StrideY;
        byte* pu = f.U + (long)(y >> 1) * f.StrideU;
        byte* pv = f.V + (long)(y >> 1) * f.StrideV;
        int w = f.Width, x = 0;

        if (Sse41.IsSupported)
        {
            var c16 = Vector128.Create((short)16);
            var c128 = Vector128.Create((short)128);
            var cy = Vector128.Create(CY2);
            var round = Vector128.Create(Round);
            var cvr = Vector128.Create(CVR);
            var cug = Vector128.Create(CUG);
            var cvg = Vector128.Create(CVG);
            var cub = Vector128.Create(CUB);
            var alpha = Vector128.Create((byte)255);
            for (; x + 8 <= w; x += 8)
            {
                var yv = Sse41.ConvertToVector128Int16(py + x);                       // 8 × Y
                var uq = Sse41.ConvertToVector128Int16(Vector128.CreateScalarUnsafe(*(int*)(pu + (x >> 1))).AsByte());
                var vq = Sse41.ConvertToVector128Int16(Vector128.CreateScalarUnsafe(*(int*)(pv + (x >> 1))).AsByte());
                var u = Sse2.Subtract(Sse2.UnpackLow(uq, uq), c128);                 // each chroma sample covers 2 pixels
                var v = Sse2.Subtract(Sse2.UnpackLow(vq, vq), c128);
                // Y below 16 (it happens) would wrap the unsigned multiply, so clamp it first.
                var l = Sse2.Add(Sse2.ShiftRightLogical(Sse2.MultiplyLow(Sse2.Subtract(Sse2.Max(yv, c16), c16), cy).AsUInt16(), 1).AsInt16(), round);

                var r = Sse2.ShiftRightArithmetic(Sse2.AddSaturate(l, Sse2.MultiplyLow(v, cvr)), 6);
                var g = Sse2.ShiftRightArithmetic(Sse2.SubtractSaturate(Sse2.SubtractSaturate(l, Sse2.MultiplyLow(u, cug)), Sse2.MultiplyLow(v, cvg)), 6);
                var b = Sse2.ShiftRightArithmetic(Sse2.AddSaturate(l, Sse2.MultiplyLow(u, cub)), 6);

                var b8 = Sse2.PackUnsignedSaturate(b, b);
                var g8 = Sse2.PackUnsignedSaturate(g, g);
                var r8 = Sse2.PackUnsignedSaturate(r, r);
                var bg = Sse2.UnpackLow(b8, g8).AsInt16();
                var ra = Sse2.UnpackLow(r8, alpha).AsInt16();
                Sse2.Store(o, Sse2.UnpackLow(bg, ra).AsByte());
                Sse2.Store(o + 16, Sse2.UnpackHigh(bg, ra).AsByte());
                o += 32;
            }
        }

        for (; x < w; x++)
        {
            int u = pu[x >> 1] - 128, v = pv[x >> 1] - 128, l = (CY2 * Math.Max(0, py[x] - 16) >> 1) + Round;
            o[0] = Clamp((l + CUB * u) >> 6);
            o[1] = Clamp((l - CUG * u - CVG * v) >> 6);
            o[2] = Clamp((l + CVR * v) >> 6);
            o[3] = 255;
            o += 4;
        }
    }

    /// <summary>NV12 (what D3D11VA hands back): chroma is one plane of interleaved U,V pairs.</summary>
    static void ConvertRowNv12(in YuvFrame f, int y, byte* o)
    {
        byte* py = f.Y + (long)y * f.StrideY;
        byte* puv = f.U + (long)(y >> 1) * f.StrideU;
        int w = f.Width, x = 0;

        if (Ssse3.IsSupported && Sse41.IsSupported)
        {
            var c16 = Vector128.Create((short)16);
            var c128 = Vector128.Create((short)128);
            var cy = Vector128.Create(CY2);
            var round = Vector128.Create(Round);
            var cvr = Vector128.Create(CVR);
            var cug = Vector128.Create(CUG);
            var cvg = Vector128.Create(CVG);
            var cub = Vector128.Create(CUB);
            var alpha = Vector128.Create((byte)255);
            // From u0 v0 u1 v1 u2 v2 u3 v3: each chroma value twice (it covers 2 pixels).
            var takeU = Vector128.Create((byte)0, 0, 2, 2, 4, 4, 6, 6, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);
            var takeV = Vector128.Create((byte)1, 1, 3, 3, 5, 5, 7, 7, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);
            for (; x + 8 <= w; x += 8)
            {
                var yv = Sse41.ConvertToVector128Int16(py + x);
                var uv = Vector128.CreateScalarUnsafe(*(long*)(puv + x)).AsByte();
                var u = Sse2.Subtract(Sse41.ConvertToVector128Int16(Ssse3.Shuffle(uv, takeU)), c128);
                var v = Sse2.Subtract(Sse41.ConvertToVector128Int16(Ssse3.Shuffle(uv, takeV)), c128);
                var l = Sse2.Add(Sse2.ShiftRightLogical(Sse2.MultiplyLow(Sse2.Subtract(Sse2.Max(yv, c16), c16), cy).AsUInt16(), 1).AsInt16(), round);

                var r = Sse2.ShiftRightArithmetic(Sse2.AddSaturate(l, Sse2.MultiplyLow(v, cvr)), 6);
                var g = Sse2.ShiftRightArithmetic(Sse2.SubtractSaturate(Sse2.SubtractSaturate(l, Sse2.MultiplyLow(u, cug)), Sse2.MultiplyLow(v, cvg)), 6);
                var b = Sse2.ShiftRightArithmetic(Sse2.AddSaturate(l, Sse2.MultiplyLow(u, cub)), 6);

                var bg = Sse2.UnpackLow(Sse2.PackUnsignedSaturate(b, b), Sse2.PackUnsignedSaturate(g, g)).AsInt16();
                var ra = Sse2.UnpackLow(Sse2.PackUnsignedSaturate(r, r), alpha).AsInt16();
                Sse2.Store(o, Sse2.UnpackLow(bg, ra).AsByte());
                Sse2.Store(o + 16, Sse2.UnpackHigh(bg, ra).AsByte());
                o += 32;
            }
        }

        for (; x < w; x++)
        {
            int u = puv[x & ~1] - 128, v = puv[(x & ~1) + 1] - 128, l = (CY2 * Math.Max(0, py[x] - 16) >> 1) + Round;
            o[0] = Clamp((l + CUB * u) >> 6);
            o[1] = Clamp((l - CUG * u - CVG * v) >> 6);
            o[2] = Clamp((l + CVR * v) >> 6);
            o[3] = 255;
            o += 4;
        }
    }

    static byte Clamp(int v) => (byte)(v < 0 ? 0 : v > 255 ? 255 : v);
}
