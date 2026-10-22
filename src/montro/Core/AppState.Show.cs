using System.Text.Json;

namespace Montro.Core;

/// <summary>Show-Dateien öffnen und sichern (Mac: ShowFileIO).</summary>
public sealed partial class AppState
{
    private string? currentShowPath;

    /// <summary>Zuletzt geöffnete bzw. gesicherte Show-Datei.</summary>
    public string? CurrentShowPath
    {
        get => currentShowPath;
        private set
        {
            if (Set(ref currentShowPath, value))
            {
                Raise(nameof(CurrentShowName));
                QueueSave();
            }
        }
    }

    public string? CurrentShowName => CurrentShowPath == null ? null : Path.GetFileNameWithoutExtension(CurrentShowPath);

    /// <summary>Nicht während einer laufenden Präsentation (Mac: canReplaceShow).</summary>
    public string? ShowBlockReason => IsShowRunning || IsStarting
        ? "Während einer laufenden Präsentation nicht möglich – erst stoppen."
        : null;

    /// <summary>Show-Datei aus dem aktuellen Stand. <paramref name="paths"/>: Präsentation → Pfad, auf den sich die Show bezieht.</summary>
    private ShowFile MakeShow(string name, string showPath, Dictionary<Guid, string>? paths = null)
    {
        string baseDir = Path.GetDirectoryName(Path.GetFullPath(showPath))!;
        var used = Items.Where(i => Preset(i.PresetId) != null).Select(i => i.PresetId!.Value).ToHashSet();
        return new ShowFile
        {
            SavedBy = $"montro {AppInfo.Version} (Windows)",
            SavedAt = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
            Name = name,
            Presentations = Items.Select(i =>
            {
                string file = paths != null && paths.TryGetValue(i.Id, out var p) ? p : i.Path;
                long? size = File.Exists(file) ? new FileInfo(file).Length : null;
                return new ShowPresentation
                {
                    File = Path.GetFileName(file),
                    RelativePath = ShowFile.RelativePath(file, baseDir),
                    AbsolutePath = file.Replace('\\', '/'),   // Pfade mit „/“ (SHOW-FORMAT.md)
                    Title = i.Title,
                    Preset = Preset(i.PresetId) != null ? i.PresetId.ToString()!.ToUpperInvariant() : null,
                    Size = size,
                };
            }).ToList(),
            StandardLayout = ShowFile.ToFile(StandardLayout),
            // alle Presets (auch nicht zugewiesene), zugewiesene zuerst
            Presets = LayoutPresets.OrderBy(p => used.Contains(p.Id) ? 0 : 1)
                .Select(p => new ShowPreset { Id = p.Id.ToString().ToUpperInvariant(), Name = p.Name, Layout = ShowFile.ToFile(p.Layout) })
                .ToList(),
            Settings = new ShowSettings
            {
                EndBehavior = ShowFile.EndBehaviorName(EndBehavior),
                Transition = ShowFile.TransitionName(Transition),
                TransitionSeconds = TransitionDuration,
                Timer = ShowFile.ToFile(Timer.Settings),
                Message = new ShowMessage
                {
                    Presets = Message.Presets.ToList(),
                    Background = ShowFile.ToFileColor(Message.Background),
                    Foreground = ShowFile.ToFileColor(Message.Foreground),
                },
            },
        };
    }

    public void SaveShow(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        try
        {
            WriteShow(MakeShow(name, path), path);
            CurrentShowPath = path;
            StatusMessage = $"Show „{name}“ gesichert ({Items.Count} Präsentationen)";
            DebugLog.Write($"Show gesichert: {path}");
        }
        catch (Exception e)
        {
            ErrorRaised?.Invoke($"Show konnte nicht gesichert werden: {e.Message}");
        }
    }

