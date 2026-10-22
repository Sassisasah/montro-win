using System.Runtime.InteropServices;
using Montro.Core;
using Montro.Display;

namespace Montro.PowerPoint;

public sealed class PowerPointException(string message) : Exception(message);

/// <summary>Einstellungen für den Start einer Show.</summary>
public sealed record ShowOptions(
    bool Kiosk,
    MonitorInfo? Target,
    bool PreselectMonitorInRegistry,
    int StartIndex = 0);   // PowerPoint-Index, 0 = von vorn

public sealed record OpenResult(SlideMap Slides, string Title, double SlideWidthPt, double SlideHeightPt);

public sealed record PlacementResult(PixelRect Target, PixelRect? Actual, int Corrections, bool Ok, string Detail);

/// <summary>
/// Steuert Microsoft PowerPoint per COM (Late Binding, ProgID „PowerPoint.Application“).
/// Alle Aufrufe laufen über <see cref="StaWorker"/> auf einem eigenen STA-Thread; die Felder mit
/// COM-Objekten werden nur dort angefasst. Nach außen gehen nur einfache Werte.
/// </summary>
public sealed class PowerPointHost
{
    private const int MsoTrue = -1;
    private const int MsoFalse = 0;
    private const int PpShowTypeSpeaker = 1;
    private const int PpShowTypeKiosk = 3;
    private const int PpShowAll = 1;
    private const int PollIntervalMs = 200;

    private readonly StaWorker sta = new("montro-PowerPoint");

    // --- nur auf dem STA-Thread ---
    private dynamic? app;
    private dynamic? pres;
    private bool ownsPres;
    private string? presPath;
    private bool presWithWindow;
    private IntPtr onTopHwnd;   // platziertes Show-Fenster, das über der Taskleiste bleiben soll
    // ------------------------------

    private CancellationTokenSource? pollCts;
    private int noShowCount;
    private string? lastPollError;

    /// <summary>Folien der geöffneten Präsentation (aus PowerPoint gelesen; ab Meilenstein 3 aus dem PPTX-Parser).</summary>
    public SlideMap? Slides { get; private set; }

    public ShowState LastState { get; private set; } = ShowState.None;

    /// <summary>Neuer Show-Zustand (auf dem Thread, der die Show gestartet hat – i. d. R. UI).</summary>
    public event Action<ShowState>? StateChanged;

    /// <summary>Show ist nicht mehr da (vier Abfragen in Folge ohne Show).</summary>
    public event Action? ShowEnded;

    // MARK: - Verbindung

    /// <summary>Mit PowerPoint verbinden (startet es bei Bedarf) und Version melden.</summary>
    public Task<string> ConnectAsync() => sta.Run(() =>
    {
        dynamic a = App();
        string version = Safe(() => (string)a.Version, "?");
        string build = Safe(() => (string)a.Build, "");
        int pv = Safe(() => (int)a.ProtectedViewWindows.Count, 0);
        var text = $"PowerPoint {version}" + (build.Length > 0 ? $" (Build {build})" : "");
        if (pv > 0) text += $" · {pv} Datei(en) in Geschützter Ansicht";
        return text;
    }, 30000, "Verbinden");

    private dynamic App()
    {
        if (app != null)
        {
            try
            {
                _ = (string)app.Version;   // lebt die Verbindung noch?
                return app;
            }
            catch
            {
                DebugLog.Write("PowerPoint: Verbindung verloren – verbinde neu");
                app = null;
                pres = null;
                presPath = null;
            }
        }
        var type = Type.GetTypeFromProgID("PowerPoint.Application")
            ?? throw new PowerPointException("Microsoft PowerPoint ist nicht installiert. montro nutzt PowerPoint als Wiedergabe-Engine.");
        object instance = Activator.CreateInstance(type)
            ?? throw new PowerPointException("PowerPoint ließ sich nicht starten.");
        app = instance;
        DebugLog.Write($"PowerPoint: verbunden, Version {Safe(() => (string)app.Version, "?")}");
        return app;
    }

    // MARK: - Öffnen

