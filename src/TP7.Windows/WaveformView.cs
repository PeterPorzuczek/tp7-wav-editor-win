using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using TP7.Core;

namespace TP7.Windows;

public sealed class WaveformView : FrameworkElement
{
    private static readonly Brush WaveBrush = Brush("#93C9AC"), Muted = Brush("#718F7E"), Orange = Brush("#FF895B");
    private static readonly Typeface Mono = new("Consolas");
    private DrawingGroup? waveformDrawing;
    private (WaveDocument?, double, double, double, double, bool) drawingKey;
    private bool draggingCue, dragCheckpoint, draggingOverview;
    private Point mouseDown;
    private uint? dragCueId;
    public bool IsOverview { get; set; }
    public EditorSession? Session { get; set; }
    public double ViewStart { get; set; }
    public double ViewDuration { get; set; } = 1;
    public double? HoverTime { get; private set; }
    public double? PlaybackTime { get; set; }
    public Action? Edited { get; set; }
    public Action<double, double>? ViewChanged { get; set; }
    private double Start => IsOverview ? 0 : ViewStart;
    private double Duration => IsOverview ? Session?.Document.Duration ?? 1 : ViewDuration;
    private double PlotTop => IsOverview ? 3 : 42;
    private double PlotBottom => Math.Max(PlotTop + 1, ActualHeight - (IsOverview ? 3 : 20));
    private double PlotWidth => Math.Max(1, ActualWidth - 24);
    public double GridStep => Session?.GridStep(ViewDuration, PlotWidth) ?? 0;

    private static readonly double[] sourceArray = new[] { 1d, 2d, 5d, 10d };

    private double X(double time) => 12 + (time - Start) / Duration * PlotWidth;

    private double Time(double x) => Math.Clamp(Start + (x - 12) / PlotWidth * Duration, 0, Session?.Document.Duration ?? 0);
    private static SolidColorBrush Brush(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }

