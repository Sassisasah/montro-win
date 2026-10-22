using System.Collections.ObjectModel;

namespace Montro.Core;

/// <summary>
/// Aufbau der Referent*innenansicht: „Standard“, Vorlagen je Präsentation und der Entwurf im
/// Baukasten (Mac: AppState, Abschnitte „layout“ und „Presets der Referent*innenansicht“).
/// Ohne laufende Präsentation wirkt der Entwurf sofort, während einer Präsentation erst nach
/// „Jetzt übernehmen“ bzw. nach dem Ende. Gespeichert wird erst mit „Speichern“.
/// </summary>
public sealed partial class AppState
{
    private PresenterLayout standardLayout = PresenterLayout.WindowsDefault();
    private PresenterLayout draftLayout = PresenterLayout.WindowsDefault();
    private Guid? builderTarget;   // null = Standard
    private bool applyDraftAfterShow;

    // MARK: - Was die Referent*innenansicht zeigt

    /// <summary>
    /// Präsentation der Referent*innenansicht: die laufende, sonst die in der Playlist ausgewählte –
    /// so lässt sich die Ansicht einer Präsentation gestalten, ohne sie live zu schalten.
    /// </summary>
    public PresentationItem? PresenterItem => LiveItem ?? Selected;

    /// <summary>Folie der Referent*innenansicht: live die aktuelle, sonst die erste sichtbare.</summary>
    public int PresenterSlide => LiveItem != null ? CurrentSlide : Selected?.FirstVisible ?? 0;

    public SlideMeta? PresenterSlideMeta => PresenterItem?.Slide(PresenterSlide);

    public System.Windows.Media.ImageSource? PresenterSlideSource => LiveItem != null
        ? CurrentSlideSource
        : Images.Convert(Selected?.Thumbnail(PresenterSlide), typeof(System.Windows.Media.ImageSource), null,
            System.Globalization.CultureInfo.InvariantCulture) as System.Windows.Media.ImageSource;

    public string? PresenterNextImage =>
        PresenterItem is { } item && item.NextVisible(PresenterSlide) is int n ? item.Thumbnail(n) : null;

    public bool PresenterAtEnd => PresenterItem is { } item && item.NextVisible(PresenterSlide) == null;

    public string PresenterNotes => PresenterSlideMeta?.Notes ?? "";

    public string PresenterPositionText
    {
        get
        {
            if (PresenterItem is not { } item) return "–";
            int pos = item.VisiblePosition(PresenterSlide) ?? PresenterSlide;
            return $"Folie {pos} von {item.VisibleCount}";
        }
    }

    /// <summary>
    /// Ohne Show folgt der Baukasten der Auswahl in der Playlist: Er bearbeitet das Preset der
    /// gewählten Präsentation (sonst „Standard“), und die Karte zeigt es. Ungespeicherte Änderungen
    /// am Entwurf bleiben dabei stehen.
    /// </summary>
    private void FollowSelection()
    {
        if (IsShowRunning || IsStarting || Selected is not { } item || HasUnsavedChanges) return;
        var target = TargetOf(item);
        if (target == builderTarget && draftLayout.SameAs(LayoutFor(target))) return;
        SelectBuilderTarget(target);
    }

    /// <summary>Gespeicherte Vorlagen (Mac: layoutPresets).</summary>
    public ObservableCollection<LayoutPreset> LayoutPresets { get; } = new();

    /// <summary>Ansicht „Standard“ für Präsentationen ohne Vorlage.</summary>
    public PresenterLayout StandardLayout => standardLayout;

    /// <summary>Entwurf im Baukasten. Nach Änderungen <see cref="DraftChanged"/> aufrufen.</summary>
    public PresenterLayout DraftLayout
    {
        get => draftLayout;
        set
        {
            draftLayout = value;
            DraftChanged();
        }
    }

    /// <summary>Was der Baukasten bearbeitet: null = Standard, sonst die Vorlage.</summary>
    public Guid? BuilderTarget => builderTarget;

    public bool ApplyDraftAfterShow => applyDraftAfterShow;

