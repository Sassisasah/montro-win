using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Montro.Core;

/// <summary>
/// Show-Datei <c>.montro</c> (Mac: ShowFile.swift, Format in docs/SHOW-FORMAT.md im Repo
/// Sassisasah/montro): Playlist mit Referent*innenansicht je Präsentation und Show-Einstellungen,
/// reines JSON, für Mac und Windows gleich. Bildschirm-Zuordnung und Vorschaubilder bleiben pro Rechner.
/// </summary>
public sealed class ShowFile
{
    public const string FormatId = "montro-show";
    public const int CurrentVersion = 1;
    public const string Extension = ".montro";

    public string Format { get; set; } = FormatId;
    public int Version { get; set; } = CurrentVersion;
    public string SavedBy { get; set; } = "";
    public string SavedAt { get; set; } = "";
    public string Name { get; set; } = "";
    public List<ShowPresentation> Presentations { get; set; } = new();
    public ShowLayout StandardLayout { get; set; } = new();
    public List<ShowPreset> Presets { get; set; } = new();
    public ShowSettings Settings { get; set; } = new();

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // MARK: - Farben: intern WPF (#RRGGBB / #AARRGGBB), in der Datei #RRGGBB / #RRGGBBAA

    public static string ToFileColor(string hex)
    {
        string t = hex.Trim().TrimStart('#').ToUpperInvariant();
        return t.Length == 8 ? $"#{t[2..]}{(t[..2] == "FF" ? "" : t[..2])}" : $"#{t}";
    }

    public static string FromFileColor(string? hex, string fallback)
    {
        if (hex is not { } s || !s.Trim().StartsWith('#')) return fallback;
        string t = s.Trim()[1..];
        if (!uint.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _)) return fallback;
        return t.Length switch
        {
            6 => $"#{t.ToUpperInvariant()}",
            8 => t[6..].Equals("FF", StringComparison.OrdinalIgnoreCase) ? $"#{t[..6].ToUpperInvariant()}" : $"#{(t[6..] + t[..6]).ToUpperInvariant()}",
            _ => fallback,
        };
    }

    // MARK: - Ansicht

    private static string KindName(WidgetKind k) => JsonNamingPolicy.CamelCase.ConvertName(k.ToString());

    public static ShowLayout ToFile(PresenterLayout l) => new()
    {
        Background = ToFileColor(l.Background),
        TextColor = ToFileColor(l.TextColor),
        Widgets = l.Widgets.Select(w => new ShowWidget
        {
            Kind = KindName(w.Kind),
            Enabled = w.Enabled,
            X = w.Frame.X,
            Y = w.Frame.Y,
            W = w.Frame.W,
            H = w.Frame.H,
            FontScale = w.FontScale,
            ShowTitle = w.ShowTitle,
        }).ToList(),
    };

    /// <summary>Unbekannte Widget-Arten (neuere Version) überspringen, fehlende ausgeschaltet ergänzen.</summary>
    public static PresenterLayout FromFile(ShowLayout l)
    {
        var p = new PresenterLayout
        {
            Background = FromFileColor(l.Background, "#202020"),
            TextColor = FromFileColor(l.TextColor, "#FFFFFF"),
        };
        foreach (var w in l.Widgets)
        {
            var kind = Enum.GetValues<WidgetKind>().Cast<WidgetKind?>().FirstOrDefault(k => KindName(k!.Value) == w.Kind);
            if (kind == null || p.Config(kind.Value) != null) continue;
            p.Widgets.Add(new WidgetConfig
            {
                Kind = kind.Value,
                Enabled = w.Enabled,
                Frame = new NormRect(w.X, w.Y, w.W, w.H).Clamped(),
                FontScale = Math.Clamp(w.FontScale, 0.2, 6),
                ShowTitle = w.ShowTitle,
            });
        }
        p.EnsureAllKinds();
        return p;
    }

    // MARK: - Timer

    public static ShowTimer ToFile(TimerSettings t) => new()
    {
        DurationSeconds = t.Duration,
        Warning1Seconds = t.Warning1,
        Warning2Seconds = t.Warning2,
        NormalColor = ToFileColor(t.NormalColor),
        Warning1Color = ToFileColor(t.Warning1Color),
        Warning2Color = ToFileColor(t.Warning2Color),
        OvertimeColor = ToFileColor(t.OvertimeColor),
        AllowOvertime = t.AllowOvertime,
        BlinkInOvertime = t.BlinkInOvertime,
        BlinkInWarning2 = t.BlinkInWarning2,
        Mode = t.CountUp ? "countup" : "countdown",
        AutoStart = t.AutoStartWithPresentation,
        ShowProgress = t.ShowProgress,
    };

    public static TimerSettings FromFile(ShowTimer t)
    {
        var d = new TimerSettings();
        return new TimerSettings
        {
            Duration = Math.Max(t.DurationSeconds, 0),
            Warning1 = Math.Max(t.Warning1Seconds, 0),
            Warning2 = Math.Max(t.Warning2Seconds, 0),
            NormalColor = FromFileColor(t.NormalColor, d.NormalColor),
            Warning1Color = FromFileColor(t.Warning1Color, d.Warning1Color),
            Warning2Color = FromFileColor(t.Warning2Color, d.Warning2Color),
            OvertimeColor = FromFileColor(t.OvertimeColor, d.OvertimeColor),
            AllowOvertime = t.AllowOvertime,
            BlinkInOvertime = t.BlinkInOvertime,
            BlinkInWarning2 = t.BlinkInWarning2,
            CountUp = t.Mode == "countup",
            AutoStartWithPresentation = t.AutoStart,
            ShowProgress = t.ShowProgress,
        };
    }

    public static string EndBehaviorName(EndBehavior e) => e == EndBehavior.BlackEverywhere ? "blackEverywhere" : "holdLastSlide";
    public static EndBehavior? ParseEndBehavior(string? s) => s switch { "holdLastSlide" => EndBehavior.HoldLastSlide, "blackEverywhere" => EndBehavior.BlackEverywhere, _ => null };
    public static string TransitionName(ShowTransition t) => t switch { ShowTransition.Cut => "cut", ShowTransition.Crossfade => "crossfade", _ => "black" };
    public static ShowTransition? ParseTransition(string? s) => s switch { "cut" => ShowTransition.Cut, "black" => ShowTransition.Black, "crossfade" => ShowTransition.Crossfade, _ => null };

    // MARK: - Pfade

    /// <summary>Pfad relativ zum Ordner der Show-Datei mit „/“; null auf anderem Laufwerk.</summary>
    public static string? RelativePath(string target, string baseDir)
    {
        if (!string.Equals(System.IO.Path.GetPathRoot(target), System.IO.Path.GetPathRoot(baseDir), StringComparison.OrdinalIgnoreCase)) return null;
        return System.IO.Path.GetRelativePath(baseDir, target).Replace('\\', '/');
    }

    /// <summary>Vergleichsschlüssel für Dateinamen: Unicode NFC und klein (Umlaute von Mac und Windows gleich).</summary>
    public static string Key(string fileName) => fileName.Normalize(NormalizationForm.FormC).ToLowerInvariant();
}

