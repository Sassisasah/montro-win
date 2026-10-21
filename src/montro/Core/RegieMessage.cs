namespace Montro.Core;

/// <summary>Nachricht aus der Regie an die Referent*in (Mac: RegieMessage in LiveModels.swift).</summary>
public sealed class RegieMessage : Observable
{
    public static readonly string[] DefaultPresets =
    [
        "Noch 5 Minuten",
        "Bitte zum Schluss kommen",
        "Bitte lauter sprechen",
        "Mikrofon prüfen",
        "Fragen aus dem Publikum",
        "Pause – wir sind gleich zurück",
    ];

    private string draft = "";
    private string text = "";
    private bool isVisible;
    private bool blink;
    private DateTime? sentAt;
    private string background = "#B2F141";   // Bass Lime
    private string foreground = "#202020";   // Dark Matter

    public System.Collections.ObjectModel.ObservableCollection<string> Presets { get; } = new(DefaultPresets);

    public string Draft { get => draft; set { if (Set(ref draft, value)) Raise(nameof(CanSend)); } }
    public string Text { get => text; private set { if (Set(ref text, value)) Raise(nameof(Quoted)); } }
    public bool IsVisible { get => isVisible; private set => Set(ref isVisible, value); }
    public bool Blink { get => blink; set => Set(ref blink, value); }
    public string Background { get => background; set { if (Set(ref background, value)) Raise(nameof(BackgroundBrush)); } }
    public string Foreground { get => foreground; set { if (Set(ref foreground, value)) Raise(nameof(ForegroundBrush)); } }

    public bool CanSend => Draft.Trim().Length > 0;
    public string Quoted => $"„{Text}“";

    public System.Windows.Media.Brush BackgroundBrush => Brush(Background);
    public System.Windows.Media.Brush ForegroundBrush => Brush(Foreground);

    /// <summary>Kurzes Aufblinken direkt nach dem Senden, damit die Nachricht auffällt.</summary>
    public bool IsFlashing => IsVisible && sentAt is { } t && (DateTime.Now - t).TotalSeconds < 3;

    public void Send(string? value = null)
    {
        string t = (value ?? Draft).Trim();
        if (t.Length == 0) return;
        Text = t;
        IsVisible = true;
        sentAt = DateTime.Now;
    }

    public void Clear() => IsVisible = false;

    /// <summary>Vom Takt der App aufgerufen (Aufblinken nach dem Senden endet nach 3 s).</summary>
    public void Tick() => Raise(nameof(IsFlashing));

    private static System.Windows.Media.Brush Brush(string hex)
    {
        try { return new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)); }
        catch { return System.Windows.Media.Brushes.Gray; }
    }
}
