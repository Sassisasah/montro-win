using System.Text;
using System.Text.RegularExpressions;
using Montro.Core;

namespace Montro.Pptx;

public static partial class PptxParser
{
    /// <summary>
    /// Schriften der Präsentation: direkt gesetzte auf den Folien (mit Foliennummer), aus Master,
    /// Layouts, Diagrammen und den Designs der Master, dazu die eingebetteten. Portiert aus
    /// <c>FontScanner</c> in <c>Sources/montro/Services/FontCheck.swift</c>; Logik gleich halten.
    /// </summary>
    private static partial class FontScanner
    {
        /// <summary>
        /// <c>&lt;a:latin typeface="…"&gt;</c>, <c>a:ea</c>, <c>a:cs</c>, <c>a:sym</c>, <c>a:buFont</c>.
        /// <c>&lt;a:font script="…"&gt;</c> im Design (Ersatzschriften je Schriftsystem) zählt bewusst nicht.
        /// </summary>
        [GeneratedRegex("<a:(?:latin|ea|cs|sym|buFont)\\b[^>]*?\\btypeface=\"([^\"]*)\"")]
        private static partial Regex TypefacePattern();

        [GeneratedRegex("<p:embeddedFont>\\s*<p:font\\b[^>]*?\\btypeface=\"([^\"]*)\"")]
        private static partial Regex EmbeddedPattern();

        public static IReadOnlyList<FontUse> Scan(Package package, byte[] presentationXml, Dictionary<string, int> slideParts)
        {
            var used = new Dictionary<string, SortedSet<int>>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();

            void Add(string name, int? slide)
            {
                string n = name.Trim();
                // „+mj-lt“ usw. verweisen nur auf die Design-Schriften, die unten direkt gelesen werden.
                if (n.Length == 0 || n.StartsWith('+')) return;
                if (!used.TryGetValue(n, out var set))
                {
                    set = new SortedSet<int>();
                    used[n] = set;
                    order.Add(n);
                }
                if (slide is int s) set.Add(s);
            }

            // Folien
            foreach (var (part, index) in slideParts.OrderBy(p => p.Value))
            {
                if (package.Read(part) is { } data)
                    foreach (var f in Typefaces(data, TypefacePattern())) Add(f, index);
            }

            // Master, Layouts, Diagramme und die Designs der Master (nicht das Design der Notizen)
            var templateParts = new List<string>();
            var parts = package.Parts.Where(p => p.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)).OrderBy(p => p, StringComparer.Ordinal).ToList();
            foreach (var dir in new[] { "ppt/slideMasters/", "ppt/slideLayouts/", "ppt/charts/", "ppt/diagrams/" })
                templateParts.AddRange(parts.Where(p => p.StartsWith(dir, StringComparison.OrdinalIgnoreCase) && Package.Dir(p).Length == dir.Length - 1));
            foreach (var master in parts.Where(p => p.StartsWith("ppt/slideMasters/", StringComparison.OrdinalIgnoreCase) && Package.Dir(p).Length == "ppt/slideMasters".Length))
            {
                string masterDir = Package.Dir(master);
                var rels = RelsParser.Parse(package.Read($"{masterDir}/_rels/{Package.FileName(master)}.rels"));
                foreach (var rel in rels.Values.Where(r => r.Type.EndsWith("/theme", StringComparison.Ordinal)))
                    templateParts.Add(Package.Resolve(rel.Target, masterDir));
            }
            foreach (var part in templateParts)
            {
                if (package.Read(part) is { } data)
                    foreach (var f in Typefaces(data, TypefacePattern())) Add(f, null);
            }

            var embeddedNames = Typefaces(presentationXml, EmbeddedPattern()).ToList();
            var embedded = new HashSet<string>(embeddedNames, StringComparer.OrdinalIgnoreCase);
            foreach (var f in embeddedNames) Add(f, null);

            return order.Select(n => new FontUse(n, used[n].ToList(), embedded.Contains(n))).ToList();
        }

        private static IEnumerable<string> Typefaces(byte[] data, Regex pattern)
        {
            string text = Encoding.UTF8.GetString(data);
            foreach (Match m in pattern.Matches(text))
                yield return System.Net.WebUtility.HtmlDecode(m.Groups[1].Value);
        }
    }
}