    private void Text(DrawingContext dc, string text, double x, double y, Brush brush, double size = 10)
        => dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip), new Point(x, y));

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (ActualWidth < 24 || ActualHeight < 10)
        {
            return;
        }

        dc.PushClip(new RectangleGeometry(new Rect(RenderSize)));
        if (Session is not { } session)
        {
            if (!IsOverview)
            {
                double y = ActualHeight / 2;
                dc.DrawLine(new Pen(Brush("#304638"), 1), new Point(35, y + 40), new Point(ActualWidth - 35, y + 40));
                Text(dc, "OPEN OR DROP A WAV FILE", Math.Max(20, ActualWidth / 2 - 142), y - 30, WaveBrush, 17);
                Text(dc, "Set the tempo. Mark the moments.", Math.Max(20, ActualWidth / 2 - 116), y + 4, Muted, 13);
            }
            dc.Pop();
            return;
        }
        var doc = session.Document;
        if (!IsOverview)
        {
            double secondsPerLabel = Duration / Math.Max(1, PlotWidth / 100);
            double power = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(.000001, secondsPerLabel))));
            double step = sourceArray.First(n => n * power >= secondsPerLabel) * power;
            for (double t = Math.Ceiling(Start / step) * step; t <= Start + Duration; t += step)
            {
                double x = X(t);
                dc.DrawLine(new Pen(Brush("#253A2E"), 1), new Point(x, 28), new Point(x, PlotBottom));
                Text(dc, FormatTime(t, Duration < 20), Math.Clamp(x - 10, 4, Math.Max(4, ActualWidth - 73)), 6, Muted);
            }
            if (session.ShowGrid && session.Tempo > 0)
            {
                double gridStep = GridStep;
                double first = Math.Ceiling((Start - session.GridOffset) / gridStep) * gridStep + session.GridOffset;
                double beat = 60 / session.Tempo;
                for (double t = first; t <= Start + Duration + .00000001; t += gridStep)
                {
                    double beats = (t - session.GridOffset) / beat;
                    bool downbeat = Math.Abs(beats / 4 - Math.Round(beats / 4)) < .00001;
                    dc.DrawLine(new Pen(Brush(downbeat ? "#587454" : "#354C36"), downbeat ? 1.3 : .7), new Point(X(t), 28), new Point(X(t), PlotBottom));
                }
            }
        }
        var key = (doc, Start, Duration, ActualWidth, ActualHeight, session.Normalized);
        if (waveformDrawing == null || drawingKey != key)
        {
            drawingKey = key;
            waveformDrawing = new DrawingGroup();
            using var waveContext = waveformDrawing.Open();
            double gain = session.Normalized ? doc.NormalizationGain : 1;
            int channels = IsOverview ? 1 : doc.Peaks.DisplayChannels;
            for (int c = 0; c < channels; c++)
            {
                double laneHeight = (PlotBottom - PlotTop) / channels;
                double center = PlotTop + laneHeight * (c + .5), amplitude = laneHeight * .43;
                waveContext.DrawLine(new Pen(Brush("#304638"), .7), new Point(12, center), new Point(ActualWidth - 12, center));
                var geometry = new StreamGeometry();
                using (var g = geometry.Open())
                {
                    for (int x = 0; x < (int)PlotWidth; x++)
                    {
                        long first = (long)((Start + x / PlotWidth * Duration) * doc.Format.SampleRate);
                        long last = (long)((Start + (x + 1) / PlotWidth * Duration) * doc.Format.SampleRate);
                        var peak = doc.Peaks.Query(first, last, c);
                        g.BeginFigure(new Point(x + 12.5, center - Math.Clamp(peak.Max * gain, -1, 1) * amplitude), false, false);
                        g.LineTo(new Point(x + 12.5, center - Math.Clamp(peak.Min * gain, -1, 1) * amplitude + .4), true, false);
                    }
                }

                geometry.Freeze();
                waveContext.DrawGeometry(null, new Pen(IsOverview ? Muted : WaveBrush, 1), geometry);
                if (!IsOverview)
                {
                    Text(waveContext, channels == 1 ? "MONO" : c == 0 ? "L" : "R", 18, PlotTop + laneHeight * c + 5, Muted, 9);
                }
            }
        }
        dc.DrawDrawing(waveformDrawing);
        for (int i = 0; i < session.Cues.Count; i++)
        {
            var cue = session.Cues[i];
            double x = X(session.Seconds(cue));
            if (x < 10 || x > ActualWidth - 10)
            {
                continue;
            }

            bool selected = cue.Id == session.SelectedId;
            dc.DrawLine(new Pen(selected ? Orange : Brush("#BA895D"), selected ? 2 : 1), new Point(x, IsOverview ? 2 : 30), new Point(x, PlotBottom));
            if (!IsOverview)
            {
                dc.DrawRoundedRectangle(selected ? Orange : Brush("#5D4B31"), null, new Rect(x - 1, 28, 28, 16), 2, 2);
                Text(dc, (i + 1).ToString("00"), x + 4, 29, selected ? Brush("#19291C") : Brush("#F5CF9D"));
            }
        }
        if (IsOverview)
        {
            double left = X(ViewStart), right = X(ViewStart + ViewDuration);
            dc.DrawRectangle(Brush("#80101A16"), null, new Rect(0, 0, Math.Max(0, left), ActualHeight));
            dc.DrawRectangle(Brush("#80101A16"), null, new Rect(Math.Min(right, ActualWidth), 0, Math.Max(0, ActualWidth - right), ActualHeight));
            dc.DrawRoundedRectangle(null, new Pen(Brush("#B0C3AB"), 1), new Rect(left, 1, Math.Max(1, right - left), ActualHeight - 2), 3, 3);
        }
        else if (HoverTime is { } hover && !draggingCue)
        {
            dc.DrawLine(new Pen(Brush("#718F7E"), 1) { DashStyle = DashStyles.Dot }, new Point(X(hover), PlotTop), new Point(X(hover), PlotBottom));
        }

        if (PlaybackTime is { } playing && playing >= Start && playing <= Start + Duration)
        {
            dc.DrawLine(new Pen(Brushes.White, 1.5), new Point(X(playing), PlotTop), new Point(X(playing), PlotBottom));
        }

        dc.Pop();
    }

    public static string FormatTime(double seconds, bool milliseconds = true)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return milliseconds ? $"{(int)time.TotalMinutes:00}:{time.Seconds:00}.{time.Milliseconds:000}" : $"{(int)time.TotalMinutes:00}:{time.Seconds:00}";
    }

    public void Zoom(double factor, double? anchor = null)
    {
        if (Session == null)
        {
            return;
        }

        double point = anchor ?? (ViewStart + ViewDuration / 2);
        double ratio = Math.Clamp((point - ViewStart) / ViewDuration, 0, 1);
        double duration = Math.Clamp(ViewDuration * factor, Math.Min(.002, Session.Document.Duration), Session.Document.Duration);
        ViewChanged?.Invoke(Math.Clamp(point - ratio * duration, 0, Session.Document.Duration - duration), duration);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (Session == null)
        {
            return;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            ViewChanged?.Invoke(Math.Clamp(ViewStart - Math.Sign(e.Delta) * ViewDuration * .15, 0, Session.Document.Duration - ViewDuration), ViewDuration);
        }
        else
        {
            Zoom(e.Delta > 0 ? .75 : 1 / .75, IsOverview ? null : Time(e.GetPosition(this).X));
        }

        e.Handled = true;
    }

    private CuePoint? HitCue(Point point) => Session?.Cues.Where(c => Math.Abs(X(Session.Seconds(c)) - point.X) < 9 || point.Y < 45 && point.Y > 27 && point.X >= X(Session.Seconds(c)) && point.X <= X(Session.Seconds(c)) + 28).OrderBy(c => Math.Abs(X(Session.Seconds(c)) - point.X)).FirstOrDefault();
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        if (Session == null)
        {
            return;
        }

        Focus();
        var point = e.GetPosition(this);
        mouseDown = point;
        if (IsOverview)
        {
            draggingOverview = true;
            CaptureMouse();
            PanOverview(point.X);
            e.Handled = true;
            return;
        }
        var hit = HitCue(point);
        if (e.ChangedButton == MouseButton.Right)
        {
            if (hit != null)
            {
                Session.SelectedId = hit.Id;
                Session.DeleteSelected();
                Edited?.Invoke();
            }
            e.Handled = true;
            return;
        }
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (hit != null)
        {
            Session.SelectedId = hit.Id;
            draggingCue = true;
            dragCheckpoint = false;
            dragCueId = hit.Id;
            CaptureMouse();
        }
        else if (point.Y > PlotTop)
        {
            Session.AddCue(Session.Snap(Time(point.X), GridStep));
        }
        else
        {
            Session.SelectedId = null;
        }

        Edited?.Invoke();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (Session == null)
        {
            return;
        }

        var point = e.GetPosition(this);
        HoverTime = Time(point.X);
        if (draggingOverview)
        {
            PanOverview(point.X);
        }
        else if (draggingCue && (point - mouseDown).Length >= 2)
        {
            if (!dragCheckpoint)
            {
                Session.Checkpoint();
                dragCheckpoint = true;
            }
            Session.SelectedId = dragCueId;
            Session.MoveSelected(Session.Snap(Time(point.X), GridStep), false);
            Edited?.Invoke();
        }
        InvalidateVisual();
    }

    private void PanOverview(double x)
    {
        if (Session != null)
        {
            ViewChanged?.Invoke(Math.Clamp(Time(x) - ViewDuration / 2, 0, Session.Document.Duration - ViewDuration), ViewDuration);
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        draggingCue = false;
        draggingOverview = false;
        ReleaseMouseCapture();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        draggingCue = false;
        draggingOverview = false;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        HoverTime = null;
        InvalidateVisual();
    }
}
