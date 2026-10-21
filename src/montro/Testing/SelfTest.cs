using System.Diagnostics;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Montro.Core;
using Montro.Display;
using Montro.PowerPoint;

namespace Montro.Testing;

/// <summary>
/// Selbsttest des PowerPoint-Kerns: <c>montro.exe --selbsttest [--kiosk-minuten N]</c>.
/// Erzeugt eine Testpräsentation, spielt sie auf jedem Bildschirm ab, prüft Lage (gemessen und
/// per Bildschirmfoto), Navigation mit ausgeblendeten Folien, Schwarz, Endbildschirm, verwaiste
/// Shows, PNG-Export, einen offenen Dialog und die Geschützte Ansicht. Ergebnis:
/// <c>%TEMP%\montro-selbsttest\&lt;Zeit&gt;\bericht.md</c> plus Bildschirmfotos.
/// </summary>
public sealed class SelfTest
{
    private enum Outcome { Ok, Fehler, Info }

    private sealed record Result(string Area, string Test, Outcome Outcome, string Detail);

    private const int Settle = 800;

    private readonly List<Result> results = new();
    private readonly PowerPointHost host = new();
    private readonly TestPowerPoint helper = new();
    private readonly string dir;
    private readonly string deck;
    private readonly int kioskMinutes;
    private string powerPointVersion = "?";
    private string? userFile;
    private readonly List<string> parserFiles = new();

