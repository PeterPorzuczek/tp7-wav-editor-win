using System.Buffers.Binary;
using System.Text;
using Xunit;

namespace TP7.Tests;

internal static class WaveFixture
{
    internal static byte[] Mutate(byte[] input, Action<byte[]> change)
    {
        byte[] output = (byte[])input.Clone();
        change(output);
        return output;
    }

    internal static int Find(byte[] wav, string id)
    {
        long end = BinaryPrimitives.ReadUInt32LittleEndian(wav.AsSpan(4)) + 8L;
        for (int offset = 12; offset < end;)
        {
            if (Encoding.ASCII.GetString(wav, offset, 4) == id)
            {
                return offset;
            }

            int size = (int)BinaryPrimitives.ReadUInt32LittleEndian(wav.AsSpan(offset + 4));
            offset += 8 + size + (size & 1);
        }
        throw new Exception("Chunk not found: " + id);
    }

    internal static Dictionary<string, byte[]> Inspect(byte[] wav)
    {
        var result = new Dictionary<string, byte[]>();
        int end = (int)BinaryPrimitives.ReadUInt32LittleEndian(wav.AsSpan(4)) + 8;
        Assert.True(end <= wav.Length, "RIFF size exceeds file");
        for (int offset = 12; offset < end;)
        {
            string id = Encoding.ASCII.GetString(wav, offset, 4);
            int size = (int)BinaryPrimitives.ReadUInt32LittleEndian(wav.AsSpan(offset + 4));
            result.Add(id, wav.AsSpan(offset + 8, size).ToArray());
            offset += 8 + size + (size & 1);
            Assert.True(offset <= end, "Bad chunk padding");
        }
        return result;
    }

    internal static byte[] Fixture(int bits, int channels, bool floating, bool extensible, bool metadata, int frames)
    {
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.ASCII, true);
        writer.Write("RIFF"u8);
        writer.Write(0u);
        writer.Write("WAVE"u8);
        void Chunk(string id, byte[] bytes, byte pad = 0)
        {
            writer.Write(Encoding.ASCII.GetBytes(id));
            writer.Write(bytes.Length);
            writer.Write(bytes);
            if ((bytes.Length & 1) != 0)
            {
                writer.Write(pad);
            }
        }
        Chunk("JUNK", [9, 8, 7], 0x5A); // before fmt: original app accidentally drops such metadata
        using var format = new MemoryStream();
        using (var fmt = new BinaryWriter(format, Encoding.ASCII, true))
        {
            fmt.Write((ushort)(extensible ? 0xFFFE : floating ? 3 : 1));
            fmt.Write((ushort)channels);
            fmt.Write(48000);
            fmt.Write(48000 * channels * bits / 8);
            fmt.Write((ushort)(channels * bits / 8));
            fmt.Write((ushort)bits);
            if (extensible)
            {
                fmt.Write((ushort)22);
                fmt.Write((ushort)bits);
                fmt.Write(3u);
                fmt.Write(new Guid(floating ? "00000003-0000-0010-8000-00aa00389b71" : "00000001-0000-0010-8000-00aa00389b71").ToByteArray());
            }
        }
        Chunk("fmt ", format.ToArray());
        if (metadata)
        {
            byte[] acid = new byte[24];
            BinaryPrimitives.WriteSingleLittleEndian(acid.AsSpan(20), 123.5f);
            Chunk("acid", acid);
            using var cues = new MemoryStream();
            using (var cw = new BinaryWriter(cues, Encoding.ASCII, true))
            {
                cw.Write(2);
                foreach (var (Id, Frame) in new[] { (Id: 42u, Frame: 480u), (Id: 7u, Frame: (uint)Math.Min(24000, frames - 1)) })
                {
                    cw.Write(Id);
                    cw.Write(0);
                    cw.Write("data"u8);
                    cw.Write(0);
                    cw.Write(0);
                    cw.Write(Frame);
                }
            }
            Chunk("cue ", cues.ToArray());
            using var labels = new MemoryStream();
            using (var lw = new BinaryWriter(labels, Encoding.ASCII, true))
            {
                lw.Write("adtl"u8);
                foreach (var (Id, Name) in new[] { (Id: 42u, Name: "first"), (Id: 7u, Name: "second") })
                {
                    byte[] name = Encoding.ASCII.GetBytes(Name + "\0");
                    lw.Write("labl"u8);
                    lw.Write(4 + name.Length);
                    lw.Write(Id);
                    lw.Write(name);
                    if ((name.Length & 1) != 0)
                    {
                        lw.Write((byte)0);
                    }
                }
            }
            Chunk("LIST", labels.ToArray());
        }
        Chunk("bext", Encoding.ASCII.GetBytes("metadata preserved unchanged"));
        byte[] audio = new byte[frames * channels * bits / 8];
        for (int frame = 0; frame < frames; frame++)
        {
            for (int channel = 0; channel < channels; channel++)
            {
                int p = (frame * channels + channel) * bits / 8;
                double value = frame % 3 == 0 ? 0 : frame % 3 == 1 ? .25 : -.5;
                if (floating)
                {
                    BinaryPrimitives.WriteSingleLittleEndian(audio.AsSpan(p), (float)value);
                }
                else
                {
                    int sample = (int)(value * (1L << (bits - 1)));
                    if (bits == 8)
                    {
                        audio[p] = (byte)(sample + 128);
                    }
                    else if (bits == 16)
                    {
                        BinaryPrimitives.WriteInt16LittleEndian(audio.AsSpan(p), (short)sample);
                    }
                    else if (bits == 24)
                    {
                        audio[p] = (byte)sample;
                        audio[p + 1] = (byte)(sample >> 8);
                        audio[p + 2] = (byte)(sample >> 16);
                    }
                    else
                    {
                        BinaryPrimitives.WriteInt32LittleEndian(audio.AsSpan(p), sample);
                    }
                }
            }
        }

        Chunk("data", audio);
        long end = output.Position;
        writer.Write("TAIL"u8);
        output.Position = 4;
        writer.Write((uint)(end - 8));
        return output.ToArray();
    }
}
