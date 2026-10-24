namespace Montro.Core;

/// <summary>
/// Schneller starten und umschalten: PowerPoint beim Start von montro schon hochfahren und die
/// nächste Präsentation vorab öffnen, damit das Live-Schalten sie nur noch übernehmen muss.
/// </summary>
public sealed partial class AppState
{
    private bool preloadNext;
    private int preloadGeneration;

    /// <summary>
    /// Auch während einer laufenden Show die nächste Präsentation vorab öffnen. Ohne laufende Show
    /// öffnet montro die ausgewählte Präsentation immer vorab (stört dann niemanden).
    /// Aus, solange nicht am Gerät gemessen ist, ob das Öffnen Klicks in der laufenden Show verzögert.
    /// </summary>
    public bool PreloadNext
    {
        get => preloadNext;
        set
        {
            if (!Set(ref preloadNext, value)) return;
            QueueSave();
            if (value) SchedulePreload();
            else if (IsShowRunning) _ = DropPreload();
        }
    }

    /// <summary>PowerPoint im Hintergrund starten, damit das erste Live-Schalten nicht darauf warten muss.</summary>
    private async Task WarmUpPowerPoint()
    {
        if (headless) return;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            string version = await ppt.ConnectAsync();
            DebugLog.Write($"PowerPoint vorab gestartet in {clock.ElapsedMilliseconds} ms ({version})");
            SchedulePreload();
        }
        catch (Exception e)
        {
            DebugLog.Write($"PowerPoint vorab starten nicht möglich – {e.Message}");
        }
    }

    /// <summary>Vorschaubilder warten, solange eine Präsentation live geschaltet wird.</summary>
    private async Task WaitWhileStarting()
    {
        while (IsStarting) await Task.Delay(100);
    }

    /// <summary>
    /// Welche Präsentation als nächste live geht: die ausgewählte, wenn sie nicht schon läuft,
    /// sonst die folgende in der Playlist.
    /// </summary>
    private PresentationItem? NextCandidate()
    {
        if (Selected is { } s && s != LiveItem) return s;
        if (LiveItem is not { } live) return Selected;
        int i = Items.IndexOf(live);
        return i >= 0 && i + 1 < Items.Count ? Items[i + 1] : null;
    }

    /// <summary>Kurz verzögert vorab öffnen (beim schnellen Durchklicken der Playlist nicht jede Datei).</summary>
    private void SchedulePreload()
    {
        if (headless) return;
        int generation = ++preloadGeneration;
        _ = ui.InvokeAsync(async () =>
        {
            await Task.Delay(800);
            if (generation != preloadGeneration) return;
            await Preload();
        });
    }

    private async Task Preload()
    {
        if (IsStarting || ReloadingInPlace) return;
        if (IsShowRunning && !PreloadNext) return;
        if (NextCandidate() is not { } item || item.State == PrepState.Failed || !File.Exists(item.Path)) return;
        // Datei wird gerade vom Master geholt oder ersetzt – dann erst danach
        if (FileSync.StagedVersion(item) != null) return;
        try { await ppt.PreloadAsync(item.Path); }
        catch (Exception e) { DebugLog.Write($"Vorab öffnen „{item.Title}“ nicht möglich – {e.Message}"); }
    }

    private async Task DropPreload()
    {
        try { await ppt.DropPreloadAsync(); }
        catch { }
    }
}
