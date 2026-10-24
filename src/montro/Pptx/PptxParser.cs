using System.IO.Compression;
using System.Xml;
using Montro.Core;

namespace Montro.Pptx;

public sealed class PptxException(string message) : Exception(message);

/// <summary>
/// Liest aus der .pptx (OOXML-ZIP) selbst, ohne PowerPoint: Folienreihenfolge, ausgeblendete
/// Folien, Notizen, Anzahl der Klick-Animationen, Videos mit Länge und Startklick und die Schriften
/// (<c>PptxParser.Fonts.cs</c>).
/// Portiert aus der Mac-Version (<c>Sources/montro/Services/PPTXParser.swift</c>); Logik und
/// Zählweise sind absichtlich gleich.
/// </summary>
public static partial class PptxParser
{
    public static readonly string[] FileExtensions = [".pptx", ".pptm", ".ppsx", ".ppsm"];

    public static PresentationMeta Parse(string path)
    {
        ZipArchive zip;
        try
        {
            zip = ZipFile.OpenRead(path);
        }
        catch (InvalidDataException)
        {
            throw new PptxException("Die Datei konnte nicht entpackt werden.");
        }
        using (zip)
        {
            var package = new Package(zip);
            var presXml = package.Read("ppt/presentation.xml")
                ?? throw new PptxException("Keine gültige PowerPoint-Datei (OOXML).");
            var presRels = RelsParser.Parse(package.Read("ppt/_rels/presentation.xml.rels"));
            var order = SlideListParser.Parse(presXml);

            var slides = new List<SlideMeta>();
            var slideParts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var tempFiles = new List<string>();
            try
            {
                for (int i = 0; i < order.Count; i++)
                {
                    if (!presRels.TryGetValue(order[i], out var rel)) continue;
                    string slidePart = Package.Resolve(rel.Target, "ppt");
                    slideParts[slidePart] = i + 1;
                    var data = package.Read(slidePart);
                    if (data == null) continue;
                    string slideDir = Package.Dir(slidePart);
                    var rels = RelsParser.Parse(package.Read($"{slideDir}/_rels/{Package.FileName(slidePart)}.rels"));
                    var info = SlideXmlParser.Parse(data);

                    string notes = "";
                    var notesRel = rels.Values.FirstOrDefault(r => r.Type.EndsWith("/notesSlide", StringComparison.Ordinal));
                    if (notesRel != null)
                        notes = NotesParser.Parse(package.Read(Package.Resolve(notesRel.Target, slideDir)));

                    var videos = new List<VideoMeta>();
                    foreach (var shape in info.VideoShapes)
                    {
                        if (!rels.TryGetValue(shape.RelId, out var vrel)) continue;
                        string? mediaFile = null;
                        string fallbackName = "Video";
                        if (vrel.External)
                        {
                            string target = Uri.UnescapeDataString(vrel.Target);
                            if (Uri.TryCreate(target, UriKind.Absolute, out var uri) && uri.IsFile) target = uri.LocalPath;
                            fallbackName = Path.GetFileName(target);
                            if (!Uri.TryCreate(target, UriKind.Absolute, out var u) || u.IsFile)
                            {
                                string full = Path.IsPathRooted(target)
                                    ? target
                                    : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)) ?? "", target);
                                if (File.Exists(full)) mediaFile = full;
                            }
                        }
                        else
                        {
                            string entry = Package.Resolve(vrel.Target, slideDir);
                            fallbackName = Package.FileName(entry);
                            mediaFile = package.ExtractToTemp(entry);
                            if (mediaFile != null) tempFiles.Add(mediaFile);
                        }
                        double duration = mediaFile != null ? MediaDuration.Seconds(mediaFile) : 0;
                        string name = string.IsNullOrEmpty(shape.Name) ? fallbackName : shape.Name!;
                        int? start = info.TriggeredShapes.Contains(shape.ShapeId)
                            ? null
                            : info.PlayStarts.TryGetValue(shape.ShapeId, out var s) ? s : null;
                        videos.Add(new VideoMeta(name, duration, start));
                    }

                    slides.Add(new SlideMeta(i + 1, info.Hidden, notes, info.ClickCount, videos));
                }
            }
            finally
            {
                foreach (var f in tempFiles)
                {
                    try { File.Delete(f); } catch { }
                }
            }
            return new PresentationMeta(slides, FontScanner.Scan(package, presXml, slideParts));
        }
    }

    // MARK: - Paket

    /// <summary>Zugriff auf die Teile im ZIP über normalisierte Pfade (ohne führenden „/“).</summary>
    private sealed class Package(ZipArchive zip)
    {
        private readonly Dictionary<string, ZipArchiveEntry> entries =
            zip.Entries.GroupBy(e => e.FullName.Replace('\\', '/').TrimStart('/'), StringComparer.OrdinalIgnoreCase)
                       .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        /// <summary>Alle Teile (normalisierte Pfade).</summary>
        public IEnumerable<string> Parts => entries.Keys;

        public byte[]? Read(string part)
        {
            if (!entries.TryGetValue(part, out var e)) return null;
            using var s = e.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }

        public string? ExtractToTemp(string part)
        {
            if (!entries.TryGetValue(part, out var e)) return null;
            string file = Path.Combine(Path.GetTempPath(), $"montro-{Guid.NewGuid():N}{Path.GetExtension(part)}");
            e.ExtractToFile(file, overwrite: true);
            return file;
        }

        /// <summary>Ziel einer Beziehung relativ zum Ordner des Quellteils auflösen (wie am Mac).</summary>
        public static string Resolve(string target, string baseDir)
        {
            string t = Uri.UnescapeDataString(target).Replace('\\', '/');
            var parts = new List<string>();
            if (!t.StartsWith('/'))
                parts.AddRange(baseDir.Split('/', StringSplitOptions.RemoveEmptyEntries));
            foreach (var seg in t.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                if (seg == ".") continue;
                if (seg == "..")
                {
                    if (parts.Count > 0) parts.RemoveAt(parts.Count - 1);
                    continue;
                }
                parts.Add(seg);
            }
            return string.Join('/', parts);
        }

        public static string Dir(string part)
        {
            int i = part.LastIndexOf('/');
            return i < 0 ? "" : part[..i];
        }

        public static string FileName(string part)
        {
            int i = part.LastIndexOf('/');
            return i < 0 ? part : part[(i + 1)..];
        }
    }

    // MARK: - XML

    private static XmlReader Reader(byte[] data) => XmlReader.Create(new MemoryStream(data), new XmlReaderSettings
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreWhitespace = false,
    });

    /// <summary>Attribut über den lokalen Namen, optional nur mit Präfix (r:id statt id).</summary>
    private static string? Attr(XmlReader r, string localName, bool prefixed = false)
    {
        if (!r.HasAttributes) return null;
        for (int i = 0; i < r.AttributeCount; i++)
        {
            r.MoveToAttribute(i);
            if (r.LocalName == localName && (!prefixed || r.Prefix.Length > 0))
            {
                string v = r.Value;
                r.MoveToElement();
                return v;
            }
        }
        r.MoveToElement();
        return null;
    }

    private sealed record Relationship(string Id, string Type, string Target, bool External);

    private static class RelsParser
    {
        public static Dictionary<string, Relationship> Parse(byte[]? data)
        {
            var rels = new Dictionary<string, Relationship>();
            if (data == null) return rels;
            using var r = Reader(data);
            while (r.Read())
            {
                if (r.NodeType != XmlNodeType.Element || r.LocalName != "Relationship") continue;
                string? id = r.GetAttribute("Id"), target = r.GetAttribute("Target");
                if (id == null || target == null) continue;
                rels[id] = new Relationship(id, r.GetAttribute("Type") ?? "", target, r.GetAttribute("TargetMode") == "External");
            }
            return rels;
        }
    }

    private static class SlideListParser
    {
        public static List<string> Parse(byte[] data)
        {
            var ids = new List<string>();
            using var r = Reader(data);
            while (r.Read())
            {
                if (r.NodeType == XmlNodeType.Element && r.LocalName == "sldId" && Attr(r, "id", prefixed: true) is { } rid)
                    ids.Add(rid);
            }
            return ids;
        }
    }

    internal sealed class SlideXmlInfo
    {
        public sealed record VideoShape(string ShapeId, string RelId, string? Name);

        public bool Hidden;
        public int ClickCount;
        public readonly List<VideoShape> VideoShapes = new();
        /// <summary>spid → Klicknummer, mit der das Video startet (0 = automatisch)</summary>
        public readonly Dictionary<string, int> PlayStarts = new();
        /// <summary>Videos, die über einen Trigger (Klick auf das Video) starten</summary>
        public readonly HashSet<string> TriggeredShapes = new();
    }

    /// <summary>Gleiche Zustandsmaschine wie SlideXMLParser am Mac.</summary>
    internal static class SlideXmlParser
    {
        public static SlideXmlInfo Parse(byte[] data)
        {
            var info = new SlideXmlInfo();
            int picDepth = 0;
            string? shapeId = null, shapeName = null;
            var ctnStack = new List<string>();
            bool inPlayCommand = false;
            bool InInteractive() => ctnStack.Contains("interactiveSeq");

            using var r = Reader(data);
            while (r.Read())
            {
                if (r.NodeType == XmlNodeType.Element)
                {
                    bool empty = r.IsEmptyElement;
                    switch (r.LocalName)
                    {
                        case "sld":
                            if (r.GetAttribute("show") == "0") info.Hidden = true;
                            break;
                        case "pic":
                            if (!empty) picDepth++;
                            shapeId = null;
                            shapeName = null;
                            break;
                        case "cNvPr":
                            if (picDepth > 0)
                            {
                                shapeId = r.GetAttribute("id");
                                shapeName = r.GetAttribute("name");
                            }
                            break;
                        case "videoFile":
                            if (picDepth > 0 && shapeId != null && Attr(r, "link", prefixed: true) is { } rid)
                                info.VideoShapes.Add(new SlideXmlInfo.VideoShape(shapeId, rid, shapeName));
                            break;
                        case "cTn":
                            string nodeType = r.GetAttribute("nodeType") ?? "";
                            if (nodeType == "clickEffect" && !InInteractive())
                                info.ClickCount++;
                            if (!empty) ctnStack.Add(nodeType);
                            break;
                        case "cmd":
                            if (r.GetAttribute("cmd") is { } c && c.StartsWith("playFrom", StringComparison.Ordinal))
                                inPlayCommand = !empty;
                            break;
                        case "spTgt":
                            if (inPlayCommand && r.GetAttribute("spid") is { } spid)
                            {
                                if (InInteractive()) info.TriggeredShapes.Add(spid);
                                else info.PlayStarts.TryAdd(spid, info.ClickCount);
                                inPlayCommand = false;
                            }
                            break;
                    }
                }
                else if (r.NodeType == XmlNodeType.EndElement)
                {
                    switch (r.LocalName)
                    {
                        case "pic":
                            picDepth = Math.Max(picDepth - 1, 0);
                            break;
                        case "cTn":
                            if (ctnStack.Count > 0) ctnStack.RemoveAt(ctnStack.Count - 1);
                            break;
                        case "cmd":
                            inPlayCommand = false;
                            break;
                    }
                }
            }
            return info;
        }
    }

    /// <summary>Text der Platzhalter vom Typ „body“ der Notizenseite, Absätze mit Zeilenumbruch.</summary>
    internal static class NotesParser
    {
        public static string Parse(byte[]? data)
        {
            if (data == null) return "";
            var bodies = new List<string>();
            bool inShape = false, isBody = false, inText = false;
            var text = new System.Text.StringBuilder();

            using var r = Reader(data);
            while (r.Read())
            {
                switch (r.NodeType)
                {
                    case XmlNodeType.Element:
                        bool empty = r.IsEmptyElement;
                        switch (r.LocalName)
                        {
                            case "sp":
                                inShape = !empty;
                                isBody = false;
                                text.Clear();
                                break;
                            case "ph":
                                if (inShape && r.GetAttribute("type") == "body") isBody = true;
                                break;
                            case "t":
                                inText = !empty;
                                break;
                            case "br":
                                if (inShape) text.Append('\n');
                                break;
                            case "p":
                                // leerer Absatz <a:p/> = Leerzeile (Mac: didEndElement „p“)
                                if (empty && inShape) text.Append('\n');
                                break;
                        }
                        break;
                    case XmlNodeType.Text:
                    case XmlNodeType.SignificantWhitespace:
                    case XmlNodeType.Whitespace:
                        if (inText && inShape) text.Append(r.Value);
                        break;
                    case XmlNodeType.EndElement:
                        switch (r.LocalName)
                        {
                            case "t":
                                inText = false;
                                break;
                            case "p":
                                if (inShape) text.Append('\n');
                                break;
                            case "sp":
                                if (isBody) bodies.Add(text.ToString().Trim());
                                inShape = false;
                                isBody = false;
                                break;
                        }
                        break;
                }
            }
            return string.Join("\n\n", bodies.Where(b => b.Length > 0));
        }
    }
}
