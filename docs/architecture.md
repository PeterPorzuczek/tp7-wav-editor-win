# Architecture

The Windows edition is a native C#/.NET 10 application with a WPF interface. It uses the supplied macOS 2.2 application as a behavioral and metadata-format reference. The Windows build has no dependency on Unity or the original application binaries.

## Boundaries

`TP7.Core` owns WAV parsing, streamed export, format conversion, waveform peaks, and editing state. It has no UI or audio-device dependency. Cue positions are integer sample frames; seconds are a presentation and interaction unit.

`EditorSession` holds tempo, cues, selection, normalization, grid settings, and undo/redo snapshots. Exportable state is represented by `EditSnapshot`. Saved-state comparison drives the unsaved-changes prompt. Grid visibility and alignment are view settings and are not written to WAV metadata.

`WaveDocument` owns the source handle for its lifetime. Export reads original bytes through that handle, including after the destination replaces the source path. This keeps repeated exports and undo independent of earlier normalized exports. The file is shared for reading and deletion, but not writing.

`PeakCache` stores a hierarchy of min/max values for waveform rendering. Audio export remains streamed rather than loading the whole recording into memory. Peak analysis still scans the full recording when it opens.

`IWaveSource` provides frame-addressed audio to the editor and preview provider. `MultitrackDocument` borrows prepared stereo sources, interleaves them in channel-pair order, and zero-pads to the longest source. It validates the 48 kHz/six-stem and 96 kHz/three-stem modes independently of the UI. Floating-point intermediate frames preserve headroom; a shared normalization gain is applied before final 24-bit encoding. Output size and cue metadata are checked before writing.

`StereoStem` owns original and prepared recordings. `StemResampler` uses NAudio's WDL sinc resampler with a 128-tap filter and 64 interpolation phases. Each conversion starts from the original audio, not a previously converted result. Temporary float WAVs are unlinked after opening with delete sharing, so closing the prepared document releases their storage. A canceled add/rate-change operation leaves the current set intact. Structural edits rebuild the editing session, preserve current metadata, and reset cue undo history; ordinary cue edits keep the existing snapshot behavior.

`TP7.Audio` adapts samples to a 16-bit PCM preview through NAudio WinMM. A multitrack preview selects a stereo pair, including its padded tail. Playback volume and conversion are separate from export. The provider can be tested without creating an audio device.

`TP7.Windows` coordinates the editing session, controls, and waveform. The small presentation layer uses WPF event handlers split by responsibility: window state, file operations, and input commands. `WaveformView` handles drawing and pointer gestures. File loading and export run off the UI thread and expose cancellation and progress.

## Validation

xUnit tests build synthetic RIFF fixtures independently of the production writer. They check preserved audio and metadata, normalization across supported formats, cue labels, malformed input, cancellation, source replacement, editing history, and waveform transients. Multitrack checks cover both mode limits, channel-pair ordering, silence padding, shared markers, float headroom, selected-pair playback, resampling duration/impulse timing, and rejection of ultrasonic energy during downsampling.

The GitHub workflow checks formatting, builds with warnings as errors, runs tests, and packages the app. It does not establish hardware compatibility, perceptual playback quality, or code-signing trust. Release binaries are currently unsigned.