    /// <summary>Datei schreibgeschützt öffnen (keine Sperrdatei, keine Speichern-Frage).</summary>
    public Task<OpenResult> OpenAsync(string path, bool withWindow) => sta.Run(() =>
    {
        dynamic a = App();
        ExitAllShowsInternal();
        CloseInternal();

        string full = Path.GetFullPath(path);
        dynamic? existing = null;
        int count = Safe(() => (int)a.Presentations.Count, 0);
        for (int i = 1; i <= count; i++)
        {
            try
            {
                dynamic p = a.Presentations.Item(i);
                if (string.Equals((string)p.FullName, full, StringComparison.OrdinalIgnoreCase))
                {
                    existing = p;
                    break;
                }
            }
            catch { }
        }

        if (existing != null)
        {
            pres = existing;
            ownsPres = false;
            DebugLog.Write("PowerPoint: Datei war bereits geöffnet – wird mitbenutzt (und nicht geschlossen)");
        }
        else
        {
            try
            {
                // Open(FileName, ReadOnly, Untitled, WithWindow)
                pres = a.Presentations.Open(full, MsoTrue, MsoFalse, withWindow ? MsoTrue : MsoFalse);
                ownsPres = true;
            }
            catch (COMException e)
            {
                if (Safe(() => (int)a.ProtectedViewWindows.Count, 0) > 0)
                    throw new PowerPointException("PowerPoint zeigt die Datei in der Geschützten Ansicht. Datei › Eigenschaften › „Zulassen“ ankreuzen oder lokal kopieren.");
                throw new PowerPointException($"PowerPoint konnte die Datei nicht öffnen: {e.Message.Trim()} (0x{e.HResult:X8})");
            }
        }
        presPath = full;
        presWithWindow = withWindow;
        if (Safe(() => (int)a.ProtectedViewWindows.Count, 0) > 0)
            DebugLog.Write("PowerPoint: Achtung – mindestens eine Datei ist in der Geschützten Ansicht offen");

        dynamic pr = pres!;
        int n = (int)pr.Slides.Count;
        var hidden = new bool[n];
        for (int i = 1; i <= n; i++)
        {
            int index = i;
            hidden[i - 1] = Safe(() => (int)pr.Slides.Item(index).SlideShowTransition.Hidden == MsoTrue, false);
        }
        var map = new SlideMap(hidden);
        Slides = map;
        string title = Safe(() => (string)pr.Name, Path.GetFileName(full));
        double w = Safe(() => (double)pr.PageSetup.SlideWidth, 960.0);
        double h = Safe(() => (double)pr.PageSetup.SlideHeight, 540.0);
        DebugLog.Write($"PowerPoint: geöffnet „{title}“ – {map}, {w:0}×{h:0} pt{(withWindow ? ", mit Dokumentfenster" : "")}");
        return new OpenResult(map, title, w, h);
    }, 60000, "Öffnen");

    // MARK: - Show

    public async Task<ShowState> StartShowAsync(ShowOptions o)
    {
        StopPolling();
        var state = await sta.Run(() =>
        {
            if (pres == null && presPath == null) throw new PowerPointException("Keine Präsentation geöffnet.");
            ExitAllShowsInternal();
            EnsurePresentationInternal();

            dynamic ss = pres!.SlideShowSettings;
            int want = o.Kiosk ? PpShowTypeKiosk : PpShowTypeSpeaker;
            // Jede Einstellung einzeln: unlizenziertes PowerPoint (Nur-Lesen) lehnt Änderungen ab,
            // die Show soll trotzdem starten (Lehre aus Mac 0.27).
            TrySet("Show-Typ", () => { if ((int)ss.ShowType != want) ss.ShowType = want; });
            TrySet("PowerPoint-Referentenansicht aus", () => { if ((int)ss.ShowPresenterView != MsoFalse) ss.ShowPresenterView = MsoFalse; });
            TrySet("Alle Folien", () => { if ((int)ss.RangeType != PpShowAll) ss.RangeType = PpShowAll; });
            TrySet("Mit Animationen", () => { if ((int)ss.ShowWithAnimation != MsoTrue) ss.ShowWithAnimation = MsoTrue; });
            // Die Einstellungen machen die Präsentation „geändert“. Endet die Show, schließt PowerPoint
            // die fensterlose Präsentation selbst – und fragte dann „Änderungen speichern?“ (21.10.2026).
            MarkSaved();

            if (o.PreselectMonitorInRegistry && o.Target != null)
                RegistryMonitor.Set(o.Target.DeviceName);

            dynamic win = ss.Run();
            DebugLog.Write($"PowerPoint: Show gestartet ({(o.Kiosk ? "Kiosk" : "Referent")}, Ziel {o.Target?.Label ?? "–"})");
            if (o.StartIndex > 1)
                TrySet($"Startfolie {o.StartIndex}", () => { win.View.GotoSlide(o.StartIndex); });
            return ReadStateInternal();
        }, 30000, "Show starten");

        noShowCount = 0;
        Publish(state);
        StartPolling();
        return state;
    }

