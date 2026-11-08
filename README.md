# TP-7 WAV Editor

A native Windows editor for tempo and cue metadata in TP-7 WAV recordings.

## Why TP-7 WAV Editor

Prepare the timing and cue points of a recording in one place. See the waveform, line up the
beat grid, audition a section, and give each cue an exact position. Metadata-only exports keep
**every audio sample unchanged**; normalization is a separate, optional step.

This is a native Windows implementation inspired by
[TP-7 Wav Editor 2.2 by EcstasyJones / Max Solomon](https://www.ecstasyjones.com/tp-7-wav-editor).
It works with local WAV files. It does not upload recordings or connect directly to the recorder.

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

Tempo **0** removes tempo metadata on export. A file without tempo metadata opens at **120 BPM**,
matching the reference application's default; set it to 0 to export without tempo.

Normalization is optional and can be undone. Exporting over the source is supported: undo and
subsequent exports continue to use the original audio rather than an earlier normalized export.

## Getting started

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
| Multiple channels | Up to 32; waveform and preview use channels 1–2, export retains all channels |

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