    /// <summary>Entwurf weicht von der laufenden Ansicht ab (Mac: hasDraftChanges).</summary>
    public bool HasDraftChanges => !draftLayout.SameAs(Layout);

    /// <summary>Entwurf weicht vom gespeicherten Stand des Ziels ab (Mac: hasUnsavedChanges).</summary>
    public bool HasUnsavedChanges => !draftLayout.SameAs(LayoutFor(builderTarget));

    public string TargetName(Guid? target) => Preset(target)?.Name ?? "Standard";

    public LayoutPreset? Preset(Guid? id) => id == null ? null : LayoutPresets.FirstOrDefault(p => p.Id == id);

    public PresenterLayout LayoutFor(Guid? target) => Preset(target)?.Layout ?? standardLayout;

    private Guid? TargetOf(PresentationItem item) => Preset(item.PresetId) != null ? item.PresetId : null;

    /// <summary>Nach jeder Änderung am Entwurf: ohne Show sofort sichtbar.</summary>
    public void DraftChanged()
    {
        if (!IsShowRunning && !IsStarting && !draftLayout.SameAs(Layout)) Layout = draftLayout.Clone();
        RaiseLayoutState();
    }

    private void RaiseLayoutState()
    {
        Raise(nameof(DraftLayout), nameof(HasDraftChanges), nameof(HasUnsavedChanges), nameof(BuilderTarget),
            nameof(ApplyDraftAfterShow));
        RefreshPresetNames();
    }

    /// <summary>Preset-Namen für die Abzeichen in der Playlist nachziehen (Mac: Preset-Kapsel in der Zeile).</summary>
    public void RefreshPresetNames()
    {
        foreach (var item in Items) item.PresetName = Preset(item.PresetId)?.Name;
    }

    /// <summary>Beim Live-Schalten: Ansicht der Präsentation (Vorlage oder Standard) übernehmen.</summary>
    private void UseLayoutOf(PresentationItem item)
    {
        builderTarget = TargetOf(item);
        applyDraftAfterShow = false;
        Layout = LayoutFor(builderTarget).Clone();
        draftLayout = Layout.Clone();
        RaiseLayoutState();
        DebugLog.Write($"Referent*innenansicht: {TargetName(builderTarget)}");
    }

    private void ApplyDraftAfterStop()
    {
        if (!applyDraftAfterShow) return;
        applyDraftAfterShow = false;
        Layout = draftLayout.Clone();
        StoreIntoTarget(draftLayout);
        DebugLog.Write("Entwurf der Referent*innenansicht nach der Präsentation übernommen");
        RaiseLayoutState();
    }

    public void SaveDraftToTarget()
    {
        StoreIntoTarget(draftLayout);
        DebugLog.Write($"Referent*innenansicht gespeichert in: {TargetName(builderTarget)}");
        RaiseLayoutState();
    }

    public void RevertDraftToTarget()
    {
        applyDraftAfterShow = false;
        DraftLayout = LayoutFor(builderTarget).Clone();
    }

    public void ApplyDraftNow()
    {
        Layout = draftLayout.Clone();
        StoreIntoTarget(draftLayout);
        applyDraftAfterShow = false;
        DebugLog.Write("Entwurf der Referent*innenansicht sofort übernommen");
        RaiseLayoutState();
    }

    public void ApplyDraftAfterPresentation()
    {
        applyDraftAfterShow = true;
        DebugLog.Write("Entwurf der Referent*innenansicht wird nach der Präsentation übernommen");
        RaiseLayoutState();
    }

    public void DiscardDraft()
    {
        applyDraftAfterShow = false;
        DraftLayout = Layout.Clone();
    }

    private void StoreIntoTarget(PresenterLayout l)
    {
        if (Preset(builderTarget) is { } p) p.Layout = l.Clone();
        else standardLayout = l.Clone();
        QueueSave();
    }

    /// <summary>Baukasten auf ein anderes Ziel umstellen (lädt dessen Aufbau in den Entwurf).</summary>
    public void SelectBuilderTarget(Guid? target)
    {
        builderTarget = Preset(target) != null ? target : null;
        applyDraftAfterShow = false;
        DraftLayout = LayoutFor(builderTarget).Clone();
    }