    /// <summary>
    /// Show-Fenster exakt auf die Bounds des Monitors legen: erst über das Objektmodell (Punkt),
    /// dann Ist-Lage in physischen Pixeln prüfen und bis zu 4× per SetWindowPos korrigieren.
    /// </summary>
    public Task<PlacementResult> PlaceAsync(MonitorInfo m)
    {
        // 1 Pixel niedriger als der Bildschirm: Ein Fenster, das den Monitor exakt füllt, gibt
        // Windows im Vollbild-Direktmodus aus (Independent Flip). Auf dem Test-PC (AMD Vega 8,
        // Verbatim MT15 per USB-C) kam dann auf den externen Monitoren nur Schwarz oder ein
        // kaputtes Hintergrundbild an – auch mit PowerPoint allein. Die fehlende Zeile unten
        // ist schwarz und fällt nicht auf.
        var b = m.Bounds;
        return PlaceAsync(new PixelRect(b.X, b.Y, b.Width, b.Height - 1), topmostWanted: true);
    }

    /// <summary>
    /// Show-Fenster auf ein beliebiges Rechteck legen (physische Pixel). Ohne <paramref name="topmostWanted"/>
    /// bleibt es ein normales Fenster (Popup-Modus neben montro).
    /// </summary>
    public Task<PlacementResult> PlaceAsync(PixelRect target, bool topmostWanted) => sta.Run(() =>
    {
        dynamic? w = FindShowWindow();
        if (w == null) throw new PowerPointException("Keine Bildschirmpräsentation aktiv.");
        IntPtr hwnd = ShowWindowHandle(w);
        if (hwnd == IntPtr.Zero) DebugLog.Write("Lage: Fenster der Bildschirmpräsentation nicht gefunden – nur Lage in Punkt möglich");
        var before = Monitors.WindowRect(hwnd);
        uint sysDpi = Monitors.SystemDpi();
        double f = 72.0 / sysDpi;

        if (before is not { } b0 || !b0.RoughlyEquals(target, 0))
        {
            TrySet("Lage in Punkt", () =>
            {
                w.Left = (float)(target.X * f);
                w.Top = (float)(target.Y * f);
                w.Width = (float)(target.Width * f);
                w.Height = (float)(target.Height * f);
            });
        }

        int corrections = 0;
        PixelRect? actual = null;
        for (int attempt = 0; attempt <= 4; attempt++)
        {
            Thread.Sleep(attempt == 0 ? 150 : 120);
            actual = Monitors.WindowRect(hwnd);
            if (actual is { } cur && cur.RoughlyEquals(target, 0)) break;
            if (attempt == 4) break;
            DebugLog.Write($"Lage: Soll {target}, Ist {actual?.ToString() ?? "unbekannt"} – Korrektur {attempt + 1}/4");
            Monitors.MoveWindow(hwnd, target);
            corrections++;
        }
        bool ok = actual is { } fin && fin.RoughlyEquals(target, 0);
        // Über alles legen: sonst bleibt auf Nebenbildschirmen die Taskleiste über der Show sichtbar
        // (PowerPoint läuft im Hintergrund, Windows blendet die Taskleiste nur für das aktive Vollbildfenster aus)
        // Während eines Übergangs direkt unter montros Abdeckung einordnen, sonst blitzt die neue
        // Folie kurz auf, bevor die Abdeckung wieder nach oben kommt („kommt, geht, kommt wieder“)
        bool topmost = topmostWanted && hwnd != IntPtr.Zero && Monitors.MoveWindow(hwnd, target, topmost: true, insertAfter: PlaceBelow);
        onTopHwnd = topmost ? hwnd : IntPtr.Zero;
        // Die Taskleiste kommt kurz nach dem Platzieren gern noch einmal hoch – 1,5 s lang dagegenhalten
        for (int i = 0; topmost && i < 15; i++)
        {
            Thread.Sleep(100);
            KeepOnTop();
        }
        string detail = $"vorher {before?.ToString() ?? "?"}, Ist {actual?.ToString() ?? "?"}, Soll {target}, "
                      + $"System-DPI {sysDpi}, Fenster-DPI {Monitors.WindowDpi(hwnd)}, Korrekturen {corrections}"
                      + (topmost ? ", im Vordergrund" : "");
        DebugLog.Write($"Lage {(ok ? "ok" : "WEICHT AB")}: {detail}");
        return new PlacementResult(target, actual, corrections, ok, detail);
    }, 10000, "Platzieren");

