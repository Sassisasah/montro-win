using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Threading;
using Montro.Core;

namespace Montro.Cluster;

/// <summary>Neue Version, die bereitliegt, weil die Datei gerade live ist (Mac: StagedVersion).</summary>
public sealed record StagedVersion(string File, string Name, string Sha256, long Size, string Source);

/// <summary>
/// Datei-Abgleich im Verbund (Mac: FileSync.swift):
/// - Prüfsummen (SHA-256) aller Präsentationen, im Hintergrund, neu bei Änderung der Datei.
/// - Slave folgt: holt alle 3 s die Playlist des Masters (<c>/api/cluster/manifest</c>), lädt fehlende
///   oder geänderte Dateien (<c>/api/cluster/file</c>) und gleicht Reihenfolge und Inhalt an.
/// - Abgekoppelter Slave: folgt nicht, darf bearbeiten und selbst live schalten; eine geänderte
///   Präsentation wird dem Master angeboten, sobald sie hier live war (oder beim Wiederverbinden).
/// - Ist eine Datei gerade live, wird die neue Version bereitgelegt („Aktualisieren“ bzw. beim
///   nächsten Live-Schalten). Ersetzte Dateien werden vorher nach „Versionen“ gesichert.
/// </summary>
public sealed class FileSync : Observable
{
    private readonly AppState state;
    private readonly Dispatcher ui = Dispatcher.CurrentDispatcher;
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(30) };
    private readonly HttpClient quick = new() { Timeout = TimeSpan.FromSeconds(3) };
    private readonly Dictionary<Guid, (long Size, DateTime Modified, string Hash)> hashes = new();
    private readonly HashSet<Guid> hashing = new();
    private readonly HashSet<string> inFlight = new();
    private readonly Dictionary<string, StagedVersion> staged = new();
    private readonly DispatcherTimer ticker;
    private bool syncing;
    private bool detached;
    private bool followPlaylist = true;
    private string? activity;
    private string? lastError;

    public FileSync(AppState state)
    {
        this.state = state;
        ticker = new DispatcherTimer(TimeSpan.FromSeconds(3), DispatcherPriority.Background, (_, _) => Tick(), ui);
    }

    private ClusterManager Cluster => state.Cluster;

    // MARK: - Ordner (in „Dokumente\montro“, damit PowerPoint und Explorer sie sehen)

    private static string Folder(string name)
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "montro", name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Vom Master geholte Präsentationen, die es hier noch nicht gab</summary>
    public static string SyncDirectory => Folder("Verbund");
    /// <summary>Sicherungen ersetzter Dateien</summary>
    public static string VersionsDirectory => Folder("Versionen");
    private static string IncomingDirectory => Folder("incoming");

    // MARK: - Zustand (gespeichert über AppState)

    /// <summary>Slave: Playlist und Dateien vom Master übernehmen</summary>
    public bool FollowPlaylist { get => followPlaylist; set { if (Set(ref followPlaylist, value)) state.QueueSave(); } }

    /// <summary>Slave: zeitweise aus dem Verbund genommen (zum Bearbeiten)</summary>
    public bool Detached { get => detached; private set { if (Set(ref detached, value)) { state.QueueSave(); Cluster.Announce(); Changed(); } } }

    /// <summary>Freigegebene eigene Versionen (Dateischlüssel → Prüfsumme), die der Slave dem Master anbietet</summary>
    public Dictionary<string, string> Released { get; private set; } = new();

    /// <summary>Letzter bekannter Stand des Masters (Dateischlüssel → Prüfsumme)</summary>
    public Dictionary<string, string> MasterHashes { get; private set; } = new();

    public string? Activity { get => activity; private set { if (Set(ref activity, value)) Changed(); } }
    public string? LastError { get => lastError; private set { if (Set(ref lastError, value)) Changed(); } }

    public void Load(SavedFileSync? s)
    {
        if (s != null)
        {
            followPlaylist = s.FollowPlaylist;
            detached = s.Detached;
            Released = s.Released ?? new();
            MasterHashes = s.MasterHashes ?? new();
        }
        ticker.Start();
    }

    public SavedFileSync Save() => new()
    {
        FollowPlaylist = FollowPlaylist,
        Detached = Detached,
        Released = new(Released),
        MasterHashes = new(MasterHashes),
    };

    /// <summary>Banner, Abzeichen und Status neu zeichnen.</summary>
    private void Changed()
    {
        foreach (var item in state.Items) item.SyncBadge = Badge(item);
        Raise(nameof(Banner));
    }

    public static string Key(PresentationItem item) => ClusterManager.FileKey(item.Path);

    public PresentationItem? ItemForKey(string key) => state.Items.FirstOrDefault(i => Key(i) == key);

    private bool IsLive(PresentationItem item) => item == state.LiveItem && (state.IsShowRunning || state.IsStarting || state.IsLocked);

    public string? Hash(PresentationItem item) => hashes.TryGetValue(item.Id, out var h) && Stamp(item.Path) is { } s && s.Size == h.Size && s.Modified == h.Modified ? h.Hash : null;

    private static (long Size, DateTime Modified)? Stamp(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            return fi.Exists ? (fi.Length, fi.LastWriteTimeUtc) : null;
        }
        catch
        {
            return null;
        }
    }

    // MARK: - Takt

    private void Tick()
    {
        if (!Cluster.IsActive) return;
        // Prüfsummen nachziehen (neue oder geänderte Dateien)
        foreach (var item in state.Items.ToList())
            if (Hash(item) == null) ComputeHash(item);
        if (Cluster.Role == ClusterRole.Slave && !syncing && Cluster.MasterAddress is var (host, port))
        {
            syncing = true;
            _ = SlaveSync(host, port).ContinueWith(_ => ui.InvokeAsync(() => syncing = false));
        }
        Changed();
    }

    private void ComputeHash(PresentationItem item)
    {
        if (!hashing.Add(item.Id)) return;
        string path = item.Path;
        _ = Task.Run(async () =>
        {
            var stamp = Stamp(path);
            string? h = await Sha256(path);
            await ui.InvokeAsync(() =>
            {
                hashing.Remove(item.Id);
                if (h != null && stamp is { } s && Stamp(path) is { } now && now == s && item.Path == path)
                    hashes[item.Id] = (s.Size, s.Modified, h);
            });
        });
    }

    public static async Task<string?> Sha256(string path)
    {
        try
        {
            await using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20, useAsync: true);
            using var sha = SHA256.Create();
            byte[] h = await sha.ComputeHashAsync(f).ConfigureAwait(false);
            return Convert.ToHexString(h).ToLowerInvariant();
        }
        catch
        {
            return null;
        }
    }

    // MARK: - Abzeichen und Banner (Mac: syncBadges, SyncBanners)

    private string? Badge(PresentationItem item)
    {
        string key = Key(item);
        if (staged.TryGetValue(key, out var v)) return IsLive(item) ? null : "Neue Version";
        if (Cluster.Role != ClusterRole.Slave || Hash(item) is not { } h) return null;
        if (Released.GetValueOrDefault(key) == h && MasterHashes.GetValueOrDefault(key) != h) return "An Master";
        if (Detached && MasterHashes.Count > 0 && MasterHashes.GetValueOrDefault(key) != h) return "Geändert";
        return null;
    }

    /// <summary>Hinweis über der Playlist (null = keiner): Text, Knopf, Warnung.</summary>
    public (string Text, string? Button, bool Warning)? Banner
    {
        get
        {
            if (state.LiveItem is { } live && StagedVersion(live) is { } v)
                return ($"Neue Version von „{live.Title}“ bereit (von {v.Source})", state.ReloadingInPlace ? "Aktualisiere …" : "Aktualisieren", false);
            if (Cluster.Role == ClusterRole.Slave && Detached)
                return ("Zeitweise abgekoppelt – folgt dem Master nicht. Bearbeiten und selbst live schalten möglich.", "Wieder in den Verbund", true);
            if (Activity is { } a) return (a, null, false);
            if (LastError is { } e) return (e, null, true);
            if (Cluster.Role == ClusterRole.Slave)
            {
                string t = Cluster.MasterReachable ? $"Slave von „{Cluster.MasterName ?? "Master"}“" : "Slave – kein Master erreichbar";
                if (PendingHandovers > 0) t += $" · {PendingHandovers} Änderung(en) warten auf den Master";
                return (t, "Abkoppeln (zum Bearbeiten)", false);
            }
            return null;
        }
    }

    // MARK: - Abkoppeln / Wiederverbinden (Slave)

    public void Detach()
    {
        if (Cluster.Role != ClusterRole.Slave || Detached) return;
        Detached = true;
        DebugLog.Write("Verbund: Slave zeitweise abgekoppelt");
        state.StatusMessage = "Abgekoppelt – dieser Rechner folgt dem Master nicht mehr und kann bearbeiten und selbst live schalten.";
    }

    /// <summary>Wieder in den Verbund: eigene Änderungen gehen an den Master, danach folgt der Rechner wieder.</summary>
    public void Rejoin()
    {
        if (!Detached) return;
        foreach (var item in state.Items)
        {
            if (Hash(item) is not { } h) continue;
            string key = Key(item);
            // geändert – oder neu hier hinzugefügt (nur wenn der Stand des Masters bekannt ist)
            if (MasterHashes.Count > 0 && MasterHashes.GetValueOrDefault(key) != h) Released[key] = h;
        }
        // Läuft hier gerade eine Präsentation, nicht sofort vom Master stoppen lassen
        Cluster.RejoinGrace = state.IsShowRunning || state.IsStarting;
        Detached = false;
        DebugLog.Write($"Verbund: Slave wieder verbunden, {PendingHandovers} Änderung(en) an den Master");
        state.StatusMessage = PendingHandovers > 0
            ? $"Wieder im Verbund – {PendingHandovers} geänderte Präsentation(en) gehen an den Master."
            : "Wieder im Verbund.";
    }

    /// <summary>Eigene Änderungen verwerfen: beim nächsten Abgleich den Stand des Masters holen.</summary>
    public void DiscardLocalChanges()
    {
        Released = new();
        state.QueueSave();
        DebugLog.Write("Verbund: eigene Änderungen verworfen – Stand des Masters wird übernommen");
        Changed();
    }

    /// <summary>Nach dem Live-Schalten: Am abgekoppelten Slave gibt das eine geänderte Version frei.</summary>
    public void WentLive(PresentationItem item)
    {
        if (Cluster.Role != ClusterRole.Slave || !Detached || MasterHashes.Count == 0 || Hash(item) is not { } h) return;
        string key = Key(item);
        if (MasterHashes.GetValueOrDefault(key) == h || Released.GetValueOrDefault(key) == h) return;
        Released[key] = h;
        state.QueueSave();
        DebugLog.Write($"Verbund: „{key}“ ist hier live – neue Version wird dem Master angeboten");
    }

    public int PendingHandovers => Released.Count(kv => MasterHashes.GetValueOrDefault(kv.Key) != kv.Value);

    /// <summary>Angebote an den Master (Teil der Antwort an den Master)</summary>
    public List<FileOffer> Offers()
    {
        if (Cluster.Role != ClusterRole.Slave) return new();
        return state.Items.Select(item =>
        {
            if (Hash(item) is not { } h || !hashes.TryGetValue(item.Id, out var info)) return null;
            string key = Key(item);
            if (Released.GetValueOrDefault(key) != h || MasterHashes.GetValueOrDefault(key) == h) return null;
            return new FileOffer { File = key, Name = Path.GetFileName(item.Path), Sha256 = h, Size = info.Size };
        }).OfType<FileOffer>().ToList();
    }

    // MARK: - Manifest / Dateien ausliefern (alle Rollen)

    public SyncManifest Manifest()
    {
        var entries = state.Items.Select(item =>
        {
            if (Hash(item) is not { } h || !hashes.TryGetValue(item.Id, out var info) || !File.Exists(item.Path)) return null;
            return new SyncManifest.Entry { File = Key(item), Name = Path.GetFileName(item.Path), Title = item.Title, Sha256 = h, Size = info.Size };
        }).OfType<SyncManifest.Entry>().ToList();
        return new SyncManifest { Group = Cluster.Group, From = Cluster.DeviceName, Items = entries, Complete = entries.Count == state.Items.Count };
    }

    /// <summary>Datei für <c>/api/cluster/file</c> – nur Präsentationen aus der Playlist, nur im eigenen Verbund.</summary>
    public string? ServableFile(string key, string? group)
    {
        if (!Cluster.IsActive || (group != null && group != Cluster.Group)) return null;
        return ItemForKey(key.ToLowerInvariant()) is { } item && File.Exists(item.Path) ? item.Path : null;
    }

    // MARK: - Slave: dem Master folgen

    private async Task SlaveSync(string host, int port)
    {
        SyncManifest? m;
        try { m = ClusterJson.Read<SyncManifest>(await quick.GetByteArrayAsync($"http://{host}:{port}/api/cluster/manifest")); }
        catch { return; }
        await ui.InvokeAsync(async () =>
        {
            if (m == null || m.Group != Cluster.Group || Cluster.Role != ClusterRole.Slave) return;
            var masterHashes = m.Items.ToDictionary(e => e.File, e => e.Sha256);
            if (!masterHashes.OrderBy(k => k.Key).SequenceEqual(MasterHashes.OrderBy(k => k.Key)))
            {
                MasterHashes = masterHashes;
                state.QueueSave();
            }
            // Übergaben, die der Master übernommen hat, sind erledigt
            var done = Released.Where(kv => masterHashes.GetValueOrDefault(kv.Key) == kv.Value).Select(kv => kv.Key).ToList();
            if (done.Count > 0)
            {
                foreach (var k in done) Released.Remove(k);
                state.QueueSave();
                DebugLog.Write($"Verbund: Master hat {string.Join(", ", done)} übernommen");
            }
            if (Detached || !FollowPlaylist) return;
            foreach (var e in m.Items)
            {
                if (ItemForKey(e.File) is { } local)
                {
                    if (Hash(local) is not { } h) continue;                  // Prüfsumme kommt noch
                    if (h == e.Sha256 || Released.GetValueOrDefault(e.File) == h) continue;
                }
                if (staged.TryGetValue(e.File, out var st) && st.Sha256 == e.Sha256) continue;
                // Laden und Prüfsumme im Hintergrund (nicht auf dem UI-Thread)
                if (await Task.Run(() => Download(e.File, e.Name, e.Sha256, host, port)) is not { } file) continue;
                await Install(e.File, e.Name, file, e.Sha256, e.Size, Cluster.MasterName ?? "Master");
            }
            MirrorPlaylist(m);
        }).Task.Unwrap();
    }

    /// <summary>Reihenfolge und Inhalt der Playlist an den Master angleichen (sobald alle Dateien da sind).</summary>
    private void MirrorPlaylist(SyncManifest m)
    {
        if (state.IsStarting || m.Complete != true || m.Items.Count == 0) return;
        var ordered = new List<PresentationItem>();
        foreach (var e in m.Items)
        {
            if (ItemForKey(e.File) is not { } it) return;   // fehlt noch → später
            if (!ordered.Contains(it)) ordered.Add(it);
        }
        var keys = m.Items.Select(e => e.File).ToHashSet();
        var extra = state.Items.Where(i => !keys.Contains(Key(i))).ToList();
        // Behalten: was gerade läuft und eigene Präsentationen, die noch an den Master gehen
        bool Keep(PresentationItem i) => IsLive(i) || Released.ContainsKey(Key(i));
        var removable = extra.Where(i => !Keep(i)).ToList();
        if (removable.Count > 0)
        {
            DebugLog.Write($"Verbund: {removable.Count} Präsentation(en) entfernt, die der Master nicht hat");
            foreach (var r in removable) state.Remove(r);
        }
        var target = ordered.Concat(extra.Where(Keep)).ToList();
        for (int i = 0; i < target.Count; i++)
        {
            int at = state.Items.IndexOf(target[i]);
            if (at >= 0 && at != i) state.Move(at, i);
        }
    }

    // MARK: - Master: Angebote übernehmen

    public void Consider(List<FileOffer> offers, string peerName, string host, int port)
    {
        if (Cluster.Role != ClusterRole.Master) return;
        foreach (var o in offers)
        {
            if (ItemForKey(o.File) is { } local && Hash(local) == o.Sha256) continue;
            if (staged.TryGetValue(o.File, out var st) && st.Sha256 == o.Sha256) continue;
            if (inFlight.Contains(o.File + o.Sha256)) continue;
            DebugLog.Write($"Verbund: „{peerName}“ bietet neue Version von „{o.Name}“ an");
            _ = Task.Run(async () =>
            {
                if (await Download(o.File, o.Name, o.Sha256, host, port) is { } file)
                    await ui.InvokeAsync(() => Install(o.File, o.Name, file, o.Sha256, o.Size, peerName)).Task.Unwrap();
            });
        }
    }

    // MARK: - Übertragen

    private async Task<string?> Download(string key, string name, string sha256, string host, int port)
    {
        string flight = key + sha256;
        lock (inFlight) if (!inFlight.Add(flight)) return null;
        try
        {
            await ui.InvokeAsync(() => Activity = $"Lade „{name}“ …");
            DebugLog.Write($"Verbund: lade „{name}“ von {host}");
            string url = $"http://{host}:{port}/api/cluster/file?name={Uri.EscapeDataString(key)}&group={Uri.EscapeDataString(Cluster.Group)}";
            using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            if (!resp.IsSuccessStatusCode)
            {
                await ui.InvokeAsync(() => LastError = $"„{name}“ konnte nicht geladen werden ({(int)resp.StatusCode})");
                return null;
            }
            string dest = Path.Combine(IncomingDirectory, $"{Guid.NewGuid()}-{name}");
            await using (var f = File.Create(dest))
                await resp.Content.CopyToAsync(f);
            if (await Sha256(dest) != sha256)
            {
                File.Delete(dest);
                DebugLog.Write($"Verbund: Prüfsumme von „{name}“ falsch – verworfen");
                await ui.InvokeAsync(() => LastError = $"„{name}“: Prüfsumme stimmt nicht (Datei hat sich während der Übertragung geändert?)");
                return null;
            }
            await ui.InvokeAsync(() => LastError = null);
            return dest;
        }
        catch (Exception e)
        {
            DebugLog.Write($"Verbund: Download fehlgeschlagen – {e.Message}");
            await ui.InvokeAsync(() => LastError = $"„{name}“: {e.Message}");
            return null;
        }
        finally
        {
            lock (inFlight) inFlight.Remove(flight);
            await ui.InvokeAsync(() => Activity = null);
        }
    }

    // MARK: - Einspielen

    /// <summary>Neue Version einspielen – oder bereitlegen, wenn die Datei gerade live bzw. ungesichert offen ist.</summary>
    public async Task Install(string key, string name, string file, string sha256, long size, string source)
    {
        var version = new StagedVersion(file, name, sha256, size, source);
        if (ItemForKey(key) is { } item)
        {
            if (IsLive(item))
            {
                Stage(key, version, "läuft gerade");
                return;
            }
            // In PowerPoint offen? Nur ohne ungesicherte Änderungen schließen.
            if (!await state.CloseInPowerPointIfSaved(item.Path))
            {
                Stage(key, version, "in PowerPoint mit ungesicherten Änderungen geöffnet");
                return;
            }
            if (IsLive(item))
            {
                Stage(key, version, "läuft gerade");
                return;
            }
            try
            {
                string target = item.Path;
                // im Hintergrund kopieren: große Dateien würden sonst die Bedienung (und die Fernsteuerung) anhalten
                await Task.Run(() => { Backup(target); File.Copy(file, target, overwrite: true); });

                File.Delete(file);
            }
            catch (Exception e)
            {
                LastError = $"„{name}“ konnte nicht ersetzt werden: {e.Message}";
                DebugLog.Write($"Verbund: Ersetzen fehlgeschlagen – {e.Message}");
                return;
            }
            if (staged.TryGetValue(key, out var st) && st.Sha256 == sha256) staged.Remove(key);
            DebugLog.Write($"Neue Version von „{name}“ übernommen (von {source})");
            if (Stamp(item.Path) is { } s) hashes[item.Id] = (s.Size, s.Modified, sha256);
            state.AdoptNewVersion(item);
            state.StatusMessage = $"Neue Version von „{item.Title}“ übernommen (von {source})";
        }
        else
        {
            // Präsentation gibt es hier noch nicht → in den Verbund-Ordner legen und hinzufügen
            string dest = Path.Combine(SyncDirectory, name);
            try
            {
                await Task.Run(() => File.Copy(file, dest, overwrite: true));
                File.Delete(file);
            }
            catch (Exception e)
            {
                LastError = $"„{name}“ konnte nicht abgelegt werden: {e.Message}";
                return;
            }
            DebugLog.Write($"Verbund: „{name}“ neu hinzugefügt (von {source})");
            state.Add([dest], select: false);
            if (state.Items.FirstOrDefault(i => string.Equals(i.Path, dest, StringComparison.OrdinalIgnoreCase)) is { } added
                && Stamp(dest) is { } s)
                hashes[added.Id] = (s.Size, s.Modified, sha256);
        }
        Changed();
    }

    private void Stage(string key, StagedVersion v, string reason)
    {
        if (staged.TryGetValue(key, out var old) && old.File != v.File) try { File.Delete(old.File); } catch { }
        staged[key] = v;
        DebugLog.Write($"Neue Version von „{v.Name}“ bereitgelegt ({reason})");
        state.StatusMessage = $"Neue Version von „{v.Name}“ bereit (von {v.Source}) – {reason}; wird beim nächsten Live-Schalten übernommen.";
        Changed();
    }

    private static void Backup(string path)
    {
        string dest = Path.Combine(VersionsDirectory,
            $"{Path.GetFileNameWithoutExtension(path)} ({DateTime.Now:yyyyMMdd-HHmmss}){Path.GetExtension(path)}");
        File.Copy(path, dest, overwrite: true);
    }

    /// <summary>Bereitliegende Version einer Präsentation einspielen (vor dem Live-Schalten). true = übernommen.</summary>
    public async Task<bool> ApplyStaged(PresentationItem item)
    {
        string key = Key(item);
        if (!staged.Remove(key, out var v)) return false;
        await Install(key, v.Name, v.File, v.Sha256, v.Size, v.Source);
        return !staged.ContainsKey(key);
    }

    /// <summary>Alle bereitliegenden Versionen einspielen, deren Präsentation nicht live ist (nach dem Stopp).</summary>
    public async Task ApplyAllStaged()
    {
        foreach (var key in staged.Keys.ToList())
            if (ItemForKey(key) is { } item && !IsLive(item)) await ApplyStaged(item);
    }

    public StagedVersion? StagedVersion(PresentationItem item) => staged.GetValueOrDefault(Key(item));

    public void Shutdown() => ticker.Stop();
}

/// <summary>Gespeicherter Stand des Datei-Abgleichs (in state.json).</summary>
public sealed class SavedFileSync
{
    public bool FollowPlaylist { get; set; } = true;
    public bool Detached { get; set; }
    public Dictionary<string, string>? Released { get; set; }
    public Dictionary<string, string>? MasterHashes { get; set; }
}
