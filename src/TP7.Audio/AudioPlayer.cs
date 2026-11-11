using NAudio.Wave;
using TP7.Core;

namespace TP7.Audio;

public sealed class AudioPlayer : IDisposable
{
    private WaveOutEvent? output;
    private PcmPreviewProvider? provider;
    private double start;
    public bool IsPlaying => output?.PlaybackState == PlaybackState.Playing;
    public double Position => start + (output == null || provider == null ? 0 : (double)output.GetPosition() / provider.WaveFormat.AverageBytesPerSecond);

    public void SetVolume(double volume)
    {
        provider?.Volume = (float)volume;
    }

    public void Play(IWaveSource document, double seconds, bool normalized, double volume, int firstChannel = 0)
    {
        Stop();
        long frame = Math.Clamp((long)(seconds * document.Format.SampleRate), 0, document.FrameCount - 1);
        start = (double)frame / document.Format.SampleRate;
        try
        {
            provider = new(document, frame, normalized, firstChannel)
            {
                Volume = (float)volume
            };
            output = new WaveOutEvent { DesiredLatency = 100, NumberOfBuffers = 3 };
            output.Init(provider);
            output.Play();
        }
        catch
        {
            Stop();
            throw;
        }
    }

    public void Stop()
    {
        output?.Stop();
        output?.Dispose();
        output = null;
        provider = null;
    }

    public void Dispose() => Stop();
}