    public Task NextAsync() => sta.Run(() => { View().Next(); KeepOnTop(); }, 5000, "Weiter");

    public Task PreviousAsync() => sta.Run(() => { View().Previous(); KeepOnTop(); }, 5000, "Zurück");

    /// <summary>Zu einem PowerPoint-Index springen (Umrechnung von „sichtbar“ über <see cref="SlideMap"/>).</summary>
    public Task GotoAsync(int pptIndex) => sta.Run(() => { View().GotoSlide(pptIndex); KeepOnTop(); }, 5000, $"Gehe zu {pptIndex}");

    public Task SetBlackAsync(bool black) => sta.Run(() =>
    {
        dynamic v = View();
        if ((int)v.State == (int)SlideShowRunState.Done) return;
        v.State = black ? (int)SlideShowRunState.BlackScreen : (int)SlideShowRunState.Running;
        KeepOnTop();
    }, 5000, black ? "Schwarz an" : "Schwarz aus");

    /// <summary>Alle Shows beenden und die Präsentation schließen.</summary>
    public async Task StopAsync()
    {
        StopPolling();
        await sta.Run(() =>
        {
            ExitAllShowsInternal();
            CloseInternal();
        }, 15000, "Beenden");
        Publish(ShowState.None);
    }

    /// <summary>Alle noch laufenden Bildschirmpräsentationen beenden (z. B. nach Absturz).</summary>
    public Task<int> ExitOrphanShowsAsync() => sta.Run(() =>
    {
        App();
        return ExitAllShowsInternal();
    }, 15000, "Verwaiste Shows beenden");

    /// <summary>Show-Zustand sofort lesen (ohne auf die Abfrage-Schleife zu warten).</summary>
    public Task<ShowState> ReadStateAsync() => sta.Run(ReadStateInternal, 5000, "Status lesen");

    /// <summary>Tatsächlich gesetzte Show-Einstellungen (Show-Typ, PowerPoint-Referentenansicht an?).</summary>
    public Task<(int ShowType, bool PresenterView)> ReadShowSettingsAsync() => sta.Run(() =>
    {
        if (pres == null) throw new PowerPointException("Keine Präsentation geöffnet.");
        dynamic ss = pres.SlideShowSettings;
        return (Safe(() => (int)ss.ShowType, 0), Safe(() => (int)ss.ShowPresenterView, 0) != MsoFalse);
    }, 5000, "Show-Einstellungen lesen");

    // MARK: - Vorschaubilder

