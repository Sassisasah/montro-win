using System.Collections.ObjectModel;
using System.Windows.Threading;
using Montro.Display;
using Montro.PowerPoint;
using Montro.Pptx;

namespace Montro.Core;

/// <summary>
/// Zentraler Zustand der App (Mac: AppState.swift): Playlist, Vorbereitung, Live-Steuerung,
/// Blackscreen, Ende-Sperre, Timer, Ausgaben. Läuft auf dem UI-Thread; PowerPoint wird nur über
/// <see cref="PowerPointHost"/> angesprochen.
/// </summary>
public sealed class AppState : Observable
{
    public static readonly (double Min, double Max) NotesFontRange = (0.4, 4.0);

    private readonly PowerPointHost ppt = new();
    private readonly Dispatcher ui = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer ticker;
    private bool preparing;
    private bool saveQueued;

    private PresentationItem? selected;
    private PresentationItem? liveItem;
    private int currentSlide;
    private int clickInSlide;
    private bool isShowRunning;
    private bool isStarting;
    private bool isBlackout;
    private bool isLocked;
    private bool isEndBlack;
    private string statusMessage = "Bereit";
    private string? audienceDevice;
    private EndBehavior endBehavior = EndBehavior.HoldLastSlide;
    private double notesFontScale = 1.0;
    private int? lockSlide;
    private DateTime clock = DateTime.Now;
    private bool blinkPhase = true;

    public ObservableCollection<PresentationItem> Items { get; } = new();
    public TalkTimer Timer { get; } = new();
    public RegieMessage Message { get; } = new();
    private readonly OutputManager outputs = new();
    private ShowTransition transition = ShowTransition.Black;
    private double transitionDuration = 1.0;

    public ShowTransition Transition
    {
        get => transition;
        set
        {
            if (Set(ref transition, value))
            {
                Raise(nameof(TransitionDetail), nameof(TransitionHasDuration));
                QueueSave();
            }
        }
    }

    public double TransitionDuration
    {
        get => transitionDuration;
        set
        {
            if (Set(ref transitionDuration, Math.Round(Math.Clamp(value, 0.3, 3), 1)))
            {
                Raise(nameof(TransitionDurationText));
                QueueSave();
            }
        }
    }

    public bool TransitionHasDuration => Transition != ShowTransition.Cut;
    public string TransitionDurationText => $"{TransitionDuration:0.0} s";

    public string TransitionDetail => Transition switch
    {
        ShowTransition.Cut => "Kein Übergang – das Umschalten (kurz Schwarz) ist zu sehen.",
        ShowTransition.Black => "Die alte Folie blendet nach Schwarz ab, die neue blendet auf, sobald sie steht.",
        _ => "Die letzte Folie bleibt als Standbild stehen und blendet direkt in die neue Präsentation über.",
    };

    /// <summary>Wird gesetzt, wenn eine Meldung als Dialog erscheinen soll (Mac: lastError).</summary>
    public event Action<string>? ErrorRaised;