    private SelfTest(int kioskMinutes)
    {
        this.kioskMinutes = kioskMinutes;
        // Unter %TEMP%: dort kann PowerPoint (eigener Prozess) sicher lesen und schreiben, auch
        // wenn montro selbst in einer Umgebung mit umgeleiteten AppData-Schreibzugriffen läuft
        dir = Path.Combine(Path.GetTempPath(), "montro-selbsttest", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(dir);
        deck = Path.Combine(dir, "montro-test.pptx");
    }

    /// <summary>Gibt die Anzahl fehlgeschlagener Prüfungen zurück (0 = alles gut, -1 = abgebrochen).</summary>
    public static async Task<int> RunAsync(string[] args)
    {
        int minutes = 0;
        int k = Array.IndexOf(args, "--kiosk-minuten");
        if (k >= 0 && k + 1 < args.Length) int.TryParse(args[k + 1], out minutes);
        int f = Array.IndexOf(args, "--datei");
        var t = new SelfTest(minutes) { userFile = f >= 0 && f + 1 < args.Length ? Path.GetFullPath(args[f + 1]) : null };
        int pz = Array.IndexOf(args, "--parser");
        if (pz >= 0)
            t.parserFiles.AddRange(args.Skip(pz + 1).TakeWhile(a => !a.StartsWith("--")).Select(Path.GetFullPath));
        return await t.Run();
    }

    private async Task<int> Run()
    {
        DebugLog.Write($"Selbsttest startet → {dir}");
        var sw = Stopwatch.StartNew();
        string? aborted = null;
        IReadOnlyList<MonitorInfo> monitors = Monitors.All();
        try
        {
            // Regel: nie in eine laufende Show hineinfahren
            int running = await helper.CountShowsAsync();
            if (running > 0)
            {
                aborted = $"Es läuft bereits eine Bildschirmpräsentation ({running}). Der Selbsttest startet nicht, damit keine Show gestört wird.";
                return -1;
            }
            powerPointVersion = await host.ConnectAsync();
            if (parserFiles.Count > 0)
            {
                // nur den PPTX-Parser gegen PowerPoint vergleichen, keine Shows
                foreach (var f in parserFiles)
                    await Step($"Parser: {Path.GetFileName(f)}", () => ParserRun(f));
                return results.Count(r => r.Outcome == Outcome.Fehler);
            }
            if (userFile != null)
            {
                // nur die eigene Präsentation auf jedem Bildschirm, ohne die übrigen Blöcke
                foreach (var m in monitors)
                    await Step($"{Path.GetFileName(userFile)} auf {Area(m)}", () => UserFileRun(m, userFile));
                return results.Count(r => r.Outcome == Outcome.Fehler);
            }
            await helper.CreateDeckAsync(deck);
            Add("Vorbereitung", "Testpräsentation erzeugt", Outcome.Info, "6 Folien, 3 und 5 ausgeblendet, Folie 2 mit 2 Klick-Animationen");

            foreach (var m in monitors)
                await Step(Area(m), () => MonitorRun(m));

            var side = monitors.FirstOrDefault(m => !m.IsPrimary) ?? monitors[0];
            await Step("Kiosk-Modus", () => KioskRun(side));
            await Step("Verwaiste Shows", () => OrphanRun(side));
            await Step("Vorschaubilder", ExportRun);
            await Step("PPTX-Parser", async () =>
            {
                string pdeck = Path.Combine(dir, "parser-test.pptx");
                await helper.CreateParserDeckAsync(pdeck);
                await ParserRun(deck, "PPTX-Parser: Testpräsentation");
                await ParserRun(pdeck, "PPTX-Parser: Animationen, Notizen, Trigger");
                var m = Pptx.PptxParser.Parse(pdeck);
                // feste Erwartung zusätzlich zum Vergleich mit PowerPoint
                Check("PPTX-Parser: Animationen, Notizen, Trigger", "Erwartung 2/0/1/0 Klicks, Folie 2 ausgeblendet",
                    m.Slides.Select(s => s.ClickCount).SequenceEqual(new[] { 2, 0, 1, 0 }) && m.Slides[1].Hidden && !m.Slides[0].Hidden,
                    string.Join(", ", m.Slides.Select(s => $"{s.Index}: {s.ClickCount}{(s.Hidden ? " ausgeblendet" : "")}")));
                Check("PPTX-Parser: Animationen, Notizen, Trigger", "Notizen mit Absätzen und Umlauten",
                    m.Slides[0].Notes == "Erster Absatz mit Umlauten: äöü ß\nZweiter Absatz\nmit Zeilenumbruch",
                    m.Slides[0].Notes.Replace("\n", "⏎"));
            });
            await Step("Offener Dialog in PowerPoint", () => DialogRun(side));
            await Step("Geschützte Ansicht", () => ProtectedViewRun(side));
        }
        catch (Exception e)
        {
            aborted = $"Selbsttest abgebrochen: {StaWorker.Describe(e)}";
        }
        finally
        {
            try { await host.ShutdownAsync(); } catch { }
            try { await helper.CleanupAsync(); } catch { }
            helper.Dispose();
            WriteReport(monitors, sw.Elapsed, aborted);
        }
        int failed = results.Count(r => r.Outcome == Outcome.Fehler);
        DebugLog.Write($"Selbsttest fertig: {results.Count(r => r.Outcome == Outcome.Ok)} ok, {failed} Fehler → {Path.Combine(dir, "bericht.md")}");
        return aborted != null ? -1 : failed;
    }

    private static string Area(MonitorInfo m) => $"Bildschirm {m.Number}: {m.FriendlyName}, {m.Scale:P0}";

    /// <summary>Ein Testblock; Fehler darin brechen nur diesen Block ab, nicht den ganzen Test.</summary>
    private async Task Step(string area, Func<Task> run)
    {
        try
        {
            await run();
        }
        catch (Exception e)
        {
            Add(area, "Ablauf", Outcome.Fehler, $"abgebrochen: {StaWorker.Describe(e)}");
        }
        finally
        {
            // für den nächsten Block sauber machen
            try { await host.StopAsync(); } catch { }
            try { await helper.CleanupAsync(); } catch { }
        }
    }

    // MARK: - Blöcke

    private async Task MonitorRun(MonitorInfo m)
    {
        string a = Area(m);
        var open = await host.OpenAsync(deck, withWindow: false);
        var map = open.Slides;
        Check(a, "Öffnen ohne Dokumentfenster", map.Count == 6 && map.VisibleCount == 4 && map.IsHidden(3) && map.IsHidden(5),
            $"{map}, {open.SlideWidthPt:0}×{open.SlideHeightPt:0} pt");

        await host.StartShowAsync(new ShowOptions(Kiosk: false, Target: m, PreselectMonitorInRegistry: false));
        var settings = await host.ReadShowSettingsAsync();
        Check(a, "Show-Typ Referent, PowerPoints Referentenansicht aus", settings.ShowType == 1 && !settings.PresenterView,
            $"ShowType {settings.ShowType}, ShowPresenterView {(settings.PresenterView ? "an" : "aus")}");

        var place = await host.PlaceAsync(m);
        Check(a, "Lage gemessen = Bildschirm", place.Ok, place.Detail);
        await Task.Delay(Settle);
        CheckEdges(a, "Bildschirmfoto: Folie füllt den Bildschirm", Shot(m, $"bildschirm{m.Number}-folie1.png"), TestPowerPoint.VisibleColor);

        var s = await host.ReadStateAsync();
        Check(a, "Start auf Folie 1", s.SlideIndex == 1, Describe(s, map));

        await Navigate(a, map, "Weiter", host.NextAsync, s => s.SlideIndex == 2 && s.Click == 0, "Folie 2, Klick 0");
        await Navigate(a, map, "Weiter", host.NextAsync, s => s.SlideIndex == 2 && s.Click == 1, "Folie 2, Animation 1");
        await Navigate(a, map, "Weiter", host.NextAsync, s => s.SlideIndex == 2 && s.Click == 2, "Folie 2, Animation 2");
        await Navigate(a, map, "Weiter", host.NextAsync, s => s.SlideIndex == 4, "überspringt ausgeblendete Folie 3 → Index 4 = Folie 3 von 4");
        CheckEdges(a, "Bildschirmfoto: keine ausgeblendete Folie zu sehen", Shot(m, $"bildschirm{m.Number}-folie3von4.png"), TestPowerPoint.VisibleColor);
        await Navigate(a, map, "Zurück", host.PreviousAsync, s => s.SlideIndex == 2, "zurück auf Index 2, ausgeblendete übersprungen");
        await Navigate(a, map, "Gehe zu 4 von 4", () => host.GotoAsync(map.IndexForVisible(4)!.Value), s => s.SlideIndex == 6, "Index 6");
        await Navigate(a, map, "Gehe zu 3 von 4", () => host.GotoAsync(map.IndexForVisible(3)!.Value), s => s.SlideIndex == 4, "Index 4");

        await host.GotoAsync(3);
        await Task.Delay(Settle);
        var hiddenState = await host.ReadStateAsync();
        Add(a, "Gehe zu ausgeblendeter Folie (Index 3)", Outcome.Info,
            $"{Describe(hiddenState, map)} – zur Info: so verhält sich PowerPoint bei GotoSlide auf eine ausgeblendete Folie");

        await host.SetBlackAsync(true);
        await Task.Delay(Settle);
        s = await host.ReadStateAsync();
        Check(a, "Schwarz an", s.IsBlack, Describe(s, map));
        CheckEdges(a, "Bildschirmfoto: schwarz", Shot(m, $"bildschirm{m.Number}-schwarz.png"), Colors.Black);
        await host.SetBlackAsync(false);
        await Task.Delay(Settle);
        s = await host.ReadStateAsync();
        Check(a, "Schwarz aus", s.RunState == SlideShowRunState.Running, Describe(s, map));

        await host.GotoAsync(6);
        await Task.Delay(Settle);
        await Navigate(a, map, "Weiter auf der letzten Folie", host.NextAsync, s => s.AtEnd, "Endbildschirm erkannt");
        Shot(m, $"bildschirm{m.Number}-ende.png");

        await host.StopAsync();
        await Task.Delay(Settle);
        int left = await helper.CountShowsAsync();
        Check(a, "Show beenden", left == 0, $"{left} Show(s) laufen noch");
    }

    /// <summary>PPTX-Parser (--parser): Ergebnis Folie für Folie mit dem vergleichen, was PowerPoint selbst meldet.</summary>
    private async Task ParserRun(string file, string? area = null)
    {
        string a = area ?? $"Parser: {Path.GetFileName(file)}";
        var sw = Stopwatch.StartNew();
        var meta = await Task.Run(() => Pptx.PptxParser.Parse(file));
        double parseSec = sw.Elapsed.TotalSeconds;
        var reference = await helper.ReadReferenceAsync(file);
        Add(a, "Gelesen", Outcome.Info, $"{meta.Subtitle}, Parser {parseSec:0.0} s");
        Check(a, "Folienzahl", meta.Slides.Count == reference.Count, $"Parser {meta.Slides.Count}, PowerPoint {reference.Count}");

        static string Norm(string s) => string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var hiddenDiff = new List<int>();
        var notesDiff = new List<int>();
        var clickDiff = new List<string>();
        var videoDiff = new List<string>();
        var videoInfo = new List<string>();
        for (int i = 0; i < Math.Min(meta.Slides.Count, reference.Count); i++)
        {
            var m = meta.Slides[i];
            var r = reference[i];
            if (m.Hidden != r.Hidden) hiddenDiff.Add(m.Index);
            if (Norm(m.Notes) != Norm(r.Notes)) notesDiff.Add(m.Index);
            if (m.ClickCount != r.ClickCount) clickDiff.Add($"{m.Index}: {m.ClickCount}≠{r.ClickCount}");
            if (m.Videos.Count != r.Videos.Count)
                videoDiff.Add($"{m.Index}: {m.Videos.Count}≠{r.Videos.Count}");
            else
                for (int k = 0; k < m.Videos.Count; k++)
                {
                    var v = m.Videos[k];
                    double rs = r.Videos[k].Seconds;
                    videoInfo.Add($"Folie {m.Index} „{v.Name}“ {v.Duration:0.0} s (PowerPoint {rs:0.0} s), Start {(v.StartClick?.ToString() ?? "Trigger")}");
                    if (rs > 0 && Math.Abs(v.Duration - rs) > 1.0)
                        videoDiff.Add($"{m.Index}: Länge {v.Duration:0.0}≠{rs:0.0} s");
                }
        }
        Check(a, "Ausgeblendete Folien", hiddenDiff.Count == 0,
            hiddenDiff.Count == 0 ? $"{meta.Slides.Count(s => s.Hidden)} gleich" : $"abweichend: Folie {string.Join(", ", hiddenDiff)}");
        Check(a, "Notizen", notesDiff.Count == 0,
            notesDiff.Count == 0 ? $"{meta.Slides.Count(s => s.Notes.Length > 0)} Folien mit Notizen, alle gleich" : $"abweichend: Folie {string.Join(", ", notesDiff)}");
        Check(a, "Klick-Animationen", clickDiff.Count == 0,
            clickDiff.Count == 0 ? $"{meta.Slides.Sum(s => s.ClickCount)} Klicks, alle gleich" : $"abweichend (Parser≠PowerPoint): {string.Join(", ", clickDiff)}");
        Check(a, "Videos", videoDiff.Count == 0,
            videoDiff.Count == 0 ? $"{meta.VideoCount} Videos, Anzahl und Länge gleich" : $"abweichend: {string.Join(", ", videoDiff)}");
        foreach (var v in videoInfo) Add(a, "Video", Outcome.Info, v);
    }

    /// <summary>Eigene Präsentation (--datei): starten, platzieren, Foto, weiter, Foto, zweimal hintereinander.</summary>
    private async Task UserFileRun(MonitorInfo m, string file)
    {
        string a = $"{Path.GetFileName(file)} auf {Area(m)}";
        var open = await host.OpenAsync(file, withWindow: false);
        var map = open.Slides;
        Add(a, "Öffnen", Outcome.Info, $"{map}, {open.SlideWidthPt:0}×{open.SlideHeightPt:0} pt");
        for (int round = 1; round <= 2; round++)
        {
            await host.StartShowAsync(new ShowOptions(Kiosk: false, Target: m, PreselectMonitorInRegistry: false));
            var place = await host.PlaceAsync(m);
            Check(a, $"Start {round}: Lage", place.Ok, place.Detail);
            await Task.Delay(1500);
            var s = await host.ReadStateAsync();
            Check(a, $"Start {round}: Show läuft", s.HasShow && s.RunState == SlideShowRunState.Running, Describe(s, map));
            Shot(m, $"datei-bildschirm{m.Number}-start{round}.png");
            await host.NextAsync();
            await Task.Delay(1500);
            s = await host.ReadStateAsync();
            Add(a, $"Start {round}: Weiter", Outcome.Info, Describe(s, map));
            Shot(m, $"datei-bildschirm{m.Number}-start{round}-weiter.png");
            // Start 2 ohne Beenden dazwischen: so wie im Testfenster „Show starten“ erneut drücken
        }
    }

    private async Task KioskRun(MonitorInfo m)
    {
        const string a = "Kiosk-Modus";
        var open = await host.OpenAsync(deck, withWindow: false);
        var map = open.Slides;
        await host.StartShowAsync(new ShowOptions(Kiosk: true, Target: m, PreselectMonitorInRegistry: false));
        var settings = await host.ReadShowSettingsAsync();
        Check(a, "Show-Typ Kiosk, PowerPoints Referentenansicht aus", settings.ShowType == 3 && !settings.PresenterView,
            $"ShowType {settings.ShowType} auf {Area(m)}");
        var place = await host.PlaceAsync(m);
        Check(a, "Lage gemessen = Bildschirm", place.Ok, place.Detail);
        await Navigate(a, map, "Weiter", host.NextAsync, s => s.SlideIndex == 2, "Folie 2");
        await Navigate(a, map, "Gehe zu 3 von 4", () => host.GotoAsync(4), s => s.SlideIndex == 4, "Index 4");

        if (kioskMinutes <= 0)
        {
            Add(a, "Rücksprung nach 5 min ohne Eingabe", Outcome.Info, "nicht geprüft – dafür montro.exe --selbsttest --kiosk-minuten 6");
            return;
        }
        DebugLog.Write($"Selbsttest: warte {kioskMinutes} min ohne Eingabe (Kiosk) …");
        await Task.Delay(TimeSpan.FromMinutes(kioskMinutes));
        var s = await host.ReadStateAsync();
        Shot(m, "kiosk-nach-warten.png");
        Check(a, $"Nach {kioskMinutes} min ohne Eingabe noch auf derselben Folie", s.SlideIndex == 4 && s.HasShow, Describe(s, map));
    }

    private async Task OrphanRun(MonitorInfo m)
    {
        const string a = "Verwaiste Shows";
        string other = Path.Combine(dir, "fremd.pptx");
        File.Copy(deck, other, overwrite: true);

        await host.OpenAsync(deck, withWindow: false);
        await helper.StartForeignShowAsync(other);
        await Task.Delay(Settle);
        int before = await helper.CountShowsAsync();
        await host.StartShowAsync(new ShowOptions(Kiosk: false, Target: m, PreselectMonitorInRegistry: false));
        await Task.Delay(Settle);
        int afterStart = await helper.CountShowsAsync();
        Check(a, "Neue Show beendet vorher alle alten", before == 1 && afterStart == 1, $"vorher {before}, danach {afterStart}");

        await helper.StartForeignShowAsync(other);
        await Task.Delay(Settle);
        int two = await helper.CountShowsAsync();
        int closed = await host.ExitOrphanShowsAsync();
        await Task.Delay(Settle);
        int left = await helper.CountShowsAsync();
        Check(a, "„Verwaiste Shows beenden“ beendet alle", two == 2 && closed == 2 && left == 0,
            $"vorher {two}, beendet {closed}, übrig {left}");
    }

    private async Task ExportRun()
    {
        const string a = "Vorschaubilder";
        await host.OpenAsync(deck, withWindow: false);
        string thumbs = Path.Combine(dir, "vorschau");
        var sw = Stopwatch.StartNew();
        int n = await host.ExportThumbnailsAsync(thumbs);
        var files = Directory.GetFiles(thumbs, "slide-*.png").OrderBy(f => f).ToArray();
        var sizes = files.Select(f =>
        {
            using var fs = File.OpenRead(f);
            var frame = BitmapDecoder.Create(fs, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            return (frame.PixelWidth, frame.PixelHeight);
        }).ToArray();
        Check(a, "Alle 6 Folien exportiert (auch ausgeblendete)", n == 6 && files.Length == 6,
            $"{files.Length} Dateien in {sw.Elapsed.TotalSeconds:0.0} s");
        Check(a, "Format 1280×720 (16:9)", sizes.All(s => s.PixelWidth == 1280 && s.PixelHeight == 720),
            string.Join(", ", sizes.Distinct().Select(s => $"{s.PixelWidth}×{s.PixelHeight}")));
    }

    private async Task DialogRun(MonitorInfo m)
    {
        const string a = "Offener Dialog in PowerPoint";
        var open = await host.OpenAsync(deck, withWindow: false);
        await host.StartShowAsync(new ShowOptions(Kiosk: false, Target: m, PreselectMonitorInRegistry: false));
        await host.PlaceAsync(m);

        var dialogThread = TestPowerPoint.OpenModalDialog();
        await Task.Delay(2500);
        var sw = Stopwatch.StartNew();
        string outcome;
        try
        {
            await host.NextAsync();
            outcome = "Befehl wurde trotz Dialog ausgeführt";
        }
        catch (Exception e)
        {
            outcome = $"Meldung: {StaWorker.Describe(e)}";
        }
        sw.Stop();
        Check(a, "Befehl kommt mit Meldung zurück statt einzufrieren", sw.Elapsed.TotalSeconds < 10,
            $"nach {sw.Elapsed.TotalSeconds:0.0} s – {outcome}");

        int closedDialogs = 0;
        for (int i = 0; i < 10 && dialogThread.IsAlive; i++)
        {
            closedDialogs += TestPowerPoint.CloseDialogs();
            await Task.Delay(500);
        }
        Add(a, "Dialog geschlossen", dialogThread.IsAlive ? Outcome.Fehler : Outcome.Info,
            dialogThread.IsAlive ? "Dialog ließ sich nicht schließen – bitte in PowerPoint von Hand schließen" : $"{closedDialogs} Dialogfenster geschlossen");
        await Task.Delay(1500);
        var s = await host.ReadStateAsync();
        Check(a, "Danach wieder bedienbar", s.HasShow, Describe(s, open.Slides));
    }

    private async Task ProtectedViewRun(MonitorInfo m)
    {
        const string a = "Geschützte Ansicht";
        string web = Path.Combine(dir, "aus-dem-internet.pptx");
        File.Copy(deck, web, overwrite: true);
        // Markierung „aus dem Internet“ (Mark of the Web), wie nach einem Download
        File.WriteAllText(web + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");
        try
        {
            var open = await host.OpenAsync(web, withWindow: false);
            await host.StartShowAsync(new ShowOptions(Kiosk: false, Target: m, PreselectMonitorInRegistry: false));
            await Task.Delay(Settle);
            var s = await host.ReadStateAsync();
            Check(a, "Datei aus dem Internet öffnen und Show starten", s.HasShow,
                $"PowerPoint öffnet die Datei per Automation direkt, {Describe(s, open.Slides)}");
        }
        catch (PowerPointException e)
        {
            Check(a, "Datei aus dem Internet: klare Meldung", e.Message.Contains("Geschützten Ansicht"), e.Message);
        }
        int pv = await helper.ProtectedViewCountAsync();
        Add(a, "Fenster in Geschützter Ansicht danach", Outcome.Info, pv.ToString());
    }

    // MARK: - Hilfen

    private async Task Navigate(string area, SlideMap map, string action, Func<Task> command, Func<ShowState, bool> expect, string expected)
    {
        await command();
        await Task.Delay(Settle);
        var s = await host.ReadStateAsync();
        Check(area, $"{action}: {expected}", expect(s), Describe(s, map));
    }

    private static string Describe(ShowState s, SlideMap map)
    {
        if (!s.HasShow) return "keine Show";
        int visible = map.VisiblePosition(s.SlideIndex);
        string pos = s.AtEnd ? "Endbildschirm" : visible > 0 ? $"Folie {visible} von {map.VisibleCount}" : $"ausgeblendete Folie";
        return $"{pos} (Index {s.SlideIndex}, Klick {s.Click}/{s.ClickCount}, {s.RunStateText})";
    }

    private ScreenShot Shot(MonitorInfo m, string name)
    {
        var shot = ScreenShot.Capture(m.Bounds);
        try { shot.SavePng(Path.Combine(dir, name)); } catch { }
        return shot;
    }

    private void CheckEdges(string area, string test, ScreenShot shot, Color expected)
    {
        var samples = shot.EdgeSamples().ToList();
        var wrong = samples.Where(x => !ScreenShot.Near(x.Color, expected)).ToList();
        Check(area, test, wrong.Count == 0,
            wrong.Count == 0
                ? $"Ränder und Ecken in Sollfarbe, Foto {shot.Width}×{shot.Height}"
                : $"abweichend: {string.Join(", ", wrong.Select(x => $"{x.Where} {x.Color.ToString().Substring(3)}"))} (Soll {expected.ToString().Substring(3)})");
    }

    private void Check(string area, string test, bool ok, string detail) =>
        Add(area, test, ok ? Outcome.Ok : Outcome.Fehler, detail);

    private void Add(string area, string test, Outcome outcome, string detail)
    {
        results.Add(new Result(area, test, outcome, detail));
        DebugLog.Write($"Selbsttest [{area}] {test}: {outcome} – {detail}");
    }

    private void WriteReport(IReadOnlyList<MonitorInfo> monitors, TimeSpan elapsed, string? aborted)
    {
        var b = new StringBuilder();
        int ok = results.Count(r => r.Outcome == Outcome.Ok);
        int failed = results.Count(r => r.Outcome == Outcome.Fehler);
        b.AppendLine($"# Selbsttest montro {AppInfo.Version} – {DateTime.Now:dd.MM.yyyy HH:mm}");
        b.AppendLine();
        b.AppendLine(aborted ?? $"**{ok} ok, {failed} Fehler** in {elapsed.TotalSeconds:0} s.");
        b.AppendLine();
        b.AppendLine($"- {powerPointVersion}");
        b.AppendLine($"- {Environment.OSVersion}, .NET {Environment.Version}, System-DPI {Monitors.SystemDpi()}");
        foreach (var m in monitors) b.AppendLine($"- {m.Label}, Arbeitsbereich {m.WorkArea}");
        foreach (var group in results.GroupBy(r => r.Area))
        {
            b.AppendLine();
            b.AppendLine($"## {group.Key}");
            b.AppendLine();
            b.AppendLine("| | Prüfung | Ergebnis |");
            b.AppendLine("|---|---|---|");
            foreach (var r in group)
            {
                string mark = r.Outcome switch { Outcome.Ok => "✓", Outcome.Fehler => "✗", _ => "ℹ" };
                b.AppendLine($"| {mark} | {r.Test} | {r.Detail.Replace("|", "/")} |");
            }
        }
        File.WriteAllText(Path.Combine(dir, "bericht.md"), b.ToString(), new UTF8Encoding(true));
    }
}
