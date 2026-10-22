using System.Text;
using Montro.Core;

namespace Montro.Testing;

/// <summary>
/// Prüft Show-Dateien ohne Oberfläche: <c>montro.exe --showtest &lt;Ordner&gt; &lt;a.pptx&gt; &lt;b.pptx&gt;</c>.
/// 1. Sichern und wieder öffnen (Playlist, Preset-Zuweisung, Ansicht, Einstellungen gleich).
/// 2. „Mit Präsentationen sichern“, Ordner verschieben, öffnen (relative Pfade).
/// 3. Eine Show im Mac-Stil (#RRGGBBAA, countup, Pfad mit „/“, unbekanntes Widget) lesen.
/// Bericht: <c>&lt;Ordner&gt;\bericht.txt</c>, Rückgabe 0 = alles ok.
/// </summary>
public static class ShowFileTest
{
    public static async Task<int> RunAsync(string[] args)
    {
        int i = Array.IndexOf(args, "--showtest");
        var rest = args.Skip(i + 1).Where(a => !a.StartsWith("--")).ToList();
        string dir = Path.GetFullPath(rest[0]);
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);
        var report = new StringBuilder();
        int failures = 0;
        void Check(string what, bool ok, string detail = "")
        {
            report.AppendLine($"{(ok ? "ok    " : "FEHLER")} {what}{(detail.Length > 0 ? " – " + detail : "")}");
            if (!ok) failures++;
        }

        // 1. Sichern und öffnen
        var a = AppState.ForTest();
        a.ErrorRaised += m => report.AppendLine($"Meldung: {m}");
        foreach (var f in rest.Skip(1)) a.Items.Add(new PresentationItem { Path = Path.GetFullPath(f), Title = Path.GetFileNameWithoutExtension(f), State = PrepState.Ready });
        a.DraftLayout.Update(WidgetKind.Timer, c => c.FontScale = 1.5);
        a.DraftLayout.Background = "#35294B";
        Guid preset = a.SaveDraftAsPreset("Speaker Test", a.Items[0]);
        a.EndBehavior = EndBehavior.BlackEverywhere;
        a.Transition = ShowTransition.Crossfade;
        a.TransitionDuration = 2.0;
        a.Timer.Settings = new TimerSettings { Duration = 1200, CountUp = true, Warning1Color = "#B088FB" };
        a.Message.Background = "#F2465A";
        string show1 = Path.Combine(dir, "Test 1.montro");
        a.SaveShow(show1);
        report.AppendLine("--- Inhalt ---").AppendLine(File.ReadAllText(show1)).AppendLine("--------------");

        var b = AppState.ForTest();
        b.ErrorRaised += m => report.AppendLine($"Meldung: {m}");
        b.OpenShow(show1);
        Check("Playlist gleich", b.Items.Select(x => x.Path).SequenceEqual(a.Items.Select(x => x.Path)), string.Join(", ", b.Items.Select(x => x.Title)));
        Check("Preset zugewiesen", b.Items.Count > 0 && b.Items[0].PresetId == preset && b.Preset(preset)?.Name == "Speaker Test");
        Check("Preset-Ansicht", b.Preset(preset)?.Layout.Config(WidgetKind.Timer)?.FontScale == 1.5 && b.Preset(preset)?.Layout.Background == "#35294B");
        Check("Standard-Ansicht", b.StandardLayout.Config(WidgetKind.Timer)?.FontScale == 1);
        Check("Ende", b.EndBehavior == EndBehavior.BlackEverywhere);
        Check("Übergang", b.Transition == ShowTransition.Crossfade && Math.Abs(b.TransitionDuration - 2.0) < 0.01);
        Check("Timer", b.Timer.Settings.Duration == 1200 && b.Timer.Settings.CountUp && b.Timer.Settings.Warning1Color == "#B088FB");
        Check("Regie-Farbe", b.Message.Background == "#F2465A");
        Check("Show-Name", b.CurrentShowName == "Test 1");

        // 2. Mit Präsentationen sichern, Ordner verschieben, öffnen
        string packDir = Path.Combine(dir, "Paket");
        string? packShow = await a.SaveShowWithPresentations(packDir);
        Check("Paket angelegt", packShow != null && File.Exists(packShow) && Directory.GetFiles(Path.Combine(packDir, "Präsentationen")).Length == a.Items.Count);
        string moved = Path.Combine(dir, "Verschoben");
        Directory.Move(packDir, moved);
        var c2 = AppState.ForTest();
        c2.OpenShow(Path.Combine(moved, "Paket.montro"));
        Check("Paket nach Verschieben gefunden", c2.Items.Count == a.Items.Count && c2.Items.All(x => x.Path.StartsWith(moved, StringComparison.OrdinalIgnoreCase) && File.Exists(x.Path)),
            string.Join(", ", c2.Items.Select(x => x.Path)));

