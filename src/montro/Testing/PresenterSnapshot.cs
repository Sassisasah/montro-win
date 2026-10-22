using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Montro.Core;
using Montro.Pptx;
using Montro.Views;

namespace Montro.Testing;

/// <summary>
/// Testbilder der Referent*innenansicht ohne PowerPoint und ohne Ausgabefenster:
/// <c>montro.exe --referentenbild &lt;Zielordner&gt; &lt;Datei.pptx&gt; [Vorschaubild-Ordner]</c>.
/// Schreibt PNGs (Vollbild 1920×1080 und Popup-Größe) für den Vergleich mit dem Mac.
/// </summary>
public static class PresenterSnapshot
{
    public static int Run(string[] args)
    {
        int i = Array.IndexOf(args, "--referentenbild");
        var rest = args.Skip(i + 1).Where(a => !a.StartsWith("--")).ToList();
        if (rest.Count < 2) return 2;
        string outDir = Path.GetFullPath(rest[0]);
        string pptx = Path.GetFullPath(rest[1]);
        string? thumbs = rest.Count > 2 ? Path.GetFullPath(rest[2]) : null;
        Directory.CreateDirectory(outDir);

        var item = new PresentationItem { Path = pptx, Title = Path.GetFileNameWithoutExtension(pptx), Meta = PptxParser.Parse(pptx) };
        if (thumbs != null && Directory.Exists(thumbs))
            item.Thumbnails = item.Slides.Where(s => File.Exists(Path.Combine(thumbs, $"slide-{s.Index}.png")))
                .ToDictionary(s => s.Index, s => Path.Combine(thumbs, $"slide-{s.Index}.png"));

        // Folie mit Notizen und Animationen bevorzugen, damit alle Bausteine etwas zeigen
        var slide = item.Slides.FirstOrDefault(s => s.Notes.Length > 0 && s.ClickCount > 0 && !s.Hidden)
                    ?? item.Slides.FirstOrDefault(s => s.Notes.Length > 0) ?? item.Slides.First();
        var state = AppState.ForPreview(item, slide.Index, Math.Min(1, slide.ClickCount));
        state.Timer.SetDuration(15);
        state.Timer.Start();
        state.Message.Send("Noch 5 Minuten");

        Render(new PresenterOutputView(state), 1920, 1080, Path.Combine(outDir, "referent-vollbild.png"));
        Render(new PresenterOutputView(state), 1120, 660, Path.Combine(outDir, "referent-popup.png"));
        var last = AppState.ForPreview(item, item.LastVisible, 0);
        Render(new PresenterOutputView(last), 1920, 1080, Path.Combine(outDir, "referent-letzte-folie.png"));
        var idle = AppState.ForPreview(item, 1, 0);
        idle.SimulateStopped();
        Render(new PresenterOutputView(idle), 1920, 1080, Path.Combine(outDir, "referent-nicht-live.png"));
        DebugLog.Write($"Referent*innenansicht: Testbilder in {outDir} (Folie {slide.Index})");
        return 0;
    }

    private static void Render(FrameworkElement view, int w, int h, string file)
    {
        view.Measure(new Size(w, h));
        view.Arrange(new Rect(0, 0, w, h));
        view.UpdateLayout();
        (view as PresenterOutputView)?.Refresh();
        view.UpdateLayout();
        var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(view);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var f = File.Create(file);
        enc.Save(f);
    }
}
