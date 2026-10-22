namespace Montro.PowerPoint;

/// <summary>PpSlideShowState (PowerPoint-Objektmodell).</summary>
public enum SlideShowRunState
{
    None = 0,
    Running = 1,
    Paused = 2,
    BlackScreen = 3,
    WhiteScreen = 4,
    Done = 5,
}

/// <summary>Momentaufnahme der laufenden Show – einfache Werte, kein COM.</summary>
public sealed record ShowState(
    bool HasShow,
    SlideShowRunState RunState,
    int SlideIndex,     // PowerPoint-Folienindex (inkl. ausgeblendeter), 0 = unbekannt / Endbildschirm
    int ShowPosition,   // Position in der Show (View.CurrentShowPosition)
    int Click,          // aktueller Klick in der Folie (0 = vor der ersten Animation)
    int ClickCount,
    VideoState? Video = null)
{
    public static readonly ShowState None = new(false, SlideShowRunState.None, 0, 0, 0, 0);

    public bool IsBlack => RunState == SlideShowRunState.BlackScreen;
    public bool AtEnd => RunState == SlideShowRunState.Done;

    public string RunStateText => RunState switch
    {
        SlideShowRunState.None => "keine Show",
        SlideShowRunState.Running => "läuft",
        SlideShowRunState.Paused => "pausiert",
        SlideShowRunState.BlackScreen => "schwarz",
        SlideShowRunState.WhiteScreen => "weiß",
        SlideShowRunState.Done => "Ende der Bildschirmpräsentation",
        _ => RunState.ToString(),
    };
}

/// <summary>Video auf der aktuellen Folie (Mac: VideoTracker.playing), Zeiten in Sekunden.</summary>
public sealed record VideoState(string Name, double Position, double Duration, bool Playing)
{
    public double Remaining => Math.Max(Duration - Position, 0);
    public double Progress => Duration > 0 ? Math.Clamp(Position / Duration, 0, 1) : 0;
}