        // 3. Show im Mac-Stil
        string macDir = Path.Combine(dir, "Mac");
        Directory.CreateDirectory(Path.Combine(macDir, "Präsentationen"));
        string first = rest[1];
        File.Copy(first, Path.Combine(macDir, "Präsentationen", Path.GetFileName(first)));
        string macJson = $$"""
        {
          "format": "montro-show", "version": 2, "savedBy": "montro 0.36 (macOS)", "savedAt": "2026-10-21T17:30:00Z",
          "name": "Mac Saal 1",
          "presentations": [
            { "file": "{{Path.GetFileName(first)}}", "relativePath": "Präsentationen/{{Path.GetFileName(first)}}",
              "absolutePath": "/Users/x/Mac Saal 1/Präsentationen/{{Path.GetFileName(first)}}", "title": "Vom Mac",
              "preset": "6F1C2A3B-0000-4000-8000-00000000ABCD", "sha256": "abc", "size": 1 },
            { "file": "fehlt.pptx", "absolutePath": "/Users/x/fehlt.pptx", "title": "Fehlt" }
          ],
          "standardLayout": { "background": "#202020FF", "textColor": "#FFFFFF",
            "widgets": [ { "kind": "notes", "enabled": true, "x": 0.5, "y": 0.1, "w": 0.4, "h": 0.8, "fontScale": 2, "showTitle": true },
                         { "kind": "hologram", "enabled": true, "x": 0, "y": 0, "w": 0.1, "h": 0.1, "fontScale": 1, "showTitle": false } ] },
          "presets": [ { "id": "6F1C2A3B-0000-4000-8000-00000000ABCD", "name": "Maltzahn",
                         "layout": { "background": "#35294B80", "textColor": "#F5F5F5", "widgets": [] } } ],
          "settings": { "endBehavior": "holdLastSlide", "transition": "cut", "transitionSeconds": 0.5,
            "timer": { "durationSeconds": 600, "warning1Seconds": 120, "warning2Seconds": 30, "normalColor": "#F5F5F5",
                       "warning1Color": "#FFC71F", "warning2Color": "#FF8C1A", "overtimeColor": "#F53838", "allowOvertime": false,
                       "blinkInOvertime": true, "blinkInWarning2": true, "mode": "countup", "autoStart": true, "showProgress": false },
            "message": { "presets": ["Noch 2 Minuten"], "background": "#B2F141", "foreground": "#202020" },
            "neuesFeld": 42 }
        }
        """;
        string macShow = Path.Combine(macDir, "Mac Saal 1.montro");
        File.WriteAllText(macShow, macJson, new UTF8Encoding(false));
        var m = AppState.ForTest();
        var messages = new List<string>();
        m.ErrorRaised += messages.Add;
        m.OpenShow(macShow);
        Check("Mac: Datei über relativen Pfad", m.Items.Count == 2 && File.Exists(m.Items[0].Path) && m.Items[0].Title == "Vom Mac", m.Items.FirstOrDefault()?.Path ?? "");
        Check("Mac: fehlende Datei markiert", m.Items.Count == 2 && m.Items[1].State == PrepState.Failed && messages.Any(x => x.Contains("nicht gefunden")));
        Check("Mac: Preset", m.Items[0].PresetId == Guid.Parse("6F1C2A3B-0000-4000-8000-00000000ABCD") && m.Preset(m.Items[0].PresetId)?.Name == "Maltzahn");
        Check("Mac: Farbe mit Alpha", m.Preset(m.Items[0].PresetId)?.Layout.Background == "#8035294B" && m.StandardLayout.Background == "#202020",
            $"{m.Preset(m.Items[0].PresetId)?.Layout.Background} / {m.StandardLayout.Background}");
        Check("Mac: Widgets ergänzt, unbekanntes ignoriert", m.StandardLayout.Widgets.Count == 8 && m.StandardLayout.Config(WidgetKind.Notes)?.FontScale == 2
              && m.StandardLayout.Config(WidgetKind.Timer)?.Enabled == false);
        Check("Mac: Einstellungen", m.Transition == ShowTransition.Cut && m.Timer.Settings.CountUp && !m.Timer.Settings.AllowOvertime
              && m.Timer.Settings.AutoStartWithPresentation && m.Message.Presets.SequenceEqual(new[] { "Noch 2 Minuten" }));
        // Zurückschreiben: Alpha bleibt im Mac-Format
        string back = Path.Combine(macDir, "zurück.montro");
        m.SaveShow(back);
        Check("Mac: Alpha zurück als #RRGGBBAA", File.ReadAllText(back).Contains("\"#35294B80\""));

        report.Insert(0, $"Show-Dateien: {(failures == 0 ? "alles ok" : $"{failures} Fehler")}\n");
        File.WriteAllText(Path.Combine(dir, "bericht.txt"), report.ToString(), new UTF8Encoding(true));
        return failures == 0 ? 0 : 1;
    }
}
