using NAudio.Wave;
using TP7.Core;

namespace TP7.Audio;

public sealed class PcmPreviewProvider(WaveDocument document, long startFrame, bool normalized) : IWaveProvider
{
    private readonly WaveDocument document = document;
    private readonly byte[] input = new byte[8192 * document.Format.BlockAlign];
    private long position = startFrame;
    private readonly double gain = normalized ? document.NormalizationGain : 1;
    public float Volume { get; set; } = .8f;
    public WaveFormat WaveFormat
    {
        get;
    } = new WaveFormat(document.Format.SampleRate, 16, Math.Min(2, document.Format.Channels));

    public int Read(byte[] buffer, int offset, int count)
    {
        int total = 0;
        var format = document.Format;
        while (count - total >= WaveFormat.BlockAlign)
        {
            int wanted = Math.Min(8192, (count - total) / WaveFormat.BlockAlign);
            int frames = document.ReadFrames(position, input.AsSpan(0, wanted * format.BlockAlign));
            if (frames == 0)
            {
                break;
            }

            for (int i = 0; i < frames; i++)
            {
                for (int channel = 0; channel < WaveFormat.Channels; channel++)
                {
                    // Preview uses channels 1/2; every original channel is retained on export.
                    double value = format.Decode(input.AsSpan(i * format.BlockAlign + channel * format.BytesPerSample));
                    short sample = (short)Math.Clamp(Math.Round(value * gain * Volume * 32768), short.MinValue, short.MaxValue);
                    System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(offset + total + i * WaveFormat.BlockAlign + channel * 2), sample);
                }
            }

            position += frames;
            total += frames * WaveFormat.BlockAlign;
        }
        return total;
    }
}
