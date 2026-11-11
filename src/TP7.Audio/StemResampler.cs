using NAudio.Dsp;
using NAudio.Wave;
using TP7.Core;

namespace TP7.Audio;

/// <summary>Offline, band-limited stereo conversion. The caller owns returned documents.</summary>
public static class StemResampler
{
    public static WaveDocument Prepare(WaveDocument source, int sampleRate, IProgress<double>? progress = null, CancellationToken token = default)
    {
        MultitrackDocument.MaximumStems(sampleRate);
        if (source.Format.Channels != 2)
        {
            throw new InvalidDataException($"{Path.GetFileName(source.FilePath)} is not stereo. Export each stem as a stereo WAV first.");
        }
        token.ThrowIfCancellationRequested();
        if (source.Format.SampleRate == sampleRate)
        {
            progress?.Report(1);
            return source;
        }

        long frames = Math.Max(1, (long)Math.Round(source.FrameCount * (double)sampleRate / source.Format.SampleRate, MidpointRounding.AwayFromZero));
        if (frames * 8 + 128 > uint.MaxValue)
        {
            throw new InvalidDataException("This stem is too long to resample into a temporary WAV. Split it into shorter parts first.");
        }
        string path = Path.Combine(Path.GetTempPath(), $"tp7-stem-{Guid.NewGuid():N}.wav");
        WaveDocument? result = null;
        try
        {
            var resampler = new WdlResampler();
            resampler.SetMode(true, 0, true, 128, 64);
            resampler.SetFeedMode(false);
            resampler.SetRates(source.Format.SampleRate, sampleRate);
            var input = new byte[8192 * source.Format.BlockAlign];
            var output = new float[8192 * 2];
            long sourcePosition = 0;
            using (var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2)))
            {
                for (long position = 0; position < frames;)
                {
                    token.ThrowIfCancellationRequested();
                    int wanted = (int)Math.Min(8192, frames - position);
                    int needed = resampler.ResamplePrepare(wanted, 2, out float[] samples, out int offset);
                    int available = 0;
                    while (available < needed)
                    {
                        token.ThrowIfCancellationRequested();
                        int count = source.ReadFrames(sourcePosition, input.AsSpan(0, Math.Min(8192, needed - available) * source.Format.BlockAlign));
                        if (count == 0)
                        {
                            break;
                        }
                        for (int sample = 0; sample < count * 2; sample++)
                        {
                            samples[offset + available * 2 + sample] = (float)source.Format.Decode(input.AsSpan(sample * source.Format.BytesPerSample));
                        }
                        available += count;
                        sourcePosition += count;
                    }
                    int produced = resampler.ResampleOut(output, 0, available, wanted, 2);
                    if (produced == 0)
                    {
                        // The duration rounds to the nearest output frame; a sub-frame tail is silence.
                        if (frames - position > 1)
                        {
                            throw new InvalidDataException("Resampling ended before the expected duration.");
                        }
                        Array.Clear(output);
                        produced = 1;
                    }
                    writer.WriteSamples(output, 0, produced * 2);
                    position += produced;
                    progress?.Report(.8 * position / frames);
                }
            }
            result = WaveDocument.Load(path, new MappedProgress(progress, .8, .2), token);
            token.ThrowIfCancellationRequested();
            // WaveDocument shares deletion: the OS releases this temporary file when its handle closes,
            // even if the application later exits unexpectedly.
            File.Delete(path);
            return result;
        }
        catch
        {
            result?.Dispose();
            throw;
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}

public sealed class MappedProgress(IProgress<double>? target, double start, double length) : IProgress<double>
{
    public void Report(double value) => target?.Report(start + Math.Clamp(value, 0, 1) * length);
}
