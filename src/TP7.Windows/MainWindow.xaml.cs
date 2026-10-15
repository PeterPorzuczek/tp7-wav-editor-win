using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using TP7.Audio;
using TP7.Core;

namespace TP7.Windows;

public partial class MainWindow : Window
{
    private EditorSession? session;
    private readonly AudioPlayer player = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly List<double> taps = [];
    private readonly Stopwatch tapClock = Stopwatch.StartNew();
    private CancellationTokenSource? operation;
    private bool refreshing, spaceHeld;
    private sealed record CueRow(uint Id, string Number, string Time)
    {
        public override string ToString() => $"Cue {Number}: {Time}";
    }

    public MainWindow()
    {
        InitializeComponent();
        Waveform.Edited = RefreshEditor;
        Waveform.ViewChanged = SetView;
        Overview.ViewChanged = SetView;
        timer.Tick += (_, _) =>
        {
            double? position = player.IsPlaying ? player.Position : null;
            Waveform.PlaybackTime = Overview.PlaybackTime = position;
            PositionText.Text = WaveformView.FormatTime(position ?? (session?.SelectedCue is { } cue ? session.Seconds(cue) : Waveform.HoverTime ?? 0));
            PlayButton.Content = player.IsPlaying ? "■  Stop" : "▶  Play";
            Waveform.InvalidateVisual();
            Overview.InvalidateVisual();
        };
        timer.Start();
        Deactivated += (_, _) =>
        {
            if (spaceHeld)
            {
                player.Stop();
                spaceHeld = false;
            }
        };
        Loaded += async (_, _) =>
        {
            string? path = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
            if (path != null)
            {
                await LoadFile(path);
            }
        };
    }

    private void SetStatus(string text)
    {
        StatusText.Text = text;
        StatusText.ToolTip = text;
    }

    private void SetView(double start, double duration)
    {
        if (session == null)
        {
            return;
        }

        duration = Math.Clamp(duration, Math.Min(.002, session.Document.Duration), session.Document.Duration);
        Waveform.ViewStart = Overview.ViewStart = Math.Clamp(start, 0, session.Document.Duration - duration);
        Waveform.ViewDuration = Overview.ViewDuration = duration;
        Waveform.InvalidateVisual();
        Overview.InvalidateVisual();
    }

    private void RefreshEditor()
    {
        if (session == null)
        {
            return;
        }

        refreshing = true;
        try
        {
            TempoBox.Text = session.Tempo.ToString("0.###", CultureInfo.InvariantCulture);
            CueList.ItemsSource = session.Cues.Select((c, i) => new CueRow(c.Id, (i + 1).ToString("00"), WaveformView.FormatTime(session.Seconds(c)))).ToArray();
            CueList.SelectedItem = CueList.Items.Cast<CueRow>().FirstOrDefault(r => r.Id == session.SelectedId);
            CueCountText.Text = session.Cues.Count.ToString();
            CueTimeBox.IsEnabled = session.SelectedCue != null;
            CueTimeBox.Text = session.SelectedCue is { } cue ? session.Seconds(cue).ToString("0.#########", CultureInfo.InvariantCulture) : "—";
            GridToggle.IsEnabled = SnapToggle.IsEnabled = session.Tempo > 0;
            GridToggle.IsChecked = session.ShowGrid;
            SnapToggle.IsChecked = session.SnapToGrid;
            UndoButton.IsEnabled = session.CanUndo;
            RedoButton.IsEnabled = session.CanRedo;
            NormalizeButton.Content = session.Normalized ? "✓  Audio normalized" : "Normalize audio  ·  N";
            NormalizeButton.IsEnabled = !session.Normalized && session.Document.Peak > 0;
            Title = $"{(session.IsDirty ? "● " : "")}{Path.GetFileName(session.Document.FilePath)} — TP-7 WAV Editor";
            Waveform.InvalidateVisual();
            Overview.InvalidateVisual();
        }
        finally { refreshing = false; }
    }

    private void StartPlayback()
    {
        if (session == null)
        {
            return;
        }

        try
        {
            player.Play(session.Document, session.SelectedCue is { } cue ? session.Seconds(cue) : Waveform.HoverTime ?? 0, session.Normalized, VolumeSlider.Value);
        }
        catch (Exception ex) { ShowError("Unable to start playback", ex); }
    }

