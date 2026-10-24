using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace Orbit.Core;

/// <summary>
/// The master volume of the default playback device, for the phone's volume slider. Follows the default
/// device when it changes (headphones plugged in, a Bluetooth headset connecting).
/// </summary>
public sealed class PcVolume : IMMNotificationClient, IDisposable
{
    readonly object _gate = new();
    MMDeviceEnumerator? _enumerator;
    MMDevice? _device;

    /// <summary>Raised on a background thread with the level (0–100) and mute state, after every change.</summary>
    public event Action<int, bool>? Changed;

    public void Start() => Task.Run(() =>
    {
        try
        {
            _enumerator = new MMDeviceEnumerator();
            _enumerator.RegisterEndpointNotificationCallback(this);
            Attach();
        }
        catch (Exception ex) { Log.Write("volume: " + ex.Message); }
    });

    void Attach()
    {
        lock (_gate)
        {
            if (_device != null)
            {
                try { _device.AudioEndpointVolume.OnVolumeNotification -= OnNotification; } catch { }
                _device.Dispose();
                _device = null;
            }
            try
            {
                if (_enumerator?.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia) == true)
                {
                    _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    _device.AudioEndpointVolume.OnVolumeNotification += OnNotification;
                }
            }
            catch (Exception ex) { Log.Write("volume device: " + ex.Message); _device = null; }
        }
        if (Read() is { } now) Changed?.Invoke(now.level, now.muted);
    }

    void OnNotification(AudioVolumeNotificationData d) =>
        Changed?.Invoke((int)Math.Round(d.MasterVolume * 100), d.Muted);

    /// <summary>Null when there's no playback device.</summary>
    public (int level, bool muted)? Read()
    {
        lock (_gate)
        {
            try
            {
                if (_device == null) return null;
                var v = _device.AudioEndpointVolume;
                return ((int)Math.Round(v.MasterVolumeLevelScalar * 100), v.Mute);
            }
            catch { return null; }
        }
    }

    public void Set(int? level, bool? muted)
    {
        lock (_gate)
        {
            try
            {
                if (_device == null) return;
                var v = _device.AudioEndpointVolume;
                if (level is { } l) v.MasterVolumeLevelScalar = Math.Clamp(l, 0, 100) / 100f;
                if (muted is { } m) v.Mute = m;
                // Raising the volume while muted unmutes, as the Windows volume flyout does.
                else if (level is > 0 && v.Mute) v.Mute = false;
            }
            catch (Exception ex) { Log.Write("volume set: " + ex.Message); }
        }
    }

    // Core Audio calls these on its own thread and must not be blocked: re-attach on a worker.
    void IMMNotificationClient.OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (flow == DataFlow.Render && role == Role.Multimedia) Task.Run(Attach);
    }
    void IMMNotificationClient.OnDeviceStateChanged(string deviceId, DeviceState newState) { }
    void IMMNotificationClient.OnDeviceAdded(string pwstrDeviceId) { }
    void IMMNotificationClient.OnDeviceRemoved(string deviceId) { }
    void IMMNotificationClient.OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

    public void Dispose()
    {
        lock (_gate)
        {
            try { _enumerator?.UnregisterEndpointNotificationCallback(this); } catch { }
            try { if (_device != null) _device.AudioEndpointVolume.OnVolumeNotification -= OnNotification; } catch { }
            _device?.Dispose();
            _enumerator?.Dispose();
            _device = null; _enumerator = null;
        }
    }
}
