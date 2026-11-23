using System.Buffers.Binary;
using TP7.Audio;
using TP7.Core;
using Xunit;

namespace TP7.Tests;

public sealed class MultitrackTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "TP7.Tests", Guid.NewGuid().ToString("N"));
    public MultitrackTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);

    private string WriteWave(string name, int rate, int frames, Func<int, int, double> sample, int channels = 2, int bits = 24, bool floating = false)
    {
        string path = Path.Combine(root, name + ".wav");
        using var writer = new BinaryWriter(File.Create(path));
        int align = channels * bits / 8, bytes = frames * align;
        writer.Write("RIFF"u8);
        writer.Write(36 + bytes + (bytes & 1));
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((ushort)(floating ? 3 : 1));
        writer.Write((ushort)channels);
        writer.Write(rate);
        writer.Write(rate * align);
        writer.Write((ushort)align);
        writer.Write((ushort)bits);
        writer.Write("data"u8);
        writer.Write(bytes);
        for (int frame = 0; frame < frames; frame++)
        {
            for (int channel = 0; channel < channels; channel++)
            {
                double value = sample(frame, channel);
                if (floating)
                {
                    writer.Write((float)value);
                }
                else
                {
                    long encoded = (long)Math.Clamp(Math.Round(value * Math.Pow(2, bits - 1)), -Math.Pow(2, bits - 1), Math.Pow(2, bits - 1) - 1);
                    if (bits == 8)
                    {
                        encoded += 128;
                    }
                    for (int b = 0; b < bits / 8; b++)
                    {
                        writer.Write((byte)(encoded >> (b * 8)));
                    }
                }
            }
        }
        if ((bytes & 1) != 0)
        {
            writer.Write((byte)0);
        }
        return path;
    }

    private static double Sample(IWaveSource source, long frame, int channel)
    {
        byte[] buffer = new byte[source.Format.BlockAlign];
        Assert.Equal(1, source.ReadFrames(frame, buffer));
        return source.Format.Decode(buffer.AsSpan(channel * source.Format.BytesPerSample));
    }

    [Theory]
    [InlineData(48000, 6)]
    [InlineData(96000, 3)]
    [InlineData(48000, 1)]
    [InlineData(96000, 1)]
    public void ExportInterleavesStereoPairsPadsSilenceAndWritesSharedCues(int rate, int count)
    {
        var sources = Enumerable.Range(0, count).Select(i => WaveDocument.Load(WriteWave($"stem-{i}", rate, 12 - i,
            (frame, channel) => (i + 1) / 16d * (channel == 0 ? 1 : -1)))).ToArray();
        try
        {
            var project = new MultitrackDocument(rate, sources);
            string output = Path.Combine(root, "multitrack.wav");
            var edit = new EditSnapshot(137.25, [new(90, 12), new(50, 3), new(30, 0)], false);
            project.Export(output, edit);
            using var actual = WaveDocument.Load(output);
            Assert.Equal(24, actual.Format.BitsPerSample);
            Assert.Equal(rate, actual.Format.SampleRate);
            Assert.Equal(count * 2, actual.Format.Channels);
            Assert.Equal(12, actual.FrameCount);
            Assert.Equal(project.ExportSize(edit), new FileInfo(output).Length);
            Assert.Equal(137.25, actual.InitialTempo);
            Assert.Equal(new long[] { 0, 3, 12 }, actual.InitialCues.Select(c => c.Frame));
            Assert.Equal(new uint[] { 1, 2, 3 }, actual.InitialCues.Select(c => c.Id));
            for (int i = 0; i < count; i++)
            {
                for (int channel = 0; channel < 2; channel++)
                {
                    Assert.Equal(Sample(sources[i], 0, channel), Sample(actual, 0, i * 2 + channel));
                    Assert.Equal(i == 0 ? Sample(sources[i], 11, channel) : 0, Sample(actual, 11, i * 2 + channel));
                }
            }
            Assert.Equal(0, project.ReadFrames(12, new byte[project.Format.BlockAlign]));
            Assert.Throws<ArgumentOutOfRangeException>(() => project.ReadFrames(-1, new byte[project.Format.BlockAlign]));
        }
        finally
        {
            foreach (var source in sources)
            {
                source.Dispose();
            }
        }
    }

    [Fact]
    public void LimitsAndStereoSampleRateValidationAreEnforcedByExporter()
    {
        using var stereo = WaveDocument.Load(WriteWave("stereo", 48000, 20, (_, _) => 0));
        using var mono = WaveDocument.Load(WriteWave("mono", 48000, 20, (_, _) => 0, channels: 1));
        using var high = WaveDocument.Load(WriteWave("high", 96000, 20, (_, _) => 0));
        Assert.Throws<InvalidDataException>(() => new MultitrackDocument(48000, []));
        Assert.Throws<InvalidDataException>(() => new MultitrackDocument(48000, Enumerable.Repeat(stereo, 7)));
        Assert.Throws<InvalidDataException>(() => new MultitrackDocument(96000, Enumerable.Repeat(high, 4)));
        Assert.Throws<InvalidDataException>(() => new MultitrackDocument(48000, [mono]));
        Assert.Throws<InvalidDataException>(() => new MultitrackDocument(96000, [stereo]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MultitrackDocument(44100, [stereo]));
        Assert.Throws<InvalidDataException>(() => StemResampler.Prepare(mono, 48000));
        Assert.Same(stereo, StemResampler.Prepare(stereo, 48000));
    }

    [Theory]
    [InlineData(44100, 48000)]
    [InlineData(48000, 96000)]
    [InlineData(96000, 48000)]
    [InlineData(44100, 96000)]
    public void ResamplingPreservesDurationStereoAndImpulseTiming(int inputRate, int outputRate)
    {
        using var source = WaveDocument.Load(WriteWave("impulse", inputRate, inputRate,
            (frame, channel) => frame == inputRate / 2 ? (channel == 0 ? .5 : -.25) : 0));
        using var converted = StemResampler.Prepare(source, outputRate);
        Assert.Equal(outputRate, converted.FrameCount);
        Assert.Equal(2, converted.Format.Channels);
        Assert.Equal(32, converted.Format.BitsPerSample);
        Assert.True(converted.Format.IsFloat);
        long center = outputRate / 2;
        double peak = Sample(converted, center, 0);
        Assert.True(peak > .2);
        Assert.Equal(-peak / 2, Sample(converted, center, 1), 6);
        Assert.True(peak > Math.Abs(Sample(converted, center - 1, 0)));
        Assert.True(peak > Math.Abs(Sample(converted, center + 1, 0)));
        // The temporary path is unlinked while the open audio remains usable.
        Assert.False(File.Exists(converted.FilePath));
        Assert.True(File.Exists(source.FilePath));
    }

    [Fact]
    public void DownsamplingRejectsUltrasonicEnergyInsteadOfAliasing()
    {
        using var source = WaveDocument.Load(WriteWave("tone", 96000, 96000,
            (frame, channel) => .5 * Math.Sin(2 * Math.PI * (channel == 0 ? 1000 : 30000) * frame / 96000)));
        using var converted = StemResampler.Prepare(source, 48000);
        var data = new byte[4096 * converted.Format.BlockAlign];
        Assert.Equal(4096, converted.ReadFrames(10000, data));
        double[] power = new double[2];
        for (int frame = 0; frame < 4096; frame++)
        {
            for (int channel = 0; channel < 2; channel++)
            {
                double value = converted.Format.Decode(data.AsSpan(frame * 8 + channel * 4));
                power[channel] += value * value / 4096;
            }
        }
        Assert.InRange(Math.Sqrt(power[0]), .35, .36);
        Assert.InRange(Math.Sqrt(power[1]), 0, .001);
    }

    [Theory]
    [InlineData(44100, 48000, 1)]
    [InlineData(96000, 48000, 1)]
    [InlineData(96000, 48000, 3)]
    [InlineData(48000, 96000, 7)]
    [InlineData(44100, 96000, 44117)]
    public void ResamplingRoundsFractionalDurationsAndHandlesVeryShortStems(int inputRate, int outputRate, int frames)
    {
        using var source = WaveDocument.Load(WriteWave("short", inputRate, frames, (_, _) => .25));
        using var converted = StemResampler.Prepare(source, outputRate);
        long expected = Math.Max(1, (long)Math.Round(frames * (double)outputRate / inputRate, MidpointRounding.AwayFromZero));
        Assert.Equal(expected, converted.FrameCount);
        Assert.InRange(converted.Peak, 0, .5);
    }

    [Fact]
    public void ChangingPreparedRateKeepsOriginalAvailableAndAvoidsCumulativeResampling()
    {
        string path = WriteWave("original", 48000, 4800, (f, _) => Math.Sin(f * .1) * .5);
        using var stem = StereoStem.Load(path, 96000);
        Assert.Equal(96000, stem.Audio.Format.SampleRate);
        stem.ReplacePrepared(StemResampler.Prepare(stem.Original, 48000));
        Assert.Same(stem.Original, stem.Audio);
        Assert.Equal(4800, stem.Audio.FrameCount);
        using var original = WaveDocument.Load(path);
        Assert.Equal(Sample(original, 4000, 0), Sample(stem.Audio, 4000, 0));
    }

    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(32)]
    public void ConvertsPcmDepthsAndWritesDigitalSilence(int bits)
    {
        using var shortStem = WaveDocument.Load(WriteWave("short", 48000, 1, (_, c) => c == 0 ? .25 : -.5, bits: bits));
        using var longStem = WaveDocument.Load(WriteWave("long", 48000, 10, (_, _) => 0));
        var project = new MultitrackDocument(48000, [shortStem, longStem]);
        string path = Path.Combine(root, "out.wav");
        project.Export(path, new(0, [], false));
        using var output = WaveDocument.Load(path);
        Assert.Equal(.25, Sample(output, 0, 0));
        Assert.Equal(-.5, Sample(output, 0, 1));
        Assert.Equal(0, Sample(output, 1, 0));
        Assert.Empty(output.InitialCues);
        Assert.DoesNotContain(output.Chunks, c => c.Id == "acid");
    }

    [Fact]
    public void SharedNormalizationPreservesBalanceAndFloatHeadroom()
    {
        using var loud = WaveDocument.Load(WriteWave("loud", 48000, 10, (_, c) => c == 0 ? 2 : -2, bits: 32, floating: true));
        using var quiet = WaveDocument.Load(WriteWave("quiet", 48000, 10, (_, _) => .5));
        var project = new MultitrackDocument(48000, [loud, quiet]);
        Assert.Equal(2, project.Peak);
        string path = Path.Combine(root, "normalized.wav");
        project.Export(path, new(120, [], true));
        using var output = WaveDocument.Load(path);
        Assert.Equal(1 - Math.Pow(2, -23), Sample(output, 0, 0));
        Assert.InRange(Sample(output, 0, 2), .2499998, .2500001);
        Assert.Equal(2, Sample(loud, 0, 0));
    }

    [Fact]
    public void ReorderingChangesChannelMappingAndSelectedPairPreview()
    {
        using var first = WaveDocument.Load(WriteWave("one", 48000, 10, (_, _) => .25));
        using var second = WaveDocument.Load(WriteWave("two", 48000, 2, (_, _) => -.5));
        var reordered = new MultitrackDocument(48000, [second, first]);
        Assert.Equal(-.5, Sample(reordered, 0, 0));
        Assert.Equal(.25, Sample(reordered, 0, 2));
        var preview = new PcmPreviewProvider(reordered, 0, false, 2) { Volume = 1 };
        byte[] data = new byte[40];
        Assert.Equal(40, preview.Read(data, 0, 40));
        Assert.Equal(8192, BinaryPrimitives.ReadInt16LittleEndian(data));
        var shortPreview = new PcmPreviewProvider(reordered, 2, false) { Volume = 1 };
        Assert.Equal(32, shortPreview.Read(data, 0, 40));
        Assert.All(data[..32], b => Assert.Equal(0, b));
        Assert.Equal(new PeakPair(0, 0), second.Peaks.Query(2, 10, 0));
    }

    [Fact]
    public void CancellationAndInvalidMetadataNeverReplaceDestination()
    {
        using var source = WaveDocument.Load(WriteWave("stem", 48000, 20000, (_, _) => .25));
        var project = new MultitrackDocument(48000, [source]);
        string path = Path.Combine(root, "destination.wav");
        byte[] original = [1, 2, 3];
        File.WriteAllBytes(path, original);
        using var canceled = new CancellationTokenSource();
        var cancelAfterFirstBlock = new CallbackProgress(_ => canceled.Cancel());
        Assert.Throws<OperationCanceledException>(() => project.Export(path, new(120, [], false), cancelAfterFirstBlock, canceled.Token));
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Empty(Directory.GetFiles(root, "*.tmp"));
        Assert.Throws<InvalidDataException>(() => project.Export(path, new(120, [new(1, 20001)], false)));
        Assert.Throws<InvalidDataException>(() => project.Export(path, new(120, [new(1, 1), new(1, 2)], false)));
        Assert.Throws<ArgumentOutOfRangeException>(() => project.Export(path, new(double.NaN, [], false)));
        Assert.Throws<IOException>(() => project.Export(source.FilePath, new(120, [], false)));
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void OversizedMultitrackFailsBeforeCreatingAnyFile()
    {
        var project = new MultitrackDocument(48000, Enumerable.Repeat(new LongSource(), 6));
        string path = Path.Combine(root, "too-large.wav");
        Assert.Throws<InvalidDataException>(() => project.Export(path, new(120, [], false)));
        Assert.Empty(Directory.GetFiles(root));
    }

    [Fact]
    public void ResamplingCancellationLeavesOriginalReadable()
    {
        using var source = WaveDocument.Load(WriteWave("stem", 44100, 44100, (_, _) => .25));
        using var canceled = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() => StemResampler.Prepare(source, 96000, new CallbackProgress(_ => canceled.Cancel()), canceled.Token));
        Assert.Equal(.25, Sample(source, 0, 0));
    }

    [Theory]
    [InlineData(48000, 4, 8, false)]
    [InlineData(48000, 8, 16, false)]
    [InlineData(48000, 8, 24, false)]
    [InlineData(48000, 12, 24, false)]
    [InlineData(96000, 4, 32, false)]
    [InlineData(96000, 6, 24, false)]
    [InlineData(48000, 4, 32, true)]
    public void ReopeningMultitrackExtractsEveryStereoPairByteForByte(int rate, int channels, int bits, bool floating)
    {
        using var source = WaveDocument.Load(WriteWave("exported", rate, 16403,
            (frame, channel) => .45 * Math.Sin(frame * .013 + channel) + channel * .01, channels, bits, floating));
        byte[] original = File.ReadAllBytes(source.FilePath);
        var stems = StereoStem.ImportMultitrack(source);
        try
        {
            Assert.Equal(channels / 2, stems.Length);
            byte[] input = new byte[(int)source.FrameCount * source.Format.BlockAlign];
            Assert.Equal(source.FrameCount, source.ReadFrames(0, input));
            var project = new MultitrackDocument(rate, stems.Select(s => s.Audio));
            for (int i = 0; i < stems.Length; i++)
            {
                var stem = stems[i];
                Assert.Equal(source.FilePath, stem.SourcePath);
                Assert.Contains($"CH {i * 2 + 1}–{i * 2 + 2}", stem.Name);
                Assert.False(File.Exists(stem.Original.FilePath));
                Assert.Equal(source.FrameCount, stem.Audio.FrameCount);
                Assert.Equal(rate, stem.Audio.Format.SampleRate);
                Assert.Equal(bits, stem.Audio.Format.BitsPerSample);
                Assert.Equal(floating, stem.Audio.Format.IsFloat);
                byte[] pair = new byte[(int)source.FrameCount * stem.Audio.Format.BlockAlign];
                Assert.Equal(source.FrameCount, stem.Audio.ReadFrames(0, pair));
                for (int frame = 0; frame < source.FrameCount; frame++)
                {
                    int size = stem.Audio.Format.BlockAlign;
                    Assert.True(input.AsSpan(frame * source.Format.BlockAlign + i * size, size).SequenceEqual(pair.AsSpan(frame * size, size)));
                }
                Assert.Equal((float)Sample(source, 8193, i * 2), stem.Audio.Peaks.Query(8193, 8194, 0).Max);
                byte[] direct = new byte[400], preview = new byte[400];
                new PcmPreviewProvider(source, 8190, false, i * 2).Read(direct, 0, direct.Length);
                new PcmPreviewProvider(project, 8190, false, i * 2).Read(preview, 0, preview.Length);
                Assert.Equal(direct, preview);
            }
            Assert.Equal(original, File.ReadAllBytes(source.FilePath));
        }
        finally
        {
            foreach (var stem in stems)
            { stem.Dispose(); }
        }
    }

    [Fact]
    public void ReopenedMultitrackKeepsSharedMetadataAndCanReorderExportAndResample()
    {
        using var source = WaveDocument.Load(WriteWave("source", 48000, 20000, (_, c) => (c + 1) / 16d, 8));
        var edit = new EditSnapshot(95, Enumerable.Range(0, 17).Select(i => new CuePoint((uint)i, i * 1250)).ToArray(), false);
        string input = Path.Combine(root, "exported.wav");
        source.Export(input, edit);
        using var exported = WaveDocument.Load(input);
        var stems = StereoStem.ImportMultitrack(exported);
        try
        {
            var project = new MultitrackDocument(48000, stems.Select(s => s.Audio));
            var session = new EditorSession(project, new(exported.InitialTempo, exported.InitialCues.ToArray(), false));
            Assert.False(session.IsDirty);
            Assert.Equal(95, session.Tempo);
            Assert.Equal(edit.Cues.Select(c => c.Frame), session.Cues.Select(c => c.Frame));
            string output = Path.Combine(root, "roundtrip.wav");
            CueCompanionExport.Export(project, output, session.Snapshot());
            Assert.True(File.Exists(Path.ChangeExtension(output, ".cue")));
            using var roundtrip = WaveDocument.Load(output);
            Assert.Equal(8, roundtrip.Format.Channels);
            Assert.Equal(95, roundtrip.InitialTempo);
            Assert.Equal(edit.Cues.Select(c => c.Frame), roundtrip.InitialCues.Select(c => c.Frame));
            Assert.Equal(WaveFixture.Inspect(File.ReadAllBytes(input))["data"], WaveFixture.Inspect(File.ReadAllBytes(output))["data"]);
            var reordered = new MultitrackDocument(48000, stems.Reverse().Select(s => s.Audio));
            string reorderedPath = Path.Combine(root, "reordered.wav");
            reordered.Export(reorderedPath, session.Snapshot());
            using var actual = WaveDocument.Load(reorderedPath);
            Assert.Equal(Sample(exported, 15000, 6), Sample(actual, 15000, 0));
            Assert.Equal(Sample(exported, 15000, 1), Sample(actual, 15000, 7));
            using var converted = StemResampler.Prepare(stems[3].Original, 96000);
            Assert.Equal(40000, converted.FrameCount);
            Assert.InRange(Sample(converted, 20000, 0), .43749, .43751);
        }
        finally
        {
            foreach (var stem in stems)
            { stem.Dispose(); }
        }
    }

    [Theory]
    [InlineData(48000, 1)]
    [InlineData(48000, 2)]
    [InlineData(48000, 3)]
    [InlineData(48000, 14)]
    [InlineData(96000, 8)]
    [InlineData(44100, 4)]
    public void OrdinaryOrUnsupportedLayoutsAreNotOpenedAsTP7Multitracks(int rate, int channels)
    {
        using var source = WaveDocument.Load(WriteWave("ordinary", rate, 10, (_, _) => 0, channels));
        Assert.False(StereoPairImport.CanImport(source));
        Assert.Throws<InvalidDataException>(() => StereoPairImport.Split(source));
    }

    [Theory]
    [InlineData(.1)]
    [InlineData(.8)]
    [InlineData(1)]
    public void CanceledImportCleansTemporaryPairsAndLeavesSourceUntouched(double cancelAt)
    {
        using var source = WaveDocument.Load(WriteWave("source", 48000, 20000, (_, c) => c / 16d, 8));
        byte[] original = File.ReadAllBytes(source.FilePath);
        using var canceled = new CancellationTokenSource();
        string[] before = Directory.GetFiles(Path.GetTempPath(), "tp7-pairs-*.wav");
        var created = new HashSet<string>();
        Assert.Throws<OperationCanceledException>(() => StereoPairImport.Split(source, new CallbackProgress(value =>
        {
            created.UnionWith(Directory.GetFiles(Path.GetTempPath(), "tp7-pairs-*.wav").Except(before));
            if (value >= cancelAt)
            { canceled.Cancel(); }
        }), canceled.Token));
        Assert.NotEmpty(created);
        Assert.All(created, path => Assert.False(File.Exists(path)));
        Assert.Equal(original, File.ReadAllBytes(source.FilePath));
        Assert.Equal(.4375, Sample(source, 19000, 7));
    }

    private sealed class CallbackProgress(Action<double> callback) : IProgress<double>
    {
        public void Report(double value) => callback(value);
    }

    private sealed class LongSource : IWaveSource
    {
        public WaveFormatInfo Format { get; } = new(1, 2, 48000, 24, 6);
        public long FrameCount => 48000L * 60 * 60;
        public double Duration => 3600;
        public double Peak => 0;
        public double NormalizationGain => 1;
        public int ReadFrames(long startFrame, Span<byte> buffer) => throw new InvalidOperationException("The exporter must reject the size before reading.");
    }
}
