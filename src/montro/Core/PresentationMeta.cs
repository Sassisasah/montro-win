namespace Montro.Core;

/// <summary>Video auf einer Folie (Mac: VideoMeta).</summary>
public sealed record VideoMeta(
    string Name,
    double Duration,   // Sekunden, 0 = unbekannt
    int? StartClick);  // 0 = startet automatisch mit der Folie, n = mit dem n-ten Klick, null = über Trigger / unbekannt

/// <summary>Eine Folie, wie sie in der .pptx steht (Mac: SlideMeta).</summary>
public sealed record SlideMeta(
    int Index,         // 1-basiert inkl. ausgeblendeter Folien = PowerPoint-Folienindex
    bool Hidden,
    string Notes,
    int ClickCount,    // Klick-Animationen der Hauptsequenz
    IReadOnlyList<VideoMeta> Videos);

/// <summary>Eine Schrift, die die Präsentation braucht (Mac: FontUse).</summary>
public sealed record FontUse(
    string Name,
    IReadOnlyList<int> Slides,  // Folien, auf denen sie direkt gesetzt ist; leer = nur Master/Layout/Design/Diagramm
    bool Embedded);             // in der .pptx eingebettet

/// <summary>Ergebnis des PPTX-Parsers (Mac: PresentationMeta).</summary>
/// <param name="Fonts">Gebrauchte Schriften, null = vor dem Schrift-Check analysiert („Neu analysieren“).</param>
public sealed record PresentationMeta(IReadOnlyList<SlideMeta> Slides, IReadOnlyList<FontUse>? Fonts = null)
{
    public SlideMap ToSlideMap() => new(Slides.Select(s => s.Hidden).ToArray());

    public int VideoCount => Slides.Sum(s => s.Videos.Count);

    /// <summary>„15 Folien · 4 ausgeblendet · 1 Video“ wie am Mac (Playlist-Untertitel).</summary>
    public string Subtitle
    {
        get
        {
            int visible = Slides.Count(s => !s.Hidden);
            var parts = new List<string> { $"{visible} Folien" };
            int hidden = Slides.Count - visible;
            if (hidden > 0) parts.Add($"{hidden} ausgeblendet");
            if (VideoCount > 0) parts.Add(VideoCount == 1 ? "1 Video" : $"{VideoCount} Videos");
            return string.Join(" · ", parts);
        }
    }
}
