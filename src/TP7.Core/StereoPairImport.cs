namespace TP7.Core;

/// <summary>Extracts TP-7 channel pairs without mixing, conversion, or timing changes.</summary>
public static class StereoPairImport
{
    public static bool CanImport(WaveDocument source)
        => source.FrameCount > 0 && source.Format.Channels > 2 && source.Format.Channels % 2 == 0
        && source.Format.SampleRate is 48000 or 96000
        && source.Format.Channels / 2 <= MultitrackDocument.MaximumStems(source.Format.SampleRate);

    /// <summary>The caller owns the returned documents. Temporary audio is unlinked while open.</summary>
    public static WaveDocument[] Split(WaveDocument source, IProgress<double>? progress = null, CancellationToken token = default)
    {
        if (!CanImport(source))
        {
            throw new InvalidDataException("A TP-7 multitrack requires 4–12 channels at 48 kHz or 4–6 channels at 96 kHz, in stereo pairs.");
        }
        token.ThrowIfCancellationRequested();
        int count = source.Format.Channels / 2, align = source.Format.BytesPerSample * 2;
        string prefix = Path.Combine(Path.GetTempPath(), $"tp7-pairs-{Guid.NewGuid():N}");
        string[] paths = Enumerable.Range(0, count).Select(i => $"{prefix}-{i}.wav").ToArray();
        var writers = new List<BinaryWriter>();
        var result = new List<WaveDocument>();
        try
        {
            // Read the interleaved recording once, copying each pair's original sample bytes.
            foreach (string path in paths)
            {
                var writer = new BinaryWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None));
                writers.Add(writer);
                uint bytes = checked((uint)(source.FrameCount * align));
                writer.Write("RIFF"u8);
                writer.Write(checked(36u + bytes));
                writer.Write("WAVEfmt "u8);
                writer.Write(16);
                writer.Write(source.Format.FormatTag);
                writer.Write((ushort)2);
                writer.Write(source.Format.SampleRate);
                writer.Write(source.Format.SampleRate * align);
                writer.Write((ushort)align);
                writer.Write((ushort)source.Format.BitsPerSample);
                writer.Write("data"u8);
                writer.Write(bytes);
            }
            var input = new byte[8192 * source.Format.BlockAlign];
            var pair = new byte[8192 * align];
            for (long position = 0; position < source.FrameCount;)
            {
                token.ThrowIfCancellationRequested();
                int frames = source.ReadFrames(position, input);
                if (frames == 0)
                {
                    throw new EndOfStreamException("The multitrack ended before its declared duration.");
                }
                for (int stem = 0; stem < count; stem++)
                {
                    for (int frame = 0; frame < frames; frame++)
                    {
                        input.AsSpan(frame * source.Format.BlockAlign + stem * align, align).CopyTo(pair.AsSpan(frame * align));
                    }
                    writers[stem].Write(pair.AsSpan(0, frames * align));
                }
                position += frames;
                progress?.Report(.65 * position / source.FrameCount);
            }
            foreach (var writer in writers)
            {
                writer.Dispose();
            }
            writers.Clear();
            for (int i = 0; i < count; i++)
            {
                result.Add(WaveDocument.Load(paths[i], null, token));
                // The OS releases this audio when its document closes, including on application exit.
                File.Delete(paths[i]);
                progress?.Report(.65 + .35 * (i + 1) / count);
            }
            token.ThrowIfCancellationRequested();
            return result.ToArray();
        }
        catch
        {
            foreach (var document in result)
            {
                document.Dispose();
            }
            throw;
        }
        finally
        {
            foreach (var writer in writers)
            {
                writer.Dispose();
            }
            foreach (string path in paths)
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }
}
