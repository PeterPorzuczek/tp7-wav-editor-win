using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using TP7.Audio;
using TP7.Core;

namespace TP7.Windows;

public partial class MainWindow
{
    private readonly List<StereoStem> stems = [];
    private bool multitrackMode, stemsDirty, refreshingStems;
    private int outputRate = 48000;
    private sealed record StemRow(string Number, string Name, string Detail, string Path)
    {
        public override string ToString() => $"Stem {Number}: {Name}, {Detail}";
    }

    private void CloseDocument()
    {
        player.Stop();
        session = null;
        Waveform.Session = Overview.Session = null;
        Waveform.PreviewDocument = Overview.PreviewDocument = null;
        recording?.Dispose();
        recording = null;
        foreach (var stem in stems)
        {
            stem.Dispose();
        }
        stems.Clear();
        stemsDirty = false;
    }

    private void SetMultitrackMode(bool enabled)
    {
        multitrackMode = enabled;
        MultitrackPanel.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        StemColumn.Width = new GridLength(enabled ? 250 : 0);
        StemGap.Width = new GridLength(enabled ? 18 : 0);
        MinWidth = enabled ? 1280 : 1040;
        MinHeight = enabled ? 760 : 740;
        MultitrackButton.Content = enabled ? "New multitrack" : "Multitrack";
        ExportButton.Content = enabled ? "Export multitrack  ↗" : "Export WAV  ↗";
    }

    private void NewMultitrack()
    {
        if (!ConfirmDiscard())
        {
            return;
        }
        CloseDocument();
        SetMultitrackMode(true);
        RefreshStems();
        SetStatus("Add or drop stereo WAVs. Different sample rates are converted automatically.");
    }

    private async void OnAddStems(object sender, RoutedEventArgs e)
    {
        if (operation != null)
        {
            return;
        }
        var dialog = new OpenFileDialog { Filter = "Stereo WAV files (*.wav)|*.wav", Title = "Add stereo stems in channel order", Multiselect = true, CheckFileExists = true };
        if (dialog.ShowDialog(this) == true)
        {
            await AddStems(dialog.FileNames);
        }
    }

    private async Task AddStems(string[] paths)
    {
        if (operation != null || !multitrackMode || paths.Length == 0 || !CommitTempo() || !CommitCueTime())
        {
            return;
        }
        int maximum = MultitrackDocument.MaximumStems(outputRate);
        if (stems.Count + paths.Length > maximum)
        {
            ShowError("Too many stems", new InvalidDataException($"{outputRate / 1000} kHz supports up to {maximum} stereo stems. You can add {maximum - stems.Count} more."));
            return;
        }
        player.Stop();
        BeginOperation("Preparing stereo stems…");
        var added = new List<StereoStem>();
        try
        {
            var token = operation!.Token;
            var progress = UiProgress();
            await Task.Run(() =>
            {
                for (int i = 0; i < paths.Length; i++)
                {
                    token.ThrowIfCancellationRequested();
                    added.Add(StereoStem.Load(paths[i], outputRate, new MappedProgress(progress, (double)i / paths.Length, 1d / paths.Length), token));
                }
                token.ThrowIfCancellationRequested();
            }, token);
            int selected = stems.Count;
            stems.AddRange(added);
            added.Clear();
            RebuildMultitrack();
            RefreshStems(selected);
            SetStatus("Stems ready. Select a stem to audition, then add shared cues on the waveform.");
        }
        catch (OperationCanceledException) { SetStatus("Adding stems canceled. The previous set is unchanged."); }
        catch (Exception ex) { ShowError("Unable to add stems", ex); }
        finally
        {
            foreach (var stem in added)
            {
                stem.Dispose();
            }
            EndOperation();
        }
    }

    private IProgress<double> UiProgress() => new Progress<double>(value => Progress.Value = value);

