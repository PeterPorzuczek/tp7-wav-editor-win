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

## Multitrack files

The multitrack panel accepts stereo WAV sources in the reader's supported sample formats. It prepares them at 48 kHz (up to six stems) or 96 kHz (up to three stems), using NAudio's [WDL sinc resampler](https://github.com/naudio/NAudio/blob/v2.2.1/NAudio.Core/Dsp/WdlResampler.cs) when rates differ. Output duration is rounded to the nearest sample, with at least one frame for very short sources. Resampled intermediate files use float32 to retain filter overshoot and input headroom.

Export writes an interleaved 24-bit PCM `fmt ` chunk (format tag 1), a `data` chunk with two channels per stem, optional `acid`, and one shared `cue ` chunk. Channel order is stem 1 L/R, stem 2 L/R, etc. Every source starts at frame zero; missing tail frames are digital silence. Cue offsets use output sample frames. The first added source supplies initial tempo and cues, with cue times rescaled when needed. Other source metadata and cue labels are not merged into the new recording.

A common optional normalization gain preserves relative stem levels. Without normalization, samples outside the 24-bit range are clipped at final encoding; the panel warns when normalization is needed. Sources at the chosen rate are read directly. Export size includes metadata and is checked against RIFF limits before a temporary file is created. Multitrack export cannot replace a source stem.

## Safe export

Export writes a temporary file in the destination directory. The completed file replaces an existing destination with `File.Replace`, or is moved into place for a new destination. Cancellation is checked before final replacement. A failure removes the temporary file and propagates an error to the UI.

The source remains open with delete sharing, allowing replacement on Windows without invalidating reads from the original handle. This is covered by a regression test that normalizes in place and then exports the original unnormalized audio again.

## Compatibility status

Automated tests verify the file structures and the editing semantics. The implementation has not been exercised on a physical TP-7. Recorder firmware may accept a narrower set of formats than the editor. The project does not implement USB transfer, recorder control, or MIDI clock.