    /// <summary>Entwurf als neue Vorlage speichern, optional einer Präsentation zuweisen.</summary>
    public Guid SaveDraftAsPreset(string name, PresentationItem? assignTo)
    {
        string clean = name.Trim();
        var p = new LayoutPreset { Name = clean.Length == 0 ? $"Preset {LayoutPresets.Count + 1}" : clean, Layout = draftLayout.Clone() };
        LayoutPresets.Add(p);
        if (assignTo != null) assignTo.PresetId = p.Id;
        builderTarget = p.Id;
        DebugLog.Write($"Vorlage gespeichert: {p.Name}");
        QueueSave();
        RaiseLayoutState();
        return p.Id;
    }

    public void RenamePreset(Guid id, string name)
    {
        string clean = name.Trim();
        if (clean.Length == 0 || Preset(id) is not { } p) return;
        p.Name = clean;
        // Liste neu melden, damit Auswahllisten den Namen zeigen
        int i = LayoutPresets.IndexOf(p);
        LayoutPresets[i] = p;
        QueueSave();
        RaiseLayoutState();
    }

    public void DeletePreset(Guid id)
    {
        if (Preset(id) is not { } p) return;
        LayoutPresets.Remove(p);
        foreach (var item in Items.Where(i => i.PresetId == id)) item.PresetId = null;
        if (builderTarget == id) SelectBuilderTarget(null);
        QueueSave();
        RaiseLayoutState();
    }

    public void AssignPreset(Guid? id, PresentationItem item)
    {
        item.PresetId = Preset(id) != null ? id : null;
        // Zuweisung an die laufende Präsentation: Ziel folgt, Ausgabe bleibt bis zur Übernahme
        // Zuweisung an die laufende bzw. (ohne Show) ausgewählte Präsentation: Ziel folgt
        if (item == (LiveItem ?? Selected)) SelectBuilderTarget(item.PresetId);
        QueueSave();
        RaiseLayoutState();
    }

    /// <summary>
    /// Notizen-Schriftgröße sofort ändern – live, im Entwurf und im gespeicherten Ziel. Bewusst ohne
    /// Entwurf: Die Referent*in muss das während der Show anpassen können (wie am Mac).
    /// </summary>
    public void SetNotesFont(double value)
    {
        double v = Math.Clamp(Math.Round(value * 20) / 20, NotesFontRange.Min, NotesFontRange.Max);
        if (Math.Abs(v - NotesFontScale) < 0.001) return;
        var live = Layout.Clone();
        live.Update(WidgetKind.Notes, c => c.FontScale = v);
        Layout = live;
        draftLayout.Update(WidgetKind.Notes, c => c.FontScale = v);
        var target = LiveItem is { } item ? TargetOf(item) : builderTarget;
        if (Preset(target) is { } p) p.Layout.Update(WidgetKind.Notes, c => c.FontScale = v);
        else standardLayout.Update(WidgetKind.Notes, c => c.FontScale = v);
        QueueSave();
        RaiseLayoutState();
    }

    private void LoadLayouts(SavedState s)
    {
        if (s.Layout is { Widgets.Count: > 0 } saved)
        {
            saved.EnsureAllKinds();
            standardLayout = saved;
        }
        // alter Stand: Notizen-Schriftgröße war eine eigene Einstellung
        if (Math.Abs(s.NotesFontScale - 1) > 0.001 && standardLayout.Config(WidgetKind.Notes) is { FontScale: 1 } notes)
            notes.FontScale = Math.Clamp(s.NotesFontScale, NotesFontRange.Min, NotesFontRange.Max);
        foreach (var p in s.LayoutPresets ?? new())
        {
            p.Layout.EnsureAllKinds();
            LayoutPresets.Add(p);
        }
        layout = standardLayout.Clone();
        draftLayout = standardLayout.Clone();
        FollowSelection();
        RefreshPresetNames();
    }
}
