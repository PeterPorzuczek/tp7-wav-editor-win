namespace TP7.Core;

public readonly record struct PeakPair(float Min, float Max);

/// <summary>Min/max pyramid: brief transients remain visible at every zoom level.</summary>
public sealed class PeakCache
{
    public const int BlockFrames = 256;
    private readonly WaveDocument document;
    private readonly List<PeakPair[][]> levels = [];
    public int DisplayChannels
    {
        get;
    }

    internal PeakCache(WaveDocument document)
    {
        this.document = document;
        DisplayChannels = Math.Min(2, document.Format.Channels);
        var first = new PeakPair[DisplayChannels][];
        for (int c = 0; c < DisplayChannels; c++)
        {
            first[c] = new PeakPair[(document.FrameCount + BlockFrames - 1) / BlockFrames];
            Array.Fill(first[c], new(float.PositiveInfinity, float.NegativeInfinity));
        }
        levels.Add(first);
    }

    internal void Add(long frame, int channel, float value)
    {
        if (channel >= DisplayChannels)
        {
            return;
        }

        int index = (int)(frame / BlockFrames);
        var pair = levels[0][channel][index];
        levels[0][channel][index] = new(Math.Min(pair.Min, value), Math.Max(pair.Max, value));
    }

    internal void Complete()
    {
        while (levels[^1][0].Length > 1)
        {
            var last = levels[^1];
            var next = new PeakPair[DisplayChannels][];
            for (int c = 0; c < DisplayChannels; c++)
            {
                next[c] = new PeakPair[(last[c].Length + 1) / 2];
                for (int i = 0; i < next[c].Length; i++)
                {
                    var a = last[c][i * 2];
                    var b = last[c][Math.Min(i * 2 + 1, last[c].Length - 1)];
                    next[c][i] = new(Math.Min(a.Min, b.Min), Math.Max(a.Max, b.Max));
                }
            }
            levels.Add(next);
        }
    }

    public PeakPair Query(long startFrame, long endFrame, int channel)
    {
        startFrame = Math.Clamp(startFrame, 0, document.FrameCount - 1);
        endFrame = Math.Clamp(endFrame, startFrame + 1, document.FrameCount);
        if (endFrame - startFrame < BlockFrames)
        {
            Span<byte> data = stackalloc byte[BlockFrames * document.Format.BlockAlign];
            int count = document.ReadFrames(startFrame, data[..((int)(endFrame - startFrame) * document.Format.BlockAlign)]);
            float min = float.PositiveInfinity, max = float.NegativeInfinity;
            for (int i = 0; i < count; i++)
            {
                float v = (float)document.Format.Decode(data[(i * document.Format.BlockAlign + channel * document.Format.BytesPerSample)..]);
                min = Math.Min(min, v);
                max = Math.Max(max, v);
            }
            return new(min, max);
        }
        int level = 0;
        long block = BlockFrames;
        while (level + 1 < levels.Count && block * 2 <= endFrame - startFrame)
        {
            level++;
            block *= 2;
        }
        var peaks = levels[level][channel];
        float low = float.PositiveInfinity, high = float.NegativeInfinity;
        for (int i = (int)(startFrame / block); i <= Math.Min(peaks.Length - 1, (endFrame - 1) / block); i++)
        {
            low = Math.Min(low, peaks[i].Min);
            high = Math.Max(high, peaks[i].Max);
        }
        return new(low, high);
    }
}
