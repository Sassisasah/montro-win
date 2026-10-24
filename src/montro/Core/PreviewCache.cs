using System.Text.Json;
using System.Text.Json.Serialization;

namespace Montro.Core;

/// <summary>
/// Analyse und Vorschaubilder nach Dateiinhalt (SHA-256) statt nach Playlist-Eintrag, je Eintrag ein
/// Ordner &lt;Prüfsumme&gt;\windows\ mit analyse.json und slide-&lt;n&gt;.jpg (der Mac legt daneben
/// &lt;Prüfsumme&gt;/mac/ an – beide rendern Schriften unterschiedlich). Gelagert wird bei der Show
/// (Ordner „montro-Analyse“ neben der .montro-Datei) – so reisen die Daten mit der Show auf andere
/// Rechner und verschwinden mit ihr. Nur ohne gesicherte Show oder bei schreibgeschütztem
/// Show-Ordner (USB-Stick) dient %LOCALAPPDATA%\montro\cache als Ausweiche, höchstens 1 GB.
/// </summary>
public static class PreviewCache
{
    /// <summary>Ordner neben der Show-Datei.</summary>
    public const string ShowFolderName = "montro-Analyse";

    /// <summary>Lokale Ausweiche: nicht benutzte Einträge werden nach so vielen Tagen gelöscht …</summary>
    public const int MaxAgeDays = 60;

    /// <summary>… und die ältesten davon, solange die Ausweiche größer ist.</summary>
    public const long MaxBytes = 1L * 1024 * 1024 * 1024;

    private const string AnalysisFile = "analyse.json";

    private const string Platform = "windows";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        // gleiche Schlüssel wie analyse.json am Mac (sha256, size, format …)
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly Dictionary<string, bool> writable = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Lokale Ausweiche.</summary>
    public static string LocalRoot => Ensure(Path.Combine(AppPaths.LocalDir, "cache"));

