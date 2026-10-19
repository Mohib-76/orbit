using System.IO;
using System.Text.Json.Nodes;
using System.Windows.Media.Imaging;
using Orbit.Core.Media;

namespace Orbit.Core;

/// <summary>
/// Media controls both ways: this PC publishes its players to the phone and runs the phone's commands; the
/// phone's players show on Home, in the tray and (optionally) in the Windows media controls.
/// </summary>
public sealed partial class OrbitCore
{
    public LocalMedia LocalMedia { get; } = new();

    /// <summary>The phone's players as last reported. Empty when disconnected.</summary>
    public MediaSnapshot PhoneMedia { get; private set; } = MediaSnapshot.Empty;
    /// <summary>The player the user picked in the switcher (null = follow the phone).</summary>
    public string? PhoneMediaPick { get; private set; }
    /// <summary>Raised on the UI thread when the phone's players change.</summary>
    public event Action? PhoneMediaChanged;

    public PlayerInfo? PhonePlayer => Settings.MediaShowPhone ? PhoneMedia.Pick(PhoneMediaPick) : null;

    readonly Dictionary<string, byte[]> _phoneArt = new();
    readonly Dictionary<string, BitmapImage> _phoneArtImages = new();
    readonly HashSet<string> _artSent = new();
    volatile bool _peerWantsMedia = true;
    MediaSnapshot? _lastSentMedia;
    PhoneMediaControls? _smtc;
    CancellationTokenSource? _mediaDebounce;

    void StartMedia()
    {
        LocalMedia.Changed += () =>
        {
            SchedulePublishMedia();
            var playing = LocalMedia.AnyPlaying();
            if (playing == PlayingHere) return;
            PlayingHere = playing;
            SchedulePublishBuds();
            Ui.Run(() => BudsChanged?.Invoke());
        };
        _ = LocalMedia.StartAsync();
    }

    // ---------------------------------------------------------------- this PC → phone

    void SchedulePublishMedia()
    {
        _mediaDebounce?.Cancel();
        var cts = _mediaDebounce = new CancellationTokenSource();
        _ = Task.Delay(250, cts.Token).ContinueWith(t => { if (!t.IsCanceled) PublishMedia(); }, TaskScheduler.Default);
    }

    void PublishMedia(bool force = false)
    {
        var link = _link;
        if (link == null || !_peerWantsMedia) return;
        var snap = LocalMedia.Snapshot();
        lock (_artSent)
        {
            if (!force && _lastSentMedia != null && !MediaDiffers(_lastSentMedia, snap)) return;
            _lastSentMedia = snap;
            foreach (var hash in snap.Players.Select(p => p.Art).Where(h => h.Length > 0).Distinct())
            {
                if (_artSent.Contains(hash) || LocalMedia.ArtFor(hash) is not { } jpeg) continue;
                link.Post("orbit.media.art", new JsonObject { ["hash"] = hash, ["jpeg"] = Convert.ToBase64String(jpeg) });
                _artSent.Add(hash);
            }
        }
        link.Post("orbit.media.state", snap.ToJson());
    }

    /// <summary>Anything the phone would show differently, including a seek (position off by more than 2 s from the estimate).</summary>
    static bool MediaDiffers(MediaSnapshot old, MediaSnapshot now)
    {
        if (old.Active != now.Active || old.Players.Count != now.Players.Count) return true;
        for (int i = 0; i < now.Players.Count; i++)
        {
            var a = old.Players[i]; var b = now.Players[i];
            if (a with { PosMs = 0 } != b with { PosMs = 0 }) return true;
            if (Math.Abs(old.PositionNow(a, now.ReceivedUtc) - b.PosMs) > 2000) return true;
        }
        return false;
    }

    void OnMediaCommand(JsonObject b)
    {
        var op = b["op"]?.GetValue<string>() ?? "";
        long pos = b["pos"] is JsonValue v && v.TryGetValue<long>(out var p) ? p : 0;
        _ = LocalMedia.CommandAsync(b["player"]?.GetValue<string>(), op, pos);
    }

    // ---------------------------------------------------------------- phone → this PC

    void OnPhoneMedia(JsonObject b)
    {
        var snap = MediaSnapshot.FromJson(b, DateTime.UtcNow);
        Ui.Run(() =>
        {
            PhoneMedia = snap;
            if (PhoneMediaPick != null && snap.Players.All(p => p.Id != PhoneMediaPick)) PhoneMediaPick = null;
            UpdateMediaControls();
            PhoneMediaChanged?.Invoke();
        });
    }

    void OnPhoneMediaArt(JsonObject b)
    {
        if (b["hash"]?.GetValue<string>() is not { Length: > 0 } hash) return;
        try
        {
            var bytes = Convert.FromBase64String(b["jpeg"]?.GetValue<string>() ?? "");
            lock (_phoneArt)
            {
                if (_phoneArt.Count > 30) { _phoneArt.Clear(); Ui.Run(() => _phoneArtImages.Clear()); }
                _phoneArt[hash] = bytes;
            }
        }
        catch { }
    }

