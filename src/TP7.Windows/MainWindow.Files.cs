using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using TP7.Core;

namespace TP7.Windows;

public partial class MainWindow
{
    private bool ConfirmDiscard()
        => session?.IsDirty != true || MessageBox.Show(this, "You have changes that have not been exported. Discard them?", "Unsaved changes", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;

    private async Task OpenFile()
    {
        if (operation != null)
        {
            return;
        }

        var dialog = new OpenFileDialog { Filter = "WAV files (*.wav)|*.wav|All files (*.*)|*.*", Title = "Open a WAV recording", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true)
        {
            await LoadFile(dialog.FileName);
        }
    }

    private async Task LoadFile(string path)
    {
        if (operation != null || !ConfirmDiscard())
        {
            return;
        }

        player.Stop();
        BeginOperation("Loading recording…");
        WaveDocument? loaded = null;
        try
        {
            var token = operation!.Token;
            loaded = await Task.Run(() => WaveDocument.Load(path, new Progress<double>(value => Dispatcher.BeginInvoke(() => Progress.Value = value)), token), token);
            var old = session?.Document;
            session = new EditorSession(loaded);
            loaded = null;
            Waveform.Session = Overview.Session = session;
            old?.Dispose();
            taps.Clear();
            Transport.IsEnabled = EditorPanel.IsEnabled = ExportButton.IsEnabled = true;
            FileNameText.Text = Path.GetFileName(session.Document.FilePath);
            FileNameText.ToolTip = session.Document.FilePath;
            FileInfoText.Text = $"{session.Document.Format.Description} · {WaveformView.FormatTime(session.Document.Duration)}";
            SetView(0, session.Document.Duration);
            RefreshEditor();
            string extra = session.Document.Format.Channels > 2 ? " Preview shows channels 1–2; export retains every channel." : "";
            SetStatus(session.Document.Warnings.Count > 0 ? string.Join(" ", session.Document.Warnings) : $"Recording loaded. Cue points: {session.Cues.Count}.{extra}");
        }
        catch (OperationCanceledException) { SetStatus("Loading canceled."); }
        catch (Exception ex) { ShowError("Unable to open WAV", ex); }
        finally
        {
            loaded?.Dispose();
            EndOperation();
        }
    }

    private void BeginOperation(string text)
    {
        operation = new();
        BusyText.Text = text;
        Progress.Value = 0;
        BusyOverlay.Visibility = Visibility.Visible;
    }

    private void EndOperation()
    {
        operation?.Dispose();
        operation = null;
        BusyOverlay.Visibility = Visibility.Collapsed;
        Waveform.Focus();
    }

    private async Task ExportFile()
    {
        if (session == null || operation != null || !CommitTempo() || !CommitCueTime())
        {
            return;
        }

        var edit = session.Snapshot();
        var dialog = new SaveFileDialog { Filter = "WAV files (*.wav)|*.wav", Title = "Export WAV for TP-7", FileName = session.Document.SuggestedFileName(edit), InitialDirectory = Path.GetDirectoryName(session.Document.FilePath), DefaultExt = ".wav", AddExtension = true, OverwritePrompt = true };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        player.Stop();
        BeginOperation("Exporting WAV…");
        try
        {
            var doc = session.Document;
            var token = operation!.Token;
            await Task.Run(() => doc.Export(dialog.FileName, edit, new Progress<double>(value => Dispatcher.BeginInvoke(() => Progress.Value = value)), token), token);
            session.MarkSaved();
            RefreshEditor();
            SetStatus($"Saved: {dialog.FileName}");
        }
        catch (OperationCanceledException) { SetStatus("Export canceled. The destination file was not changed."); }
        catch (Exception ex) { ShowError("Unable to save WAV", ex); }
        finally { EndOperation(); }
    }

    private void ShowError(string title, Exception ex)
    {
        SetStatus($"{title}: {ex.Message}");
        MessageBox.Show(this, ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = operation == null && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths)
        {
            await LoadFile(paths[0]);
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (operation != null)
        {
            e.Cancel = true;
            operation.Cancel();
            SetStatus("Canceling the operation. You can close the window when it finishes.");
            return;
        }
        if (!ConfirmDiscard())
        {
            e.Cancel = true;
            return;
        }
        timer.Stop();
        player.Dispose();
        session?.Document.Dispose();
    }
}
