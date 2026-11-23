using System.Buffers.Binary;
using System.Text;

namespace TP7.Core;

/// <summary>Version 1 audio carrier understood by tp7-midi. This is not a text CUE sheet.</summary>
public static class CueCarrier
{
    public const int MaximumCues = 256;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static byte[] CreatePayload(string waveFileName, int sampleRate, long frameCount, EditSnapshot edit)
    {
        WaveMetadata.Validate(edit, frameCount);
        if (!waveFileName.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) || waveFileName.IndexOfAny(['\0', '/', '\\']) >= 0
            || Utf8.GetByteCount(waveFileName) > 255)
        {
            throw new InvalidDataException("The companion needs a WAV filename of at most 255 UTF-8 bytes, without a path.");
        }
        if (sampleRate is < 8000 or > 192000 || frameCount is <= 0 or > uint.MaxValue)
        {
            throw new InvalidDataException("The companion supports 8–192 kHz WAVs with 1–4,294,967,295 frames. Disable the companion to export WAV only.");
        }
        if (edit.Cues.Length > MaximumCues)
        {
            throw new InvalidDataException($"The companion supports up to {MaximumCues} cues. Disable the companion to export WAV only.");
        }
        byte[] name = Utf8.GetBytes(waveFileName);
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);
        writer.Write((byte)1); // Version.
        writer.Write((byte)0); // Original WAV markers, not generated test points.
        writer.Write((uint)sampleRate);
        writer.Write((uint)frameCount);
        writer.Write((double)(float)edit.Tempo); // Match the ACID chunk's stored precision exactly.
        writer.Write((ushort)name.Length);
        writer.Write((ushort)edit.Cues.Length);
        writer.Write((ushort)0);
        writer.Write(name);
        foreach (var cue in edit.Cues.OrderBy(c => c.Frame).ThenBy(c => c.Id))
        {
            writer.Write(checked((uint)cue.Frame));
        }
        return output.ToArray();
    }

    public static byte[] CreateWave(string waveFileName, int sampleRate, long frameCount, EditSnapshot edit)
    {
        byte[] payload = CreatePayload(waveFileName, sampleRate, frameCount, edit);
        byte[] frame = new byte[22 + payload.Length];
        frame.AsSpan(0, 8).Fill(0x55);
        new byte[] { 0xD3, 0x91, 0x6B, 0xC5, 0xA7, 0x2E, 0x84, 0xF0 }.CopyTo(frame, 8);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(16), (ushort)payload.Length);
        payload.CopyTo(frame, 18);
        uint crc = uint.MaxValue;
        foreach (byte value in frame.AsSpan(16, 2 + payload.Length))
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xEDB88320u : 0);
            }
        }
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(18 + payload.Length), crc ^ uint.MaxValue);

        // TP-7 can end a BLE transfer after full 250 ms ADPCM blocks, omitting
        // a partial tail. Keep one second after the CRC and align the whole file.
        const int rate = 48000, samplesPerBit = 120, leading = 7200, blockFrames = 12000;
        int signalEnd = leading + frame.Length * 8 * samplesPerBit;
        int samples = (signalEnd + rate + blockFrames - 1) / blockFrames * blockFrames;
        using var output = new MemoryStream(44 + samples * 2);
        using var writer = new BinaryWriter(output);
        writer.Write("RIFF"u8);
        writer.Write(36 + samples * 2);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((ushort)1);
        writer.Write((ushort)1);
        writer.Write(rate);
        writer.Write(rate * 2);
        writer.Write((ushort)2);
        writer.Write((ushort)16);
        writer.Write("data"u8);
        writer.Write(samples * 2);
        writer.Write(new byte[leading * 2]);
        foreach (byte value in frame)
        {
            for (int bit = 7; bit >= 0; bit--)
            {
                int cycles = (value & (1 << bit)) != 0 ? 6 : 3;
                for (int i = 0; i < samplesPerBit; i++)
                {
                    // Match JavaScript Math.round, including negative half ties.
                    writer.Write((short)Math.Floor(18000 * Math.Sin(2 * Math.PI * cycles * i / samplesPerBit) + 0.5));
                }
            }
        }
        writer.Write(new byte[(samples - signalEnd) * 2]);
        return output.ToArray();
    }
}
