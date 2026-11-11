namespace TP7.Core;

/// <summary>Frame-addressable audio shared by the cue editor and playback.</summary>
public interface IWaveSource
{
    WaveFormatInfo Format { get; }
    long FrameCount { get; }
    double Duration { get; }
    double Peak { get; }
    double NormalizationGain { get; }
    int ReadFrames(long startFrame, Span<byte> buffer);
}