    private static void WriteShow(ShowFile show, string path)
    {
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(show, ShowFile.Json), new System.Text.UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>
    /// Ordner <c>&lt;Name&gt;\&lt;Name&gt;.montro</c> mit Kopien aller Präsentationen in
    /// <c>&lt;Name&gt;\Präsentationen\</c> – für USB-Stick oder einen anderen Rechner.
    /// Gleichnamige Dateien bekommen „(2)“ angehängt. Gibt den Pfad der Show-Datei zurück.
    /// </summary>
    public async Task<string?> SaveShowWithPresentations(string folder)
    {
        string name = Path.GetFileName(folder.TrimEnd('\\', '/'));
        var items = Items.ToList();
        var paths = new Dictionary<Guid, string>();
        var missing = new List<string>();
        try
        {
            string media = Path.Combine(folder, "Präsentationen");
            Directory.CreateDirectory(media);
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int n = 0; n < items.Count; n++)
            {
                var item = items[n];
                if (!File.Exists(item.Path))
                {
                    missing.Add(Path.GetFileName(item.Path));
                    continue;
                }
                string fileName = Path.GetFileName(item.Path);
                for (int k = 2; usedNames.Contains(fileName); k++)
                    fileName = $"{Path.GetFileNameWithoutExtension(item.Path)} ({k}){Path.GetExtension(item.Path)}";
                usedNames.Add(fileName);
                string dest = Path.Combine(media, fileName);
                StatusMessage = $"Kopiere {n + 1}/{items.Count}: {fileName} …";
                await Task.Run(() => File.Copy(item.Path, dest, overwrite: true));
                paths[item.Id] = dest;
            }
            string showPath = Path.Combine(folder, name + ShowFile.Extension);
            WriteShow(MakeShow(name, showPath, paths), showPath);
            CurrentShowPath = showPath;
            StatusMessage = missing.Count == 0
                ? $"Show „{name}“ mit {paths.Count} Präsentationen gesichert"
                : $"Show „{name}“ gesichert – {missing.Count} Datei(en) fehlten: {string.Join(", ", missing)}";
            DebugLog.Write($"Show mit Präsentationen gesichert: {folder}");
            return showPath;
        }
        catch (Exception e)
        {
            ErrorRaised?.Invoke($"Show konnte nicht gesichert werden: {e.Message}");
            return null;
        }
    }

    /// <summary>Show-Datei öffnen: ersetzt Playlist, Standard-Ansicht und Show-Einstellungen; Presets werden ergänzt.</summary>
    public void OpenShow(string path)
    {
        if (ShowBlockReason is { } reason)
        {
            ErrorRaised?.Invoke(reason.Replace("nicht möglich", "kann keine andere Show geöffnet werden"));
            return;
        }
        ShowFile? show;
        try
        {
            show = JsonSerializer.Deserialize<ShowFile>(File.ReadAllText(path), ShowFile.Json);
        }
        catch (Exception e)
        {
            ErrorRaised?.Invoke($"„{Path.GetFileName(path)}“ ist keine lesbare montro-Show: {e.Message}");
            return;
        }
        if (show == null || show.Format != ShowFile.FormatId)
        {
            ErrorRaised?.Invoke($"„{Path.GetFileName(path)}“ ist keine montro-Show.");
            return;
        }
        if (show.Version > ShowFile.CurrentVersion)
            DebugLog.Write($"Show-Format {show.Version} ist neuer als {ShowFile.CurrentVersion} – lese, was bekannt ist");
        ApplyShow(show, path);
    }

    private void ApplyShow(ShowFile show, string path)
    {
        string baseDir = Path.GetDirectoryName(Path.GetFullPath(path))!;

        // Presets: gleiche ID → aktualisieren, neue ergänzen, andere vorhandene bleiben
        var presetMap = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in show.Presets)
        {
            var id = Guid.TryParse(p.Id, out var g) ? g : Guid.NewGuid();
            presetMap[p.Id] = id;
            var layout = ShowFile.FromFile(p.Layout);
            if (Preset(id) is { } existing)
            {
                existing.Name = p.Name;
                existing.Layout = layout;
                LayoutPresets[LayoutPresets.IndexOf(existing)] = existing;
            }
            else LayoutPresets.Add(new LayoutPreset { Id = id, Name = p.Name, Layout = layout });
        }

        // Playlist ersetzen; Analyse und Vorschaubilder bekannter Dateien behalten
        var old = Items.ToList();
        var items = new List<PresentationItem>();
        int missing = 0;
        foreach (var p in show.Presentations)
        {
            string? found = Resolve(p, baseDir, old);
            if (found == null) missing++;
            // nicht gefunden: Windows-Pfad des speichernden Rechners, sonst neben der Show (für „Fehlende Dateien suchen“)
            string file = found ?? (p.AbsolutePath is { } abs && abs.Contains(':') ? Path.GetFullPath(abs) : Path.Combine(baseDir, p.File));
            if (items.Any(i => string.Equals(i.Path, file, StringComparison.OrdinalIgnoreCase))) continue;
            var item = old.FirstOrDefault(i => string.Equals(i.Path, file, StringComparison.OrdinalIgnoreCase))
                       ?? new PresentationItem { Path = file };
            item.Title = p.Title.Length > 0 ? p.Title : Path.GetFileNameWithoutExtension(file);
            item.PresetId = p.Preset != null && presetMap.TryGetValue(p.Preset, out var pid) ? pid : null;
            if (found == null)
            {
                item.State = PrepState.Failed;
                item.StateText = "Datei nicht gefunden – „Fehlende Dateien suchen …“";
            }
            else if (item.State == PrepState.Failed) item.State = PrepState.Pending;
            items.Add(item);
        }
        Items.Clear();
        foreach (var i in items) Items.Add(i);
        Renumber();
        Selected = Items.FirstOrDefault();

