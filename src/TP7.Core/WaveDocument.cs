using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace TP7.Core;


/// <summary>RIFF editor. Source audio is held open, streamed, and never re-encoded for metadata edits.</summary>
public sealed partial class WaveDocument : IWaveSource, IDisposable
{
    private readonly FileStream source;
    private readonly List<WaveChunk> chunks = [];
    private long riffEnd;
    public string FilePath
    {
        get;
    }

    public WaveFormatInfo Format { get; private set; } = null!;
    public IReadOnlyList<WaveChunk> Chunks => chunks;
    public WaveChunk DataChunk { get; private set; } = null!;
    public long FrameCount => DataChunk.Size / Format.BlockAlign;
    public double Duration => (double)FrameCount / Format.SampleRate;
    public double InitialTempo { get; private set; } = 120;
    public List<CuePoint> InitialCues { get; } = [];
    public List<string> Warnings { get; } = [];
    public double Peak { get; private set; }
    public PeakCache Peaks { get; private set; } = null!;
    public double NormalizationGain => Peak > 0 ? (Format.IsFloat ? 1d : 1d - Math.Pow(2, 1 - Format.BitsPerSample)) / Peak : 1d;

    private WaveDocument(string path)
    {
        FilePath = Path.GetFullPath(path);
        // Delete sharing permits atomic replacement; the handle still points to the original bytes.
        source = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 1, FileOptions.RandomAccess);
    }

    public static WaveDocument Load(string path, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var document = new WaveDocument(path);
        try
        {
            document.Parse();
            document.BuildPeaks(progress, cancellationToken);
            return document;
        }
        catch
        {
            document.Dispose();
            throw;
        }
    }

    private void Parse()
    {
        if (source.Length < 12)
        {
            throw new InvalidDataException("The file is too short to be a WAV file.");
        }

        byte[] header = new byte[12];
        ReadAt(0, header);
        if (!header.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !header.AsSpan(8, 4).SequenceEqual("WAVE"u8))
        {
            throw new InvalidDataException("A RIFF/WAVE file is required. RF64, RIFX and other containers are not supported.");
        }

        riffEnd = 8L + BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4));
        if (riffEnd < 12 || riffEnd > source.Length)
        {
            throw new InvalidDataException("The WAV file is truncated or declares an invalid RIFF size.");
        }

        long position = 12;
        byte[] chunkHeader = new byte[8];
        while (position < riffEnd)
        {
            if (riffEnd - position < 8)
            {
                throw new InvalidDataException("Incomplete WAV chunk header.");
            }

            ReadAt(position, chunkHeader);
            string id = Encoding.ASCII.GetString(chunkHeader, 0, 4);
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader.AsSpan(4));
            long next = position + 8 + size + (size & 1);
            if (next > riffEnd)
            {
                throw new InvalidDataException($"Invalid WAV chunk: {id}.");
            }

            byte padding = 0;
            if ((size & 1) != 0)
            {
                byte[] pad = new byte[1];
                ReadAt(next - 1, pad);
                padding = pad[0];
            }
            chunks.Add(new(id, position + 8, size, padding));
            if (chunks.Count > 100000)
            {
                throw new InvalidDataException("The WAV file contains too many chunks.");
            }

            position = next;
        }
        if (chunks.Count(c => c.Id == "fmt ") != 1 || chunks.Count(c => c.Id == "data") != 1)
        {
            throw new InvalidDataException("The WAV file must contain exactly one fmt chunk and one data chunk.");
        }

        Format = WaveFormatInfo.Parse(ReadChunk(chunks.Single(c => c.Id == "fmt ")));
        DataChunk = chunks.Single(c => c.Id == "data");
        if (DataChunk.Size == 0 || DataChunk.Size % Format.BlockAlign != 0)
        {
            throw new InvalidDataException("The WAV file does not contain complete audio frames.");
        }

        var acid = chunks.FirstOrDefault(c => c.Id == "acid");
        if (acid is { Size: >= 24 })
        {
            byte[] tempoBytes = new byte[4];
            ReadAt(acid.Offset + 20, tempoBytes);
            float bpm = BinaryPrimitives.ReadSingleLittleEndian(tempoBytes);
            if (float.IsFinite(bpm) && bpm is >= 0 and <= 999)
            {
                InitialTempo = bpm;
            }
            else
            {
                Warnings.Add("Invalid ACID tempo; defaulted to 120 BPM.");
            }
        }
        else if (acid != null)
        {
            Warnings.Add("Incomplete ACID chunk; defaulted to 120 BPM.");
        }

        var ids = new HashSet<uint>();
        foreach (var chunk in chunks.Where(c => c.Id == "cue "))
        {
            var data = ReadChunk(chunk);
            if (data.Length < 4)
            {
                throw new InvalidDataException("Invalid cue chunk.");
            }

            uint count = BinaryPrimitives.ReadUInt32LittleEndian(data);
            if (count > 100000 || 4L + count * 24L > data.Length)
            {
                throw new InvalidDataException("Invalid cue point count.");
            }

            for (int i = 0; i < count; i++)
            {
                var entry = data.AsSpan(4 + i * 24, 24);
                if (!entry.Slice(8, 4).SequenceEqual("data"u8) || BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]) != 0 || BinaryPrimitives.ReadUInt32LittleEndian(entry[16..]) != 0)
                {
                    throw new InvalidDataException("The cues refer to segmented audio, which is not supported.");
                }

                uint id = BinaryPrimitives.ReadUInt32LittleEndian(entry);
                uint frame = BinaryPrimitives.ReadUInt32LittleEndian(entry[20..]);
                if (!ids.Add(id))
                {
                    throw new InvalidDataException("The file contains duplicate cue identifiers.");
                }

                if (frame > FrameCount)
                {
                    throw new InvalidDataException("A cue point is outside the audio data.");
                }

                InitialCues.Add(new(id, frame));
            }
        }
        InitialCues.Sort((a, b) => a.Frame.CompareTo(b.Frame));
    }

    public void ReadAt(long offset, Span<byte> buffer)
    {
        while (!buffer.IsEmpty)
        {
            int count = RandomAccess.Read(source.SafeFileHandle, buffer, offset);
            if (count == 0)
            {
                throw new EndOfStreamException("Unexpected end of WAV file.");
            }

            offset += count;
            buffer = buffer[count..];
        }
    }

    private byte[] ReadChunk(WaveChunk chunk)
    {
        if (chunk.Size > 16 * 1024 * 1024)
        {
            throw new InvalidDataException($"The {chunk.Id} metadata chunk exceeds the size limit.");
        }

        byte[] result = new byte[chunk.Size];
        ReadAt(chunk.Offset, result);
        return result;
    }

    public int ReadFrames(long startFrame, Span<byte> buffer)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(startFrame);

        int frames = (int)Math.Min(Math.Max(0, FrameCount - startFrame), buffer.Length / Format.BlockAlign);
        ReadAt(DataChunk.Offset + startFrame * Format.BlockAlign, buffer[..(frames * Format.BlockAlign)]);
        return frames;
    }

    private void BuildPeaks(IProgress<double>? progress, CancellationToken token)
    {
        Peaks = new PeakCache(this);
        var buffer = new byte[4096 * Format.BlockAlign];
        for (long frame = 0; frame < FrameCount;)
        {
            token.ThrowIfCancellationRequested();
            int count = ReadFrames(frame, buffer);
            for (int i = 0; i < count; i++)
            {
                for (int channel = 0; channel < Format.Channels; channel++)
                {
                    double value = Format.Decode(buffer.AsSpan(i * Format.BlockAlign + channel * Format.BytesPerSample));
                    Peak = Math.Max(Peak, Math.Abs(value));
                    Peaks.Add(frame + i, channel, (float)value);
                }
            }
            frame += count;
            if (frame % (4096 * 64) == 0 || frame == FrameCount)
            {
                progress?.Report((double)frame / FrameCount);
            }
        }
        Peaks.Complete();
    }

    public string SuggestedFileName(EditSnapshot edit)
    {
        string name = MyRegex().Replace(Path.GetFileNameWithoutExtension(FilePath), "");
        return $"{name}_{(edit.Tempo > 0 ? $"tempo_{Math.Round(edit.Tempo)}" : "no_tempo")}_cues_{edit.Cues.Length}.wav";
    }

    public void Export(string destination, EditSnapshot edit, IProgress<double>? progress = null, CancellationToken token = default)
    {
        WaveMetadata.Validate(edit, FrameCount);

        string target = Path.GetFullPath(destination);
        string temporary = Path.Combine(Path.GetDirectoryName(target)!, $".tp7-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(output, Encoding.ASCII, true))
            {
                writer.Write("RIFF"u8);
                writer.Write(0u);
                writer.Write("WAVE"u8);
                CopyChunk(chunks.Single(c => c.Id == "fmt "), writer, token);
                if (edit.Tempo > 0)
                {
                    WriteChunk(writer, "acid", WaveMetadata.Acid((float)edit.Tempo));
                }

                writer.Write("data"u8);
                writer.Write(DataChunk.Size);
                var buffer = new byte[8192 * Format.BlockAlign];
                double gain = edit.Normalized ? NormalizationGain : 1;
                for (long frame = 0; frame < FrameCount;)
                {
                    token.ThrowIfCancellationRequested();
                    int count = ReadFrames(frame, buffer), bytes = count * Format.BlockAlign;
                    if (edit.Normalized && gain != 1)
                    {
                        for (int i = 0; i < bytes; i += Format.BytesPerSample)
                        {
                            Format.Encode(buffer.AsSpan(i), Format.Decode(buffer.AsSpan(i)) * gain);
                        }
                    }

                    writer.Write(buffer, 0, bytes);
                    frame += count;
                    if (frame % (8192 * 64) == 0 || frame == FrameCount)
                    {
                        progress?.Report((double)frame / FrameCount);
                    }
                }
                if ((DataChunk.Size & 1) != 0)
                {
                    writer.Write(DataChunk.Padding);
                }

                var sortedCues = edit.Cues.OrderBy(c => c.Frame).ThenBy(c => c.Id).ToArray();
                var idMap = sortedCues.Select((cue, index) => (cue.Id, NewId: (uint)index + 1)).ToDictionary(x => x.Id, x => x.NewId);
                foreach (var chunk in chunks.Where(c => c.Id is not ("fmt " or "data" or "cue " or "acid")))
                {
                    if (chunk.Id == "LIST" && chunk.Size >= 4)
                    {
                        byte[] kind = new byte[4];
                        ReadAt(chunk.Offset, kind);
                        if (kind.AsSpan().SequenceEqual("adtl"u8))
                        {
                            WriteChunk(writer, "LIST", RewriteCueLabels(ReadChunk(chunk), idMap));
                            continue;
                        }
                    }
                    CopyChunk(chunk, writer, token);
                }
                // An empty cue chunk is required by the original 2.2 export to clear TP-7 cues.
                WriteChunk(writer, "cue ", WaveMetadata.Cues(sortedCues));
                long riffLength = output.Position;
                if (riffLength - 8 > uint.MaxValue)
                {
                    throw new InvalidDataException("The output exceeds the 4 GB RIFF size limit.");
                }

                CopyRange(riffEnd, source.Length - riffEnd, writer, token);
                output.Position = 4;
                writer.Write((uint)(riffLength - 8));
                writer.Flush();
                output.Flush(true);
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

    private static byte[] RewriteCueLabels(byte[] data, Dictionary<uint, uint> ids)
    {
        using var result = new MemoryStream();
        using var writer = new BinaryWriter(result);
        writer.Write("adtl"u8);
        for (int offset = 4; offset < data.Length;)
        {
            if (offset + 8 > data.Length)
            {
                throw new InvalidDataException("Invalid cue label list.");
            }

            string id = Encoding.ASCII.GetString(data, offset, 4);
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 4));
            long next = offset + 8L + size + (size & 1);
            if (next > data.Length)
            {
                throw new InvalidDataException("Invalid cue label.");
            }

            byte[] payload = data.AsSpan(offset + 8, (int)size).ToArray();
            bool keep = true;
            if (id is "labl" or "note" or "ltxt")
            {
                if (size < 4)
                {
                    throw new InvalidDataException("A cue label has no identifier.");
                }

                keep = ids.TryGetValue(BinaryPrimitives.ReadUInt32LittleEndian(payload), out uint replacement);
                if (keep)
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(payload, replacement);
                }
            }
            if (keep)
            {
                WriteChunk(writer, id, payload, (size & 1) != 0 ? data[(int)next - 1] : (byte)0);
            }

            offset = (int)next;
        }
        return result.ToArray();
    }

    private void CopyChunk(WaveChunk chunk, BinaryWriter writer, CancellationToken token)
    {
        writer.Write(Encoding.ASCII.GetBytes(chunk.Id));
        writer.Write(chunk.Size);
        CopyRange(chunk.Offset, chunk.Size, writer, token);
        if ((chunk.Size & 1) != 0)
        {
            writer.Write(chunk.Padding);
        }
    }

    private void CopyRange(long offset, long length, BinaryWriter writer, CancellationToken token)
    {
        byte[] buffer = new byte[65536];
        while (length > 0)
        {
            token.ThrowIfCancellationRequested();
            int count = (int)Math.Min(length, buffer.Length);
            ReadAt(offset, buffer.AsSpan(0, count));
            writer.Write(buffer, 0, count);
            offset += count;
            length -= count;
        }
    }

    private static void WriteChunk(BinaryWriter writer, string id, byte[] data, byte padding = 0)
    {
        writer.Write(Encoding.ASCII.GetBytes(id));
        writer.Write((uint)data.Length);
        writer.Write(data);
        if ((data.Length & 1) != 0)
        {
            writer.Write(padding);
        }
    }

    public void Dispose() => source.Dispose();
    [GeneratedRegex(@"_(tempo_\d+|no_tempo)_cues_\d+$")]
    private static partial Regex MyRegex();
}
