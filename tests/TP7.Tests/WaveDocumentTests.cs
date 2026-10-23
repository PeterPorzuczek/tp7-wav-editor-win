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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FloatExportPreservesAudioAndSupportsNormalization(bool extensible)
    {
        string input = Input("float-" + extensible, 32, 2, true, extensible);
        using var doc = WaveDocument.Load(input);
        Assert.True(doc.Format.IsFloat, "Not float");
        string output = Output("float-" + extensible);
        doc.Export(output, new(120, [], false));
        Assert.Equal(Inspect(File.ReadAllBytes(input))["data"], Inspect(File.ReadAllBytes(output))["data"]);
        doc.Export(output, new(120, [], true));
        using var normalized = WaveDocument.Load(output);
        Near(1, normalized.Peak);
    }

    [Fact]
    public void ExtensiblePcmPreservesFormatChunk()
    {
        string input = Input("extensible-pcm", 24, 2, false, true);
        using var doc = WaveDocument.Load(input);
        doc.Export(Output("extensible-pcm"), new(120, [], false));
        Assert.Equal(Inspect(File.ReadAllBytes(input))["fmt "], Inspect(File.ReadAllBytes(Output("extensible-pcm")))["fmt "]);
    }

    [Fact]
    public void ZeroTempoAndEmptyCuesRemoveDeviceMetadata()
    {
        using var doc = WaveDocument.Load(Input("empty-cues"));
        doc.Export(Output("empty-cues"), new(0, [], false));
        var chunks = Inspect(File.ReadAllBytes(Output("empty-cues")));
        Assert.False(chunks.ContainsKey("acid"));
        Assert.Equal([0, 0, 0, 0], chunks["cue "]);
        Assert.Equal("adtl"u8.ToArray(), chunks["LIST"]);
        using var result = WaveDocument.Load(Output("empty-cues"));
        Assert.Empty(result.InitialCues);
    }

    [Fact]
    public void CueLabelIdentifiersFollowSurvivingCues()
    {
        using var doc = WaveDocument.Load(Input("labels"));
        doc.Export(Output("labels"), new(120, [new(7, 100)], false));
        byte[] list = Inspect(File.ReadAllBytes(Output("labels")))["LIST"];
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(list.AsSpan(12)));
        Assert.Contains("second", Encoding.ASCII.GetString(list));
        Assert.DoesNotContain("first", Encoding.ASCII.GetString(list));
    }

    [Fact]
    public void ReplacingSourcePreservesOriginalHandleForLaterExports()
    {
        string input = Input("in-place");
        byte[] audio = Inspect(File.ReadAllBytes(input))["data"];
        using var doc = WaveDocument.Load(input);
        doc.Export(input, new(88, [], true));
        using (var result = WaveDocument.Load(input))
        {
            Near(88, result.InitialTempo);
        }

        doc.Export(Output("in-place"), new(99, [], false));
        Assert.Equal(audio, Inspect(File.ReadAllBytes(Output("in-place")))["data"]);
    }

    [Fact]
    public void CanceledExportLeavesDestinationUnchanged()
    {
        using var doc = WaveDocument.Load(Input("cancel"));
        string target = Output("cancel");
        File.WriteAllText(target, "original");
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => doc.Export(target, new(120, [], false), token: cancel.Token));
        Assert.Equal("original", File.ReadAllText(target));
        Assert.Empty(Directory.GetFiles(root, ".tp7-*.tmp"));
    }

    [Fact]
    public void MalformedRiffFormatAndCueChunksAreRejected()
    {
        byte[] good = Fixture(16, 2, false, false, true, 48001);
        foreach (byte[] bad in new[] { new byte[10], good[..^10], Mutate(good, b => b[0] = 0), Mutate(good, b => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(16), uint.MaxValue)) })
        {
            string path = Path.Combine(root, "bad.wav");
            File.WriteAllBytes(path, bad);
            Assert.Throws<InvalidDataException>(() => WaveDocument.Load(path).Dispose());
        }
        foreach (string id in new[] { "fmt ", "cue " })
        {
            byte[] bad = (byte[])good.Clone();
            int at = Find(bad, id);
            if (id == "fmt ")
            {
                bad[at + 12] = 1;
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(bad.AsSpan(at + 8), uint.MaxValue);
            }

            string path = Path.Combine(root, "bad-" + id.Trim() + ".wav");
            File.WriteAllBytes(path, bad);
            Assert.Throws<InvalidDataException>(() => WaveDocument.Load(path).Dispose());
        }
    }

    [Fact]
    public void NonFiniteFloatSamplesAreRejected()
    {
        byte[] data = Fixture(32, 1, true, false, false, 32);
        int pos = Find(data, "data") + 8;
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(pos), float.NaN);
        string path = Path.Combine(root, "nan.wav");
        File.WriteAllBytes(path, data);
        Assert.Throws<InvalidDataException>(() => WaveDocument.Load(path).Dispose());
    }

}
