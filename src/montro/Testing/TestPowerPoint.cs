using System.Text;
using Montro.Core;
using Montro.Display;
using Montro.PowerPoint;

namespace Montro.Testing;

/// <summary>
/// Zweite, unabhängige COM-Verbindung zu PowerPoint für den Selbsttest: Testpräsentation
/// erzeugen, „fremde“ Shows starten, einen Dialog öffnen, aufräumen. Läuft auf einem eigenen
/// STA-Thread, damit der Prüfling (<see cref="PowerPointHost"/>) davon nichts mitbekommt.
/// </summary>
public sealed class TestPowerPoint : IDisposable
{
    private const int MsoTrue = -1;
    private const int MsoFalse = 0;

    /// <summary>Hintergrund sichtbarer Folien: Cloud Purple #B088FB.</summary>
    public static readonly System.Windows.Media.Color VisibleColor = System.Windows.Media.Color.FromRgb(0xB0, 0x88, 0xFB);
    /// <summary>Hintergrund ausgeblendeter Folien: Live-Rot #F2465A (fällt sofort auf).</summary>
    public static readonly System.Windows.Media.Color HiddenColor = System.Windows.Media.Color.FromRgb(0xF2, 0x46, 0x5A);

    private readonly StaWorker sta = new("montro-Selbsttest");
    private dynamic? app;
    private readonly List<dynamic> foreign = new();

    private dynamic App()
    {
        if (app != null) return app;
        var type = Type.GetTypeFromProgID("PowerPoint.Application")
            ?? throw new PowerPointException("Microsoft PowerPoint ist nicht installiert.");
        app = Activator.CreateInstance(type)!;
        return app;
    }

    private static int Rgb(System.Windows.Media.Color c) => c.R | (c.G << 8) | (c.B << 16);

    /// <summary>Anzahl laufender Bildschirmpräsentationen (egal von wem).</summary>
    public Task<int> CountShowsAsync() => sta.Run(() =>
    {
        dynamic a = App();
        return (int)a.SlideShowWindows.Count;
    }, 30000, "Selbsttest: Shows zählen");

    /// <summary>
    /// Testpräsentation 16:9 mit 6 Folien: 3 und 5 ausgeblendet (rot), Folie 2 mit zwei
    /// Klick-Animationen, sonst lila Hintergrund und große Foliennummer.
    /// </summary>
    public Task CreateDeckAsync(string path) => sta.Run(() =>
    {
        dynamic a = App();
        dynamic p = a.Presentations.Add(MsoFalse);
        try
        {
            p.PageSetup.SlideWidth = 960f;
            p.PageSetup.SlideHeight = 540f;
            for (int i = 1; i <= 6; i++)
            {
                bool hidden = i == 3 || i == 5;
                dynamic s = p.Slides.Add(i, 12);   // ppLayoutBlank
                s.FollowMasterBackground = MsoFalse;
                s.Background.Fill.Solid();
                s.Background.Fill.ForeColor.RGB = Rgb(hidden ? HiddenColor : VisibleColor);
                dynamic tb = s.Shapes.AddTextbox(1, 0f, 150f, 960f, 200f);
                dynamic tr = tb.TextFrame.TextRange;
                tr.Text = hidden ? $"Folie {i} (ausgeblendet)" : $"Folie {i}";
                tr.Font.Size = 96;
                tr.Font.Color.RGB = 0xFFFFFF;
                tr.ParagraphFormat.Alignment = 2;   // ppAlignCenter
                if (i == 2)
                {
                    for (int k = 0; k < 2; k++)
                    {
                        dynamic box = s.Shapes.AddShape(1, 230f + k * 300f, 380f, 200f, 80f);   // Rechteck
                        box.Fill.ForeColor.RGB = 0x41F1B2;   // Bass Lime (BGR)
                        box.TextFrame.TextRange.Text = $"Klick {k + 1}";
                        box.TextFrame.TextRange.Font.Size = 28;
                        box.TextFrame.TextRange.Font.Color.RGB = 0x202020;
                        s.TimeLine.MainSequence.AddEffect(box, 10, 0, 1);   // Verblassen, bei Klick
                    }
                }
                if (hidden) s.SlideShowTransition.Hidden = MsoTrue;
            }
            p.SaveAs(path, 24);   // ppSaveAsOpenXMLPresentation
        }
        finally
        {
            p.Close();
        }
    }, 60000, "Selbsttest: Testpräsentation erzeugen");

