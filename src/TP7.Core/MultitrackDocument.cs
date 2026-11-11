using System.Buffers;

namespace TP7.Core;

/// <summary>A synchronized set of borrowed stereo sources, in export channel order.</summary>
public sealed class MultitrackDocument : IWaveSource
{
    private readonly IWaveSource[] stems;
    public WaveFormatInfo Format { get; }
    public long FrameCount { get; }
    public double Duration => (double)FrameCount / Format.SampleRate;
    public double Peak { get; }
    public double NormalizationGain => Peak > 0 ? (1d - Math.Pow(2, -23)) / Peak : 1;
    public int StemCount => stems.Length;

    public static int MaximumStems(int sampleRate) => sampleRate switch
    {
        48000 => 6,
        96000 => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(sampleRate), "Choose 48 kHz or 96 kHz.")
    };

    public MultitrackDocument(int sampleRate, IEnumerable<IWaveSource> sources)
    {
        int maximum = MaximumStems(sampleRate);
        stems = sources.ToArray();
        if (stems.Length < 1 || stems.Length > maximum)
        {
            throw new InvalidDataException($"{sampleRate / 1000} kHz supports 1–{maximum} stereo stems.");
        }
        if (stems.Any(s => s.Format.Channels != 2 || s.Format.SampleRate != sampleRate || s.FrameCount < 1))
        {
            throw new InvalidDataException("Every stem must be stereo and prepared at the output sample rate.");
        }
        // Float internally retains headroom until the final 24-bit encoding/normalization.
        Format = new(3, stems.Length * 2, sampleRate, 32, stems.Length * 8);
        FrameCount = stems.Max(s => s.FrameCount);
        Peak = stems.Max(s => s.Peak);
    }

    public int ReadFrames(long startFrame, Span<byte> buffer)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(startFrame);
        if (startFrame >= FrameCount)
        {
            return 0;
        }
        int frames = (int)Math.Min(FrameCount - startFrame, buffer.Length / Format.BlockAlign);
        buffer[..(frames * Format.BlockAlign)].Clear();
        if (frames == 0)
        {
            return 0;
        }
        byte[] input = ArrayPool<byte>.Shared.Rent(frames * stems.Max(s => s.Format.BlockAlign));
        try
        {
            for (int stem = 0; stem < stems.Length; stem++)
            {
                var source = stems[stem];
                var format = source.Format;
                int read = source.ReadFrames(startFrame, input.AsSpan(0, frames * format.BlockAlign));
                for (int frame = 0; frame < read; frame++)
                {
                    for (int channel = 0; channel < 2; channel++)
                    {
                        float sample = (float)format.Decode(input.AsSpan(frame * format.BlockAlign + channel * format.BytesPerSample));
                        System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(buffer[(frame * Format.BlockAlign + stem * 8 + channel * 4)..], sample);
                    }
                }
            }
        }
        finally { ArrayPool<byte>.Shared.Return(input); }
        return frames;
    }

    public long ExportSize(EditSnapshot edit)
    {
        WaveMetadata.Validate(edit, FrameCount);
        long size = checked(12 + 24 + (edit.Tempo > 0 ? 32 : 0) + 8 + FrameCount * StemCount * 6 + 12 + edit.Cues.LongLength * 24);
        if (size - 8 > uint.MaxValue)
        {
            throw new InvalidDataException("The multitrack exceeds the 4 GB WAV limit. Use shorter stems or fewer tracks.");
        }
        return size;
    }

    public void Export(string destination, EditSnapshot edit, IProgress<double>? progress = null, CancellationToken token = default)
    {
        long size = ExportSize(edit);
        string target = Path.GetFullPath(destination);
        if (stems.OfType<WaveDocument>().Any(s => string.Equals(s.FilePath, target, StringComparison.OrdinalIgnoreCase)))
        {
            throw new IOException("Choose a new filename; a multitrack export cannot replace a source stem.");
        }
        string temporary = Path.Combine(Path.GetDirectoryName(target)!, $".tp7-{Guid.NewGuid():N}.tmp");
        try
        {
            token.ThrowIfCancellationRequested();
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream))
            {
                var output = new WaveFormatInfo(1, StemCount * 2, Format.SampleRate, 24, StemCount * 6);
                writer.Write("RIFF"u8);
                writer.Write((uint)(size - 8));
                writer.Write("WAVEfmt "u8);
                writer.Write(16u);
                writer.Write(output.FormatTag);
                writer.Write((ushort)output.Channels);
                writer.Write(output.SampleRate);
                writer.Write(output.SampleRate * output.BlockAlign);
                writer.Write((ushort)output.BlockAlign);
                writer.Write((ushort)output.BitsPerSample);
                if (edit.Tempo > 0)
                {
                    writer.Write("acid"u8);
                    writer.Write(24u);
                    writer.Write(WaveMetadata.Acid((float)edit.Tempo));
                }
                writer.Write("data"u8);
                writer.Write(checked((uint)(FrameCount * output.BlockAlign)));
                var input = new byte[8192 * Format.BlockAlign];
                var encoded = new byte[8192 * output.BlockAlign];
                double gain = edit.Normalized ? NormalizationGain : 1;
                for (long frame = 0; frame < FrameCount;)
                {
                    token.ThrowIfCancellationRequested();
                    int read = ReadFrames(frame, input);
                    for (int sample = 0; sample < read * Format.Channels; sample++)
                    {
                        output.Encode(encoded.AsSpan(sample * 3), Format.Decode(input.AsSpan(sample * 4)) * gain);
                    }
                    writer.Write(encoded, 0, read * output.BlockAlign);
                    frame += read;
                    progress?.Report((double)frame / FrameCount);
                }
                byte[] cues = WaveMetadata.Cues(edit.Cues);
                writer.Write("cue "u8);
                writer.Write((uint)cues.Length);
                writer.Write(cues);
                writer.Flush();
                stream.Flush(true);
            }
            token.ThrowIfCancellationRequested();
            if (File.Exists(target))
            {
                File.Replace(temporary, target, null);
            }
            else
            {
                File.Move(temporary, target);
            }
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
