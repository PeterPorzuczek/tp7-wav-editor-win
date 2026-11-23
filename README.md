<div align="center">

# TP-7 WAV Editor

**Tempo, cue points, and multitrack export for TP-7, on Windows.**<br>
Edit a recording or build a multitrack WAV from stereo stems, with shared tempo and cue points.

![Windows 10 · 11](https://img.shields.io/badge/Windows-10%20%C2%B7%2011-293331)
![.NET 10 · WPF](https://img.shields.io/badge/.NET%2010-WPF-293331)
[![MIT License](https://img.shields.io/badge/license-MIT-ff895b)](LICENSE)

**[Getting started](#getting-started)** ·
**[The workflow](#from-recording-to-export)** ·
**[Multitrack export](#multitrack-export)** ·
**[Build from source](#build-and-test)** ·
**[Documentation](#documentation)**

<br>

<a href="https://imgur.com/a/sIn9dRK">
<img src="https://i.imgur.com/lzh3w4t.jpeg" width="960" alt="TP-7 WAV Editor on Windows: a stereo waveform with four cue points, a 120 BPM grid, playback controls, and precise cue editing.">
</a>
<br>
<sub><b>The Windows editor</b> — a real screenshot using a synthetic stereo recording.</sub>

</div>

## Why TP-7 WAV Editor

Prepare the timing and cue points of a recording in one place. See the waveform, line up the
beat grid, audition a section, and give each cue an exact position. Metadata-only exports keep
**every audio sample unchanged**; normalization is a separate, optional step.

This is a native Windows implementation inspired by
[TP-7 Wav Editor 2.2 by EcstasyJones / Max Solomon](https://www.ecstasyjones.com/tp-7-wav-editor).
It works with local WAV files. It does not upload recordings or connect directly to the recorder.

<div align="center">

<img src="https://i.imgur.com/eZpkuwY.png" width="460" alt="The Teenage Engineering TP-7 field recorder.">
<br>
<sub><b>The TP-7</b> — the Teenage Engineering recorder this project is intended for.</sub>

</div>

<div align="center">

<a href="https://www.youtube.com/shorts/p9uwnBtGGeg">
<img src="https://i.ytimg.com/vi/p9uwnBtGGeg/hqdefault.jpg" width="460" alt="Watch the TP-7 YouTube Short.">
</a>
<br>
<b><a href="https://www.youtube.com/shorts/p9uwnBtGGeg">▶ Watch the Short on YouTube</a></b>

</div>

## What it does

<table>
<tr>
<td width="50%" valign="top">

### Cue points you can place precisely

Click to add a cue, drag it along the waveform, or type its position in seconds. Cue positions
are stored as sample frames. Move between markers with the keyboard and undo or redo your edits.

### A grid that follows your tempo

Enter BPM, tap the beat, or calculate tempo from two adjacent cues. The interval between those
cues represents four beats. Align the grid to a selected cue and enable snapping when you need it.

### A waveform you can navigate

Zoom into a detail, pan along the timeline, or use the overview to find your place. The main view
shows stereo channels separately, with cue markers and playback position visible together.

</td>
<td width="50%" valign="top">

### Original audio stays intact

Editing tempo and cues does not re-encode the audio. Unknown WAV chunks, padding, and trailing
bytes are retained. Cue labels follow surviving markers when their IDs are updated.

### Preview before exporting

Hold Space to audition from a cue or the pointer, or use Play for continuous playback. The volume
slider changes only what you hear. Optional peak normalization preserves the original sample
rate, channel count, and bit depth on export.

### Changes you can take back

Cue edits, tempo changes, and normalization support undo and redo. Closing with unexported
changes asks first. Export finishes in a temporary file before replacing the destination;
canceling before replacement leaves an existing file unchanged.

</td>
</tr>
</table>

## From recording to export

1. **Open your WAV.** Choose **Open WAV** or drop a recording into the window.
2. **Set the beat.** Enter BPM, use **TAP**, or select a cue and choose **Calculate from cues**.
3. **Place your markers.** Click to add, drag to move, and enter an exact time when needed.
4. **Check the timing.** Enable **Grid** and **Snap**, align to a cue, and audition the result.
5. **Export.** Choose **Export WAV**. The suggested filename includes the tempo and cue count.

Version 1.2.1 also opens existing exports whose RIFF header incorrectly includes
the first eight bytes in its length. The app validates the audio and metadata and
shows a compatibility notice. Re-export to save the corrected header; opening a
file never changes the original. This includes multichannel WAVs with native cues.

### Matching companion for Bluetooth cues

**Export matching .cue companion** is enabled by default for single-file and multitrack
exports. One export creates `track.wav` with native TP-7 markers and `track.cue` with
the same sample offsets, output sample rate, duration, filename and stored BPM. Copy
**both files into the same TP-7 folder**, select the music WAV on the recorder, then
use **Read TP-7 → Start remote session → Read cue carrier** in
[the Bluetooth MIDI app](https://peterporzuczek.github.io/tp7-midi/), with **track.cue** selected.

Version **1.3.1** adds at least one second of trailing silence and aligns the companion
to 250 ms blocks. This addresses a reproduced truncation when TP-7 ends Bluetooth
transfer without the final partial audio block. Regenerate and copy older companions;
updating only the web app cannot restore missing data. The padded version still needs
device validation. Choose the final WAV filename before export: renaming both files
does not update the filename embedded inside `.cue`.

The `.cue` is this project's binary audio carrier, **not a text CUE sheet**. Reading
the carrier as `.tp7-cues.wav` has been confirmed on hardware. A later report accepted
`.cue` audio but exposed the truncation above. Keeping it out of the recorder's track
list still needs a TP-7 test. If rejected,
rename only `track.cue` to `track.tp7-cues.wav` and choose **older exports** in the
web app; this fallback will appear as an extra track on TP-7.

The companion supports 0–256 markers, 8–192 kHz and up to 4,294,967,295 frames.
No markers are silently dropped. End markers are retained but disabled for web jumps.
Disable the checkbox for WAV-only export if a file exceeds these limits. Re-export
and copy both files after changes; editing cues on TP-7 is not synchronized back.
The web app jumps to positions without switching the recorder's native CUE selection.
Export preserves exact sample offsets; Bluetooth seeks round once to milliseconds
and still have device/connection latency.

Tempo **0** removes tempo metadata on export. A file without tempo metadata opens at **120 BPM**,
matching the reference application's default; set it to 0 to export without tempo.

Normalization is optional and can be undone. Exporting over the source is supported: undo and
subsequent exports continue to use the original audio rather than an earlier normalized export.

## Multitrack export

<div align="center">

<a href="https://i.imgur.com/19a7Auf.png">
<img src="https://i.imgur.com/19a7Auf.png" width="960" alt="TP-7 WAV Editor multitrack view with stereo stems, sample-rate modes, a waveform, and shared tempo and cue controls.">
</a>
<br>
<sub><b>Multitrack view</b> — arrange stereo stems, audition each pair, and place cue points on one shared timeline.</sub>

</div>

Choose **Multitrack** (Ctrl+M) to combine stereo stems into one multichannel WAV with shared tempo and cue points.

**Reopen an exported multitrack with Open WAV (Ctrl+O).** Version 1.3.0 automatically
opens 4–12 channels at 48 kHz, or 4–6 channels at 96 kHz, as separate stereo stems:
channels 1–2, 3–4, and so on. Select a stem to see its waveform and audition that pair.
The recording's BPM and sample-accurate cue positions are shared across every stem.
You can reorder, add, or remove stems and export the edited multitrack with its matching
`.cue` companion. A WAV does not retain the original separate stem filenames or
unpadded lengths, so imported pairs use channel labels and the recording's full duration.

Opening creates temporary stereo copies (approximately the source audio size); the
original remains untouched. If the channel layout and rate stay unchanged, metadata
edits preserve original audio bytes, encoding, and unknown WAV chunks. Changing the
stem arrangement or rate builds a new 24-bit multitrack. Normalization changes samples
only when enabled. Exports must use a different path from their source recording.
Mono/stereo WAVs and layouts outside the TP-7 limits keep the ordinary WAV view.

| Output mode | Stereo stems | Channels | Encoding |
| --- | --- | --- | --- |
| 48 kHz | 1–6 | 2–12 | 24-bit PCM |
| 96 kHz | 1–3 | 2–6 | 24-bit PCM |

1. **Add stereo WAVs.** Select several files or drop them into the panel. Other sample rates are converted automatically with a band-limited resampler.
2. **Arrange the stems.** Use the arrows to set channel order. Stem 1 occupies channels 1–2, stem 2 channels 3–4, and so on. All stems start at zero; shorter files are padded with silence to the longest stem.
3. **Make your cues.** Select a stem to view its waveform and audition its stereo pair. The tempo, grid, and cue points belong to the entire set. Tempo and cues from the first added stem initialize the timeline.
4. **Export multitrack.** Save one 24-bit WAV with every stereo pair and the shared markers. Source recordings stay untouched.

Switching sample rate converts from the original recordings and keeps cue times to the nearest output sample. Remove excess stems before switching to 96 kHz. Changing the stem list or output rate resets cue undo history; ordinary cue edits still support undo/redo. A stem cannot be removed while doing so would leave cues beyond the new end.

Normalization applies **one gain to the whole set**, preserving the balance between stems. Resampled audio retains floating-point headroom until export. If the panel reports peaks over 0 dBFS, normalize before exporting to avoid clipping. Conversion may use temporary disk space; preparation and export are cancelable. The final file must fit the standard RIFF limit (approximately 4 GB).

Multitrack export creates a new file: individual stems' cue labels and unrelated metadata are not merged. Hardware validation on a physical TP-7 is still required.

## Getting started

[**Download the latest Windows ZIP**](https://github.com/PeterPorzuczek/tp7-wav-editor-win/releases/latest/download/TP7-Wav-Editor-Windows-x64.zip)

**Windows 10/11, x64.** Extract `TP7-Wav-Editor-Windows-x64.zip` and open `TP7-Wav-Editor.exe`.
Keep the license files with the executable when redistributing the package.

The portable build includes .NET. No separate runtime, Unity installation, or administrator
access is required. To create the package yourself, follow [Build and test](#build-and-test).
The interface and documentation are in English.

## Supported files

| Format | Support |
| --- | --- |
| RIFF/WAVE PCM | 8, 16, 24, and 32-bit |
| IEEE floating point | 32-bit |
| WAVE_FORMAT_EXTENSIBLE | PCM and float formats above, with matching valid and container bit depths |
| Multiple channels | Up to 32; supported TP-7 layouts reopen as stereo stems, other layouts preview channels 1–2 and retain all channels on export |

Standard RIFF size limits apply, approximately **4 GB**. RF64, RIFX, compressed audio, and
segmented WAV data are unsupported. See [WAV compatibility](docs/wav-format.md) for the metadata
layout, preservation rules, and export behavior.

**Hardware validation is still required.** Automated tests cover file-format and editing
behavior, but this implementation has not been tested on a physical TP-7. The recorder may
accept fewer formats than the editor.

## Keyboard shortcuts

<details>
<summary><b>Show the shortcut reference</b></summary>

| Shortcut | Action |
| --- | --- |
| Ctrl+O / I | Open WAV |
| Ctrl+M | New multitrack set |
| Ctrl+S / E | Export WAV |
| Hold Space | Preview from the selected cue, pointer, or start |
| Click / drag / right-click | Add / move / delete a cue |
| Insert / Delete | Add / delete a cue |
| Left / Right | Select previous / next cue |
| Down | Select the cue nearest the center of the view |
| Esc / Up | Deselect cue |
| `[` / `]` | Nudge selected cue; Shift for one sample |
| Scroll / + / − | Zoom |
| Shift + scroll | Pan |
| Home | Fit the entire recording |
| T / C | Tap tempo / calculate from cues |
| G / S / Tab | Grid / snap / align grid |
| N | Normalize audio |
| Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z | Undo / redo |
| F1 | Open the shortcut reference in the app |

Letter shortcuts are inactive while typing in a text field. Numeric input accepts both a decimal
point and a decimal comma.

</details>

## Build and test

Install the **.NET SDK version specified in `global.json`** on Windows, currently 10.0.303.
NuGet access is needed for the initial restore. The SDK version is pinned, and NuGet dependencies
have checked-in lock files.

```powershell
dotnet restore --locked-mode
dotnet build -c Release --no-restore
.\scripts\test.ps1 -NoBuild -NoRestore
.\scripts\publish.ps1 -SkipTests -NoRestore
```

For a complete release build with restore and tests included, run `scripts/publish.ps1` without
switches. It creates a self-contained x64 executable, a portable ZIP, and a source ZIP in `dist/`.

| Script | Purpose |
| --- | --- |
| `scripts/test.ps1` | Run regression tests and save TRX results to `artifacts/test-results/` |
| `scripts/publish.ps1` | Restore, test, publish, and package a Windows release |
| `scripts/package-source.ps1` | Package source files without building or requiring Git |

All scripts resolve paths relative to the repository and can be invoked from any working
directory. Use `-NoRestore` after a successful restore, `-NoBuild` on the test script after a
Release build, and `-SkipTests` on the publish script after tests have passed.

Run directly from source with:

```powershell
dotnet run --project src/TP7.Windows
```

Check formatting with `dotnet format --verify-no-changes --no-restore`. The GitHub Actions
workflow restores dependencies, checks formatting, builds, tests, and packages on Windows.
Generated output and local WAV recordings are excluded from Git.

## Documentation

**For users:** start with [Getting started](#getting-started), follow
[the editing workflow](#from-recording-to-export), and check
[supported files](#supported-files) before exporting.

**For contributors:** begin with [CONTRIBUTING.md](CONTRIBUTING.md), then explore:

| Document | What it covers |
| --- | --- |
| [Architecture](docs/architecture.md) | Application layers, editing state, audio preview, and validation |
| [WAV compatibility](docs/wav-format.md) | Samples, tempo metadata, cues, labels, and safe export |
| [Keyboard reference](src/TP7.Windows/Resources/KeyboardShortcuts.txt) | The help text included in the app |
| [Third-party notices](THIRD-PARTY-NOTICES.txt) | Dependencies, licenses, and original application credits |

<details>
<summary><b>Project layout</b></summary>

| Directory | Responsibility |
| --- | --- |
| `src/TP7.Core` | RIFF parsing, export, sample conversion, cue editing, history, waveform peaks |
| `src/TP7.Audio` | PCM preview provider and Windows audio playback |
| `src/TP7.Windows` | WPF interface, input handling, file dialogs, waveform rendering |
| `tests/TP7.Tests` | xUnit regression tests with synthetic WAV fixtures |
| `scripts` | Release packaging and test entry points used locally and in CI |
| `docs` | Architecture and file-format notes |
| `licenses` | Third-party license texts distributed with the app |

</details>

## License and credits

[MIT](LICENSE). The Windows implementation is free and open source. Third-party components
retain their respective licenses; see [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).

The original macOS application belongs to EcstasyJones / Max Solomon. Its binaries, Unity
assets, and diagnostic decompilation output are not included in this repository or covered by
this project's MIT license.

TP-7 and Teenage Engineering are names of their respective owners. This project is independent
and is not affiliated with or endorsed by Teenage Engineering or the original application author.
