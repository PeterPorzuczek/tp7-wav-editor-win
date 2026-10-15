using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TP7.Windows;

public partial class MainWindow
{
    private async void OnAction(object sender, RoutedEventArgs e) => await ExecuteAction((string)((Button)sender).Tag);

    private async Task ExecuteAction(string command)
    {
        if (command == "cancel")
        {
            operation?.Cancel();
            return;
        }
        if (operation != null)
        {
            return;
        }

        if (command == "open")
        {
            await OpenFile();
            return;
        }
        if (command == "help")
        {
            ShowHelp();
            return;
        }
        if (session == null)
        {
            return;
        }

        switch (command)
        {
            case "export":
                await ExportFile();
                break;
            case "play":
                if (player.IsPlaying)
                {
                    player.Stop();
                }
                else
                {
                    StartPlayback();
                }

                break;
            case "stop":
                player.Stop();
                break;
            case "tap":
                TapTempo();
                break;
            case "calculate":
                SetStatus(session.CalculateTempoFromCues() ? $"Tempo: {session.Tempo:0} BPM. The interval between cues represents 4 beats." : "Select a cue that has a following marker. The interval represents 4 beats.");
                break;
            case "align":
                session.AlignGrid();
                SetStatus(session.SelectedCue != null ? "Grid aligned to the selected cue." : "Select a cue first.");
                break;
            case "add":
                session.AddCue(session.Snap(player.IsPlaying ? player.Position : Waveform.HoverTime ?? Waveform.ViewStart + Waveform.ViewDuration / 2, Waveform.GridStep));
                break;
            case "delete":
                session.DeleteSelected();
                break;
            case "clear":
                session.DeleteAll();
                SetStatus("All cues cleared. Press Ctrl+Z to undo.");
                break;
            case "normalize":
                player.Stop();
                session.Normalize();
                SetStatus(session.Normalized ? $"Normalization: {20 * Math.Log10(session.Document.NormalizationGain):+0.00;-0.00;0} dB. Press Ctrl+Z to undo." : "The recording is silent. There is nothing to normalize.");
                break;
            case "undo":
                player.Stop();
                session.Undo();
                break;
            case "redo":
                player.Stop();
                session.Redo();
                break;
            case "zoomin":
                Waveform.Zoom(.5);
                break;
            case "zoomout":
                Waveform.Zoom(2);
                break;
            case "fit":
                SetView(0, session.Document.Duration);
                break;
        }
        RefreshEditor();
    }

    private async void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (operation != null)
        {
            if (e.Key == Key.Escape)
            {
                operation.Cancel();
            }

            return;
        }
        bool editing = Keyboard.FocusedElement is TextBox;
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        string? command = null;
        if (e.Key == Key.F1)
        {
            command = "help";
        }
        else if (ctrl && e.Key == Key.O)
        {
            command = "open";
        }
        else if (ctrl && e.Key == Key.S)
        {
            command = "export";
        }
        else if (editing)
        {
            return;
        }
        else if (ctrl && e.Key == Key.Z)
        {
            command = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? "redo" : "undo";
        }
        else if (ctrl && e.Key == Key.Y)
        {
            command = "redo";
        }
        else if (ctrl || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            return;
        }
        else if (e.Key == Key.Space)
        {
            e.Handled = true;
            if (!e.IsRepeat)
            {
                spaceHeld = true;
                StartPlayback();
            }
            return;
        }
        else
        {
            command = e.Key switch
            {
                Key.I => "open",
                Key.E => "export",
                Key.N => "normalize",
                Key.T => "tap",
                Key.C => "calculate",
                Key.Tab => "align",
                Key.Delete or Key.Back => "delete",
                Key.Insert => "add",
                Key.Home => "fit",
                Key.Add or Key.OemPlus => "zoomin",
                Key.Subtract or Key.OemMinus => "zoomout",
                _ => null
            };
        }

        if (command != null)
        {
            e.Handled = true;
            if (!e.IsRepeat)
            {
                await ExecuteAction(command);
            }

            return;
        }
        if (session == null)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Left:
                SelectCue(-1);
                break;
            case Key.Right:
                SelectCue(1);
                break;
            case Key.Down:
                session.SelectedId = session.Cues.OrderBy(c => Math.Abs(session.Seconds(c) - (Waveform.ViewStart + Waveform.ViewDuration / 2))).FirstOrDefault()?.Id;
                CenterSelectedCue();
                break;
            case Key.Escape:
            case Key.Up:
                session.SelectedId = null;
                break;
            case Key.G:
                session.ShowGrid = session.Tempo > 0 && !session.ShowGrid;
                if (!session.ShowGrid)
                {
                    session.SnapToGrid = false;
                }

                break;
            case Key.S:
                session.SnapToGrid = session.Tempo > 0 && !session.SnapToGrid;
                if (session.SnapToGrid)
                {
                    session.ShowGrid = true;
                }

                break;
            case Key.OemOpenBrackets:
                Nudge(-1);
                break;
            case Key.OemCloseBrackets:
                Nudge(1);
                break;
            default:
                return;
        }
        e.Handled = true;
        RefreshEditor();
    }

    private void OnKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && spaceHeld)
        {
            player.Stop();
            spaceHeld = false;
            e.Handled = true;
        }
    }

    private void ShowHelp()
    {
        var window = new Window { Owner = this, Title = "Keyboard shortcuts — TP-7 WAV Editor", Width = 610, Height = 610, ResizeMode = ResizeMode.CanResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Background, Foreground = Foreground };
        using var stream = typeof(MainWindow).Assembly.GetManifestResourceStream("TP7.Windows.Resources.KeyboardShortcuts.txt")!;
        using var reader = new StreamReader(stream);
        string text = reader.ReadToEnd();
        window.Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new TextBlock { Text = text, FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 12, Margin = new Thickness(24), TextWrapping = TextWrapping.Wrap, Foreground = Foreground } };
        window.ShowDialog();
    }
}
