# Architecture

The Windows edition is a native C#/.NET 10 application with a WPF interface. It uses the supplied macOS 2.2 application as a behavioral and metadata-format reference. The Windows build has no dependency on Unity or the original application binaries.

## Boundaries

`TP7.Core` owns WAV parsing, streamed export, format conversion, waveform peaks, and editing state. It has no UI or audio-device dependency. Cue positions are integer sample frames; seconds are a presentation and interaction unit.

`EditorSession` holds tempo, cues, selection, normalization, grid settings, and undo/redo snapshots. Exportable state is represented by `EditSnapshot`. Saved-state comparison drives the unsaved-changes prompt. Grid visibility and alignment are view settings and are not written to WAV metadata.

`WaveDocument` owns the source handle for its lifetime. Export reads original bytes through that handle, including after the destination replaces the source path. This keeps repeated exports and undo independent of earlier normalized exports. The file is shared for reading and deletion, but not writing.

`PeakCache` stores a hierarchy of min/max values for waveform rendering. Audio export remains streamed rather than loading the whole recording into memory. Peak analysis still scans the full recording when it opens.

`TP7.Audio` adapts original samples to a 16-bit PCM preview through NAudio WinMM. Playback volume and conversion are separate from export. The provider can be tested without creating an audio device.

`TP7.Windows` coordinates the editing session, controls, and waveform. The small presentation layer uses WPF event handlers split by responsibility: window state, file operations, and input commands. `WaveformView` handles drawing and pointer gestures. File loading and export run off the UI thread and expose cancellation and progress.

## Validation

xUnit tests build synthetic RIFF fixtures independently of the production writer. They check preserved audio and metadata, normalization across supported formats, cue labels, malformed input, cancellation, source replacement, editing history, and waveform transients.

The GitHub workflow checks formatting, builds with warnings as errors, runs tests, and packages the app. It does not establish hardware compatibility, perceptual playback quality, or code-signing trust. Release binaries are currently unsigned.