    private void TapTempo()
    {
        if (session == null)
        {
            return;
        }

        double now = tapClock.Elapsed.TotalSeconds;
        if (taps.Count > 0 && now - taps[^1] > 2)
        {
            taps.Clear();
        }

        taps.Add(now);
        if (taps.Count > 8)
        {
            taps.RemoveAt(0);
        }

        if (taps.Count > 1)
        {
            double bpm = 60 * (taps.Count - 1) / (taps[^1] - taps[0]);
            session.SetTempo(Math.Clamp(Math.Round(bpm), 1, 999));
            RefreshEditor();
        }
        SetStatus(taps.Count == 1 ? "First tap recorded. Tap again (T)." : $"Tap tempo: {session.Tempo:0} BPM · {taps.Count} taps.");
    }

    private static bool TryNumber(string text, out double value) => double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

    private bool CommitTempo()
    {
        if (session == null || refreshing)
        {
            return true;
        }

        if (!TryNumber(TempoBox.Text, out double value) || value is < 0 or > 999)
        {
            SetStatus("Enter a tempo from 0 to 999 BPM. Use 0 to remove tempo on export.");
            TempoBox.Text = session.Tempo.ToString("0.###");
            return false;
        }
        if (value != session.Tempo)
        {
            session.SetTempo(value);
            RefreshEditor();
        }
        return true;
    }

    private bool CommitCueTime()
    {
        if (session?.SelectedCue == null || refreshing)
        {
            return true;
        }

        if (!TryNumber(CueTimeBox.Text, out double value) || value < 0 || value > session.Document.Duration)
        {
            SetStatus("The cue position must be within the recording.");
            RefreshEditor();
            return false;
        }
        session.MoveSelected(value);
        RefreshEditor();
        return true;
    }

    private void OnTempoCommit(object sender, KeyboardFocusChangedEventArgs e) => CommitTempo();

    private void OnTempoKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitTempo();
            Waveform.Focus();
            e.Handled = true;
        }
    }

    private void OnCueTimeCommit(object sender, KeyboardFocusChangedEventArgs e) => CommitCueTime();

    private void OnCueTimeKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitCueTime();
            Waveform.Focus();
            e.Handled = true;
        }
    }

    private void OnVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => player.SetVolume(e.NewValue);

    private void OnGridToggle(object sender, RoutedEventArgs e)
    {
        if (session == null)
        {
            return;
        }

        session.ShowGrid = GridToggle.IsChecked == true;
        if (!session.ShowGrid)
        {
            session.SnapToGrid = false;
        }

        RefreshEditor();
    }

    private void OnSnapToggle(object sender, RoutedEventArgs e)
    {
        if (session == null)
        {
            return;
        }

        session.SnapToGrid = SnapToggle.IsChecked == true;
        if (session.SnapToGrid)
        {
            session.ShowGrid = true;
        }

        RefreshEditor();
    }

    private void OnCueSelected(object sender, SelectionChangedEventArgs e)
    {
        if (refreshing || session == null)
        {
            return;
        }

        session.SelectedId = (CueList.SelectedItem as CueRow)?.Id;
        CenterSelectedCue();
        RefreshEditor();
    }

    private void CenterSelectedCue()
    {
        if (session?.SelectedCue is { } cue)
        {
            SetView(session.Seconds(cue) - Waveform.ViewDuration / 2, Waveform.ViewDuration);
        }
    }

    private void SelectCue(int direction)
    {
        if (session == null || session.Cues.Count == 0)
        {
            return;
        }

        int index = session.SelectedCue is { } selected ? session.Cues.IndexOf(selected) + direction : direction > 0 ? 0 : session.Cues.Count - 1;
        session.SelectedId = session.Cues[Math.Clamp(index, 0, session.Cues.Count - 1)].Id;
        CenterSelectedCue();
        RefreshEditor();
    }

    private void Nudge(int direction)
    {
        if (session?.SelectedCue is not { } cue)
        {
            return;
        }

        double step = session.SnapToGrid ? Waveform.GridStep : Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 1d / session.Document.Format.SampleRate : .05 * Waveform.ViewDuration / session.Document.Duration;
        session.MoveSelected(session.Snap(session.Seconds(cue) + step * direction, Waveform.GridStep));
        RefreshEditor();
    }
}