    /// <summary>
    /// Testpräsentation für den Parser: Notizen mit mehreren Absätzen, Klick-Effekte gemischt mit
    /// „Mit vorherigem“/„Nach vorherigem“ (zählen nicht als Klick), eine ausgeblendete Folie und eine
    /// Trigger-Animation (interaktive Sequenz, zählt nicht).
    /// </summary>
    public Task CreateParserDeckAsync(string path) => sta.Run(() =>
    {
        dynamic a = App();
        dynamic p = a.Presentations.Add(MsoFalse);
        try
        {
            dynamic Box(dynamic slide, string text, float x)
            {
                dynamic b = slide.Shapes.AddShape(1, x, 200f, 180f, 80f);
                b.TextFrame.TextRange.Text = text;
                return b;
            }
            void Notes(dynamic slide, string text)
            {
                dynamic ph = slide.NotesPage.Shapes.Placeholders;
                for (int k = 1; k <= (int)ph.Count; k++)
                {
                    dynamic s = ph.Item(k);
                    if ((int)s.PlaceholderFormat.Type == 2) s.TextFrame.TextRange.Text = text;
                }
            }

            // 1: zwei Klicks, dazwischen „Mit vorherigem“ und „Nach vorherigem“
            dynamic s1 = p.Slides.Add(1, 12);
            dynamic seq1 = s1.TimeLine.MainSequence;
            seq1.AddEffect(Box(s1, "A", 20f), 10, 0, 1);    // Klick
            seq1.AddEffect(Box(s1, "B", 220f), 10, 0, 2);   // mit vorherigem
            seq1.AddEffect(Box(s1, "C", 420f), 10, 0, 3);   // nach vorherigem
            seq1.AddEffect(Box(s1, "D", 620f), 10, 0, 1);   // Klick
            Notes(s1, "Erster Absatz mit Umlauten: äöü ß\rZweiter Absatz\vmit Zeilenumbruch");

            // 2: ausgeblendet, mit Notiz
            dynamic s2 = p.Slides.Add(2, 12);
            Box(s2, "versteckt", 20f);
            Notes(s2, "Notiz auf ausgeblendeter Folie");
            s2.SlideShowTransition.Hidden = MsoTrue;

            // 3: ein Klick plus eine Trigger-Animation (Klick auf Form startet Effekt)
            dynamic s3 = p.Slides.Add(3, 12);
            dynamic trigger = Box(s3, "Auslöser", 20f);
            dynamic target = Box(s3, "Ziel", 420f);
            s3.TimeLine.MainSequence.AddEffect(Box(s3, "Klick", 220f), 10, 0, 1);
            dynamic inter = s3.TimeLine.InteractiveSequences.Add();
            inter.AddTriggerEffect(target, 10, 4, trigger);   // msoAnimTriggerOnShapeClick

            // 4: ohne alles
            p.Slides.Add(4, 12);

            p.SaveAs(path, 24);
        }
        finally
        {
            p.Close();
        }
    }, 60000, "Selbsttest: Parser-Testpräsentation erzeugen");

    /// <summary>Eine weitere Show außerhalb des Prüflings starten (simuliert eine verwaiste Show).</summary>
    public Task StartForeignShowAsync(string path) => sta.Run(() =>
    {
        dynamic a = App();
        dynamic p = a.Presentations.Open(path, MsoTrue, MsoFalse, MsoFalse);
        foreign.Add(p);
        dynamic ss = p.SlideShowSettings;
        try { ss.ShowPresenterView = MsoFalse; } catch { }
        ss.Run();
    }, 30000, "Selbsttest: fremde Show starten");

    /// <summary>Selbst geöffnete Präsentationen und Geschützte-Ansicht-Fenster schließen.</summary>
    public Task CleanupAsync() => sta.Run(() =>
    {
        if (app == null) return;
        dynamic a = app;
        foreach (var p in foreign)
        {
            try { p.Close(); } catch { }
        }
        foreign.Clear();
        int pv = 0;
        try { pv = (int)a.ProtectedViewWindows.Count; } catch { }
        for (int i = pv; i >= 1; i--)
        {
            try { a.ProtectedViewWindows.Item(i).Close(); } catch { }
        }
    }, 15000, "Selbsttest: aufräumen");

    /// <summary>Was PowerPoint selbst über eine Folie sagt – Vergleichswert für den PPTX-Parser.</summary>
    public sealed record ReferenceSlide(bool Hidden, string Notes, int ClickCount, IReadOnlyList<(string Name, double Seconds)> Videos);

