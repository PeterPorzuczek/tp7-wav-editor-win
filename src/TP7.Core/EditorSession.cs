namespace TP7.Core;

public sealed class EditorSession(WaveDocument document)
{
    private readonly Stack<EditSnapshot> undo = new(), redo = new();
    private EditSnapshot saved = new(document.InitialTempo, [.. document.InitialCues], false);
    public WaveDocument Document { get; } = document;
    public double Tempo { get; private set; } = document.InitialTempo;
    public List<CuePoint> Cues { get; } = [.. document.InitialCues];
    public bool Normalized { get; private set; }
    public uint? SelectedId { get; set; }
    public CuePoint? SelectedCue => Cues.FirstOrDefault(c => c.Id == SelectedId);
    public double GridOffset { get; set; }
    public bool ShowGrid { get; set; }
    public bool SnapToGrid { get; set; }
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public bool IsDirty => !Equal(Snapshot(), saved);

    public EditSnapshot Snapshot() => new(Tempo, [.. Cues], Normalized);
    private static bool Equal(EditSnapshot a, EditSnapshot b) => a.Tempo == b.Tempo && a.Normalized == b.Normalized && a.Cues.SequenceEqual(b.Cues);

    public void MarkSaved() => saved = Snapshot();

    public void Checkpoint()
    {
        undo.Push(Snapshot());
        redo.Clear();
    }

    private void Restore(EditSnapshot state)
    {
        Tempo = state.Tempo;
        Normalized = state.Normalized;
        Cues.Clear();
        Cues.AddRange(state.Cues);
        if (SelectedCue == null)
        {
            SelectedId = null;
        }

        if (Tempo == 0)
        {
            ShowGrid = false;
            SnapToGrid = false;
        }
    }

    public void Undo()
    {
        if (undo.Count == 0)
        {
            return;
        }

        redo.Push(Snapshot());
        Restore(undo.Pop());
    }

    public void Redo()
    {
        if (redo.Count == 0)
        {
            return;
        }

        undo.Push(Snapshot());
        Restore(redo.Pop());
    }

    public void SetTempo(double value)
    {
        if (!double.IsFinite(value) || value is < 0 or > 999)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Tempo must be between 0 and 999 BPM.");
        }

        if (Tempo == value)
        {
            return;
        }

        Checkpoint();
        Tempo = value;
        if (Tempo == 0)
        {
            ShowGrid = false;
            SnapToGrid = false;
        }
    }

    public void Normalize()
    {
        if (Normalized || Document.Peak == 0)
        {
            return;
        }

        Checkpoint();
        Normalized = true;
    }

    public void AddCue(double seconds)
    {
        long frame = ToFrame(seconds);
        var existing = Cues.FirstOrDefault(c => c.Frame == frame);
        if (existing != null)
        {
            SelectedId = existing.Id;
            return;
        }
        if (Cues.Count >= 100000)
        {
            throw new InvalidOperationException("The maximum number of cue points has been reached.");
        }

        var ids = Cues.Select(c => c.Id).ToHashSet();
        uint id = 1;
        while (ids.Contains(id))
        {
            id++;
        }

        Checkpoint();
        Cues.Add(new(id, frame));
        SelectedId = id;
        Sort();
    }

    public void MoveSelected(double seconds, bool checkpoint = true)
    {
        if (SelectedCue is not { } cue)
        {
            return;
        }

        long frame = ToFrame(seconds);
        if (frame == cue.Frame)
        {
            return;
        }

        if (checkpoint)
        {
            Checkpoint();
        }

        Cues[Cues.IndexOf(cue)] = cue with
        {
            Frame = frame
        };
        Sort();
    }

    public void DeleteSelected()
    {
        if (SelectedCue is not { } cue)
        {
            return;
        }

        Checkpoint();
        int index = Cues.IndexOf(cue);
        Cues.Remove(cue);
        SelectedId = Cues.Count > 0 ? Cues[Math.Min(index, Cues.Count - 1)].Id : null;
    }

    public void DeleteAll()
    {
        if (Cues.Count == 0)
        {
            return;
        }

        Checkpoint();
        Cues.Clear();
        SelectedId = null;
    }

    public long ToFrame(double seconds) => (long)Math.Clamp(Math.Round(seconds * Document.Format.SampleRate), 0, Document.FrameCount);

    public double Seconds(CuePoint cue) => (double)cue.Frame / Document.Format.SampleRate;

    public void Sort() => Cues.Sort((a, b) => a.Frame != b.Frame ? a.Frame.CompareTo(b.Frame) : a.Id.CompareTo(b.Id));

    public void AlignGrid()
    {
        if (SelectedCue is { } cue && Tempo > 0)
        {
            GridOffset = Seconds(cue);
        }
    }

    public bool CalculateTempoFromCues()
    {
        if (SelectedCue is not { } cue)
        {
            return false;
        }

        var next = Cues.FirstOrDefault(c => c.Frame > cue.Frame);
        if (next == null)
        {
            return false;
        }

        SetTempo(Math.Clamp(Math.Round(240 / (Seconds(next) - Seconds(cue))), 1, 999));
        AlignGrid();
        return true;
    }

    public double GridStep(double viewSeconds, double width)
    {
        if (Tempo <= 0)
        {
            return 0;
        }

        double beat = 60 / Tempo;
        foreach (double beats in new[] { .25, .5, 1, 2, 4, 8 })
        {
            if (beats * beat * width / Math.Max(.00001, viewSeconds) >= 24)
            {
                return beats * beat;
            }
        }

        return 8 * beat * Math.Pow(2, Math.Max(0, Math.Ceiling(Math.Log2(viewSeconds * 24 / Math.Max(1, width) / (8 * beat)))));
    }

    public double Snap(double seconds, double step) => SnapToGrid && ShowGrid && Tempo > 0 && step > 0
        ? Math.Clamp(Math.Round((seconds - GridOffset) / step) * step + GridOffset, 0, (double)(Document.FrameCount - 1) / Document.Format.SampleRate) : seconds;
}
