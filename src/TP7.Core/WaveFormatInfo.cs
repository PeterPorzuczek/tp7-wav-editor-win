using System.Buffers.Binary;

namespace TP7.Core;

public sealed record WaveFormatInfo(ushort FormatTag, int Channels, int SampleRate, int BitsPerSample, int BlockAlign)
{
    public int BytesPerSample => BitsPerSample / 8;
    public bool IsFloat => FormatTag == 3;
    public string Description => $"{SampleRate / 1000d:0.###} kHz · {BitsPerSample}-bit {(IsFloat ? "float" : "PCM")} · {(Channels == 1 ? "Mono" : Channels == 2 ? "Stereo" : $"{Channels} ch")}";

    internal static WaveFormatInfo Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 16)
        {
            throw new InvalidDataException("Incomplete WAV format header.");
        }

        var tag = BinaryPrimitives.ReadUInt16LittleEndian(data);
        int channels = BinaryPrimitives.ReadUInt16LittleEndian(data[2..]);
        uint rate = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
        int align = BinaryPrimitives.ReadUInt16LittleEndian(data[12..]);
        int bits = BinaryPrimitives.ReadUInt16LittleEndian(data[14..]);
        if (tag == 0xfffe)
        {
            if (data.Length < 40 || BinaryPrimitives.ReadUInt16LittleEndian(data[16..]) < 22)
            {
                throw new InvalidDataException("Incomplete WAVE_FORMAT_EXTENSIBLE header.");
            }

            var subformat = new Guid(data.Slice(24, 16));
            tag = subformat == new Guid("00000001-0000-0010-8000-00aa00389b71") ? (ushort)1
                : subformat == new Guid("00000003-0000-0010-8000-00aa00389b71") ? (ushort)3 : (ushort)0;
            int validBits = BinaryPrimitives.ReadUInt16LittleEndian(data[18..]);
            if (validBits != 0 && validBits != bits)
            {
                throw new InvalidDataException("Different container and valid sample bit depths are not supported. Use 8/16/24/32-bit PCM.");
            }
        }
        if (channels is < 1 or > 32 || rate is < 1 or > 768000)
        {
            throw new InvalidDataException("Invalid WAV channel count or sample rate.");
        }

        if (!(tag == 1 && bits is 8 or 16 or 24 or 32) && !(tag == 3 && bits == 32))
        {
            throw new InvalidDataException("Supported formats: WAV PCM 8/16/24/32-bit and 32-bit float. Convert compressed audio before opening it.");
        }

        if (align != channels * (bits / 8) || BinaryPrimitives.ReadUInt32LittleEndian(data[8..]) != (long)rate * align)
        {
            throw new InvalidDataException("Inconsistent WAV block alignment or byte rate.");
        }

        return new(tag, channels, (int)rate, bits, align);
    }

    public double Decode(ReadOnlySpan<byte> sample)
    {
        if (IsFloat)
        {
            float value = BinaryPrimitives.ReadSingleLittleEndian(sample);
            if (!float.IsFinite(value))
            {
                throw new InvalidDataException("The audio contains NaN or Infinity samples.");
            }

            return value;
        }
        return BitsPerSample switch
        {
            8 => (sample[0] - 128) / 128d,
            16 => BinaryPrimitives.ReadInt16LittleEndian(sample) / 32768d,
            24 => ((sample[0] | sample[1] << 8 | sample[2] << 16) << 8 >> 8) / 8388608d,
            32 => BinaryPrimitives.ReadInt32LittleEndian(sample) / 2147483648d,
            _ => throw new InvalidOperationException()
        };
    }

    public void Encode(Span<byte> target, double value)
    {
        if (!double.IsFinite(value))
        {
            throw new InvalidDataException("Invalid sample value.");
        }

        value = Math.Clamp(value, -1, 1);
        if (IsFloat)
        {
            BinaryPrimitives.WriteSingleLittleEndian(target, (float)value);
            return;
        }
        long max = (1L << (BitsPerSample - 1)) - 1;
        long min = -(1L << (BitsPerSample - 1));
        long sample = Math.Clamp((long)Math.Round(value * -min), min, max);
        switch (BitsPerSample)
        {
            case 8:
                target[0] = (byte)(sample + 128);
                break;
            case 16:
                BinaryPrimitives.WriteInt16LittleEndian(target, (short)sample);
                break;
            case 24:
                target[0] = (byte)sample;
                target[1] = (byte)(sample >> 8);
                target[2] = (byte)(sample >> 16);
                break;
            case 32:
                BinaryPrimitives.WriteInt32LittleEndian(target, (int)sample);
                break;
        }
    }
}
