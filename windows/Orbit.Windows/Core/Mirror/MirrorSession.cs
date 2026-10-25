using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;

namespace Orbit.Core.Mirror;

/// <param name="Camera">"back" or "front" to stream that camera (Android 12+) instead of the screen; null = the screen.</param>
public sealed record MirrorOptions(int MaxSize, int MaxFps, int BitRate, bool Audio, bool KeepAwake, bool ShowTouches, bool HardwareDecode = true,
    string? Camera = null);

/// <summary>
/// One mirroring session, scrcpy-style: push scrcpy-server to the phone, start it through adb shell,
/// reach its sockets through an adb forward, then decode H.264 (and raw PCM audio) and send input back.
/// Protocol: scrcpy doc/develop.md, app/src/demuxer.c and control_msg.c (v5.0.1).
/// </summary>
public sealed class MirrorSession : IDisposable
{
    public const string ServerVersion = "5.0.1";
    const string RemoteJar = "/data/local/tmp/orbit-scrcpy-" + ServerVersion + ".jar";

    readonly Adb _adb;
    public string Serial { get; }
    public MirrorOptions Options { get; }
    readonly CancellationTokenSource _cts = new();
    Process? _server;
    TcpClient? _video, _audio, _control;
    readonly Channel<byte[]> _out = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });
    AudioOut? _audioOut;
    volatile bool _stopping;
    string? _serverError;

    /// <summary>Connecting checklist: 0 = adb reached, 1 = server started, 2 = first frame.</summary>
    public event Action<int>? Progress;
    public event Action? FrameReady;
    public event Action<int, int>? VideoSizeChanged;
    /// <summary>The stream ended without Stop() being called. Argument: reason for the log.</summary>
    public event Action<string>? Ended;
    public event Action<string>? PhoneClipboard;
    /// <summary>Each encoded access unit (Annex-B, config merged in) on the video thread; used for recording.</summary>
    public event PacketHandler? Packet;
    public delegate void PacketHandler(ReadOnlySpan<byte> data, long ptsUs, bool keyFrame);
    /// <summary>The latest SPS/PPS config packet (Annex-B).</summary>
    public byte[]? LastConfig { get; private set; }

    public string DeviceName { get; private set; } = "";
    public int VideoWidth { get; private set; }
    public int VideoHeight { get; private set; }
    public double Fps { get; private set; }
    public int DelayMs { get; private set; }
    public bool AudioActive => _audioOut != null;

    public MirrorSession(Adb adb, string serial, MirrorOptions options)
    {
        _adb = adb; Serial = serial; Options = options;
    }

    // =====================================================================
    // Start / stop
    // =====================================================================

    public async Task StartAsync()
    {
        var ct = _cts.Token;
        var (code, state) = await _adb.RunAsync($"-s {Serial} get-state", 8000);
        if (code != 0 || !state.Contains("device")) throw new IOException("The phone isn't reachable over adb.");
        Progress?.Invoke(0);

        var (_, exists) = await _adb.ShellAsync(Serial, $"\"[ -f {RemoteJar} ] && echo yes\"");
        if (!exists.Contains("yes"))
        {
            var local = Path.Combine(FFmpeg.Dir, "scrcpy-server");
            var (pc, po) = await _adb.RunAsync($"-s {Serial} push \"{local}\" {RemoteJar}", 60000);
            if (pc != 0) throw new IOException("Could not copy the mirror service to the phone: " + po);
        }

        var scid = (Random.Shared.Next() & 0x7FFFFFFF).ToString("x8");
        int port = FreePort();
        var (fc, fo) = await _adb.RunAsync($"-s {Serial} forward tcp:{port} localabstract:scrcpy_{scid}");
        if (fc != 0) throw new IOException("adb forward failed: " + fo);

        try
        {
            StartServer(scid);
            _video = await ConnectFirstAsync(port, ct);
            if (Options.Audio) _audio = await ConnectAsync(port, ct);
            // A camera stream runs without control, as scrcpy's own client does: no input, no clipboard, no screen wake-up.
            if (Options.Camera == null)
            {
                _control = await ConnectAsync(port, ct);
                _control.NoDelay = true;
            }

            var name = new byte[64];
            await ReadExactAsync(_video.GetStream(), name, ct);
            DeviceName = Encoding.UTF8.GetString(name).TrimEnd('\0');
        }
        finally
        {
            _ = _adb.RunAsync($"-s {Serial} forward --remove tcp:{port}");
        }
        Progress?.Invoke(1);

        new Thread(VideoLoop) { IsBackground = true, Name = "mirror-video" }.Start();
        new Thread(ConvertLoop) { IsBackground = true, Name = "mirror-convert" }.Start();
        if (_audio != null) new Thread(AudioLoop) { IsBackground = true, Name = "mirror-audio" }.Start();
        if (_control != null) new Thread(DeviceMessageLoop) { IsBackground = true, Name = "mirror-control-in" }.Start();
        if (_control != null) _ = Task.Run(WriteLoop);
        _ = Task.Run(StatsLoop);
    }

    void StartServer(string scid)
    {
        var o = Options;
        var args = new StringBuilder($"-s {Serial} shell CLASSPATH={RemoteJar} app_process / com.genymobile.scrcpy.Server {ServerVersion}");
        args.Append($" scid={scid} log_level=info tunnel_forward=true video_codec=h264");
        args.Append($" video_bit_rate={o.BitRate} max_fps={o.MaxFps}");
        if (o.MaxSize > 0) args.Append($" max_size={o.MaxSize}");
        if (o.Camera != null) args.Append($" video_source=camera camera_facing={o.Camera} control=false");
        // A camera stream never carries sound: scrcpy would otherwise record the microphone.
        if (o.Audio && o.Camera == null) args.Append(" audio_codec=raw"); else args.Append(" audio=false");
        if (o.KeepAwake) args.Append(" keep_active=true");
        if (o.ShowTouches && o.Camera == null) args.Append(" show_touches=true");

        var psi = new ProcessStartInfo(_adb.ExePath!, args.ToString())
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8
        };
        _server = Process.Start(psi)!;
        _server.EnableRaisingEvents = true;
        DataReceivedEventHandler log = (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;
            Log.Write("mirror server: " + e.Data);
            if (e.Data.Contains("ERROR")) _serverError ??= e.Data;
        };
        _server.OutputDataReceived += log;
        _server.ErrorDataReceived += log;
        _server.BeginOutputReadLine();
        _server.BeginErrorReadLine();
        _server.Exited += (_, _) => Finish("server exited" + (_serverError != null ? ": " + _serverError : ""));
    }

    /// <summary>The first socket proves the server is listening by sending one dummy byte.</summary>
    async Task<TcpClient> ConnectFirstAsync(int port, CancellationToken ct)
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            if (_server is { HasExited: true }) break;
            var c = new TcpClient();
            try
            {
                await c.ConnectAsync(IPAddress.Loopback, port, ct);
                var one = new byte[1];
                using var t = CancellationTokenSource.CreateLinkedTokenSource(ct);
                t.CancelAfter(2000);
                if (await c.GetStream().ReadAsync(one, t.Token) == 1) return c;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
            catch (SocketException) { }
            catch (IOException) { }
            c.Dispose();
            await Task.Delay(100, ct);
        }
        throw new IOException(_serverError ?? "The mirror service on the phone didn't start.");
    }

    static async Task<TcpClient> ConnectAsync(int port, CancellationToken ct)
    {
        var c = new TcpClient();
        await c.ConnectAsync(IPAddress.Loopback, port, ct);
        return c;
    }

    static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int p = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return p;
    }

    public void Stop()
    {
        _stopping = true;
        Teardown();
    }

    void Finish(string reason)
    {
        if (_stopping) return;
        _stopping = true;
        Log.Write("mirror ended: " + reason);
        Teardown();
        Ended?.Invoke(reason);
    }

    void Teardown()
    {
        _cts.Cancel();
        _out.Writer.TryComplete();
        foreach (var c in new[] { _video, _audio, _control }) try { c?.Dispose(); } catch { }
        try { if (_server is { HasExited: false }) _server.Kill(true); } catch { }
        _audioOut?.Dispose();
        _audioOut = null;
    }

    public void Dispose() => Stop();

    // =====================================================================
    // Video
    // =====================================================================

    readonly object _frameLock = new();
    byte[] _back = Array.Empty<byte>(), _front = Array.Empty<byte>();
    int _frontW, _frontH;
    long _frameSeq, _shownSeq;
    int _framesThisSecond;
    long _lagMinCur = long.MaxValue, _lagMinPrev = long.MaxValue, _lagWindowStart, _lastArrivalUs = long.MinValue / 2;
    double _delayEma;
    // Perf counters, logged every 10 s (decode and convert time include the frame-lock wait).
    long _statDecodeTicks, _statTransferTicks, _statConvertTicks, _statLockTicks, _statLagUs;
    bool _gotFirstFrame;

    void VideoLoop()
    {
        try
        {
            var s = _video!.GetStream();
            var hdr = new byte[12];
            ReadExact(s, hdr.AsSpan(0, 4));
            uint codec = BinaryPrimitives.ReadUInt32BigEndian(hdr);
            if (codec == 0) throw new IOException("video disabled by the phone");
            if (codec == 1) throw new IOException(_serverError ?? "video configuration error on the phone");

            ReadExact(s, hdr);
            if ((hdr[0] & 0x80) == 0) throw new IOException("expected a session header");
            SetSize(hdr);

            var decoder = new H264Decoder(tryHardware: Options.HardwareDecode);
            HardwareDecoding = decoder.Hardware;
            Log.Write("mirror decoder: " + (decoder.Hardware ? "D3D11VA (GPU)" : "software"));
            _decoder = decoder;
            byte[] buf = new byte[1 << 20];
            byte[]? config = null;
            var clock = Stopwatch.StartNew();

            while (!_cts.IsCancellationRequested)
            {
                ReadExact(s, hdr);
                if ((hdr[0] & 0x80) != 0) { SetSize(hdr); continue; }

                long ptsFlags = BinaryPrimitives.ReadInt64BigEndian(hdr);
                int len = BinaryPrimitives.ReadInt32BigEndian(hdr.AsSpan(8));
                if (len <= 0) throw new IOException("bad packet length");
                bool isConfig = (ptsFlags & (1L << 62)) != 0;
                int off = isConfig || config == null ? 0 : config.Length;
                if (buf.Length < off + len) buf = new byte[(off + len) * 2];
                ReadExact(s, buf.AsSpan(off, len));

                if (isConfig)
                {
                    config = buf.AsSpan(0, len).ToArray();
                    LastConfig = config;
                    _lagMinCur = _lagMinPrev = long.MaxValue; // a new encoder starts a new timestamp base
                    _delayEma = 0;
                    continue;
                }
                if (config != null) { config.CopyTo(buf, 0); config = null; }

                long ptsUs = ptsFlags & ((1L << 61) - 1);
                Packet?.Invoke(buf.AsSpan(0, off + len), ptsUs, (ptsFlags & (1L << 61)) != 0);

                // Delay estimate: queueing (arrival lag beyond the best of the last 5–10 s) + decode +
                // ~half a display frame. Only sampled while frames flow; an idle screen's first frame
                // carries a stale timestamp. The window baseline keeps clock drift from piling up.
                long nowUs = clock.ElapsedTicks * 1_000_000 / Stopwatch.Frequency;
                long lag = nowUs - ptsUs;
                bool flowing = nowUs - _lastArrivalUs < 100_000;
                _lastArrivalUs = nowUs;
                if (nowUs - _lagWindowStart > 5_000_000) { _lagMinPrev = _lagMinCur; _lagMinCur = long.MaxValue; _lagWindowStart = nowUs; }
                if (lag < _lagMinCur) _lagMinCur = lag;
                long baseline = Math.Min(_lagMinCur, _lagMinPrev);
                _statLagUs = lag - baseline;

                long t0 = Stopwatch.GetTimestamp();
                decoder.Decode(buf.AsSpan(0, off + len), OnDecoded);
                if (decoder.HardwareFailed || _gpuCopyFailed)
                {
                    // The GPU accepted the stream but gave nothing back: decode in software from the next key frame.
                    Log.Write("mirror decoder: D3D11VA produced no frames, switching to software");
                    decoder.Dispose();
                    decoder = _decoder = new H264Decoder(tryHardware: false);
                    HardwareDecoding = false;
                    _gpuCopyFailed = false;
                    Send(Msg.ResetVideo());
                }
                long decodeTicks = Stopwatch.GetTimestamp() - t0;
                Interlocked.Add(ref _statDecodeTicks, decodeTicks);
                double decodeMs = decodeTicks * 1000.0 / Stopwatch.Frequency;
                if (flowing)
                {
                    double sample = (lag - baseline) / 1000.0 + decodeMs + 8;
                    _delayEma = _delayEma == 0 ? sample : _delayEma * 0.9 + sample * 0.1;
                    DelayMs = (int)Math.Round(_delayEma);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException or OperationCanceledException)
        {
            Finish("video: " + ex.Message);
        }
        catch (Exception ex)
        {
            Log.Write("mirror video: " + ex);
            Finish("video: " + ex.Message);
        }
        finally
        {
            _decoder?.Dispose();
            _decoder = null;
            _frameSignal.Set(); // let the conversion thread see the end
        }
    }

    H264Decoder? _decoder;
    volatile bool _gpuCopyFailed;

    // ---- decode → convert handoff: one slot, newest frame wins ----
    readonly object _pendingLock = new();
    DecodedFrame? _pending;
    readonly AutoResetEvent _frameSignal = new(false);
    int _droppedFrames;

    /// <summary>On the video thread: park the picture for the conversion thread, replacing one it hasn't taken.</summary>
    void OnDecoded(DecodedFrame d)
    {
        DecodedFrame? stale;
        lock (_pendingLock) { stale = _pending; _pending = d; }
        if (stale != null) { stale.Dispose(); Interlocked.Increment(ref _droppedFrames); }
        _frameSignal.Set();
    }

    /// <summary>
    /// Copies GPU frames back (if any) and converts to BGRA, always on the newest picture. Decoding never
    /// waits for this, so a slow frame here means a skipped picture, not a growing backlog.
    /// </summary>
    void ConvertLoop()
    {
        try { ConvertLoopCore(); }
        catch (Exception ex)
        {
            // Never let this thread take the app down; end the stream instead.
            Log.Write("mirror convert: " + ex);
            Finish("convert: " + ex.Message);
        }
    }

    void ConvertLoopCore()
    {
        using var reader = new FrameReader();
        while (!_cts.IsCancellationRequested)
        {
            _frameSignal.WaitOne(500);
            DecodedFrame? d;
            lock (_pendingLock) { d = _pending; _pending = null; }
            if (d == null) continue;
            using (d)
            {
                long tr0 = reader.TransferTicks;
                if (reader.TryRead(d, out var yuv)) OnFrame(yuv);
                Interlocked.Add(ref _statTransferTicks, reader.TransferTicks - tr0);
                if (reader.TransferErrors > 5) _gpuCopyFailed = true;
            }
        }
        lock (_pendingLock) { _pending?.Dispose(); _pending = null; }
    }
    /// <summary>Video is decoded on the GPU (D3D11VA).</summary>
    public bool HardwareDecoding { get; private set; }

    void SetSize(byte[] hdr)
    {
        int w = BinaryPrimitives.ReadInt32BigEndian(hdr.AsSpan(4));
        int h = BinaryPrimitives.ReadInt32BigEndian(hdr.AsSpan(8));
        if (w == VideoWidth && h == VideoHeight) return;
        VideoWidth = w; VideoHeight = h;
        Log.Write($"mirror video size {w}x{h}");
        VideoSizeChanged?.Invoke(w, h);
    }

    unsafe void OnFrame(YuvFrame f)
    {
        int need = f.Width * f.Height * 4;
        if (_back.Length != need) _back = new byte[need];
        long t0 = Stopwatch.GetTimestamp();
        fixed (byte* p = _back) Yuv.ToBgra(f, p, f.Width * 4);
        long t1 = Stopwatch.GetTimestamp();
        lock (_frameLock)
        {
            (_back, _front) = (_front, _back);
            _frontW = f.Width; _frontH = f.Height;
            _frameSeq++;
        }
        long t2 = Stopwatch.GetTimestamp();
        Interlocked.Add(ref _statConvertTicks, t1 - t0);
        Interlocked.Add(ref _statLockTicks, t2 - t1);
        _framesThisSecond++;
        if (!_gotFirstFrame) { _gotFirstFrame = true; Progress?.Invoke(2); }
        FrameReady?.Invoke();
    }

    /// <summary>Hands the newest frame to <paramref name="copy"/> (pixels, width, height) if there is one not shown yet.</summary>
    public bool TakeFrame(Action<byte[], int, int> copy)
    {
        lock (_frameLock)
        {
            if (_frameSeq == _shownSeq || _front.Length == 0) return false;
            _shownSeq = _frameSeq;
            copy(_front, _frontW, _frontH);
            return true;
        }
    }

    async Task StatsLoop()
    {
        var sw = Stopwatch.StartNew();
        int tick = 0, frames = 0;
        while (!_cts.IsCancellationRequested)
        {
            try { await Task.Delay(1000, _cts.Token); } catch { break; }
            int n = Interlocked.Exchange(ref _framesThisSecond, 0);
            Fps = Math.Round(n / sw.Elapsed.TotalSeconds);
            sw.Restart();
            frames += n;
            if (++tick % 10 != 0 || frames == 0) continue;
            int dropped = Interlocked.Exchange(ref _droppedFrames, 0);
            double ms(ref long ticks, int n) => Interlocked.Exchange(ref ticks, 0) * 1000.0 / Stopwatch.Frequency / Math.Max(1, n);
            Log.Write($"mirror perf: {VideoWidth}x{VideoHeight} {(HardwareDecoding ? "gpu" : "cpu")}, {frames / 10.0:0.#} fps shown, {dropped} skipped, " +
                      $"decode {ms(ref _statDecodeTicks, frames + dropped):0.0} ms, gpu copy {ms(ref _statTransferTicks, frames):0.0} ms, " +
                      $"convert {ms(ref _statConvertTicks, frames):0.0} ms, lock {ms(ref _statLockTicks, frames):0.0} ms, queue {_statLagUs / 1000} ms, shown {DelayMs} ms");
            frames = 0;
        }
    }

    // =====================================================================
    // Audio (raw 48 kHz stereo s16le)
    // =====================================================================

    void AudioLoop()
    {
        try
        {
            var s = _audio!.GetStream();
            var hdr = new byte[12];
            ReadExact(s, hdr.AsSpan(0, 4));
            uint codec = BinaryPrimitives.ReadUInt32BigEndian(hdr);
            if (codec is 0 or 1) { Log.Write("mirror audio: not available on this phone"); return; }
            _audioOut = new AudioOut();
            var buf = new byte[64 * 1024];
            while (!_cts.IsCancellationRequested)
            {
                ReadExact(s, hdr);
                int len = BinaryPrimitives.ReadInt32BigEndian(hdr.AsSpan(8));
                if (buf.Length < len) buf = new byte[len];
                ReadExact(s, buf.AsSpan(0, len));
                if ((BinaryPrimitives.ReadInt64BigEndian(hdr) & (1L << 62)) != 0) continue; // config
                _audioOut?.Write(buf, len);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException or OperationCanceledException) { }
        catch (Exception ex) { Log.Write("mirror audio: " + ex.Message); }
    }

    // =====================================================================
    // Control
    // =====================================================================

    public void Send(byte[] msg) => _out.Writer.TryWrite(msg);

    async Task WriteLoop()
    {
        try
        {
            var s = _control!.GetStream();
            await foreach (var m in _out.Reader.ReadAllAsync(_cts.Token)) await s.WriteAsync(m, _cts.Token);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException or SocketException) { }
    }

    void DeviceMessageLoop()
    {
        try
        {
            var s = _control!.GetStream();
            var b = new byte[8];
            while (!_cts.IsCancellationRequested)
            {
                ReadExact(s, b.AsSpan(0, 1));
                switch (b[0])
                {
                    case 0: // clipboard
                        ReadExact(s, b.AsSpan(0, 4));
                        var text = new byte[BinaryPrimitives.ReadInt32BigEndian(b)];
                        ReadExact(s, text);
                        PhoneClipboard?.Invoke(Encoding.UTF8.GetString(text));
                        break;
                    case 1: ReadExact(s, b.AsSpan(0, 8)); break; // clipboard ack
                    case 2: // uhid output
                        ReadExact(s, b.AsSpan(0, 4));
                        ReadExact(s, new byte[BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(2))]);
                        break;
                    default: throw new IOException("unknown device message " + b[0]);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException) { }
    }

    // =====================================================================

    static void ReadExact(Stream s, Span<byte> buf)
    {
        while (buf.Length > 0)
        {
            int n = s.Read(buf);
            if (n <= 0) throw new EndOfStreamException("stream closed");
            buf = buf[n..];
        }
    }

    static async Task ReadExactAsync(Stream s, byte[] buf, CancellationToken ct)
    {
        int got = 0;
        while (got < buf.Length)
        {
            int n = await s.ReadAsync(buf.AsMemory(got), ct);
            if (n <= 0) throw new EndOfStreamException("stream closed");
            got += n;
        }
    }
}
