using Windows.Media;
using Windows.Storage.Streams;

namespace Orbit.Core.Media;

/// <summary>
/// Shows the phone's player in the Windows media controls (volume flyout, lock screen, Quick Settings) and turns
/// the media keys and the flyout's buttons into commands for the phone. Uses a silent MediaPlayer only for its
/// SystemMediaTransportControls; nothing is ever played on this PC. Create and use it on the UI thread.
/// </summary>
public sealed class PhoneMediaControls : IDisposable
{
    readonly Windows.Media.Playback.MediaPlayer _player;
    readonly SystemMediaTransportControls _smtc;
    string _shownArt = "";
    string _shownTrack = "";

    /// <summary>"play", "pause", "next", "prev", or "seek" with a position in ms. Raised on a background thread.</summary>
    public event Action<string, long>? Command;

    public PhoneMediaControls()
    {
        _player = new Windows.Media.Playback.MediaPlayer();
        _player.CommandManager.IsEnabled = false;
        _smtc = _player.SystemMediaTransportControls;
        _smtc.IsEnabled = false;
        _smtc.ButtonPressed += (_, e) =>
        {
            var op = e.Button switch
            {
                SystemMediaTransportControlsButton.Play => "play",
                SystemMediaTransportControlsButton.Pause => "pause",
                SystemMediaTransportControlsButton.Next => "next",
                SystemMediaTransportControlsButton.Previous => "prev",
                _ => null
            };
            if (op != null) Command?.Invoke(op, 0);
        };
        _smtc.PlaybackPositionChangeRequested += (_, e) => Command?.Invoke("seek", (long)e.RequestedPlaybackPosition.TotalMilliseconds);
    }

    /// <summary>Shows <paramref name="p"/> (null hides the player). <paramref name="art"/> is its JPEG, if any.</summary>
    public void Update(PlayerInfo? p, long posMs, string phoneName, byte[]? art, bool controllable)
    {
        try
        {
            if (p == null) { Hide(); return; }
            _smtc.IsPlayEnabled = controllable && p.CanPlay;
            _smtc.IsPauseEnabled = controllable && p.CanPause;
            _smtc.IsNextEnabled = controllable && p.CanNext;
            _smtc.IsPreviousEnabled = controllable && p.CanPrev;
            _smtc.PlaybackStatus = p.Playing ? MediaPlaybackStatus.Playing : MediaPlaybackStatus.Paused;

            var track = $"{p.Id}\n{p.Title}\n{p.Artist}\n{p.Album}";
            if (track != _shownTrack || p.Art != _shownArt)
            {
                var d = _smtc.DisplayUpdater;
                d.ClearAll();
                d.Type = MediaPlaybackType.Music;
                d.MusicProperties.Title = p.Title.Length > 0 ? p.Title : p.App;
                d.MusicProperties.Artist = p.Artist.Length > 0 ? $"{p.Artist} · on {phoneName}" : $"{p.App} on {phoneName}";
                d.MusicProperties.AlbumTitle = p.Album;
                if (art != null)
                {
                    var stream = new InMemoryRandomAccessStream();
                    using (var w = new DataWriter(stream.GetOutputStreamAt(0)))
                    {
                        w.WriteBytes(art);
                        w.StoreAsync().AsTask().GetAwaiter().GetResult();
                        w.DetachStream();
                    }
                    stream.Seek(0);
                    d.Thumbnail = RandomAccessStreamReference.CreateFromStream(stream);
                }
                d.Update();
                _shownTrack = track;
                _shownArt = art != null ? p.Art : "";
            }

            _smtc.UpdateTimelineProperties(new SystemMediaTransportControlsTimelineProperties
            {
                StartTime = TimeSpan.Zero,
                EndTime = TimeSpan.FromMilliseconds(p.LenMs),
                Position = TimeSpan.FromMilliseconds(posMs),
                MinSeekTime = TimeSpan.Zero,
                MaxSeekTime = controllable && p.CanSeek ? TimeSpan.FromMilliseconds(p.LenMs) : TimeSpan.Zero
            });
            _smtc.IsEnabled = true;
        }
        catch (Exception ex) { Log.Write("media controls: " + ex.Message); }
    }

    void Hide()
    {
        if (!_smtc.IsEnabled) return;
        _smtc.PlaybackStatus = MediaPlaybackStatus.Closed;
        _smtc.DisplayUpdater.ClearAll();
        _smtc.DisplayUpdater.Update();
        _smtc.IsEnabled = false;
        _shownTrack = ""; _shownArt = "";
    }

    public void Dispose()
    {
        try { Hide(); } catch { }
        _player.Dispose();
    }
}
