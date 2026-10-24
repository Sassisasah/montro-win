using System.Text.Json;
using System.Text.Json.Serialization;

namespace Montro.Core;

/// <summary>Was zwischen zwei Starts erhalten bleibt (Mac: Persistence.swift → state.json).</summary>
public sealed class SavedState
{
    public List<SavedItem> Items { get; set; } = new();
    public Guid? Selection { get; set; }
    /// <summary>Gerätename des Publikumsbildschirms (\\.\DISPLAY2), null = Popup-Fenster</summary>
    public string? AudienceDevice { get; set; }
    /// <summary>Bildschirm der Referent*innenansicht, null = Popup-Fenster</summary>
    public string? PresenterDevice { get; set; }
    /// <summary>Referent*innenansicht „Standard“ (Präsentationen ohne Vorlage)</summary>
    public PresenterLayout? Layout { get; set; }
    public List<LayoutPreset>? LayoutPresets { get; set; }
    public TimerSettings Timer { get; set; } = new();
    public EndBehavior EndBehavior { get; set; } = EndBehavior.HoldLastSlide;
    /// <summary>Nur noch zum Übernehmen alter Stände; die Schriftgröße steckt im Baustein „Notizen“.</summary>
    public double NotesFontScale { get; set; } = 1.0;
    public ShowTransition Transition { get; set; } = ShowTransition.Black;
    public double TransitionDuration { get; set; } = 1.0;
    /// <summary>Nächste Präsentation auch während einer Show vorab öffnen</summary>
    public bool PreloadNext { get; set; }
    public List<string>? MessagePresets { get; set; }
    public string? MessageBackground { get; set; }
    public string? MessageForeground { get; set; }
    /// <summary>Zuletzt geöffnete bzw. gesicherte Show-Datei (.montro)</summary>
    public string? ShowPath { get; set; }
    /// <summary>Fernsteuerung (Remote-API): an/aus, Port, nur von diesem Rechner</summary>
    public bool? RemoteEnabled { get; set; }
    public int? RemotePort { get; set; }
    public bool RemoteLocalOnly { get; set; }
    /// <summary>Verbund (Rolle, Name, Adressen) und Datei-Abgleich</summary>
    public Montro.Cluster.SavedCluster? Cluster { get; set; }
    public Montro.Cluster.SavedFileSync? FileSync { get; set; }
}

/// <summary>Übergang auf dem Publikumsbildschirm, wenn eine andere Präsentation live geht (Mac: ShowTransition).</summary>
public enum ShowTransition { Cut, Black, Crossfade }

public sealed class SavedItem
{
    public Guid Id { get; set; }
    public string Path { get; set; } = "";
    public string Title { get; set; } = "";
    public PresentationMeta? Meta { get; set; }
    public Dictionary<int, string> Thumbnails { get; set; } = new();
    public DateTime FileTime { get; set; }
    public Guid? PresetId { get; set; }
}

public enum EndBehavior { HoldLastSlide, BlackEverywhere }

public static class StateStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static SavedState Load()
    {
        try
        {
            if (File.Exists(AppPaths.StateFile))
                return JsonSerializer.Deserialize<SavedState>(File.ReadAllText(AppPaths.StateFile), Options) ?? new SavedState();
        }
        catch (Exception e)
        {
            DebugLog.Write($"Zustand: {AppPaths.StateFile} nicht lesbar – {e.Message}");
            try { File.Copy(AppPaths.StateFile, AppPaths.StateFile + ".defekt", overwrite: true); } catch { }
        }
        return new SavedState();
    }

    public static void Save(SavedState s)
    {
        try
        {
            string tmp = AppPaths.StateFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(s, Options));
            File.Move(tmp, AppPaths.StateFile, overwrite: true);
        }
        catch (Exception e)
        {
            DebugLog.Write($"Zustand: Speichern fehlgeschlagen – {e.Message}");
        }
    }
}
