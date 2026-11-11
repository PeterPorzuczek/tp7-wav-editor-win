using NAudio.Wave;
using TP7.Core;

namespace TP7.Audio;

public sealed class PcmPreviewProvider(IWaveSource document, long startFrame, bool normalized, int firstChannel = 0) : IWaveProvider
{
    private readonly IWaveSource document = document;
    private readonly int channelOffset = firstChannel >= 0 && firstChannel + Math.Min(2, document.Format.Channels) <= document.Format.Channels
        ? firstChannel : throw new ArgumentOutOfRangeException(nameof(firstChannel));
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
                    double value = format.Decode(input.AsSpan(i * format.BlockAlign + (channelOffset + channel) * format.BytesPerSample));
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