    public AppState()
    {
        ppt.StateChanged += OnShowState;
        ppt.ShowEnded += OnShowEnded;
        ticker = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Normal, (_, _) => Tick(), ui);
        Load();
        ticker.Start();
    }

    // MARK: - Eigenschaften

    public PresentationItem? Selected
    {
        get => selected;
        set
        {
            if (Set(ref selected, value)) Raise(nameof(NavigatorItem), nameof(CanGoLive));
        }
    }

    public PresentationItem? LiveItem
    {
        get => liveItem;
        private set
        {
            if (liveItem != null) liveItem.IsLive = false;
            Set(ref liveItem, value);
            if (liveItem != null) liveItem.IsLive = true;
            RaiseDerived();
        }
    }

    public int CurrentSlide { get => currentSlide; private set { if (Set(ref currentSlide, value)) RaiseDerived(); } }
    public int ClickInSlide { get => clickInSlide; private set { if (Set(ref clickInSlide, value)) RaiseDerived(); } }
    public bool IsShowRunning { get => isShowRunning; private set { if (Set(ref isShowRunning, value)) RaiseDerived(); } }
    public bool IsStarting { get => isStarting; private set { if (Set(ref isStarting, value)) RaiseDerived(); } }
    public bool IsBlackout { get => isBlackout; private set { if (Set(ref isBlackout, value)) RaiseDerived(); } }
    public bool IsLocked { get => isLocked; private set { if (Set(ref isLocked, value)) RaiseDerived(); } }
    public bool IsEndBlack { get => isEndBlack; private set { if (Set(ref isEndBlack, value)) RaiseDerived(); } }
    public string StatusMessage { get => statusMessage; set => Set(ref statusMessage, value); }

    public EndBehavior EndBehavior { get => endBehavior; set { if (Set(ref endBehavior, value)) QueueSave(); } }

    public double NotesFontScale
    {
        get => notesFontScale;
        set
        {
            double v = Math.Round(Math.Clamp(value, NotesFontRange.Min, NotesFontRange.Max), 2);
            if (Set(ref notesFontScale, v))
            {
                Raise(nameof(NotesFontPercent));
                QueueSave();
            }
        }
    }

    public string NotesFontPercent => $"{Math.Round(NotesFontScale * 100)} %";

    public DateTime Clock { get => clock; private set => Set(ref clock, value); }
    public string ClockText => Clock.ToString("HH:mm:ss");

    /// <summary>Halbsekunden-Takt für alles, was blinkt (Mac: blinkPhase).</summary>
    public bool BlinkPhase { get => blinkPhase; private set => Set(ref blinkPhase, value); }

    // MARK: - Ausgaben

    /// <summary>Externe Bildschirme (alle außer dem Hauptbildschirm).</summary>
    public IReadOnlyList<MonitorInfo> ExternalScreens => Monitors.All().Where(m => !m.IsPrimary).ToList();

    public string? AudienceDevice
    {
        get => audienceDevice;
        set
        {
            if (Set(ref audienceDevice, value))
            {
                Raise(nameof(AudienceCaption), nameof(AudienceIsPopup));
                QueueSave();
                blackKey = "?";
                UpdateOutputs();
                // Ausgabe nur beim Start wählbar → laufende Show an gleicher Stelle neu starten (wie am Mac)
                if (IsShowRunning && LiveItem is { } live) _ = GoLive(live, CurrentSlide);
            }
        }
    }

    public MonitorInfo? AudienceScreen =>
        AudienceDevice == null ? null : Monitors.All().FirstOrDefault(m => m.DeviceName == AudienceDevice && !m.IsPrimary);

    public bool AudienceIsPopup => AudienceScreen == null;

    public string AudienceCaption => AudienceScreen is { } m ? $"Ausgabe: {m.FriendlyName} ({m.Number})" : "Ausgabe: Popup-Fenster";

    public string ExternalScreensText => $"{ExternalScreens.Count} externe(r) Bildschirm(e)";

    public void RefreshScreens()
    {
        Raise(nameof(ExternalScreens), nameof(ExternalScreensText), nameof(AudienceCaption), nameof(AudienceIsPopup));
        blackKey = "?";
        UpdateOutputs();
    }

    // MARK: - Abgeleitete Werte

    public bool CanControl => IsShowRunning && !IsLocked && !IsStarting;
    public bool CanGoLive => Selected != null && !IsStarting;
    public bool CanStop => IsShowRunning || IsStarting;

    public SlideMeta? CurrentSlideMeta => LiveItem?.Slide(CurrentSlide);

    public PresentationItem? NavigatorItem => LiveItem ?? Selected;

    public string SlidePositionText
    {
        get
        {
            if (LiveItem is not { } item) return "–";
            int pos = item.VisiblePosition(CurrentSlide) ?? CurrentSlide;
            return $"Folie {pos} von {item.VisibleCount}";
        }
    }

    public string LiveStatusLine
    {
        get
        {
            if (LiveItem is not { } item)
                return IsStarting ? "Wird gestartet …" : "Keine Präsentation live – Ausgänge sind schwarz";
            var parts = new List<string> { item.Title, SlidePositionText };
            int clicks = CurrentSlideMeta?.ClickCount ?? 0;
            if (clicks > 0) parts.Add($"Animation {ClickInSlide}/{clicks}");
            if (IsLocked) parts.Add("gesperrt");
            return string.Join(" · ", parts);
        }
    }

    /// <summary>Folienbild der Live-Folie (Live-Bild aus dem Fenster folgt später).</summary>
    public string? CurrentSlideImage => LiveItem?.Thumbnail(CurrentSlide);

    public string? NextSlideImage =>
        LiveItem is { } item && item.NextVisible(CurrentSlide) is int n ? item.Thumbnail(n) : null;

    public string LiveOverlayText =>
        IsBlackout || IsEndBlack ? "Publikum sieht Schwarz"
        : !IsShowRunning ? (IsStarting ? "Wird gestartet …" : "Keine Präsentation live")
        : "";

    public string BlackoutButtonText => IsBlackout ? "Blackscreen aktiv – zurück zum Bild" : "Blackscreen";

    public string CurrentNotes => CurrentSlideMeta?.Notes ?? "";

    private void RaiseDerived()
    {
        Raise(nameof(CanControl), nameof(CanGoLive), nameof(CanStop), nameof(CurrentSlideMeta),
            nameof(NavigatorItem), nameof(SlidePositionText), nameof(LiveStatusLine), nameof(CurrentSlideImage),
            nameof(NextSlideImage), nameof(LiveOverlayText), nameof(BlackoutButtonText), nameof(CurrentNotes));
        UpdateOutputs();
    }

    private string blackKey = "";

    /// <summary>
    /// Nicht live (oder Ende auf Schwarz): zugewiesene Bildschirme schwarz. Blackscreen und Start:
    /// Publikumsbildschirm schwarz (Mac: updateOutputs). Popup-Modus hat keinen eigenen Bildschirm.
    /// </summary>
    public void UpdateOutputs()
    {
        var black = new HashSet<string>();
        TaskbarHider.Apply(AudienceScreen is { } shown ? new[] { shown } : Array.Empty<MonitorInfo>());
        // Während eines Übergangs nichts ändern: dessen Fenster deckt ab, und ein ausblendendes
        // Schwarzbild darunter würde kurz den Desktop durchscheinen lassen. Danach erneut aufrufen.
        if (outputs.TransitionActive) return;
        if (AudienceScreen is { } audience)
        {
            if (!IsShowRunning || IsEndBlack || IsBlackout || IsStarting) black.Add(audience.DeviceName);
        }
        string key = string.Join("|", black.OrderBy(x => x));
        if (key == blackKey) return;
        blackKey = key;
        outputs.ApplyBlack(black);
    }

    // MARK: - Playlist

    public void Add(IEnumerable<string> paths)
    {
        PresentationItem? added = null;
        foreach (var raw in paths)
        {
            string path = Path.GetFullPath(raw);
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (!PresentationItem.FileExtensions.Contains(ext))
            {
                if (ext == ".ppt")
                    ErrorRaised?.Invoke($"„{Path.GetFileName(path)}“ ist im alten .ppt-Format. Bitte in PowerPoint als .pptx speichern.");
                continue;
            }
            var item = new PresentationItem { Path = path, Title = Path.GetFileNameWithoutExtension(path) };
            Items.Add(item);
            added = item;
        }
        Renumber();
        if (added != null) Selected = added;
        QueueSave();
        _ = PrepareAll();
    }

    public void Remove(PresentationItem item)
    {
        if (item == LiveItem) return;
        Items.Remove(item);
        try { Directory.Delete(Path.Combine(AppPaths.ThumbsDir, item.Id.ToString()), recursive: true); } catch { }
        if (Selected == item) Selected = Items.FirstOrDefault();
        Renumber();
        QueueSave();
    }

    public void Move(int from, int to)
    {
        if (from < 0 || from >= Items.Count || to < 0 || to >= Items.Count || from == to) return;
        Items.Move(from, to);
        Renumber();
        QueueSave();
    }

    private void Renumber()
    {
        for (int i = 0; i < Items.Count; i++) Items[i].Number = i + 1;
    }

    public void Regenerate(PresentationItem item)
    {
        item.State = PrepState.Pending;
        item.Thumbnails = new();
        _ = PrepareAll();
    }

    /// <summary>Alle ausstehenden Einträge nacheinander analysieren und Vorschaubilder erzeugen.</summary>
    public async Task PrepareAll()
    {
        if (preparing) return;
        preparing = true;
        try
        {
            while (Items.FirstOrDefault(i => i.State == PrepState.Pending) is { } item)
                await Prepare(item);
        }
        finally
        {
            preparing = false;
        }
    }

    private async Task Prepare(PresentationItem item)
    {
        if (!File.Exists(item.Path))
        {
            item.State = PrepState.Failed;
            item.StateText = "Datei nicht gefunden";
            return;
        }
        try
        {
            item.State = PrepState.Preparing;
            item.StateText = "Analysiere Folien …";
            item.Meta = await Task.Run(() => PptxParser.Parse(item.Path));
            item.StateText = "Erzeuge Vorschaubilder …";
            var progress = new Progress<(int done, int total)>(p => item.StateText = $"Vorschau {p.done}/{p.total}");
            item.Thumbnails = await ppt.ExportFileThumbnailsAsync(item.Path, AppPaths.ThumbsFor(item.Id.ToString()), progress);
            item.State = PrepState.Ready;
            item.StateText = "";
            DebugLog.Write($"Vorbereitet: „{item.Title}“ – {item.Subtitle}, {item.Thumbnails.Count} Vorschaubilder");
        }
        catch (Exception e)
        {
            string msg = StaWorker.Describe(e);
            item.State = PrepState.Failed;
            item.StateText = msg;
            StatusMessage = msg;
            DebugLog.Write($"Vorbereitung „{item.Title}“ fehlgeschlagen – {msg}");
        }
        RaiseDerived();
        QueueSave();
    }

    // MARK: - Live

    public void GoLiveSelected()
    {
        if (Selected != null) _ = GoLive(Selected);
    }

    public async Task GoLive(PresentationItem item, int? startAt = null)
    {
        if (IsStarting) return;
        if (!File.Exists(item.Path))
        {
            ErrorRaised?.Invoke($"Die Datei „{Path.GetFileName(item.Path)}“ wurde nicht gefunden.");
            return;
        }
        // Übergang: Publikumsbildschirm abdecken (Standbild bzw. Schwarz), darunter umschalten
        var style = AudienceScreen == null ? ShowTransition.Cut : Transition;
        bool covered = false;
        if (style != ShowTransition.Cut && AudienceScreen is { } coverScreen)
        {
            // Standbild = Bildschirmfoto des Publikumsbildschirms (scharf, mit Animationsstand), wie am Mac das Live-Bild
            System.Windows.Media.ImageSource? still = null;
            if (style == ShowTransition.Crossfade && IsShowRunning && !IsBlackout && !IsEndBlack)
            {
                try { still = ScreenShot.Capture(coverScreen.Bounds).Image; }
                catch (Exception e) { DebugLog.Write($"Übergang: kein Standbild – {e.Message}"); }
            }
            // Ist der Bildschirm schon schwarz (nichts live, Blackscreen), sofort abdecken
            bool alreadyBlack = !IsShowRunning || IsBlackout || IsEndBlack;
            double fadeIn = alreadyBlack ? 0.01 : still != null ? 0.12 : TransitionDuration / 2;
            DebugLog.Write($"Übergang {style} ({TransitionDuration:0.0} s), Standbild: {still != null}");
            ppt.KeepOnTopPaused = true;
            await outputs.BeginTransition(still, coverScreen, fadeIn);
            ppt.PlaceBelow = outputs.TransitionHandle;
            covered = true;
        }

        IsStarting = true;
        StatusMessage = $"Starte „{item.Title}“ …";
        IsLocked = false;
        IsEndBlack = false;
        IsBlackout = false;
        lockSlide = null;
        try
        {
            var open = await ppt.OpenAsync(item.Path, withWindow: false);
            if (item.Meta == null) item.Meta = await Task.Run(() => PptxParser.Parse(item.Path));
            int start = startAt ?? item.FirstVisible;
            var screen = AudienceScreen;
            await ppt.StartShowAsync(new ShowOptions(Kiosk: false, Target: screen, PreselectMonitorInRegistry: false,
                StartIndex: start));
            if (screen != null)
                await ppt.PlaceAsync(screen);
            else
                await ppt.PlaceAsync(PopupRect(open.SlideWidthPt, open.SlideHeightPt), topmostWanted: false);
            LiveItem = item;
            Selected = item;
            CurrentSlide = start;
            ClickInSlide = 0;
            IsShowRunning = true;
            StatusMessage = $"Live: {item.Title}";
            if (Timer.Settings.AutoStartWithPresentation)
            {
                Timer.Reset();
                Timer.Start();
            }
        }
        catch (Exception e)
        {
            StatusMessage = "Start fehlgeschlagen";
            ErrorRaised?.Invoke(StaWorker.Describe(e));
        }
        finally
        {
            IsStarting = false;
        }
        if (covered)
        {
            // erst aufblenden, wenn die neue Folie sicher steht
            outputs.RaiseAll();
            await Task.Delay(450);
            await outputs.EndTransition(style == ShowTransition.Crossfade ? TransitionDuration : TransitionDuration / 2);
            ppt.PlaceBelow = IntPtr.Zero;
            ppt.KeepOnTopPaused = false;
            blackKey = "?";
            UpdateOutputs();
        }
    }

    /// <summary>Popup-Modus ohne externen Bildschirm: PowerPoint rechts auf dem Hauptbildschirm, Seitenverhältnis der Folien.</summary>
    private static PixelRect PopupRect(double slideW, double slideH)
    {
        var main = Monitors.All().FirstOrDefault(m => m.IsPrimary) ?? Monitors.All()[0];
        var wa = main.WorkArea;
        int w = wa.Width / 2;
        int h = (int)Math.Round(w * slideH / Math.Max(slideW, 1));
        if (h > wa.Height)
        {
            h = wa.Height;
            w = (int)Math.Round(h * slideW / Math.Max(slideH, 1));
        }
        return new PixelRect(wa.Right - w, wa.Y + (wa.Height - h) / 2, w, h);
    }

    public async Task Stop()
    {
        if (!IsShowRunning && !IsStarting) return;
        try { await ppt.StopAsync(); }
        catch (Exception e) { DebugLog.Write($"Stopp: {StaWorker.Describe(e)}"); }
        MarkStopped("Gestoppt – Ausgänge schwarz");
    }

    private void MarkStopped(string message)
    {
        IsShowRunning = false;
        IsBlackout = false;
        IsLocked = false;
        IsEndBlack = false;
        lockSlide = null;
        LiveItem = null;
        CurrentSlide = 0;
        ClickInSlide = 0;
        StatusMessage = message;
    }

    public void Next()
    {
        if (!CanControl || LiveItem is not { } item) return;
        if (IsBlackout)
        {
            SetBlackout(false);
            return;
        }
        int clicks = item.Slide(CurrentSlide)?.ClickCount ?? 0;
        bool isLast = CurrentSlide >= item.LastVisible;
        if (isLast && ClickInSlide >= clicks)
        {
            HandleEnd(fromPowerPoint: false);
            return;
        }
        Send(ppt.NextAsync);
    }

    public void Previous()
    {
        if (!CanControl || LiveItem is not { } item) return;
        if (IsBlackout)
        {
            SetBlackout(false);
            return;
        }
        if (ClickInSlide == 0 && CurrentSlide <= item.FirstVisible) return;
        Send(ppt.PreviousAsync);
    }

    /// <summary>Direkt zu einer Folie springen (PowerPoint-Index, inkl. ausgeblendeter).</summary>
    public void Jump(int index)
    {
        if (!CanControl || LiveItem?.Slide(index) == null) return;
        DebugLog.Write($"Sprung zu Folie {index}");
        Send(() => ppt.GotoAsync(index));
    }

    /// <summary>Sprung über die angezeigte Nummer („Folie x von n“, ohne ausgeblendete).</summary>
    public void Jump(string visiblePosition)
    {
        if (LiveItem is not { } item) return;
        var visible = item.Slides.Where(s => !s.Hidden).ToList();
        if (!int.TryParse(visiblePosition.Trim(), out int n) || n < 1 || n > visible.Count)
        {
            StatusMessage = $"Folie {visiblePosition.Trim()} gibt es nicht (1–{visible.Count})";
            return;
        }
        Jump(visible[n - 1].Index);
    }

    public void ToggleBlackout() => SetBlackout(!IsBlackout);

    public void SetBlackout(bool on)
    {
        if (!IsShowRunning) return;
        IsBlackout = on;
        Send(() => ppt.SetBlackAsync(on));
    }

    public void Unlock()
    {
        bool wasBlack = IsEndBlack;
        IsLocked = false;
        IsEndBlack = false;
        lockSlide = null;
        StatusMessage = "Entsperrt";
        if (wasBlack && !IsBlackout) Send(() => ppt.SetBlackAsync(false));
    }

    private void HandleEnd(bool fromPowerPoint)
    {
        lockSlide = LiveItem?.LastVisible;
        DebugLog.Write($"Ende erreicht ({EndBehavior}), Sperre auf Folie {lockSlide}");
        IsLocked = true;
        if (EndBehavior == EndBehavior.HoldLastSlide)
        {
            StatusMessage = "Ende erreicht – letzte Folie wird gehalten, Steuerung gesperrt";
            if (fromPowerPoint) Send(ppt.PreviousAsync);
        }
        else
        {
            IsEndBlack = true;
            StatusMessage = "Ende erreicht – alle Ausgänge schwarz";
            Send(async () =>
            {
                if (fromPowerPoint) await ppt.PreviousAsync();
                await ppt.SetBlackAsync(true);
            });
        }
    }

    private async void Send(Func<Task> command)
    {
        try { await command(); }
        catch (Exception e) { StatusMessage = StaWorker.Describe(e); }
    }

    /// <summary>Neuer Show-Zustand aus der Abfrage-Schleife (UI-Thread).</summary>
    private void OnShowState(ShowState s)
    {
        if (!IsShowRunning || !s.HasShow) return;
        if (s.AtEnd)
        {
            if (!IsLocked) HandleEnd(fromPowerPoint: true);
            else Send(ppt.PreviousAsync);   // Sperre hält auch gegen Eingaben direkt in PowerPoint
            return;
        }
        if (IsLocked && lockSlide is int ls && s.SlideIndex != 0 && s.SlideIndex != ls)
        {
            DebugLog.Write($"Sperre: Folie {s.SlideIndex} in PowerPoint angesteuert → zurück auf Folie {ls}");
            Send(() => ppt.GotoAsync(ls));
            return;
        }
        if (s.SlideIndex > 0) CurrentSlide = s.SlideIndex;
        ClickInSlide = s.Click;
        if (!IsEndBlack) IsBlackout = s.IsBlack;
    }

    private void OnShowEnded()
    {
        if (IsShowRunning) MarkStopped("Präsentation wurde in PowerPoint beendet");
    }

    // MARK: - Takt, Laden, Speichern

    private void Tick()
    {
        Timer.Tick();
        Message.Tick();
        // Schwarzbild/Übergang über allem halten – aber nicht, solange die Show sichtbar sein soll
        // oder gerade startet: Sonst wechseln sich Schwarzbild und PowerPoint-Fenster (das ebenfalls
        // nach oben geholt wird) ständig ab, und die neue Folie flackert.
        if (outputs.TransitionActive || (!IsStarting && (!IsShowRunning || IsBlackout || IsEndBlack)))
            outputs.RaiseAll();
        // Taskleiste auf dem Publikumsbildschirm ausgeblendet halten (Explorer zeigt sie z. B. bei
        // automatisch ausblendender Taskleiste von selbst wieder)
        TaskbarHider.Apply(AudienceScreen is { } a ? new[] { a } : Array.Empty<MonitorInfo>());
        Clock = DateTime.Now;
        Raise(nameof(ClockText));
        BlinkPhase = DateTime.Now.Millisecond < 500;
    }

    private void Load()
    {
        var s = StateStore.Load();
        foreach (var si in s.Items)
        {
            var item = new PresentationItem { Id = si.Id, Path = si.Path, Title = si.Title };
            item.Meta = si.Meta;
            item.Thumbnails = si.Thumbnails;
            bool fresh = File.Exists(si.Path) && File.GetLastWriteTimeUtc(si.Path) == si.FileTime
                         && si.Meta != null && si.Thumbnails.Values.All(File.Exists) && si.Thumbnails.Count > 0;
            item.State = !File.Exists(si.Path) ? PrepState.Failed : fresh ? PrepState.Ready : PrepState.Pending;
            if (item.State == PrepState.Failed) item.StateText = "Datei nicht gefunden";
            Items.Add(item);
        }
        Renumber();
        Selected = Items.FirstOrDefault(i => i.Id == s.Selection) ?? Items.FirstOrDefault();
        audienceDevice = s.AudienceDevice;
        Timer.Settings = s.Timer;
        endBehavior = s.EndBehavior;
        notesFontScale = s.NotesFontScale;
        transition = s.Transition;
        transitionDuration = Math.Clamp(s.TransitionDuration, 0.3, 3);
        if (s.MessagePresets is { Count: > 0 } presets)
        {
            Message.Presets.Clear();
            foreach (var p in presets) Message.Presets.Add(p);
        }
        if (s.MessageBackground != null) Message.Background = s.MessageBackground;
        if (s.MessageForeground != null) Message.Foreground = s.MessageForeground;
        Message.Presets.CollectionChanged += (_, _) => QueueSave();
        Message.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(RegieMessage.Background) or nameof(RegieMessage.Foreground)) QueueSave();
        };
        CleanOrphanThumbs();
        TaskbarHider.RestoreAll();   // falls montro vorher abgestürzt ist
        _ = ui.InvokeAsync(() => _ = PrepareAll(), DispatcherPriority.ApplicationIdle);
        _ = ui.InvokeAsync(UpdateOutputs, DispatcherPriority.Loaded);
    }

    private void CleanOrphanThumbs()
    {
        try
        {
            var keep = Items.Select(i => i.Id.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            int removed = 0;
            foreach (var dir in Directory.GetDirectories(AppPaths.ThumbsDir))
            {
                string name = Path.GetFileName(dir);
                if (keep.Contains(name) || !Guid.TryParse(name, out _)) continue;
                Directory.Delete(dir, recursive: true);
                removed++;
            }
            if (removed > 0) DebugLog.Write($"Aufgeräumt: {removed} Vorschaubild-Ordner ohne Playlist-Eintrag");
        }
        catch { }
    }

    public void QueueSave()
    {
        if (saveQueued) return;
        saveQueued = true;
        ui.InvokeAsync(() =>
        {
            saveQueued = false;
            SaveNow();
        }, DispatcherPriority.Background);
    }

    public void SaveNow()
    {
        StateStore.Save(new SavedState
        {
            Items = Items.Select(i => new SavedItem
            {
                Id = i.Id,
                Path = i.Path,
                Title = i.Title,
                Meta = i.Meta,
                Thumbnails = i.Thumbnails,
                FileTime = File.Exists(i.Path) ? File.GetLastWriteTimeUtc(i.Path) : default,
            }).ToList(),
            Selection = Selected?.Id,
            AudienceDevice = AudienceDevice,
            Timer = Timer.Settings,
            EndBehavior = EndBehavior,
            NotesFontScale = NotesFontScale,
            Transition = Transition,
            TransitionDuration = TransitionDuration,
            MessagePresets = Message.Presets.ToList(),
            MessageBackground = Message.Background,
            MessageForeground = Message.Foreground,
        });
    }

    public async Task ShutdownAsync()
    {
        ticker.Stop();
        SaveNow();
        outputs.CloseAll();
        TaskbarHider.RestoreAll();
        await ppt.ShutdownAsync();
    }
}