    /// <summary>Präsentation schreibgeschützt öffnen und je Folie Ausblendung, Notizen, Klicks und Videos lesen.</summary>
    public Task<List<ReferenceSlide>> ReadReferenceAsync(string path) => sta.Run(() =>
    {
        dynamic a = App();
        dynamic p = a.Presentations.Open(path, MsoTrue, MsoFalse, MsoFalse);
        try
        {
            var list = new List<ReferenceSlide>();
            int n = (int)p.Slides.Count;
            for (int i = 1; i <= n; i++)
            {
                dynamic s = p.Slides.Item(i);
                bool hidden = (int)s.SlideShowTransition.Hidden == MsoTrue;

                string notes = "";
                try
                {
                    dynamic ph = s.NotesPage.Shapes.Placeholders;
                    int pc = (int)ph.Count;
                    var parts = new List<string>();
                    for (int k = 1; k <= pc; k++)
                    {
                        dynamic shp = ph.Item(k);
                        if ((int)shp.PlaceholderFormat.Type == 2 && (int)shp.HasTextFrame == MsoTrue)   // ppPlaceholderBody
                            parts.Add(((string)shp.TextFrame.TextRange.Text).Trim());
                    }
                    notes = string.Join("\n\n", parts.Where(x => x.Length > 0));
                }
                catch { }

                int clicks = 0;
                dynamic seq = s.TimeLine.MainSequence;
                int ec = (int)seq.Count;
                for (int k = 1; k <= ec; k++)
                {
                    if ((int)seq.Item(k).Timing.TriggerType == 1) clicks++;   // msoAnimTriggerOnPageClick
                }

                var videos = new List<(string, double)>();
                void Collect(dynamic shapes)
                {
                    int sc = (int)shapes.Count;
                    for (int k = 1; k <= sc; k++)
                    {
                        dynamic sh = shapes.Item(k);
                        int type = (int)sh.Type;
                        if (type == 6) { Collect(sh.GroupItems); continue; }   // msoGroup
                        if (type == 16 && (int)sh.MediaType == 3)              // msoMedia, ppMediaTypeMovie
                        {
                            double sec = 0;
                            try { sec = (int)sh.MediaFormat.Length / 1000.0; } catch { }
                            videos.Add(((string)sh.Name, sec));
                        }
                    }
                }
                Collect(s.Shapes);
                list.Add(new ReferenceSlide(hidden, notes, clicks, videos));
            }
            return list;
        }
        finally
        {
            p.Close();
        }
    }, 300000, "Selbsttest: Vergleichswerte lesen");

    /// <summary>Anzahl Fenster in der Geschützten Ansicht.</summary>
    public Task<int> ProtectedViewCountAsync() => sta.Run(() =>
    {
        dynamic a = App();
        return (int)a.ProtectedViewWindows.Count;
    }, 10000, "Selbsttest: Geschützte Ansicht zählen");

    /// <summary>
    /// Öffnet in PowerPoint einen modalen Dialog („Datei öffnen“) auf einem eigenen Thread –
    /// der Aufruf blockiert, bis der Dialog zu ist. Mit <see cref="CloseDialogs"/> schließen.
    /// </summary>
    public static Thread OpenModalDialog()
    {
        var t = new Thread(() =>
        {
            try
            {
                var type = Type.GetTypeFromProgID("PowerPoint.Application")!;
                dynamic a = Activator.CreateInstance(type)!;
                dynamic fd = a.FileDialog(1);   // msoFileDialogOpen
                fd.Show();
            }
            catch (Exception e)
            {
                DebugLog.Write($"Selbsttest: Dialog – {StaWorker.Describe(e)}");
            }
        })
        { IsBackground = true, Name = "montro-Selbsttest-Dialog" };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        return t;
    }

    /// <summary>Sichtbare Dialogfenster (#32770) von PowerPoint per WM_CLOSE schließen.</summary>
    public static int CloseDialogs()
    {
        var pids = System.Diagnostics.Process.GetProcessesByName("POWERPNT").Select(p => (uint)p.Id).ToHashSet();
        int closed = 0;
        Native.EnumWindows((h, _) =>
        {
            Native.GetWindowThreadProcessId(h, out var pid);
            if (!pids.Contains(pid) || !Native.IsWindowVisible(h)) return true;
            var cls = new StringBuilder(64);
            Native.GetClassName(h, cls, cls.Capacity);
            if (cls.ToString() == "#32770")
            {
                Native.PostMessage(h, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                closed++;
            }
            return true;
        }, IntPtr.Zero);
        return closed;
    }

    public void Dispose()
    {
        app = null;
        sta.Dispose();
    }
}
