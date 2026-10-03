namespace TP7.Core;

public sealed record WaveChunk(string Id, long Offset, uint Size, byte Padding = 0);
