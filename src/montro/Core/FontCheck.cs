using System.Runtime.InteropServices;
using System.Windows.Media;

namespace Montro.Core;

/// <summary>Ob eine Schrift auf diesem Rechner für PowerPoint verfügbar ist (Mac: FontAvailability).</summary>
public enum FontAvailability
{
    /// <summary>im System installiert (auch nur für die angemeldete Person)</summary>
    Installed,
    /// <summary>heruntergeladene Office-Cloud-Schrift</summary>
    Office,
    /// <summary>fehlt, ist aber in der .pptx eingebettet</summary>
    Embedded,
    /// <summary>fehlt, PowerPoint ersetzt sie</summary>
    Missing,
}

/// <summary>
/// Schrift-Check (Mac: <c>FontCheck</c> in <c>Services/FontCheck.swift</c>). Vergleicht die Schriften
/// der Präsentation mit den GDI-Schriftfamilien, die PowerPoint unter Windows sieht, und mit dem
/// Cache der Office-Cloud-Schriften.
/// </summary>
public static class FontCheck
{
    private static HashSet<string>? systemNames;
    private static HashSet<string>? officeNames;
    private static readonly object gate = new();

    /// <summary>Schriftlisten neu einlesen (z. B. nach dem Installieren einer Schrift).</summary>
    public static void Reload()
    {
        lock (gate)
        {
            systemNames = null;
            officeNames = null;
        }
    }

    public static FontAvailability Availability(FontUse font)
    {
        if (SystemNames().Contains(font.Name)) return FontAvailability.Installed;
        if (Office().Contains(font.Name)) return FontAvailability.Office;
        return font.Embedded ? FontAvailability.Embedded : FontAvailability.Missing;
    }

    public static IReadOnlyList<FontUse> Missing(IEnumerable<FontUse>? fonts) =>
        fonts?.Where(f => Availability(f) == FontAvailability.Missing).ToList() ?? [];

    /// <summary>
    /// GDI-Familien („Segoe UI Light“ ist dort eine eigene Familie) und volle Namen. GDI zählt auch
    /// Schriften, die nur für die angemeldete Person installiert sind.
    /// </summary>
    private static HashSet<string> SystemNames()
    {
        lock (gate)
        {
            if (systemNames != null) return systemNames;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            IntPtr hdc = GetDC(IntPtr.Zero);
            try
            {
                var lf = new LOGFONTW { lfCharSet = DEFAULT_CHARSET, lfFaceName = "" };
                EnumFontFamExProc proc = (ref ENUMLOGFONTEXW e, IntPtr tm, uint type, IntPtr lp) =>
                {
                    string face = e.elfLogFont.lfFaceName ?? "";
                    if (face.StartsWith('@')) face = face[1..];   // senkrechte Variante asiatischer Schriften
                    if (face.Length > 0) names.Add(face);
                    if (!string.IsNullOrEmpty(e.elfFullName)) names.Add(e.elfFullName);
                    return 1;
                };
                EnumFontFamiliesExW(hdc, ref lf, proc, IntPtr.Zero, 0);
                GC.KeepAlive(proc);
            }
            catch (Exception ex)
            {
                DebugLog.Write($"Schrift-Check: Schriftliste nicht lesbar ({ex.Message})");
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, hdc);
            }
            // WPF-Familien als Ergänzung (DirectWrite-Namen)
            try
            {
                foreach (var family in Fonts.SystemFontFamilies)
                    foreach (var n in family.FamilyNames.Values) names.Add(n);
            }
            catch { }
            systemNames = names;
            return names;
        }
    }

    /// <summary>Office-Cloud-Schriften: <c>%LOCALAPPDATA%\Microsoft\FontCache\4\CloudFonts\&lt;Familie&gt;\…</c>.</summary>
    private static HashSet<string> Office()
    {
        lock (gate)
        {
            if (officeNames != null) return officeNames;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "FontCache", "4", "CloudFonts");
            try
            {
                if (Directory.Exists(dir))
                {
                    foreach (var sub in Directory.EnumerateDirectories(dir)) names.Add(Path.GetFileName(sub));
                    foreach (var file in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
                    {
                        string ext = Path.GetExtension(file).ToLowerInvariant();
                        if (ext is not (".ttf" or ".otf")) continue;
                        try
                        {
                            var g = new GlyphTypeface(new Uri(file));
                            foreach (var n in g.Win32FamilyNames.Values) names.Add(n);
                            foreach (var n in g.FamilyNames.Values) names.Add(n);
                            foreach (var n in g.FaceNames.Values)
                                foreach (var f in g.FamilyNames.Values) names.Add($"{f} {n}");
                        }
                        catch { }
                    }
                }
            }
            catch { }
            officeNames = names;
            return names;
        }
    }

    // MARK: - GDI

    private const byte DEFAULT_CHARSET = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct LOGFONTW
    {
        public int lfHeight, lfWidth, lfEscapement, lfOrientation, lfWeight;
        public byte lfItalic, lfUnderline, lfStrikeOut, lfCharSet, lfOutPrecision, lfClipPrecision, lfQuality, lfPitchAndFamily;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string lfFaceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ENUMLOGFONTEXW
    {
        public LOGFONTW elfLogFont;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string elfFullName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string elfStyle;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string elfScript;
    }

    private delegate int EnumFontFamExProc(ref ENUMLOGFONTEXW lpelfe, IntPtr lpntme, uint fontType, IntPtr lParam);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern int EnumFontFamiliesExW(IntPtr hdc, ref LOGFONTW lpLogfont, EnumFontFamExProc lpProc, IntPtr lParam, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
}
