# WAV compatibility

## Container and samples

The reader accepts little-endian RIFF/WAVE with one contiguous `data` chunk. Supported samples are PCM8/16/24/32 and IEEE float32, including their WAVE_FORMAT_EXTENSIBLE subtypes. Extensible valid bits must be zero or match the container bit depth. The reader validates chunk bounds, frame alignment, byte rate, sample rate, and channel count. Non-finite floating-point samples are rejected.

Metadata-only export copies the original sample bytes. Peak normalization applies one gain across all channels and encodes samples in the original format. Integer encoding clamps to the representable range; unsigned PCM8 uses 128 as digital silence.

RF64, big-endian RIFX, compressed WAV, and segmented audio are unsupported. The RIFF 32-bit length field limits file size. Preview and waveform rendering use the first two channels; exports retain all channels.

## Tempo

The `acid` payload follows the reference macOS 2.2 application's 24-byte layout. The tempo is a little-endian float at payload offset 20. The first 20 bytes are:

```text
01 00 00 00 30 00 00 80 00 00 00 00 00 00 00 00 04 00 04 00
```

Export replaces existing `acid` metadata. A tempo of zero omits the chunk. Opening a file without tempo defaults to 120 BPM; this is an editor default rather than a claim that the recording has that tempo.

## Cue points and labels

The `cue ` payload contains a 32-bit count followed by 24-byte records referencing `data`. Positions are sample frames. Export writes sorted cues with consecutive IDs starting at 1. An empty cue list produces a `cue ` chunk with count zero, matching the reference application.

Known cue-label subchunks in `LIST/adtl` are remapped to the new IDs. Labels for removed cues are dropped. Unknown top-level chunks, their original padding bytes, and bytes after the declared RIFF end are retained.

## Safe export

Export writes a temporary file in the destination directory. The completed file replaces an existing destination with `File.Replace`, or is moved into place for a new destination. Cancellation is checked before final replacement. A failure removes the temporary file and propagates an error to the UI.

The source remains open with delete sharing, allowing replacement on Windows without invalidating reads from the original handle. This is covered by a regression test that normalizes in place and then exports the original unnormalized audio again.

## Compatibility status

Automated tests verify the file structures and the editing semantics. The implementation has not been exercised on a physical TP-7. Recorder firmware may accept a narrower set of formats than the editor. The project does not implement USB transfer, recorder control, or MIDI clock.
