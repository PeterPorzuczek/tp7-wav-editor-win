using System.Buffers.Binary;
using System.Text;
using TP7.Audio;
using TP7.Core;
using Xunit;
using static TP7.Tests.WaveFixture;

namespace TP7.Tests;

public sealed class WaveDocumentTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "TP7.Tests", Guid.NewGuid().ToString("N"));

    public WaveDocumentTests() => Directory.CreateDirectory(root);

    public void Dispose() => Directory.Delete(root, true);

    private string Input(string name, int bits = 16, int channels = 2, bool floating = false, bool extensible = false, bool metadata = true, int frames = 48001)
    {
        string path = Path.Combine(root, name + ".wav");
        File.WriteAllBytes(path, Fixture(bits, channels, floating, extensible, metadata, frames));
        return path;
    }

    private string Output(string name) => Path.Combine(root, name + "-out.wav");
    private static void Near(double expected, double actual, double tolerance = .000001)
        => Assert.InRange(actual, expected - tolerance, expected + tolerance);

    [Theory]
    [InlineData(8, 1)]
    [InlineData(8, 2)]
    [InlineData(16, 1)]
    [InlineData(16, 2)]
    [InlineData(24, 1)]
    [InlineData(24, 2)]
    [InlineData(32, 1)]
    [InlineData(32, 2)]
    public void MetadataExportPreservesPcmAndUnknownChunks(int depth, int ch)
    {
        string input = Input($"pcm-{depth}-{ch}", depth, ch);
        using var doc = WaveDocument.Load(input);
        Assert.Equal(depth, doc.Format.BitsPerSample);
        Assert.Equal(ch, doc.Format.Channels);
        Assert.Equal(48001L, doc.FrameCount);
        Near(123.5, doc.InitialTempo);
        Assert.Equal(2, doc.InitialCues.Count);
        byte[] before = File.ReadAllBytes(input);
        var edit = new EditSnapshot(137.25, [new(42, 12), new(7, 44123)], false);
        string output = Output($"pcm-{depth}-{ch}");
        doc.Export(output, edit);
        var a = Inspect(before);
        var b = Inspect(File.ReadAllBytes(output));
        Assert.Equal(a["data"], b["data"]);
        Assert.Equal(a["fmt "], b["fmt "]);
        Assert.Equal(a["JUNK"], b["JUNK"]);
        Assert.Equal(a["bext"], b["bext"]);
        Assert.Equal(before, File.ReadAllBytes(input));
        using var reloaded = WaveDocument.Load(output);
        Near(137.25, reloaded.InitialTempo);
        Assert.Equal(12L, reloaded.InitialCues[0].Frame);
        Assert.Equal(44123L, reloaded.InitialCues[1].Frame);
        Assert.Equal(1u, reloaded.InitialCues[0].Id);
        Assert.Equal(2u, reloaded.InitialCues[1].Id);
        Assert.Equal(new byte[] { 1, 0, 0, 0, 48, 0, 0, 128, 0, 0, 0, 0, 0, 0, 0, 0, 4, 0, 4, 0 }, b["acid"][..20]);
        byte[] raw = File.ReadAllBytes(output);
        Assert.True(raw.AsSpan(^4).SequenceEqual("TAIL"u8), "Trailing bytes not preserved");
        var junk = reloaded.Chunks.Single(c => c.Id == "JUNK");
        Assert.Equal((byte)0x5A, junk.Padding);
    }

    [Theory]
    [InlineData(8, 1)]
    [InlineData(8, 2)]
    [InlineData(16, 1)]
    [InlineData(16, 2)]
    [InlineData(24, 1)]
    [InlineData(24, 2)]
    [InlineData(32, 1)]
    [InlineData(32, 2)]
    public void NormalizationPreservesFormatAndPreviewSamples(int depth, int ch)
    {
        string input = Input($"normalize-{depth}-{ch}", depth, ch);
        using var doc = WaveDocument.Load(input);
        string output = Output($"normalize-{depth}-{ch}");
        doc.Export(output, new(120, [.. doc.InitialCues], true));
        using var normalized = WaveDocument.Load(output);
        Near(1d - Math.Pow(2, 1 - depth), normalized.Peak, 1d / Math.Pow(2, depth - 1));
        Assert.Equal(doc.FrameCount, normalized.FrameCount);
        Assert.Equal(doc.Format, normalized.Format);
        var data = Inspect(File.ReadAllBytes(output))["data"];
        // Frame 0 is zero, frame 1 is +0.25, frame 2 is -0.5 in independently written fixture.
        Near(.5, normalized.Format.Decode(data.AsSpan(ch * depth / 8)), depth == 8 ? .01 : .0001);
        Assert.True(normalized.Format.Decode(data.AsSpan(2 * ch * depth / 8)) < -.98, "Negative peak clipped or wrapped");
        var preview = new PcmPreviewProvider(doc, 1, false) { Volume = 1 };
        byte[] buffer = new byte[4 * ch];
        Assert.Equal(buffer.Length, preview.Read(buffer, 0, buffer.Length));
        Near(.25, BinaryPrimitives.ReadInt16LittleEndian(buffer) / 32768d, depth == 8 ? .01 : .0001);
        var end = new PcmPreviewProvider(doc, doc.FrameCount, false);
        Assert.Equal(0, end.Read(buffer, 0, buffer.Length));
    }

}
