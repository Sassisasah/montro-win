using System.Text.Json;
using System.Text.Json.Serialization;

namespace Montro.Core;

/// <summary>
/// Analyse und Vorschaubilder nach Dateiinhalt (SHA-256) statt nach Playlist-Eintrag:
/// %LOCALAPPDATA%\montro\cache\&lt;Prüfsumme&gt;\ mit analyse.json und slide-&lt;n&gt;.png.
/// Dieselbe Datei wird so nur einmal gerendert – auch nach Show-Wechsel, Entfernen und
/// Neu-Hinzufügen, Verschieben oder einer gleichen Kopie aus dem Verbund.
/// </summary>
public static class PreviewCache
{
    /// <summary>Nicht benutzte Einträge werden nach so vielen Tagen gelöscht.</summary>
    public const int MaxAgeDays = 60;

    /// <summary>Darüber werden die ältesten nicht benutzten Einträge gelöscht.</summary>
    public const long MaxBytes = 5L * 1024 * 1024 * 1024;

    private const string AnalysisFile = "analyse.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        // gleiche Schlüssel wie analyse.json am Mac (sha256, size, format …)
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Root => Ensure(Path.Combine(AppPaths.LocalDir, "cache"));

    public static string DirFor(string hash) => Path.Combine(Root, hash);

    /// <summary>Analyse und Vorschaubilder aus dem Cache, null wenn nicht (vollständig) vorhanden.</summary>
    public static (PresentationMeta Meta, Dictionary<int, string> Thumbnails)? TryLoad(string hash)
    {
        try
        {
            string dir = DirFor(hash);
            string file = Path.Combine(dir, AnalysisFile);
            if (!File.Exists(file)) return null;
            var a = JsonSerializer.Deserialize<CachedAnalysis>(File.ReadAllText(file), Options);
            if (a?.Meta == null || a.Sha256 != hash || a.Slides.Count == 0) return null;
            var thumbs = a.Slides.ToDictionary(i => i, i => Path.Combine(dir, $"slide-{i}.png"));
            if (!thumbs.Values.All(File.Exists)) return null;
            Touch(dir);
            return (a.Meta, thumbs);
        }
        catch (Exception e)
        {
            DebugLog.Write($"Vorschau-Cache: {hash[..12]} nicht lesbar – {e.Message}");
            return null;
        }
    }

    /// <summary>Analyse zu den bereits in <see cref="DirFor"/> liegenden Vorschaubildern schreiben.</summary>
    public static void Save(string hash, long size, string sourcePath, PresentationMeta meta, Dictionary<int, string> thumbnails)
    {
        var a = new CachedAnalysis
        {
            Sha256 = hash,
            Size = size,
            Format = Path.GetExtension(sourcePath).TrimStart('.').ToLowerInvariant(),
            FileName = Path.GetFileName(sourcePath),
            Platform = "windows",
            Created = DateTime.UtcNow,
            Meta = meta,
            Slides = thumbnails.Keys.Order().ToList(),
        };
        string dir = Ensure(DirFor(hash));
        string tmp = Path.Combine(dir, AnalysisFile + ".tmp");
        File.WriteAllText(tmp, JsonSerializer.Serialize(a, Options));
        File.Move(tmp, Path.Combine(dir, AnalysisFile), overwrite: true);
    }

    /// <summary>
    /// Vorschaubilder aus dem alten Ablageort (thumbs\&lt;ID&gt;) in den Cache übernehmen,
    /// damit nach dem Update nichts neu gerendert werden muss.
    /// </summary>
    public static Dictionary<int, string>? Adopt(string hash, long size, string sourcePath, PresentationMeta meta, Dictionary<int, string> thumbnails)
    {
        try
        {
            if (TryLoad(hash) is { } cached) return cached.Thumbnails;
            string dir = Ensure(DirFor(hash));
            var result = new Dictionary<int, string>();
            foreach (var (slide, src) in thumbnails)
            {
                string dst = Path.Combine(dir, $"slide-{slide}.png");
                File.Copy(src, dst, overwrite: true);
                result[slide] = dst;
            }
            Save(hash, size, sourcePath, meta, result);
            return result;
        }
        catch (Exception e)
        {
            DebugLog.Write($"Vorschau-Cache: Übernahme von „{Path.GetFileName(sourcePath)}“ fehlgeschlagen – {e.Message}");
            return null;
        }
    }

    /// <summary>Eintrag verwerfen („Neu analysieren & Vorschaubilder erzeugen“).</summary>
    public static void Remove(string hash)
    {
        try { Directory.Delete(DirFor(hash), recursive: true); } catch { }
    }

    /// <summary>
    /// Aufräumen: Einträge, die keine Playlist mehr benutzt, nach <see cref="MaxAgeDays"/> Tagen
    /// löschen, und die ältesten davon, solange der Cache größer als <see cref="MaxBytes"/> ist.
    /// </summary>
    public static void Clean(IEnumerable<string?> inUse)
    {
        try
        {
            var keep = inUse.OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
            var unused = new DirectoryInfo(Root).GetDirectories()
                .Where(d => !keep.Contains(d.Name))
                .Select(d => (Dir: d, Bytes: d.EnumerateFiles().Sum(f => f.Length)))
                .OrderBy(e => e.Dir.LastWriteTimeUtc)
                .ToList();
            long total = new DirectoryInfo(Root).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
            var cutoff = DateTime.UtcNow.AddDays(-MaxAgeDays);
            int removed = 0;
            foreach (var (d, bytes) in unused)
            {
                if (d.LastWriteTimeUtc >= cutoff && total <= MaxBytes) continue;
                d.Delete(recursive: true);
                total -= bytes;
                removed++;
            }
            if (removed > 0) DebugLog.Write($"Vorschau-Cache: {removed} alte Einträge gelöscht, jetzt {total / (1024 * 1024)} MB");
        }
        catch (Exception e)
        {
            DebugLog.Write($"Vorschau-Cache: Aufräumen fehlgeschlagen – {e.Message}");
        }
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
    }
}