    private void RebuildMultitrack()
    {
        var previous = session;
        var document = stems.Count > 0 ? new MultitrackDocument(outputRate, stems.Select(s => s.Audio)) : null;
        if (document == null)
        {
            session = null;
        }
        else
        {
            double ratio = previous == null ? 1 : (double)outputRate / previous.Document.Format.SampleRate;
            var initial = previous?.Snapshot() ?? new EditSnapshot(stems[0].Original.InitialTempo,
                stems[0].Original.InitialCues.Select(c => c with { Frame = (long)Math.Round(c.Frame * (double)outputRate / stems[0].Original.Format.SampleRate, MidpointRounding.AwayFromZero) }).ToArray(), false);
            if (previous != null)
            {
                initial = initial with { Cues = initial.Cues.Select(c => c with { Frame = (long)Math.Round(c.Frame * ratio, MidpointRounding.AwayFromZero) }).ToArray() };
            }
            // Rounding between sample rates can move an end marker by at most one frame.
            initial = initial with { Cues = initial.Cues.Select(c => c with { Frame = Math.Min(c.Frame, document.FrameCount) }).ToArray() };
            session = new(document, initial)
            {
                SelectedId = previous?.SelectedId,
                ShowGrid = previous?.ShowGrid ?? false,
                SnapToGrid = previous?.SnapToGrid ?? false,
                GridOffset = previous?.GridOffset ?? 0
            };
        }
        stemsDirty = true;
        Waveform.Session = Overview.Session = session;
        taps.Clear();
    }

    private void RefreshStems(int selected = 0)
    {
        refreshingStems = true;
        try
        {
            Mode48.IsChecked = outputRate == 48000;
            Mode96.IsChecked = outputRate == 96000;
            StemList.ItemsSource = stems.Select((s, i) => new StemRow((i + 1).ToString("00"), s.Name,
                $"CH {i * 2 + 1}–{i * 2 + 2} · {WaveformView.FormatTime(s.Audio.Duration, false)} · {s.Original.Format.SampleRate / 1000d:0.###}{(s.Original.Format.SampleRate != outputRate ? $" → {outputRate / 1000}" : "")} kHz", s.Original.FilePath)).ToArray();
            StemList.SelectedIndex = stems.Count > 0 ? Math.Clamp(selected, 0, stems.Count - 1) : -1;
            int maximum = MultitrackDocument.MaximumStems(outputRate);
            StemCountText.Text = $"{stems.Count} / {maximum}";
            AddStemsButton.IsEnabled = stems.Count < maximum;
            StemEmptyText.Visibility = stems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            Transport.IsEnabled = EditorPanel.IsEnabled = ExportButton.IsEnabled = session != null;
        }
        finally { refreshingStems = false; }
        SelectStemPreview();
        if (session != null)
        {
            SetView(0, session.Document.Duration);
            RefreshEditor();
        }
        else
        {
            FileNameText.Text = "Your stems. One timeline.";
            FileInfoText.Text = "Add stereo WAVs to build a multitrack recording.";
            MultitrackSummary.Text = "Add stems to begin.";
            CueList.ItemsSource = null;
            CueCountText.Text = "0";
            CueTimeBox.Text = "—";
            Title = "Multitrack — TP-7 WAV Editor";
        }
    }

    private void UpdateMultitrackSummary()
    {
        if (session?.Document is not MultitrackDocument document)
        {
            return;
        }
        try
        {
            long size = document.ExportSize(session.Snapshot());
            MultitrackSummary.Text = $"{document.StemCount * 2} channels · 24-bit PCM\n{WaveformView.FormatTime(document.Duration)} · {size / 1048576d:0.0} MB\n{session.Cues.Count} shared cue points";
            if (document.Peak > 1 && !session.Normalized)
            {
                MultitrackSummary.Text += "\nOver 0 dBFS: normalize to avoid clipping.";
            }
            ExportButton.IsEnabled = true;
        }
        catch (InvalidDataException ex)
        {
            MultitrackSummary.Text = ex.Message;
            ExportButton.IsEnabled = false;
        }
    }

