using Montro.Cluster;

namespace Montro.Core;

/// <summary>Verbund mehrerer Rechner (Mac: AppState, Abschnitt „Verbund“).</summary>
public sealed partial class AppState
{
    private ClusterManager? cluster;
    private FileSync? fileSync;
    private bool reloadingInPlace;

    public ClusterManager Cluster => cluster ??= new ClusterManager(this);
    public FileSync FileSync => fileSync ??= new FileSync(this);

    /// <summary>„Aktualisieren“ läuft: neue Version wird an gleicher Stelle geladen.</summary>
    public bool ReloadingInPlace
    {
        get => reloadingInPlace;
        private set => Set(ref reloadingInPlace, value);
    }

    private void StartCluster(SavedState s)
    {
        FileSync.Load(s.FileSync);
        Cluster.Load(s.Cluster);
        if (remote != null) remote.StatusChanged += Cluster.Announce;
    }

    /// <summary>Slave mit Bediensperre: lokale Eingaben ignorieren, nur Befehle vom Master ausführen.</summary>
    private bool LocalControlAllowed()
    {
        if (Cluster.PermitsLocalControl) return true;
        StatusMessage = "Slave – Bedienung erfolgt über den Master";
        return false;
    }

    /// <summary>Master ohne eigene Show, aber Slaves sind live: Befehl nur weiterreichen (Mac: forwardOnlyIfMasterIdle).</summary>
    private bool ForwardOnlyIfMasterIdle(string kind, bool? on = null)
    {
        if (Cluster.Role != Montro.Cluster.ClusterRole.Master || IsShowRunning || IsStarting || !Cluster.AnySlaveLive) return false;
        Cluster.Forward(kind, on: on);
        return true;
    }

    /// <summary>
    /// „Aktualisieren“: neue Version der laufenden Präsentation an der aktuellen Folie laden. Im
    /// Verbund laufen die Slaves dabei weiter und bekommen die Klicks; danach übernimmt dieser
    /// Rechner ihre Position (Mac: reloadLiveWithNewVersion).
    /// </summary>
    public async void ReloadLiveWithNewVersion()
    {
        if (LiveItem is not { } item || !LocalControlAllowed() || ReloadingInPlace) return;
        int slide = CurrentSlide;
        string file = ClusterManager.FileKey(item.Path);
        DebugLog.Write($"Aktualisieren: neue Version von „{file}“ wird geladen (Folie {slide})");
        ReloadingInPlace = true;
        Cluster.HoldSync = true;
        try
        {
            await StartShow(item, slide);
            // Slaves haben inzwischen evtl. weitergeklickt → Folie und Klickstand übernehmen. Nur hier in
            // PowerPoint, nicht an den Verbund: ein weitergegebener Sprung würde die Slaves an den
            // Folienanfang setzen und ihre schon gelaufenen Animationen zurücknehmen (Mac: genauso).
            if (await Cluster.SlavePosition(file) is (int s, int c) && IsShowRunning && (s != CurrentSlide || c != ClickInSlide))
            {
                DebugLog.Write($"Aktualisieren: übernehme Position des Slaves – Folie {s}, Klick {c}");
                if (s != CurrentSlide && LiveItem?.Slide(s) != null)
                {
                    await ppt.GotoAsync(s);
                    CurrentSlide = s;
                    ClickInSlide = 0;
                }
                int clicks = Math.Min(c, LiveItem?.Slide(CurrentSlide)?.ClickCount ?? 0);
                while (ClickInSlide < clicks)
                {
                    await ppt.NextAsync();
                    ClickInSlide++;
                }
            }
        }
        catch (Exception e)
        {
            // async void: ein Fehler beim Nachklicken darf die App nicht beenden
            DebugLog.Write($"Aktualisieren: Position übernehmen fehlgeschlagen – {e.Message}");
        }
        finally
        {
            ReloadingInPlace = false;
            Cluster.HoldSync = false;
            StatusMessage = $"Aktualisiert: {LiveItem?.Title} – neue Version läuft";
        }
    }

    /// <summary>Datei in PowerPoint schließen, wenn sie dort ohne ungesicherte Änderungen offen ist.</summary>
    public async Task<bool> CloseInPowerPointIfSaved(string path)
    {
        try { return await ppt.CloseIfSavedAsync(path); }
        catch (Exception e)
        {
            DebugLog.Write($"Verbund: PowerPoint-Prüfung für „{Path.GetFileName(path)}“ – {e.Message}");
            return false;
        }
    }

    /// <summary>Nach dem Ersetzen durch eine neue Version: Ausgangsstand merken, neu analysieren.</summary>
    public void AdoptNewVersion(PresentationItem item)
    {
        // Beim „Aktualisieren“ der laufenden Präsentation keine Vorschaubilder jetzt (PowerPoint öffnet
        // die Datei gleich für die Show) – RefreshIfChanged analysiert sie vor dem Start, Bilder nach dem Stopp.
        if (item == LiveItem) return;
        if (Stamp(item.Path) is { } s) baselines[item.Id] = s;
        candidates.Remove(item.Id);
        Regenerate(item);
    }
}
