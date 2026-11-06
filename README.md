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

