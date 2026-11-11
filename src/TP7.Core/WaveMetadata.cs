using System.Buffers.Binary;

namespace TP7.Core;

internal static class WaveMetadata
{
    internal static void Validate(EditSnapshot edit, long frameCount)
    {
        if (!double.IsFinite(edit.Tempo) || edit.Tempo is < 0 or > 999)
        {
            throw new ArgumentOutOfRangeException(nameof(edit), "Tempo must be between 0 and 999 BPM.");
        }
        if (edit.Cues.Length > 100000 || edit.Cues.Any(c => c.Frame < 0 || c.Frame > frameCount)
            || edit.Cues.Select(c => c.Id).Distinct().Count() != edit.Cues.Length)
        {
            throw new InvalidDataException("Invalid cue points.");
        }
    }

    internal static byte[] Acid(float tempo)
    {
        byte[] data = [1, 0, 0, 0, 48, 0, 0, 128, 0, 0, 0, 0, 0, 0, 0, 0, 4, 0, 4, 0, 0, 0, 0, 0];
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(20), tempo);
        return data;
    }

    internal static byte[] Cues(IEnumerable<CuePoint> cues)
    {
        var sorted = cues.OrderBy(c => c.Frame).ThenBy(c => c.Id).ToArray();
        using var bytes = new MemoryStream();
        using var writer = new BinaryWriter(bytes);
        writer.Write(sorted.Length);
        for (int i = 0; i < sorted.Length; i++)
        {
            writer.Write((uint)i + 1);
            writer.Write(0u);
            writer.Write("data"u8);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(checked((uint)sorted[i].Frame));
        }
        return bytes.ToArray();
    }
}
