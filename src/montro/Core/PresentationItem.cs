namespace Montro.Core;

/// <summary>Vorbereitungsstand eines Playlist-Eintrags (Mac: PrepStatus).</summary>
public enum PrepState { Pending, Preparing, Ready, Failed }

/// <summary>Ein Eintrag der Playlist (Mac: PresentationItem).</summary>
public sealed class PresentationItem : Observable
{
    private string title = "";
    private PresentationMeta? meta;
    private PrepState state = PrepState.Pending;
    private string stateText = "";
    private Dictionary<int, string> thumbnails = new();
    private int number;
    private bool isLive;

    public Guid Id { get; init; } = Guid.NewGuid();
    private string path = "";

    /// <summary>Pfad der Datei; ändert sich nur über „Fehlende Dateien suchen …“.</summary>
    public string Path { get => path; set => Set(ref path, value); }

    public string Title { get => title; set => Set(ref title, value); }

    public PresentationMeta? Meta
    {
        get => meta;
        set
        {
            if (Set(ref meta, value)) Raise(nameof(Subtitle), nameof(Slides), nameof(MissingFontsText));
        }
    }

    /// <summary>PowerPoint-Folienindex → Pfad des PNG (1280 px)</summary>
    public Dictionary<int, string> Thumbnails
    {
        get => thumbnails;
        set
        {
            thumbnails = value;
            Raise(nameof(Thumbnails), nameof(FirstThumbnail));
        }
    }

    public PrepState State
    {
        get => state;
        set
        {
            if (Set(ref state, value)) Raise(nameof(IsReady), nameof(IsFailed), nameof(IsBusy));
        }
    }

    /// <summary>„Analysiere Folien …“ / Fehlertext</summary>
    public string StateText { get => stateText; set => Set(ref stateText, value); }

    /// <summary>Position in der Playlist (1-basiert), nur für die Anzeige.</summary>
    public int Number { get => number; set => Set(ref number, value); }

    public bool IsLive { get => isLive; set => Set(ref isLive, value); }

    /// <summary>SHA-256 des Inhalts, unter dem Analyse und Vorschaubilder im <see cref="PreviewCache"/> liegen.</summary>
    public string? ContentHash { get; set; }

    /// <summary>Größe und Änderungszeit der Datei, als Analyse und Vorschaubilder entstanden.</summary>
    public long PreparedSize { get; set; }

    public DateTime PreparedTime { get; set; }

    /// <summary>Zugewiesene Vorlage der Referent*innenansicht, null = Standard (Mac: presetID).</summary>
    public Guid? PresetId { get; set; }

    private string? syncBadge;

    /// <summary>Verbund: „Neue Version“, „Geändert“, „An Master“ (Mac: syncBadges), null = keins.</summary>
    public string? SyncBadge { get => syncBadge; set => Set(ref syncBadge, value); }

    private string? presetName;

    /// <summary>Name des zugewiesenen Presets der Referent*innenansicht (Abzeichen in der Playlist), null = Standard.</summary>
    public string? PresetName { get => presetName; set => Set(ref presetName, value); }

    /// <summary>Hinweis für das Schrift-Symbol in der Playlist, null = keine Schrift fehlt (Mac: missingFonts).</summary>
    public string? MissingFontsText
    {
        get
        {
            var missing = FontCheck.Missing(Meta?.Fonts);
            return missing.Count == 0 ? null
                : $"Schrift fehlt auf diesem Rechner: {string.Join(", ", missing.Select(f => f.Name))}. PowerPoint ersetzt sie.";
        }
    }

    /// <summary>Nach FontCheck.Reload: Schrift-Symbol neu bewerten.</summary>
    public void RefreshFonts() => Raise(nameof(MissingFontsText));

    public bool IsReady => State == PrepState.Ready;
    public bool IsFailed => State == PrepState.Failed;
    public bool IsBusy => State is PrepState.Pending or PrepState.Preparing;

    public IReadOnlyList<SlideMeta> Slides => Meta?.Slides ?? [];

    public string Subtitle => Meta?.Subtitle ?? System.IO.Path.GetFileName(Path);

    public int FirstVisible => Slides.FirstOrDefault(s => !s.Hidden)?.Index ?? 1;
    public int LastVisible => Slides.LastOrDefault(s => !s.Hidden)?.Index ?? Math.Max(Slides.Count, 1);

    public string? FirstThumbnail => Thumbnail(FirstVisible);

    public string? Thumbnail(int slide) =>
        Thumbnails.TryGetValue(slide, out var p) && File.Exists(p) ? p : null;

    public SlideMeta? Slide(int index) => Slides.FirstOrDefault(s => s.Index == index);

    public int? NextVisible(int index) => Slides.FirstOrDefault(s => !s.Hidden && s.Index > index)?.Index;

    public int? VisiblePosition(int index)
    {
        int pos = 0;
        foreach (var s in Slides)
        {
            if (s.Hidden) continue;
            pos++;
            if (s.Index == index) return pos;
        }
        return null;
    }

    public int VisibleCount => Slides.Count(s => !s.Hidden);

    public static readonly string[] FileExtensions = [".pptx", ".pptm", ".ppsx", ".ppsm"];
}
