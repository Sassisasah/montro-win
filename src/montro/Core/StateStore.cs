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
    public string? PresenterDevice { get; set; }
    public TimerSettings Timer { get; set; } = new();
    public EndBehavior EndBehavior { get; set; } = EndBehavior.HoldLastSlide;
    public double NotesFontScale { get; set; } = 1.0;
}

public sealed class SavedItem
{
    public Guid Id { get; set; }
    public string Path { get; set; } = "";
    public string Title { get; set; } = "";
    public PresentationMeta? Meta { get; set; }
    public Dictionary<int, string> Thumbnails { get; set; } = new();
    public DateTime FileTime { get; set; }
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
