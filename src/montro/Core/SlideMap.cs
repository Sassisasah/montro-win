namespace Montro.Core;

/// <summary>
/// Die <b>einzige</b> Stelle für die Umrechnung zwischen PowerPoint-Folienindex (inkl.
/// ausgeblendeter Folien – so zählen PowerPoint und der Verbund) und sichtbarer Position
/// („Folie x von n“ – so zählen Anzeige und Remote-API für Companion). Alle Werte 1-basiert.
/// </summary>
public sealed class SlideMap
{
    private readonly bool[] hidden;
    private readonly int[] visibleIndices;

    public SlideMap(IReadOnlyList<bool> hiddenFlags)
    {
        hidden = hiddenFlags.ToArray();
        visibleIndices = Enumerable.Range(1, hidden.Length).Where(i => !hidden[i - 1]).ToArray();
    }

    /// <summary>Anzahl aller Folien (PowerPoint)</summary>
    public int Count => hidden.Length;

    /// <summary>Anzahl sichtbarer Folien („n“ in „Folie x von n“)</summary>
    public int VisibleCount => visibleIndices.Length;

    public bool IsHidden(int index) => index >= 1 && index <= hidden.Length && hidden[index - 1];

    /// <summary>Sichtbare Position eines PowerPoint-Index, 0 = ausgeblendet/ungültig.</summary>
    public int VisiblePosition(int index)
    {
        var pos = Array.IndexOf(visibleIndices, index);
        return pos < 0 ? 0 : pos + 1;
    }

    /// <summary>PowerPoint-Index der n-ten sichtbaren Folie, null = gibt es nicht.</summary>
    public int? IndexForVisible(int position) =>
        position >= 1 && position <= visibleIndices.Length ? visibleIndices[position - 1] : null;

    /// <summary>Nächste sichtbare Folie nach einem PowerPoint-Index.</summary>
    public int? NextVisible(int index)
    {
        foreach (var i in visibleIndices)
            if (i > index) return i;
        return null;
    }

    public int FirstVisible => visibleIndices.Length > 0 ? visibleIndices[0] : 1;
    public int LastVisible => visibleIndices.Length > 0 ? visibleIndices[^1] : Math.Max(Count, 1);

    public override string ToString()
    {
        var h = Count - VisibleCount;
        return h > 0 ? $"{VisibleCount} Folien, {h} ausgeblendet" : $"{VisibleCount} Folien";
    }
}
