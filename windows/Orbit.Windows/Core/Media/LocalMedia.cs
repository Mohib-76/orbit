using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Orbit.Core.Media;

/// <summary>
/// This PC's media sessions (the ones Windows shows in its media flyout), through
/// GlobalSystemMediaTransportControlsSessionManager. Orbit's own session (the phone's player) is left out.
/// <see cref="Changed"/> is raised on a thread-pool thread.
/// </summary>
public sealed class LocalMedia
{
    sealed class Tracked
    {
        public required GlobalSystemMediaTransportControlsSession Session;
        public required string Id;
        public required string App;
        public string Title = "", Artist = "", Album = "", Art = "";
        public byte[]? ArtJpeg;
    }

    GlobalSystemMediaTransportControlsSessionManager? _mgr;
    readonly object _gate = new();
    List<Tracked> _tracked = new();

    public event Action? Changed;
    public bool Available => _mgr != null;

    public async Task StartAsync()
    {
        try { _mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync(); }
        catch (Exception ex) { Log.Write("media: " + ex.Message); return; }
        _mgr.SessionsChanged += (_, _) => _ = ResyncAsync();
        _mgr.CurrentSessionChanged += (_, _) => Changed?.Invoke();
        await ResyncAsync();
    }

    async Task ResyncAsync()
    {
        if (_mgr == null) return;
        IReadOnlyList<GlobalSystemMediaTransportControlsSession> sessions;
        try { sessions = _mgr.GetSessions(); } catch { return; }

        var next = new List<Tracked>();
        var seen = new Dictionary<string, int>();
        foreach (var s in sessions)
        {
            string aumid;
            try { aumid = s.SourceAppUserModelId ?? ""; } catch { continue; }
            if (IsOwn(aumid)) continue;
            // Several sessions can share an app (browser tabs): make ids unique like "chrome.exe#2".
            seen[aumid] = seen.TryGetValue(aumid, out var n) ? n + 1 : 1;
            var id = seen[aumid] == 1 ? aumid : $"{aumid}#{seen[aumid]}";
            Tracked? old;
            lock (_gate) old = _tracked.FirstOrDefault(t => t.Session == s);
            if (old != null) { next.Add(old); continue; }
            var t = new Tracked { Session = s, Id = id, App = AppName(aumid) };
            s.MediaPropertiesChanged += (_, _) => _ = RefreshPropertiesAsync(t);
            s.PlaybackInfoChanged += (_, _) => Changed?.Invoke();
            s.TimelinePropertiesChanged += (_, _) => Changed?.Invoke();
            next.Add(t);
            _ = RefreshPropertiesAsync(t);
        }
        lock (_gate) _tracked = next;
        Changed?.Invoke();
    }

    async Task RefreshPropertiesAsync(Tracked t)
    {
        try
        {
            // Can throw "Element not found" while an app is switching tracks.
            var p = await t.Session.TryGetMediaPropertiesAsync();
            t.Title = p.Title ?? ""; t.Artist = p.Artist ?? ""; t.Album = p.AlbumTitle ?? "";
            var (hash, jpeg) = await ReadArtAsync(p.Thumbnail);
            t.Art = hash; t.ArtJpeg = jpeg;
        }
        catch (COMException) { }
        catch (Exception ex) { Log.Write("media properties: " + ex.GetType().Name); }
        Changed?.Invoke();
    }