    /// <summary>Jede Folie als PNG (1280 px breit) exportieren, auch ausgeblendete. Ein Aufruf je Folie.</summary>
    public async Task<int> ExportThumbnailsAsync(string dir, IProgress<(int done, int total)>? progress = null)
    {
        Directory.CreateDirectory(dir);
        var (count, width, height) = await sta.Run(() =>
        {
            EnsurePresentationInternal();
            dynamic pr = pres!;
            int n = (int)pr.Slides.Count;
            double sw = Safe(() => (double)pr.PageSetup.SlideWidth, 960.0);
            double sh = Safe(() => (double)pr.PageSetup.SlideHeight, 540.0);
            int h = (int)Math.Round(1280 * sh / Math.Max(sw, 1));
            return (n, 1280, h);
        }, 8000, "Export vorbereiten");

        for (int i = 1; i <= count; i++)
        {
            int index = i;
            string file = Path.Combine(dir, $"slide-{index}.png");
            await sta.Run(() =>
            {
                if (pres == null) throw new PowerPointException("Präsentation wurde geschlossen.");
                dynamic pr = pres;
                pr.Slides.Item(index).Export(file, "PNG", width, height);
            }, 20000, $"Export Folie {index}");
            progress?.Report((index, count));
        }
        DebugLog.Write($"PowerPoint: {count} Vorschaubilder ({width}×{height}) nach {dir}");
        return count;
    }

