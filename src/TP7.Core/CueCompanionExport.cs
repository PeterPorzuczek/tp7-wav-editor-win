namespace TP7.Core;

/// <summary>Stages both exports from one snapshot and restores the companion if committing the WAV fails.</summary>
public static class CueCompanionExport
{
    public static string CompanionPath(string wavePath) => Path.ChangeExtension(wavePath, ".cue");

    public static void Export(IWaveSource document, string destination, EditSnapshot edit, IProgress<double>? progress = null, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        edit = edit with { Cues = (CuePoint[])edit.Cues.Clone() };
        string target = Path.GetFullPath(destination), companion = CompanionPath(target);
        if (document is MultitrackDocument multitrack)
        {
            multitrack.ValidateDestination(target);
        }
        else if (document is not WaveDocument)
        {
            throw new ArgumentException("Expected a WAV or multitrack document.", nameof(document));
        }
        // Validate and generate the small companion before writing a potentially large WAV.
        byte[] carrier = CueCarrier.CreateWave(Path.GetFileName(target), document.Format.SampleRate, document.FrameCount, edit);
        string prefix = Path.Combine(Path.GetDirectoryName(target)!, $".tp7-pair-{Guid.NewGuid():N}");
        string stagedWave = prefix + ".wav.tmp", stagedCue = prefix + ".cue.tmp", backupCue = prefix + ".cue.bak";
        bool cueCommitted = false, previousCue = false, preserveBackup = false;
        try
        {
            if (document is MultitrackDocument multi)
            {
                multi.Export(stagedWave, edit, progress, token);
            }
            else
            {
                ((WaveDocument)document).Export(stagedWave, edit, progress, token);
            }
            using (var output = new FileStream(stagedCue, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                output.Write(carrier);
                output.Flush(true);
            }
            token.ThrowIfCancellationRequested();
            // No cancellation between these two commits. There is no filesystem-wide atomic rename
            // for a pair; a failed second commit rolls back the first. Backups survive rollback errors.
            previousCue = File.Exists(companion);
            if (previousCue)
            {
                File.Replace(stagedCue, companion, backupCue);
            }
            else
            {
                File.Move(stagedCue, companion);
            }
            cueCommitted = true;
            if (File.Exists(target))
            {
                File.Replace(stagedWave, target, null);
            }
            else
            {
                File.Move(stagedWave, target);
            }
        }
        catch (Exception failure)
        {
            if (cueCommitted)
            {
                try
                {
                    if (previousCue)
                    {
                        File.Move(backupCue, companion, true);
                    }
                    else
                    {
                        File.Delete(companion);
                    }
                }
                catch (Exception rollback)
                {
                    preserveBackup = true;
                    throw new IOException($"Export failed and the companion could not be restored. Do not use the pair. Previous companion backup (if present): {backupCue}", new AggregateException(failure, rollback));
                }
            }
            throw;
        }
        finally
        {
            DeleteTemporary(stagedWave);
            DeleteTemporary(stagedCue);
            if (!preserveBackup)
            {
                DeleteTemporary(backupCue);
            }
        }
    }

    private static void DeleteTemporary(string path)
    {
        try
        { File.Delete(path); }
        catch (IOException) { /* Keep temporary output if an external process locks it. */ }
        catch (UnauthorizedAccessException) { /* Cleanup must not mask the export/rollback result. */ }
    }
}