    private void OnStemSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!refreshingStems)
        {
            player.Stop();
            SelectStemPreview();
        }
    }

    private void SelectStemPreview()
    {
        int index = StemList.SelectedIndex;
        var stem = index >= 0 && index < stems.Count ? stems[index] : null;
        Waveform.PreviewDocument = Overview.PreviewDocument = stem?.Audio;
        if (stem != null)
        {
            FileNameText.Text = $"Stem {index + 1:00} / {stem.Name}";
            FileNameText.ToolTip = stem.Original.FilePath;
            FileInfoText.Text = $"Preview: channels {index * 2 + 1}–{index * 2 + 2} · {outputRate / 1000} kHz · shared cue timeline";
        }
        StemUpButton.IsEnabled = index > 0;
        StemDownButton.IsEnabled = index >= 0 && index < stems.Count - 1;
        RemoveStemButton.IsEnabled = stem != null;
        Waveform.InvalidateVisual();
        Overview.InvalidateVisual();
    }

    private void OnStemUp(object sender, RoutedEventArgs e) => MoveStem(-1);
    private void OnStemDown(object sender, RoutedEventArgs e) => MoveStem(1);
    private void MoveStem(int direction)
    {
        int from = StemList.SelectedIndex, to = from + direction;
        if (operation != null || from < 0 || to < 0 || to >= stems.Count || !CommitTempo() || !CommitCueTime())
        {
            return;
        }
        player.Stop();
        (stems[from], stems[to]) = (stems[to], stems[from]);
        RebuildMultitrack();
        RefreshStems(to);
        SetStatus("Channel order updated. Cue positions are unchanged; cue undo history has been reset.");
    }

    private void OnRemoveStem(object sender, RoutedEventArgs e)
    {
        int index = StemList.SelectedIndex;
        if (operation != null || index < 0 || !CommitTempo() || !CommitCueTime())
        {
            return;
        }
        long remainingFrames = stems.Where((_, i) => i != index).Select(s => s.Audio.FrameCount).DefaultIfEmpty(0).Max();
        if (session?.Cues.Any(c => c.Frame > remainingFrames) == true || (stems.Count == 1 && session?.Cues.Count > 0))
        {
            SetStatus("Move or clear cues beyond the remaining stems before removing this stem.");
            return;
        }
        player.Stop();
        var removed = stems[index];
        stems.RemoveAt(index);
        RebuildMultitrack();
        RefreshStems(index);
        removed.Dispose();
        SetStatus("Stem removed. Cue undo history has been reset.");
    }

    private async void OnOutputRateChanged(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized || refreshingStems || !multitrackMode || operation != null)
        {
            return;
        }
        int rate = Mode96.IsChecked == true ? 96000 : 48000;
        if (rate == outputRate)
        {
            return;
        }
        int selected = StemList.SelectedIndex;
        if (stems.Count > MultitrackDocument.MaximumStems(rate))
        {
            RefreshStems(selected);
            ShowError("Three stems maximum at 96 kHz", new InvalidDataException("Remove stems until three or fewer remain before switching to 96 kHz."));
            return;
        }
        if (!CommitTempo() || !CommitCueTime())
        {
            RefreshStems(selected);
            return;
        }
        player.Stop();
        BeginOperation($"Preparing {rate / 1000} kHz stems…");
        var prepared = new List<WaveDocument>();
        try
        {
            var token = operation!.Token;
            var progress = UiProgress();
            await Task.Run(() =>
            {
                for (int i = 0; i < stems.Count; i++)
                {
                    prepared.Add(StemResampler.Prepare(stems[i].Original, rate, new MappedProgress(progress, (double)i / stems.Count, 1d / stems.Count), token));
                }
                token.ThrowIfCancellationRequested();
            }, token);
            for (int i = 0; i < stems.Count; i++)
            {
                stems[i].ReplacePrepared(prepared[i]);
            }
            prepared.Clear();
            outputRate = rate;
            RebuildMultitrack();
            SetStatus($"{rate / 1000} kHz ready. Cue times preserved to the nearest sample; cue undo history has been reset.");
        }
        catch (OperationCanceledException) { SetStatus("Rate change canceled. The previous set is unchanged."); }
        catch (Exception ex) { ShowError("Unable to change sample rate", ex); }
        finally
        {
            for (int i = 0; i < prepared.Count; i++)
            {
                if (prepared[i] != stems[i].Original)
                {
                    prepared[i].Dispose();
                }
            }
            RefreshStems(selected);
            EndOperation();
        }
    }
}