    /// <summary>Lager neben der Show-Datei, null ohne Show.</summary>
    public static string? ShowStore(string? showPath) =>
        string.IsNullOrEmpty(showPath) ? null
            : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(showPath))!, ShowFolderName);

    /// <summary>Wohin neue Einträge kommen: zur Show, wenn deren Ordner beschreibbar ist, sonst lokal.</summary>
    public static string RootFor(string? showStore) =>
        showStore != null && IsWritable(showStore) ? showStore : LocalRoot;

    public static string DirFor(string hash, string? showStore) => Entry(RootFor(showStore), hash);

    private static string Entry(string root, string hash) => Path.Combine(root, hash, Platform);

    /// <summary>
    /// Analyse und Vorschaubilder suchen: erst bei der Show, dann lokal. Ein lokaler Treffer wird zur
    /// Show kopiert (falls beschreibbar), damit er mit ihr reist. null = nicht (vollständig) vorhanden.
    /// </summary>
    public static (PresentationMeta Meta, Dictionary<int, string> Thumbnails)? TryLoad(string hash, string? showStore)
    {
        if (showStore != null && Read(Entry(showStore, hash), hash) is { } atShow) return atShow;
        if (Read(Entry(LocalRoot, hash), hash) is not { } local) return null;
        if (showStore == null || !IsWritable(showStore) || !Copy(Entry(LocalRoot, hash), Entry(showStore, hash))) return local;
        if (Read(Entry(showStore, hash), hash) is not { } moved) return local;
        // lokal nur Ausweiche: was bei der Show liegt, dort nicht doppelt halten
        try { Directory.Delete(Path.Combine(LocalRoot, hash), recursive: true); } catch { }
        return moved;
    }

    private static (PresentationMeta Meta, Dictionary<int, string> Thumbnails)? Read(string dir, string hash)
    {
        try
        {
            string file = Path.Combine(dir, AnalysisFile);
            if (!File.Exists(file)) return null;
            var a = JsonSerializer.Deserialize<CachedAnalysis>(File.ReadAllText(file), Options);
            if (a?.Meta == null || a.Sha256 != hash || a.Platform != Platform || a.Slides.Count == 0) return null;
            var thumbs = a.Slides.ToDictionary(i => i, i => Path.Combine(dir, $"slide-{i}.{a.Image}"));
            if (!thumbs.Values.All(File.Exists)) return null;
            Touch(Path.GetDirectoryName(dir)!);
            return (a.Meta, thumbs);
        }
        catch (Exception e)
        {
            DebugLog.Write($"Vorschau-Cache: {dir} nicht lesbar – {e.Message}");
            return null;
        }
    }

    /// <summary>Analyse zu den bereits in <paramref name="dir"/> liegenden Vorschaubildern schreiben.</summary>
    public static void Save(string dir, string hash, long size, string sourcePath, PresentationMeta meta, Dictionary<int, string> thumbnails)
    {
        var a = new CachedAnalysis
        {
            Sha256 = hash,
            Size = size,
            Format = Path.GetExtension(sourcePath).TrimStart('.').ToLowerInvariant(),
            FileName = Path.GetFileName(sourcePath),
            Platform = Platform,
            Created = DateTime.UtcNow,
            Meta = meta,
            Slides = thumbnails.Keys.Order().ToList(),
            Image = thumbnails.Values.Select(p => Path.GetExtension(p).TrimStart('.').ToLowerInvariant()).FirstOrDefault() ?? "jpg",
        };
        Ensure(dir);
        string tmp = Path.Combine(dir, AnalysisFile + ".tmp");
        File.WriteAllText(tmp, JsonSerializer.Serialize(a, Options));
        File.Move(tmp, Path.Combine(dir, AnalysisFile), overwrite: true);
    }

    /// <summary>
    /// Vorschaubilder aus dem alten Ablageort (thumbs\&lt;ID&gt;) übernehmen, damit nach dem Update
    /// nichts neu gerendert werden muss.
    /// </summary>
    public static Dictionary<int, string>? Adopt(string hash, string? showStore, long size, string sourcePath, PresentationMeta meta, Dictionary<int, string> thumbnails)
    {
        try
        {
            if (TryLoad(hash, showStore) is { } cached) return cached.Thumbnails;
            string dir = Ensure(DirFor(hash, showStore));
            var result = new Dictionary<int, string>();
            foreach (var (slide, src) in thumbnails)
            {
                string dst = Path.Combine(dir, $"slide-{slide}{Path.GetExtension(src).ToLowerInvariant()}");
                File.Copy(src, dst, overwrite: true);
                result[slide] = dst;
            }
            Save(dir, hash, size, sourcePath, meta, result);
            return result;
        }
        catch (Exception e)
        {
            DebugLog.Write($"Vorschau-Cache: Übernahme von „{Path.GetFileName(sourcePath)}“ fehlgeschlagen – {e.Message}");
            return null;
        }
    }

    /// <summary>Eintrag verwerfen („Neu analysieren & Vorschaubilder erzeugen“), bei der Show und lokal.</summary>
    public static void Remove(string hash, string? showStore)
    {
        if (showStore != null) try { Directory.Delete(Entry(showStore, hash), recursive: true); } catch { }
        try { Directory.Delete(Entry(LocalRoot, hash), recursive: true); } catch { }
    }

    /// <summary>
    /// Nach dem Sichern einer Show: Einträge der Playlist zur Show holen und dort alles löschen, was
    /// keine .montro-Datei in diesem Ordner mehr braucht (mehrere Shows dürfen sich den Ordner teilen).
    /// Gibt die neuen Vorschaubild-Pfade je Prüfsumme zurück.
    /// </summary>
    public static Dictionary<string, Dictionary<int, string>> SyncShowStore(string showPath, IEnumerable<string> inPlaylist)
    {
        var moved = new Dictionary<string, Dictionary<int, string>>();
        if (ShowStore(showPath) is not { } store || !IsWritable(store)) return moved;
        var wanted = inPlaylist.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var hash in wanted)
        {
            if (Read(Entry(store, hash), hash) != null) continue;
            if (TryLoad(hash, store) is { } entry) moved[hash] = entry.Thumbnails;
        }
        // was andere Shows im selben Ordner brauchen
        var keep = new HashSet<string>(wanted, StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var other in Directory.EnumerateFiles(Path.GetDirectoryName(store)!, "*" + ShowFile.Extension))
            {
                try
                {
                    var show = JsonSerializer.Deserialize<ShowFile>(File.ReadAllText(other), ShowFile.Json);
                    foreach (var p in show?.Presentations ?? new()) if (p.Sha256 is { } h) keep.Add(h);
                }
                catch { }
            }
            int removed = 0;
            foreach (var dir in Directory.GetDirectories(store))
            {
                if (keep.Contains(Path.GetFileName(dir))) continue;
                Directory.Delete(dir, recursive: true);
                removed++;
            }
            if (removed > 0) DebugLog.Write($"Vorschau-Cache: {removed} nicht mehr benutzte Einträge bei der Show gelöscht");
        }
        catch (Exception e)
        {
            DebugLog.Write($"Vorschau-Cache: Aufräumen bei der Show fehlgeschlagen – {e.Message}");
        }
        return moved;
    }

    /// <summary>
    /// Lokale Ausweiche aufräumen: Einträge, die keine Playlist mehr benutzt, nach <see cref="MaxAgeDays"/>
    /// Tagen löschen, und die ältesten davon, solange sie größer als <see cref="MaxBytes"/> ist.
    /// </summary>
    public static void Clean(IEnumerable<string?> inUse)
    {
        try
        {
            var keep = inUse.OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
            var unused = new DirectoryInfo(LocalRoot).GetDirectories()
                .Where(d => !keep.Contains(d.Name))
                .Select(d => (Dir: d, Bytes: d.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length)))
                .OrderBy(e => e.Dir.LastWriteTimeUtc)
                .ToList();
            long total = new DirectoryInfo(LocalRoot).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
            var cutoff = DateTime.UtcNow.AddDays(-MaxAgeDays);
            int removed = 0;
            foreach (var (d, bytes) in unused)
            {
                if (d.LastWriteTimeUtc >= cutoff && total <= MaxBytes) continue;
                d.Delete(recursive: true);
                total -= bytes;
                removed++;
            }
            if (removed > 0) DebugLog.Write($"Vorschau-Cache: {removed} alte Einträge gelöscht, lokal jetzt {total / (1024 * 1024)} MB");
        }
        catch (Exception e)
        {
            DebugLog.Write($"Vorschau-Cache: Aufräumen fehlgeschlagen – {e.Message}");
        }
    }

    private static bool Copy(string from, string to)
    {
        try
        {
            string tmp = to + ".tmp";
            Ensure(tmp);
            foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(tmp, Path.GetFileName(f)), overwrite: true);
            if (Directory.Exists(to)) Directory.Delete(to, recursive: true);
            Directory.Move(tmp, to);
            return true;
        }
        catch (Exception e)
        {
            DebugLog.Write($"Vorschau-Cache: Kopie zur Show fehlgeschlagen – {e.Message}");
            return false;
        }
    }

    /// <summary>Show-Ordner beschreibbar? (USB-Stick mit Schreibschutz, Netzlaufwerk nur lesend …)</summary>
    private static bool IsWritable(string store)
    {
        lock (writable)
            if (writable.TryGetValue(store, out var known)) return known;
        bool ok;
        try
        {
            Directory.CreateDirectory(store);
            string probe = Path.Combine(store, $".probe-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            ok = true;
        }
        catch
        {
            ok = false;
            DebugLog.Write($"Vorschau-Cache: {store} nicht beschreibbar – lokale Ausweiche");
        }
        lock (writable) writable[store] = ok;
        return ok;
    }

    /// <summary>Zuletzt benutzt = Änderungszeit des Ordners (fürs Aufräumen).</summary>
    private static void Touch(string dir)
    {
        try { Directory.SetLastWriteTimeUtc(dir, DateTime.UtcNow); } catch { }
    }

    private static string Ensure(string dir)
    {
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Inhalt von analyse.json (Vorstufe der Analysedatei, Format v1 folgt).</summary>
    public sealed class CachedAnalysis
    {
        public int Version { get; set; } = 1;
        public string Sha256 { get; set; } = "";
        public long Size { get; set; }
        public string Format { get; set; } = "";
        public string FileName { get; set; } = "";
        public string Platform { get; set; } = "";
        public DateTime Created { get; set; }
        public PresentationMeta? Meta { get; set; }
        public List<int> Slides { get; set; } = new();
        /// <summary>Endung der Vorschaubilder: „jpg“ (neu) oder „png“ (übernommen).</summary>
        public string Image { get; set; } = "png";
    }
}