    /// <summary>Album art the phone sent, as a frozen image (UI thread).</summary>
    public BitmapSource? PhoneArt(string hash)
    {
        if (hash.Length == 0) return null;
        if (_phoneArtImages.TryGetValue(hash, out var img)) return img;
        byte[]? bytes;
        lock (_phoneArt) _phoneArt.TryGetValue(hash, out bytes);
        if (bytes == null) return null;
        try
        {
            img = new BitmapImage();
            img.BeginInit(); img.CacheOption = BitmapCacheOption.OnLoad; img.StreamSource = new MemoryStream(bytes); img.EndInit();
            img.Freeze();
            _phoneArtImages[hash] = img;
            return img;
        }
        catch { return null; }
    }

    /// <summary>The icon of a phone app, if a notification from it brought one.</summary>
    public static string? PhoneAppIcon(string package)
    {
        var p = Path.Combine(Paths.Icons, string.Concat(package.Split(Path.GetInvalidFileNameChars())) + ".png");
        return File.Exists(p) ? p : null;
    }

    public void PickPhonePlayer(string id)
    {
        PhoneMediaPick = id;
        UpdateMediaControls();
        PhoneMediaChanged?.Invoke();
    }

    /// <summary>play | pause | toggle | next | prev | seek, for the player shown on this PC.</summary>
    public void PhoneMediaCommand(string op, long posMs = 0)
    {
        var p = PhoneMedia.Pick(PhoneMediaPick);
        if (_link == null || p == null || !PhoneMedia.Controllable) return;
        var cmd = new JsonObject { ["player"] = p.Id, ["op"] = op };
        if (op == "seek") cmd["pos"] = posMs;
        _link.Post("orbit.media.cmd", cmd);

        // Answer the click right away; the phone's next state confirms it.
        var now = DateTime.UtcNow;
        var pos = PhoneMedia.PositionNow(p, now);
        var updated = op switch
        {
            "play" => p with { Playing = true, PosMs = pos },
            "pause" => p with { Playing = false, PosMs = pos },
            "toggle" => p with { Playing = !p.Playing, PosMs = pos },
            "seek" => p with { PosMs = posMs },
            _ => p
        };
        if (updated == p) return;
        PhoneMedia = PhoneMedia with { Players = PhoneMedia.Players.Select(x => x.Id == p.Id ? updated : x).ToList(), ReceivedUtc = now };
        UpdateMediaControls();
        PhoneMediaChanged?.Invoke();
    }

    /// <summary>The play/pause hotkey.</summary>
    public void TogglePhoneMedia() => PhoneMediaCommand("toggle");

    void OnMediaPeerGone()
    {
        lock (_artSent) { _artSent.Clear(); _lastSentMedia = null; }
        _peerWantsMedia = true;
        Ui.Run(() =>
        {
            PhoneMedia = MediaSnapshot.Empty;
            PhoneMediaPick = null;
            UpdateMediaControls();
            PhoneMediaChanged?.Invoke();
        });
    }

    void OnMediaConnected()
    {
        lock (_artSent) { _artSent.Clear(); _lastSentMedia = null; }
        PublishMedia(force: true);
    }

    void OnPeerMediaWanted(bool wanted)
    {
        if (wanted == _peerWantsMedia) return;
        _peerWantsMedia = wanted;
        lock (_artSent) _lastSentMedia = null;
        if (wanted) PublishMedia(force: true);
    }

    public void SetMediaShowPhone(bool on)
    {
        Settings.MediaShowPhone = on;
        Settings.Save();
        SendSettings();
        if (!on) PhoneMedia = MediaSnapshot.Empty;
        UpdateMediaControls();
        PhoneMediaChanged?.Invoke();
    }

    public void SetMediaKeys(bool on)
    {
        Settings.MediaKeys = on;
        Settings.Save();
        UpdateMediaControls();
    }

    /// <summary>Keeps the Windows media controls in step with the phone's player (UI thread).</summary>
    void UpdateMediaControls()
    {
        var p = PhonePlayer;
        if (!Settings.MediaKeys || p == null)
        {
            if (_smtc != null && (!Settings.MediaKeys || !Settings.MediaShowPhone)) { _smtc.Dispose(); _smtc = null; }
            _smtc?.Update(null, 0, "", null, false);
            return;
        }
        if (_smtc == null)
        {
            try
            {
                _smtc = new PhoneMediaControls();
                _smtc.Command += (op, pos) => Ui.Run(() => PhoneMediaCommand(op, pos));
            }
            catch (Exception ex) { Log.Write("media controls unavailable: " + ex.Message); return; }
        }
        byte[]? art;
        lock (_phoneArt) _phoneArt.TryGetValue(p.Art, out art);
        _smtc.Update(p, PhoneMedia.PositionNow(p, DateTime.UtcNow), Phone.Name, art, PhoneMedia.Controllable);
    }
}
