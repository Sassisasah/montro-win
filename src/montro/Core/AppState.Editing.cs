using Montro.Display;
using Montro.Pptx;

namespace Montro.Core;

/// <summary>
/// Bearbeiten in letzter Minute (Mac: FileSync, ohne Verbund): „In PowerPoint bearbeiten“ öffnet
/// die Datei in PowerPoint; ein Dateiwächter erneuert Analyse und Vorschaubilder, sobald sie
/// gespeichert wurde. Die laufende Präsentation wird erst nach dem Stopp erneuert.
/// </summary>
public sealed partial class AppState
{
    private readonly record struct FileStamp(long Size, DateTime Modified);

    private readonly Dictionary<Guid, FileStamp> baselines = new();
    private readonly Dictionary<Guid, FileStamp> candidates = new();
    private readonly HashSet<Guid> refreshAfterShow = new();
    private int watchTick;

    /// <summary>Bearbeiten nicht während einer Show (PowerPoint holt sich sonst ständig den Fokus).</summary>
    public bool CanEdit => !IsShowRunning && !IsStarting;

    public string EditHint => CanEdit
        ? "In PowerPoint bearbeiten – nach dem Speichern erneuert montro Analyse und Vorschaubilder."
        : "Während einer laufenden Präsentation nicht möglich – erst stoppen.";

    public async void EditInPowerPoint(PresentationItem item)
    {
        if (!CanEdit)
        {
            StatusMessage = EditHint;
            return;
        }
        if (!File.Exists(item.Path))
        {
            ErrorRaised?.Invoke($"Die Datei „{Path.GetFileName(item.Path)}“ wurde nicht gefunden.");
            return;
        }
        try
        {
            StatusMessage = $"Öffne „{item.Title}“ in PowerPoint …";
            await ppt.EditAsync(item.Path);
            BringEditorToFront(Path.GetFileNameWithoutExtension(item.Path));
            StatusMessage = $"„{item.Title}“ ist in PowerPoint geöffnet – nach dem Speichern (Strg+S) erneuert montro Analyse und Vorschaubilder.";
        }
        catch (Exception e)
        {
            StatusMessage = ExplainPowerPointError(e);
            DebugLog.Write($"Bearbeiten: {StatusMessage}");
        }
    }

    /// <summary>
    /// Bearbeitungsfenster nach vorne holen. Liegt es auf einem Ausgabebildschirm (dort liegen ohne
    /// Show die Schwarzbilder darüber), kommt es auf den Hauptbildschirm (Mac: bringEditorToFront).
    /// </summary>
    private static void BringEditorToFront(string title)
    {
        var hwnd = Monitors.FindProcessWindow("POWERPNT", "PPTFrameClass", title);
        if (hwnd == IntPtr.Zero) return;
        var main = Monitors.All().FirstOrDefault(m => m.IsPrimary);
        if (main != null && Monitors.WindowRect(hwnd) is { } r)
        {
            int cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            var b = main.Bounds;
            if (cx < b.X || cx >= b.Right || cy < b.Y || cy >= b.Bottom)
            {
                Native.ShowWindow(hwnd, Native.SW_RESTORE);
                var wa = main.WorkArea;
                int w = wa.Width * 4 / 5, h = wa.Height * 4 / 5;
                Monitors.MoveWindow(hwnd, new PixelRect(wa.X + (wa.Width - w) / 2, wa.Y + (wa.Height - h) / 2, w, h));
                DebugLog.Write("Bearbeiten: PowerPoint-Fenster auf den Hauptbildschirm geholt");
            }
        }
        Native.SetForegroundWindow(hwnd);
    }

    private static FileStamp? Stamp(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            return fi.Exists ? new FileStamp(fi.Length, fi.LastWriteTimeUtc) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Dateiwächter, alle 2 s aus dem Takt: Geänderte Dateien (zwei Prüfungen lang gleich, damit
    /// PowerPoint fertig gespeichert hat) neu analysieren. Die Live-Präsentation erst nach dem Stopp.
    /// </summary>
    private void WatchFiles()
    {
        if (++watchTick % 8 != 0 || headless) return;
        foreach (var item in Items.ToList())
        {
            if (Stamp(item.Path) is not { } s) continue;
            if (!baselines.TryGetValue(item.Id, out var b))
            {
                baselines[item.Id] = s;
                continue;
            }
            if (b == s)
            {
                candidates.Remove(item.Id);
                continue;
            }
            if (!candidates.TryGetValue(item.Id, out var c) || c != s)
            {
                candidates[item.Id] = s;
                continue;
            }
            candidates.Remove(item.Id);
            baselines[item.Id] = s;
            if (item == LiveItem && (IsShowRunning || IsStarting))
            {
                refreshAfterShow.Add(item.Id);
                DebugLog.Write($"Datei geändert: „{Path.GetFileName(item.Path)}“ – läuft gerade, wird nach dem Stopp erneuert");
                continue;
            }
            DebugLog.Write($"Datei geändert: „{Path.GetFileName(item.Path)}“ → neu analysieren");
            StatusMessage = $"„{item.Title}“ wurde geändert – Analyse und Vorschaubilder werden erneuert";
            Regenerate(item);
        }
    }

    /// <summary>Nach dem Stopp: während der Show gespeicherte Änderungen übernehmen.</summary>
    private void RefreshAfterShow()
    {
        foreach (var id in refreshAfterShow.ToList())
        {
            refreshAfterShow.Remove(id);
            if (Items.FirstOrDefault(i => i.Id == id) is { } item) Regenerate(item);
        }
    }

    /// <summary>
    /// Vor dem Live-Schalten: frisch gespeicherte Änderungen sofort analysieren (der Wächter hätte
    /// erst ein paar Sekunden später reagiert). Vorschaubilder folgen nach der Show.
    /// </summary>
    private async Task RefreshIfChanged(PresentationItem item)
    {
        if (Stamp(item.Path) is not { } s || (baselines.TryGetValue(item.Id, out var b) && b == s && item.Meta != null)) return;
        if (!baselines.ContainsKey(item.Id) && item.Meta != null)
        {
            baselines[item.Id] = s;
            return;
        }
        DebugLog.Write($"Live: „{Path.GetFileName(item.Path)}“ wurde geändert → Analyse vor dem Start");
        try { item.Meta = await Task.Run(() => PptxParser.Parse(item.Path)); }
        catch (Exception e) { DebugLog.Write($"Analyse vor dem Start fehlgeschlagen – {e.Message}"); }
        baselines[item.Id] = s;
        candidates.Remove(item.Id);
        refreshAfterShow.Add(item.Id);
    }
}