        // Referent*innenansicht und Einstellungen
        standardLayout = ShowFile.FromFile(show.StandardLayout);
        SelectBuilderTarget(null);
        Layout = standardLayout.Clone();
        DraftLayout = Layout.Clone();
        var s = show.Settings;
        if (ShowFile.ParseEndBehavior(s.EndBehavior) is { } eb) EndBehavior = eb;
        if (ShowFile.ParseTransition(s.Transition) is { } tr) Transition = tr;
        TransitionDuration = s.TransitionSeconds;
        Timer.Settings = ShowFile.FromFile(s.Timer);
        Timer.Reset();
        if (s.Message.Presets.Count > 0)
        {
            Message.Presets.Clear();
            foreach (var m in s.Message.Presets) Message.Presets.Add(m);
        }
        Message.Background = ShowFile.FromFileColor(s.Message.Background, Message.Background);
        Message.Foreground = ShowFile.FromFileColor(s.Message.Foreground, Message.Foreground);

        CurrentShowPath = path;
        ShowLoaded?.Invoke();
        CleanOrphanThumbs();
        QueueSave();
        _ = PrepareAll();
        string msg = $"Show „{show.Name}“ geöffnet – {items.Count} Präsentationen, {show.Presets.Count} Preset(s)";
        StatusMessage = missing > 0 ? msg + $" · {missing} Datei(en) nicht gefunden" : msg;
        if (missing > 0)
            ErrorRaised?.Invoke($"{missing} Präsentation(en) der Show wurden nicht gefunden. Über „Show › Fehlende Dateien suchen …“ einen Ordner wählen.");
        DebugLog.Write($"Show geöffnet: {path} ({items.Count} Präsentationen, {missing} fehlen, gespeichert von {show.SavedBy})");
    }

    /// <summary>Nach dem Öffnen einer Show: Oberfläche lädt Timer- und Übergangs-Einstellungen neu.</summary>
    public event Action? ShowLoaded;

    /// <summary>
    /// Datei finden: relativ zur Show → absoluter Pfad → neben der Show → Unterordner
    /// „Präsentationen“ → gleichnamig in der bisherigen Playlist (Mac: resolve).
    /// </summary>
    private static string? Resolve(ShowPresentation p, string baseDir, List<PresentationItem> previous)
    {
        var candidates = new List<string>();
        if (p.RelativePath is { } rel)
            candidates.Add(Path.GetFullPath(Path.Combine(baseDir, rel.Replace('/', Path.DirectorySeparatorChar))));
        if (p.AbsolutePath is { } abs && abs.Contains(':') && Path.IsPathFullyQualified(abs)) candidates.Add(Path.GetFullPath(abs));
        candidates.Add(Path.Combine(baseDir, p.File));
        candidates.Add(Path.Combine(baseDir, "Präsentationen", p.File));
        if (previous.FirstOrDefault(i => ShowFile.Key(Path.GetFileName(i.Path)) == ShowFile.Key(p.File)) is { } same)
            candidates.Add(same.Path);
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>Fehlende Dateien in einem Ordner (mit Unterordnern) nach Dateinamen suchen.</summary>
    public async Task LocateMissing(string folder)
    {
        var missing = Items.Where(i => !File.Exists(i.Path)).ToList();
        if (missing.Count == 0)
        {
            StatusMessage = "Alle Dateien sind vorhanden.";
            return;
        }
        var wanted = missing.Select(i => ShowFile.Key(Path.GetFileName(i.Path))).ToHashSet();
        StatusMessage = "Suche fehlende Dateien …";
        var found = await Task.Run(() =>
        {
            var result = new Dictionary<string, string>();
            var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
            foreach (var f in Directory.EnumerateFiles(folder, "*", opts))
            {
                string k = ShowFile.Key(Path.GetFileName(f));
                if (wanted.Contains(k)) result.TryAdd(k, f);
                if (result.Count == wanted.Count) break;
            }
            return result;
        });
        int n = 0;
        foreach (var item in missing)
        {
            if (!found.TryGetValue(ShowFile.Key(Path.GetFileName(item.Path)), out var f)) continue;
            item.Path = f;
            item.Meta = null;
            item.Thumbnails = new();
            item.State = PrepState.Pending;
            item.StateText = "";
            n++;
        }
        QueueSave();
        _ = PrepareAll();
        StatusMessage = $"{n} von {missing.Count} fehlenden Dateien gefunden";
    }
}
