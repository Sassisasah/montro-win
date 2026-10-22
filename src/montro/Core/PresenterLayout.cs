namespace Montro.Core;

/// <summary>Alle Bausteine der Referent*innenansicht (Mac: WidgetKind).</summary>
public enum WidgetKind { CurrentSlide, NextSlide, Notes, SlideCounter, Timer, Clock, VideoTime, Message }

/// <summary>Rechteck in normierten Koordinaten (0…1), Ursprung oben links (Mac: NormRect).</summary>
public sealed record NormRect(double X, double Y, double W, double H)
{
    public NormRect Clamped()
    {
        double w = Math.Clamp(W, 0.04, 1), h = Math.Clamp(H, 0.03, 1);
        return new NormRect(Math.Clamp(X, 0, 1 - w), Math.Clamp(Y, 0, 1 - h), w, h);
    }

    public NormRect Snapped(double step) =>
        new(Math.Round(X / step) * step, Math.Round(Y / step) * step, Math.Round(W / step) * step, Math.Round(H / step) * step);
}

/// <summary>Ein Baustein mit Lage, Schriftgröße und Überschrift (Mac: WidgetConfig).</summary>
public sealed class WidgetConfig
{
    public WidgetKind Kind { get; set; }
    public bool Enabled { get; set; } = true;
    public NormRect Frame { get; set; } = new(0, 0, 0.2, 0.1);
    public double FontScale { get; set; } = 1;
    public bool ShowTitle { get; set; } = true;

    public WidgetConfig Clone() => new() { Kind = Kind, Enabled = Enabled, Frame = Frame, FontScale = FontScale, ShowTitle = ShowTitle };

    public static string Title(WidgetKind k) => k switch
    {
        WidgetKind.CurrentSlide => "Aktuelle Folie",
        WidgetKind.NextSlide => "Nächste Folie",
        WidgetKind.Notes => "Notizen",
        WidgetKind.SlideCounter => "Foliennummer / Folienanzahl",
        WidgetKind.Timer => "Redezeit-Timer",
        WidgetKind.Clock => "Uhrzeit",
        WidgetKind.VideoTime => "Video-Laufzeit",
        _ => "Regie-Nachricht",
    };
}

/// <summary>Aufbau der Referent*innenansicht (Mac: PresenterLayout), Farben als #RRGGBB.</summary>
public sealed class PresenterLayout
{
    public List<WidgetConfig> Widgets { get; set; } = new();
    public string Background { get; set; } = "#202020";   // Dark Matter
    public string TextColor { get; set; } = "#FFFFFF";

    /// <summary>
    /// Standard angelehnt an die Referentenansicht von PowerPoint unter Windows (wie am Mac):
    /// Timer-Leiste oben, große aktuelle Folie links, Foliennummer darunter, nächste Folie rechts
    /// oben, Notizen rechts darunter.
    /// </summary>
    public static PresenterLayout WindowsDefault() => new()
    {
        Widgets =
        [
            new() { Kind = WidgetKind.Timer, Frame = new(0.02, 0.02, 0.28, 0.09), ShowTitle = false },
            new() { Kind = WidgetKind.Clock, Frame = new(0.32, 0.02, 0.14, 0.09), ShowTitle = false },
            new() { Kind = WidgetKind.VideoTime, Frame = new(0.48, 0.02, 0.50, 0.09), ShowTitle = false },
            new() { Kind = WidgetKind.CurrentSlide, Frame = new(0.02, 0.13, 0.60, 0.60), ShowTitle = false },
            new() { Kind = WidgetKind.SlideCounter, Frame = new(0.02, 0.74, 0.60, 0.06), ShowTitle = false },
            new() { Kind = WidgetKind.Message, Frame = new(0.02, 0.82, 0.60, 0.16), ShowTitle = false },
            new() { Kind = WidgetKind.NextSlide, Frame = new(0.65, 0.13, 0.33, 0.30) },
            new() { Kind = WidgetKind.Notes, Frame = new(0.65, 0.45, 0.33, 0.53) },
        ],
    };

    public WidgetConfig? Config(WidgetKind kind) => Widgets.FirstOrDefault(w => w.Kind == kind);

    /// <summary>Fehlende Bausteine ergänzen (z. B. nach einem Update) – ausgeschaltet.</summary>
    public void EnsureAllKinds()
    {
        foreach (var d in WindowsDefault().Widgets)
        {
            if (Config(d.Kind) != null) continue;
            d.Enabled = false;
            Widgets.Add(d);
        }
    }

    public PresenterLayout Clone() => new()
    {
        Widgets = Widgets.Select(w => w.Clone()).ToList(),
        Background = Background,
        TextColor = TextColor,
    };

    /// <summary>Ändert einen Baustein (Mac: update(kind)).</summary>
    public void Update(WidgetKind kind, Action<WidgetConfig> change)
    {
        if (Config(kind) is { } c) change(c);
    }

    /// <summary>Inhaltlich gleich (für „nicht gespeichert“ und „Entwurf geändert“).</summary>
    public bool SameAs(PresenterLayout? other) =>
        other != null && System.Text.Json.JsonSerializer.Serialize(this) == System.Text.Json.JsonSerializer.Serialize(other);
}

/// <summary>Gespeicherte Referent*innenansicht mit Namen, einer Präsentation zuweisbar (Mac: LayoutPreset).</summary>
public sealed class LayoutPreset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public PresenterLayout Layout { get; set; } = PresenterLayout.WindowsDefault();
}