public sealed class ShowPresentation
{
    public string File { get; set; } = "";
    public string? RelativePath { get; set; }
    public string? AbsolutePath { get; set; }
    public string Title { get; set; } = "";
    public string? Preset { get; set; }
    public string? Sha256 { get; set; }
    public long? Size { get; set; }
}

public sealed class ShowWidget
{
    public string Kind { get; set; } = "";
    public bool Enabled { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; }
    public double H { get; set; }
    public double FontScale { get; set; } = 1;
    public bool ShowTitle { get; set; }
}

public sealed class ShowLayout
{
    public string Background { get; set; } = "#202020";
    public string TextColor { get; set; } = "#FFFFFF";
    public List<ShowWidget> Widgets { get; set; } = new();
}

public sealed class ShowPreset
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public ShowLayout Layout { get; set; } = new();
}

public sealed class ShowTimer
{
    public double DurationSeconds { get; set; } = 900;
    public double Warning1Seconds { get; set; } = 300;
    public double Warning2Seconds { get; set; } = 60;
    public string NormalColor { get; set; } = "#F5F5F5";
    public string Warning1Color { get; set; } = "#FFC71F";
    public string Warning2Color { get; set; } = "#FF8C1A";
    public string OvertimeColor { get; set; } = "#F53838";
    public bool AllowOvertime { get; set; } = true;
    public bool BlinkInOvertime { get; set; } = true;
    public bool BlinkInWarning2 { get; set; }
    public string Mode { get; set; } = "countdown";
    public bool AutoStart { get; set; }
    public bool ShowProgress { get; set; } = true;
}

public sealed class ShowMessage
{
    public List<string> Presets { get; set; } = new();
    public string Background { get; set; } = "#B2F141";
    public string Foreground { get; set; } = "#202020";
}

public sealed class ShowSettings
{
    public string EndBehavior { get; set; } = "holdLastSlide";
    public string Transition { get; set; } = "black";
    public double TransitionSeconds { get; set; } = 1;
    public ShowTimer Timer { get; set; } = new();
    public ShowMessage Message { get; set; } = new();
}