    /// <summary>
    /// Vorschaubilder einer beliebigen Datei für die Playlist, ohne laufende Shows oder die
    /// geöffnete Präsentation anzufassen: eigene schreibgeschützte Kopie öffnen, je Folie ein PNG
    /// (1280 px) nach <c>%TEMP%</c>, schließen, dann nach <paramref name="dir"/> verschieben.
    /// Über %TEMP%, weil PowerPoint als eigener Prozess nicht überall hinschreiben darf, wo montro es kann.
    /// </summary>
    public async Task<Dictionary<int, string>> ExportFileThumbnailsAsync(string path, string dir, IProgress<(int done, int total)>? progress = null)
    {
        string temp = Path.Combine(Path.GetTempPath(), $"montro-thumbs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        object? handle = null;
        bool close = false;
        try
        {
            var (count, height) = await sta.Run(() =>
            {
                dynamic a = App();
                string full = Path.GetFullPath(path);
                dynamic? p = null;
                // schon offen (z. B. gerade live)? Dann mitbenutzen und auf keinen Fall schließen
                int open = Safe(() => (int)a.Presentations.Count, 0);
                for (int i = 1; i <= open && p == null; i++)
                {
                    int k = i;
                    dynamic? q = Safe<dynamic?>(() => a.Presentations.Item(k), null);
                    if (q != null && string.Equals(Safe(() => (string)q!.FullName, ""), full, StringComparison.OrdinalIgnoreCase))
                        p = q;
                }
                if (p == null)
                {
                    p = a.Presentations.Open(full, MsoTrue, MsoFalse, MsoFalse);
                    close = true;
                }
                handle = p;
                double sw = Safe(() => (double)p.PageSetup.SlideWidth, 960.0);
                double sh = Safe(() => (double)p.PageSetup.SlideHeight, 540.0);
                return ((int)p.Slides.Count, (int)Math.Round(1280 * sh / Math.Max(sw, 1)));
            }, 60000, "Vorschau öffnen");

            for (int i = 1; i <= count; i++)
            {
                int index = i;
                await sta.Run(() =>
                {
                    dynamic p = handle!;
                    p.Slides.Item(index).Export(Path.Combine(temp, $"slide-{index}.png"), "PNG", 1280, height);
                }, 20000, $"Vorschau Folie {index}");
                progress?.Report((index, count));
            }

            Directory.CreateDirectory(dir);
            var result = new Dictionary<int, string>();
            for (int i = 1; i <= count; i++)
            {
                string src = Path.Combine(temp, $"slide-{i}.png");
                if (!File.Exists(src)) continue;
                string dst = Path.Combine(dir, $"slide-{i}.png");
                File.Move(src, dst, overwrite: true);
                result[i] = dst;
            }
            return result;
        }
        finally
        {
            if (handle != null && close)
            {
                try { await sta.Run(() => { ((dynamic)handle).Close(); }, 10000, "Vorschau schließen"); } catch { }
            }
            try { Directory.Delete(temp, recursive: true); } catch { }
        }
    }

    // MARK: - Beenden der App

    public async Task ShutdownAsync()
    {
        StopPolling();
        try
        {
            await sta.Run(() =>
            {
                ExitAllShowsInternal();
                CloseInternal();
                app = null;
            }, 5000, "Aufräumen");
        }
        catch { }
        sta.Dispose();
    }

    // MARK: - Statusabfrage

    private void StartPolling()
    {
        StopPolling();
        var cts = new CancellationTokenSource();
        pollCts = cts;
        _ = PollLoop(cts.Token);
    }

    private void StopPolling()
    {
        pollCts?.Cancel();
        pollCts = null;
    }

    private async Task PollLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var s = await sta.Run(() =>
                {
                    // bei jeder Abfrage (200 ms): Show-Fenster wieder über die Taskleiste holen
                    KeepOnTop();
                    return ReadStateInternal();
                }, 2000);
                if (ct.IsCancellationRequested) break;
                lastPollError = null;
                if (!s.HasShow)
                {
                    // vier Abfragen in Folge ohne Show = beendet (wie am Mac)
                    if (++noShowCount >= 4)
                    {
                        DebugLog.Write("PowerPoint: Show ist beendet");
                        Publish(s);
                        ShowEnded?.Invoke();
                        break;
                    }
                }
                else
                {
                    noShowCount = 0;
                    Publish(s);
                }
            }
            catch (Exception e)
            {
                // Abfragefehler (PowerPoint beschäftigt) zählen nicht als „beendet“
                var msg = StaWorker.Describe(e);
                if (msg != lastPollError) DebugLog.Write($"PowerPoint: Statusabfrage – {msg}");
                lastPollError = msg;
            }
            try { await Task.Delay(PollIntervalMs, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private void Publish(ShowState s)
    {
        if (Equals(s, LastState)) return;
        LastState = s;
        StateChanged?.Invoke(s);
    }

    // MARK: - nur auf dem STA-Thread

    private ShowState ReadStateInternal()
    {
        dynamic? w = FindShowWindow();
        if (w == null) return ShowState.None;
        dynamic v = w.View;
        int st = Safe(() => (int)v.State, 0);
        int pos = Safe(() => (int)v.CurrentShowPosition, 0);
        int idx = 0, click = 0, clicks = 0;
        if (st != (int)SlideShowRunState.Done)
        {
            // am Endbildschirm wirft View.Slide eine Ausnahme
            idx = Safe(() => (int)v.Slide.SlideIndex, 0);
            click = Safe(() => (int)v.GetClickIndex(), 0);
            clicks = Safe(() => (int)v.GetClickCount(), 0);
        }
        return new ShowState(true, (SlideShowRunState)st, idx, pos, click, clicks);
    }

    private dynamic? FindShowWindow()
    {
        if (app == null) return null;
        dynamic a = app;
        int n = Safe(() => (int)a.SlideShowWindows.Count, 0);
        if (n == 0) return null;
        if (presPath != null)
        {
            for (int i = 1; i <= n; i++)
            {
                try
                {
                    dynamic w = a.SlideShowWindows.Item(i);
                    if (string.Equals((string)w.Presentation.FullName, presPath, StringComparison.OrdinalIgnoreCase))
                        return w;
                }
                catch { }
            }
        }
        try { return a.SlideShowWindows.Item(1); }
        catch { return null; }
    }

    /// <summary>
    /// Fenster-Handle der Show. <c>SlideShowWindow.HWND</c> fehlt in aktuellen PowerPoint-Versionen
    /// (Microsoft 365, Build 19929: Eigenschaft nicht vorhanden) – dann das sichtbare Fenster der
    /// Klasse „screenClass“ von POWERPNT.EXE suchen, dessen Titel den Präsentationsnamen enthält.
    /// </summary>
    private static IntPtr ShowWindowHandle(dynamic w)
    {
        int h = Safe(() => (int)w.HWND, 0);
        if (h != 0) return new IntPtr(h);
        string name = Safe(() => Path.GetFileNameWithoutExtension((string)w.Presentation.Name), "");
        return Monitors.FindProcessWindow("POWERPNT", "screenClass", name);
    }

    /// <summary>
    /// Die Taskleiste eines Nebenbildschirms schiebt sich wieder über die Show (z. B. bei
    /// Folienwechsel oder Fokuswechsel), solange PowerPoint nicht das aktive Fenster ist.
    /// Deshalb nach jedem Befehl und regelmäßig erneut nach oben holen.
    /// </summary>
    /// <summary>Während eines Übergangs liegt montros Abdeckung über der Show – dann nicht nach oben holen.</summary>
    public volatile bool KeepOnTopPaused;

    /// <summary>Fenster, unter das die Show beim Platzieren eingeordnet wird (Übergangsabdeckung), sonst Zero.</summary>
    public IntPtr PlaceBelow;

    private void KeepOnTop()
    {
        if (onTopHwnd == IntPtr.Zero || KeepOnTopPaused) return;
        if (!Monitors.BringToTop(onTopHwnd)) onTopHwnd = IntPtr.Zero;   // Fenster ist weg
    }

    private dynamic View()
    {
        dynamic? w = FindShowWindow();
        if (w == null) throw new PowerPointException("Keine Bildschirmpräsentation aktiv.");
        return w.View;
    }

    private int ExitAllShowsInternal()
    {
        onTopHwnd = IntPtr.Zero;
        if (app == null) return 0;
        dynamic a = app;
        int n = Safe(() => (int)a.SlideShowWindows.Count, 0);
        int closed = 0;
        for (int i = n; i >= 1; i--)
        {
            try
            {
                dynamic sw = a.SlideShowWindows.Item(i);
                TrySet("Als gespeichert markieren", () => { sw.Presentation.Saved = MsoTrue; });
                sw.View.Exit();
                closed++;
            }
            catch (Exception e)
            {
                DebugLog.Write($"PowerPoint: Show {i} ließ sich nicht beenden – {StaWorker.Describe(e)}");
            }
        }
        if (closed > 0) DebugLog.Write($"PowerPoint: {closed} Show(s) beendet");
        return closed;
    }

    private bool PresentationAlive()
    {
        if (pres == null) return false;
        dynamic p = pres;
        try
        {
            _ = (string)p.Name;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Eine ohne Fenster geöffnete Präsentation schließt PowerPoint von selbst, sobald ihre Show
    /// endet (danach „Object does not exist“, 0x80048010). Dann still neu öffnen.
    /// </summary>
    private void EnsurePresentationInternal()
    {
        if (PresentationAlive()) return;
        if (presPath == null) throw new PowerPointException("Keine Präsentation geöffnet.");
        dynamic a = App();
        DebugLog.Write("PowerPoint: Präsentation wurde mit dem Show-Ende geschlossen – öffne sie neu");
        pres = a.Presentations.Open(presPath, MsoTrue, MsoFalse, presWithWindow ? MsoTrue : MsoFalse);
        ownsPres = true;
    }

    /// <summary>Präsentation als „gespeichert“ markieren, damit PowerPoint nie nach dem Speichern fragt.</summary>
    private void MarkSaved()
    {
        if (pres == null) return;
        dynamic p = pres;
        TrySet("Als gespeichert markieren", () => { p.Saved = MsoTrue; });
    }

    private void CloseInternal()
    {
        if (pres != null && ownsPres && PresentationAlive())
        {
            MarkSaved();
            try
            {
                // per Automation schließt PowerPoint ohne „Speichern?“-Frage
                pres!.Close();
                DebugLog.Write("PowerPoint: Präsentation geschlossen");
            }
            catch (Exception e)
            {
                DebugLog.Write($"PowerPoint: Schließen fehlgeschlagen – {StaWorker.Describe(e)}");
            }
        }
        pres = null;
        ownsPres = false;
        presPath = null;
        Slides = null;
    }

    private static void TrySet(string what, Action action)
    {
        try { action(); }
        catch (Exception e) { DebugLog.Write($"PowerPoint: {what} nicht möglich – {StaWorker.Describe(e)}"); }
    }

    private static T Safe<T>(Func<T> f, T fallback)
    {
        try { return f(); }
        catch { return fallback; }
    }
}
