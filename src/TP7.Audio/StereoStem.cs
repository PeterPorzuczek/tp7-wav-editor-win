using TP7.Core;

namespace TP7.Audio;

/// <summary>Owns the original recording and, when needed, its resampled preview/export source.</summary>
public sealed class StereoStem : IDisposable
{
    public WaveDocument Original { get; }
    public WaveDocument Audio { get; private set; }
    public string Name => Path.GetFileName(Original.FilePath);

    private StereoStem(WaveDocument original, WaveDocument audio)
    {
        Original = original;
        Audio = audio;
    }

    public static StereoStem Load(string path, int sampleRate, IProgress<double>? progress = null, CancellationToken token = default)
    {
        var original = WaveDocument.Load(path, new MappedProgress(progress, 0, .4), token);
        try
        {
            return new(original, StemResampler.Prepare(original, sampleRate, new MappedProgress(progress, .4, .6), token));
        }
        catch
        {
            original.Dispose();
            throw;
        }
    }

    public void ReplacePrepared(WaveDocument audio)
    {
        if (audio.Format.Channels != 2)
        {
            throw new ArgumentException("A stereo source is required.", nameof(audio));
        }
        var previous = Audio;
        Audio = audio;
        if (previous != Original && previous != audio)
        {
            previous.Dispose();
        }
    }

    public void Dispose()
    {
        if (Audio != Original)
        {
            Audio.Dispose();
        }
        Original.Dispose();
    }
}