    static async Task<(string hash, byte[]? jpeg)> ReadArtAsync(IRandomAccessStreamReference? thumb)
    {
        if (thumb == null) return ("", null);
        try
        {
            using var stream = await thumb.OpenReadAsync();
            if (stream.Size == 0 || stream.Size > 16_000_000) return ("", null);
            var buf = new Windows.Storage.Streams.Buffer((uint)stream.Size);
            var read = await stream.ReadAsync(buf, (uint)stream.Size, InputStreamOptions.None);
            var bytes = new byte[read.Length];
            using (var reader = DataReader.FromBuffer(read)) reader.ReadBytes(bytes);
            return (Hash(bytes), ToJpeg(bytes, 320));
        }
        catch { return ("", null); }
    }

    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA1.HashData(bytes))[..12].ToLowerInvariant();

    /// <summary>Re-encodes an image as a JPEG no bigger than <paramref name="max"/> px on its longer side.</summary>
    public static byte[]? ToJpeg(byte[] image, int max)
    {
        try
        {
            using var ms = new MemoryStream(image);
            var frame = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            BitmapSource src = frame;
            double scale = Math.Min(1.0, (double)max / Math.Max(frame.PixelWidth, frame.PixelHeight));
            if (scale < 1) src = new TransformedBitmap(frame, new System.Windows.Media.ScaleTransform(scale, scale));
            var enc = new JpegBitmapEncoder { QualityLevel = 85 };
            enc.Frames.Add(BitmapFrame.Create(src));
            using var outMs = new MemoryStream();
            enc.Save(outMs);
            return outMs.ToArray();
        }
        catch { return null; }
    }

    /// <summary>The current state of every session, positions as of now.</summary>
    public MediaSnapshot Snapshot()
    {
        List<Tracked> list;
        lock (_gate) list = _tracked.ToList();
        var players = new List<PlayerInfo>();
        foreach (var t in list)
        {
            try
            {
                var info = t.Session.GetPlaybackInfo();
                var c = info.Controls;
                bool playing = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                var tl = t.Session.GetTimelineProperties();
                long len = (long)(tl.EndTime - tl.StartTime).TotalMilliseconds;
                long pos = (long)(tl.Position - tl.StartTime).TotalMilliseconds;
                if (playing && tl.LastUpdatedTime.Year > 2000)
                    pos += (long)(DateTimeOffset.Now - tl.LastUpdatedTime).TotalMilliseconds;
                if (len <= 0) len = 0; else pos = Math.Clamp(pos, 0, len);
                if (t.Title.Length == 0 && !playing && info.PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused) continue;
                players.Add(new PlayerInfo(t.Id, t.App, t.Title, t.Artist, t.Album, playing, Math.Max(0, pos), len,
                    c.IsPlayEnabled, c.IsPauseEnabled, c.IsNextEnabled, c.IsPreviousEnabled, c.IsPlaybackPositionEnabled && len > 0, t.Art));
            }
            catch { }
        }
        string? active = null;
        try
        {
            var cur = _mgr?.GetCurrentSession();
            active = list.FirstOrDefault(t => t.Session == cur)?.Id;
        }
        catch { }
        return new MediaSnapshot(players, active, true, DateTime.UtcNow);
    }

    public byte[]? ArtFor(string hash)
    {
        lock (_gate) return _tracked.FirstOrDefault(t => t.Art == hash && hash.Length > 0)?.ArtJpeg;
    }

    /// <summary>True while any of this PC's own players is playing (used by the buds' "Play here").</summary>
    public bool AnyPlaying()
    {
        List<Tracked> list;
        lock (_gate) list = _tracked.ToList();
        foreach (var t in list)
            try { if (t.Session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing) return true; }
            catch { }
        return false;
    }

    GlobalSystemMediaTransportControlsSession? Find(string? id)
    {
        lock (_gate)
        {
            if (id == null)
            {
                try
                {
                    var cur = _mgr?.GetCurrentSession();
                    if (cur != null && _tracked.Any(t => t.Session == cur)) return cur;
                }
                catch { }
                return _tracked.FirstOrDefault()?.Session;
            }
            return _tracked.FirstOrDefault(t => t.Id == id)?.Session;
        }
    }

    /// <summary>Runs a remote command. <paramref name="id"/> null = the current session.</summary>
    public async Task CommandAsync(string? id, string op, long posMs = 0)
    {
        var s = Find(id);
        if (s == null) return;
        try
        {
            switch (op)
            {
                case "play": await s.TryPlayAsync(); break;
                case "pause": await s.TryPauseAsync(); break;
                case "toggle": await s.TryTogglePlayPauseAsync(); break;
                case "next": await s.TrySkipNextAsync(); break;
                case "prev": await s.TrySkipPreviousAsync(); break;
                case "seek":
                    var start = s.GetTimelineProperties().StartTime;
                    await s.TryChangePlaybackPositionAsync((start + TimeSpan.FromMilliseconds(posMs)).Ticks);
                    break;
            }
        }
        catch (Exception ex) { Log.Write($"media {op}: {ex.GetType().Name}"); }
        Changed?.Invoke();
    }

    // ---------------------------------------------------------------- names

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int GetCurrentProcessExplicitAppUserModelID(out IntPtr appId);

    static readonly string? OwnAumid = ReadOwnAumid();
    static readonly string OwnExe = Path.GetFileName(Environment.ProcessPath ?? "Orbit.exe");

    static string? ReadOwnAumid()
    {
        try
        {
            if (GetCurrentProcessExplicitAppUserModelID(out var p) != 0 || p == IntPtr.Zero) return null;
            var s = Marshal.PtrToStringUni(p);
            Marshal.FreeCoTaskMem(p);
            return s;
        }
        catch { return null; }
    }

    /// <summary>The session Orbit itself publishes for the phone's player.</summary>
    static bool IsOwn(string aumid) =>
        (OwnAumid != null && aumid.Equals(OwnAumid, StringComparison.OrdinalIgnoreCase)) ||
        Path.GetFileName(aumid).Equals(OwnExe, StringComparison.OrdinalIgnoreCase);

    /// <summary>"Spotify.exe" → "Spotify"; packaged apps get their display name.</summary>
    static string AppName(string aumid)
    {
        try
        {
            var info = Windows.ApplicationModel.AppInfo.GetFromAppUserModelId(aumid);
            var name = info?.DisplayInfo?.DisplayName;
            if (!string.IsNullOrWhiteSpace(name)) return name;
        }
        catch { }
        var n = aumid;
        var bang = n.IndexOf('!');
        if (bang > 0) n = n[..bang];
        n = Path.GetFileNameWithoutExtension(n.Replace('\\', '/').Split('/').Last());
        if (n.Contains('_')) n = n[..n.IndexOf('_')];
        if (n.Contains('.')) n = n.Split('.').Last();
        return n.Length == 0 ? "Media" : char.ToUpper(n[0]) + n[1..];
    }
}
