using System.Text.Json;
using System.Text.Json.Serialization;

namespace Montro.Cluster;

/// <summary>Rolle dieses Rechners im Verbund (Mac: ClusterRole).</summary>
public enum ClusterRole { Standalone, Master, Slave }

public static class ClusterRoles
{
    public static string Key(ClusterRole r) => r switch { ClusterRole.Master => "master", ClusterRole.Slave => "slave", _ => "standalone" };

    public static ClusterRole? Parse(string? s) => s switch
    {
        "master" => ClusterRole.Master,
        "slave" => ClusterRole.Slave,
        "standalone" => ClusterRole.Standalone,
        _ => null,
    };

    public static string Title(ClusterRole? r) => r switch
    {
        ClusterRole.Master => "Master",
        ClusterRole.Slave => "Slave",
        ClusterRole.Standalone => "Einzelplatz",
        _ => "–",
    };
}

/// <summary>Ein anderer montro-Rechner im Netz, gefunden oder manuell eingetragen (Mac: ClusterPeer).</summary>
public sealed class ClusterPeer
{
    /// <summary>Instanz-ID des anderen Players, bei manuellen Einträgen vorerst „manual:host:port“</summary>
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Host { get; set; }
    public int Port { get; set; } = Remote.RemoteApi.DefaultPort;
    public ClusterRole? Role { get; set; }
    public string Group { get; set; } = "";
    public bool Manual { get; set; }
    public bool Discovered { get; set; }
    public bool Reachable { get; set; }
    public DateTime? LastContact { get; set; }
    public bool Live { get; set; }
    public string File { get; set; } = "";
    public int Slide { get; set; }
    public bool Blackout { get; set; }
    public string? Issue { get; set; }
    /// <summary>Slave ist zeitweise abgekoppelt (zum Bearbeiten)</summary>
    public bool Detached { get; set; }
    /// <summary>null = unbekannt / nicht relevant</summary>
    public bool? InSync { get; set; }

    public string Address => Host != null ? $"{Host}:{Port}" : "wird aufgelöst …";
    public string ManualKey => Host != null ? $"{Host}:{Port}" : Id;
}

// MARK: - Nachrichten zwischen den Rechnern (JSON wie am Mac, Feldnamen klein, null weggelassen)

/// <summary>Befehl Master → Slave, sofort (Mac: ClusterCommand).</summary>
public sealed class ClusterCommand
{
    public string Group { get; set; } = "";
    public string From { get; set; } = "";
    public string FromName { get; set; } = "";
    /// <summary>next · previous · golive · stop · goto · blackout · unlock · testpattern</summary>
    public string Cmd { get; set; } = "";
    /// <summary>Dateiname der Präsentation (klein, ohne Pfad)</summary>
    public string? File { get; set; }
    /// <summary>PowerPoint-Folienindex (inkl. ausgeblendeter Folien)</summary>
    public int? Slide { get; set; }
    public bool? On { get; set; }
    /// <summary>Port der Remote-API des Masters</summary>
    public int? ApiPort { get; set; }
}

/// <summary>Kompletter Stand Master → Slave, periodisch und bei Änderung (Mac: ClusterSync).</summary>
public sealed class ClusterSync
{
    public sealed class TimerState
    {
        public double Total { get; set; }
        public double Elapsed { get; set; }
        public string State { get; set; } = "stopped";
        public bool Blink { get; set; }
    }

    public sealed class MessageState
    {
        public bool Visible { get; set; }
        public string Text { get; set; } = "";
        public bool Blink { get; set; }
    }

    public string Group { get; set; } = "";
    public string From { get; set; } = "";
    public string FromName { get; set; } = "";
    public bool Live { get; set; }
    public bool Starting { get; set; }
    public string? File { get; set; }
    public int Slide { get; set; }
    public int Click { get; set; }
    public bool Blackout { get; set; }
    public bool Locked { get; set; }
    public TimerState Timer { get; set; } = new();
    public MessageState Message { get; set; } = new();
    public int? ApiPort { get; set; }
}

/// <summary>Bearbeitete Präsentation, die ein abgekoppelter Slave dem Master anbietet (Mac: FileOffer).</summary>
public sealed class FileOffer
{
    /// <summary>Dateischlüssel (kleingeschriebener Dateiname)</summary>
    public string File { get; set; } = "";
    public string Name { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long Size { get; set; }
}

/// <summary>Antwort eines Players: wer er ist und was er zeigt (Mac: ClusterInfo).</summary>
public sealed class ClusterInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Role { get; set; } = "standalone";
    public string Group { get; set; } = "";
    public bool Live { get; set; }
    public string? File { get; set; }
    public int Slide { get; set; }
    public bool Blackout { get; set; }
    public string? Issue { get; set; }
    public bool? Accepted { get; set; }
    public int? Click { get; set; }
    public bool? Detached { get; set; }
    public List<FileOffer>? Offers { get; set; }
}

/// <summary>Playlist des Masters mit Prüfsummen für den Datei-Abgleich (Mac: SyncManifest).</summary>
public sealed class SyncManifest
{
    public sealed class Entry
    {
        public string File { get; set; } = "";
        public string Name { get; set; } = "";
        public string Title { get; set; } = "";
        public string Sha256 { get; set; } = "";
        public long Size { get; set; }
    }

    public string Group { get; set; } = "";
    public string From { get; set; } = "";
    public List<Entry> Items { get; set; } = new();
    /// <summary>false, solange noch Prüfsummen berechnet werden</summary>
    public bool? Complete { get; set; }
}

public static class ClusterJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static T? Read<T>(byte[]? data) where T : class
    {
        if (data == null || data.Length == 0) return null;
        try { return JsonSerializer.Deserialize<T>(data, Options); }
        catch { return null; }
    }
}
