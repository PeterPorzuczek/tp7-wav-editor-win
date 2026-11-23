using TP7.Core;

namespace TP7.Audio;

/// <summary>Owns the original recording and, when needed, its resampled preview/export source.</summary>
public sealed class StereoStem : IDisposable
{
    public WaveDocument Original { get; }
    public WaveDocument Audio { get; private set; }
    public string SourcePath { get; }
    public string Name { get; }

    private StereoStem(WaveDocument original, WaveDocument audio, string? sourcePath = null, string? name = null)
    {
        Original = original;
        Audio = audio;
        SourcePath = sourcePath ?? original.FilePath;
        Name = name ?? Path.GetFileName(SourcePath);
    }

    public static StereoStem[] ImportMultitrack(WaveDocument source, IProgress<double>? progress = null, CancellationToken token = default)
        => StereoPairImport.Split(source, progress, token).Select((pair, i) =>
            new StereoStem(pair, pair, source.FilePath, $"{Path.GetFileNameWithoutExtension(source.FilePath)} · CH {i * 2 + 1}–{i * 2 + 2}")).ToArray();

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
