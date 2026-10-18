using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Orbit.Core.Mirror;

/// <summary>Plays the phone's audio (48 kHz stereo PCM) with a short buffer so it stays in step with the video.</summary>
sealed class AudioOut : IDisposable
{
    const int BytesPerSecond = 48000 * 4;
    const int MaxBuffered = BytesPerSecond * 120 / 1000; // drop audio that falls more than 120 ms behind

    readonly BufferedWaveProvider _buffer = new(new WaveFormat(48000, 16, 2))
    {
        BufferDuration = TimeSpan.FromSeconds(1), DiscardOnBufferOverflow = true, ReadFully = true
    };
    readonly WasapiOut _out = new(AudioClientShareMode.Shared, true, 40);

    public AudioOut()
    {
        _out.Init(_buffer);
        _out.Play();
    }

    public void Write(byte[] data, int count)
    {
        if (_buffer.BufferedBytes > MaxBuffered) _buffer.ClearBuffer();
        _buffer.AddSamples(data, 0, count);
    }

    public void Dispose()
    {
        try { _out.Stop(); } catch { }
        _out.Dispose();
    }
}
