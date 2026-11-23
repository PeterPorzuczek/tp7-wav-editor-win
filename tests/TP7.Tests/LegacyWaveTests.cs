using System.Buffers.Binary;
using TP7.Core;
using Xunit;

namespace TP7.Tests;

public sealed class LegacyWaveTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "TP7.Tests", Guid.NewGuid().ToString("N"));
    public LegacyWaveTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);

    // Matches the observed header/chunk layout without including any user's recording.
    private static byte[] LegacyExport(int channels = 8, int sampleRate = 48000, int cueCount = 17)
    {
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);
        writer.Write("RIFF"u8);
        writer.Write(0u);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((ushort)1);
        writer.Write((ushort)channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * 3);
        writer.Write((ushort)(channels * 3));
        writer.Write((ushort)24);
        writer.Write("acid"u8);
        writer.Write(24);
        writer.Write(new byte[20]);
        writer.Write(95.25f);
        writer.Write("data"u8);
        writer.Write(48001 * channels * 3);
        for (int i = 0; i < 48001 * channels * 3; i++)
            writer.Write((byte)(i * 31));
        writer.Write("cue "u8);
        writer.Write(4 + cueCount * 24);
        writer.Write(cueCount);
        for (int i = 0; i < cueCount; i++)
        {
            writer.Write((uint)i);
            writer.Write(0);
            writer.Write("data"u8);
            writer.Write(0);
            writer.Write(0);
            writer.Write((uint)i * 2800u);
        }
        long length = output.Length;
        output.Position = 4;
        writer.Write((uint)length); // Legacy off-by-eight RIFF length, independent of production writer.
        return output.ToArray();
    }

    [Theory]
    [InlineData(8, 48000, 17)]
    [InlineData(2, 96000, 1)]
    [InlineData(2, 48000, 0)]
    public void OpensLegacyExportAndReexportsAStandardWaveWithoutChangingSamplesOrMarkers(int channels, int rate, int count)
    {
        byte[] input = LegacyExport(channels, rate, count);
        string path = Path.Combine(root, "legacy.wav"), exported = Path.Combine(root, "corrected.wav");
        File.WriteAllBytes(path, input);
        using var document = WaveDocument.Load(path);
        Assert.Contains("legacy RIFF size", Assert.Single(document.Warnings));
        Assert.Equal(channels, document.Format.Channels);
        Assert.Equal(rate, document.Format.SampleRate);
        Assert.Equal(48001, document.FrameCount);
        Assert.Equal(95.25, document.InitialTempo);
        Assert.Equal(Enumerable.Range(0, count).Select(i => i * 2800L), document.InitialCues.Select(c => c.Frame));
        CueCompanionExport.Export(document, exported, new(document.InitialTempo, [.. document.InitialCues], false));
        byte[] output = File.ReadAllBytes(exported);
        Assert.Equal(output.LongLength - 8, BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(4)));
        // Fix only the fixture's outer length for independent chunk inspection.
        byte[] standardInput = (byte[])input.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(standardInput.AsSpan(4), (uint)standardInput.Length - 8);
        Assert.Equal(WaveFixture.Inspect(standardInput)["data"], WaveFixture.Inspect(output)["data"]);
        using var reloaded = WaveDocument.Load(exported);
        Assert.Empty(reloaded.Warnings);
        Assert.Equal(document.Format, reloaded.Format);
        Assert.Equal(document.FrameCount, reloaded.FrameCount);
        Assert.Equal(document.InitialTempo, reloaded.InitialTempo);
        Assert.Equal(document.InitialCues.Select(c => c.Frame), reloaded.InitialCues.Select(c => c.Frame));
        Assert.True(File.Exists(CueCompanionExport.CompanionPath(exported)));
        Assert.Equal(input, File.ReadAllBytes(path));
    }

    [Fact]
    public void LegacySizeDoesNotBypassChunkBoundsFramesOrCueValidation()
    {
        byte[] good = LegacyExport();
        var badFiles = new List<byte[]>
        {
            good[..^8],
            WaveFixture.Mutate(good, b => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(4), (uint)b.Length + 1)),
            WaveFixture.Mutate(good, b => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(4), (uint)b.Length - 7)),
            WaveFixture.Mutate(good, b => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(4), uint.MaxValue)),
            WaveFixture.Mutate(good, b => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(72), uint.MaxValue)),
            WaveFixture.Mutate(good, b => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(b.Length - 4), 48002)),
        };
        // A valid RIFF truncated by exactly eight bytes superficially matches the legacy size.
        // Its final cue chunk is incomplete and must still fail, never get silently shortened.
        byte[] truncatedStandard = good[..^8];
        BinaryPrimitives.WriteUInt32LittleEndian(truncatedStandard.AsSpan(4), (uint)good.Length - 8);
        badFiles.Add(truncatedStandard);
        // Truncated audio disguised with a whole-file RIFF size still has an oversized data chunk.
        byte[] truncatedAudio = good[..1000];
        BinaryPrimitives.WriteUInt32LittleEndian(truncatedAudio.AsSpan(4), (uint)truncatedAudio.Length);
        badFiles.Add(truncatedAudio);
        foreach (byte[] bad in badFiles)
        {
            string path = Path.Combine(root, "broken.wav");
            File.WriteAllBytes(path, bad);
            Assert.Throws<InvalidDataException>(() => WaveDocument.Load(path).Dispose());
        }
    }

    [Fact]
    public void LegacySizeRetainsUnknownChunksAndOddPadding()
    {
        byte[] input = WaveFixture.Fixture(8, 1, false, false, true, 48001)[..^4]; // No trailing non-RIFF bytes.
        BinaryPrimitives.WriteUInt32LittleEndian(input.AsSpan(4), (uint)input.Length);
        string path = Path.Combine(root, "legacy-unknown.wav"), output = Path.Combine(root, "retained.wav");
        File.WriteAllBytes(path, input);
        using var document = WaveDocument.Load(path);
        document.Export(output, new(document.InitialTempo, [.. document.InitialCues], false));
        using var reloaded = WaveDocument.Load(output);
        Assert.Equal(0x5A, reloaded.Chunks.Single(c => c.Id == "JUNK").Padding);
        foreach (string id in new[] { "JUNK", "bext", "data" })
        {
            byte[] before = new byte[document.Chunks.Single(c => c.Id == id).Size];
            byte[] after = new byte[reloaded.Chunks.Single(c => c.Id == id).Size];
            document.ReadAt(document.Chunks.Single(c => c.Id == id).Offset, before);
            reloaded.ReadAt(reloaded.Chunks.Single(c => c.Id == id).Offset, after);
            Assert.Equal(before, after);
        }
    }
}
