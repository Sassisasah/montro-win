using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;
using Montro.Core;
using Montro.PowerPoint;

namespace Montro.Cluster;

/// <summary>
/// Verbund mehrerer montro-Rechner (Master/Slave) für Redundanz, kompatibel mit montro für macOS
/// (Mac: ClusterManager.swift).
/// - Finden: DNS-SD-Dienst <c>_montro._tcp</c>, Verbundname im TXT-Record, dazu manuelle Adressen.
/// - Master → Slave: Befehle sofort (<c>/api/cluster/command</c>), dazu jede Sekunde und bei jeder
///   Änderung der komplette Stand (<c>/api/cluster/sync</c>). Der Slave korrigiert Abweichungen, die
///   länger als 1,5 s bestehen.
/// - Zuordnung der Präsentationen über den Dateinamen (klein).
/// Läuft auf dem UI-Thread; HTTP im Hintergrund.
/// </summary>
public sealed class ClusterManager : Observable
{
    private readonly AppState state;
    private readonly Dispatcher ui = Dispatcher.CurrentDispatcher;
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(1.5) };
    private readonly DnsSd dnsSd = new();
    private readonly DispatcherTimer ticker;
    private readonly Dictionary<string, DateTime> mismatchSince = new();
    private string lastSyncSignature = "";
    private DateTime lastSyncSent = DateTime.MinValue;
    private int tick;
    private bool browsing;
    private bool pushing;

    private ClusterRole role = ClusterRole.Standalone;
    private string group = "Verbund 1";
    private string deviceName = Environment.MachineName;
    private bool autoDiscovery = true;
    private bool slaveLocksLocalControl = true;
    private bool followTimer = true;
    private bool followMessage = true;
    private string? masterName;
    private DateTime? lastMasterContact;
    private string? issue;

    public ClusterManager(AppState state)
    {
        this.state = state;
        ticker = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Normal, (_, _) => Tick(), ui);
    }

    // MARK: - Einstellungen (gespeichert über AppState)

    /// <summary>Feste Kennung dieses Players</summary>
    public string InstanceId { get; private set; } = Guid.NewGuid().ToString().ToUpperInvariant();

    public ClusterRole Role { get => role; set { if (Set(ref role, value)) ConfigurationChanged(); } }
    public string Group { get => group; set { if (Set(ref group, value.Trim())) ConfigurationChanged(); } }
    public string DeviceName { get => deviceName; set { if (Set(ref deviceName, value.Trim())) ConfigurationChanged(); } }
    public bool AutoDiscovery { get => autoDiscovery; set { if (Set(ref autoDiscovery, value)) ConfigurationChanged(); } }

    /// <summary>Slave: Bedienung am Slave selbst sperren (Tastatur, Clicker, Knöpfe, Companion)</summary>
    public bool SlaveLocksLocalControl { get => slaveLocksLocalControl; set { if (Set(ref slaveLocksLocalControl, value)) state.QueueSave(); } }
    public bool FollowTimer { get => followTimer; set { if (Set(ref followTimer, value)) state.QueueSave(); } }
    public bool FollowMessage { get => followMessage; set { if (Set(ref followMessage, value)) state.QueueSave(); } }

    /// <summary>Manuell eingetragene Rechner („host:port“)</summary>
    public List<string> ManualAddresses { get; private set; } = new();

    public void Load(SavedCluster? s)
    {
        if (s != null)
        {
            if (!string.IsNullOrEmpty(s.InstanceId)) InstanceId = s.InstanceId;
            role = ClusterRoles.Parse(s.Role) ?? ClusterRole.Standalone;
            group = string.IsNullOrWhiteSpace(s.Group) ? "Verbund 1" : s.Group;
            deviceName = string.IsNullOrWhiteSpace(s.DeviceName) ? Environment.MachineName : s.DeviceName;
            autoDiscovery = s.AutoDiscovery;
            slaveLocksLocalControl = s.SlaveLocksLocalControl;
            followTimer = s.FollowTimer;
            followMessage = s.FollowMessage;
            ManualAddresses = s.ManualAddresses ?? new();
        }
        RebuildManualPeers();
        ticker.Start();
        ApplyConfiguration();
    }

    public SavedCluster Save() => new()
    {
        InstanceId = InstanceId,
        Role = ClusterRoles.Key(Role),
        Group = Group,
        DeviceName = DeviceName,
        AutoDiscovery = AutoDiscovery,
        SlaveLocksLocalControl = SlaveLocksLocalControl,
        FollowTimer = FollowTimer,
        FollowMessage = FollowMessage,
        ManualAddresses = ManualAddresses.ToList(),
    };

    // MARK: - Laufzeit

    public List<ClusterPeer> Peers { get; } = new();

    /// <summary>Slave: Name und Adresse des Masters (aus seinen Anfragen)</summary>
    public string? MasterName => masterName;
    public string? MasterHost { get; private set; }
    public int MasterPort { get; private set; } = Remote.RemoteApi.DefaultPort;

    /// <summary>Master: keinen Stand senden (während „Aktualisieren“ zeigen die Slaves weiter)</summary>
    public bool HoldSync { get; set; }

    /// <summary>true, während ein Befehl vom Master ausgeführt wird (umgeht die Bediensperre am Slave)</summary>
    public bool ApplyingRemote { get; private set; }

    public string? Issue { get => issue; private set { if (Set(ref issue, value)) RaiseSummary(); } }

    public bool IsActive => Role != ClusterRole.Standalone;

    /// <summary>Rechner dieses Verbunds (gleicher Name) oder manuell eingetragen</summary>
    public List<ClusterPeer> Members => Peers.Where(p => p.Manual || (p.Group == Group && p.Role is ClusterRole.Master or ClusterRole.Slave)).ToList();

    /// <summary>Gefundene Player, die nicht zum Verbund gehören</summary>
    public List<ClusterPeer> Others => Peers.Where(p => !p.Manual && !(p.Group == Group && p.Role is ClusterRole.Master or ClusterRole.Slave)).ToList();

    public List<ClusterPeer> Slaves => Members.Where(p => p.Role == ClusterRole.Slave || (p.Manual && p.Role == null)).ToList();

    public bool AnySlaveLive => Slaves.Any(p => p.Reachable && p.Live);

    public bool MasterReachable => lastMasterContact is { } t && (DateTime.Now - t).TotalSeconds < 4;

    /// <summary>Darf lokal bedient werden? (Slave mit Sperre: nur Befehle vom Master)</summary>
    public bool PermitsLocalControl =>
        Role != ClusterRole.Slave || state.FileSync.Detached || !SlaveLocksLocalControl || ApplyingRemote;

    /// <summary>Slave: wo der Master erreichbar ist (aus seinen Anfragen, sonst gefunden/manuell)</summary>
    public (string Host, int Port)? MasterAddress
    {
        get
        {
            if (MasterHost != null) return (MasterHost, MasterPort);
            if (Members.FirstOrDefault(p => p.Role == ClusterRole.Master) is { Host: { } h } m) return (h, m.Port);
            return null;
        }
    }

    /// <summary>Kurzer Status für Statusleiste und Einstellungen</summary>
    public string Summary
    {
        get
        {
            switch (Role)
            {
                case ClusterRole.Master:
                    var s = Slaves;
                    int ok = s.Count(p => p.Reachable && p.InSync != false);
                    return s.Count == 0 ? "Master · keine Slaves" : $"Master · {ok}/{s.Count} Slave(s) synchron";
                case ClusterRole.Slave:
                    if (state.FileSync.Detached) return "Slave · zeitweise abgekoppelt";
                    if (Issue != null) return $"Slave · {Issue}";
                    if (MasterReachable) return $"Slave von „{MasterName ?? "Master"}“ · verbunden";
                    return "Slave · kein Master erreichbar";
                default:
                    return "Einzelplatz";
            }
        }
    }

    public bool SummaryIsWarning => Role switch
    {
        ClusterRole.Master => Slaves.Any(p => !p.Reachable || p.InSync == false || p.Issue != null),
        ClusterRole.Slave => state.FileSync.Detached || Issue != null || !MasterReachable,
        _ => false,
    };

    /// <summary>Peers oder Status geändert (für Einstellungen und Statusleiste)</summary>
    public event Action? Changed;

    private void RaiseSummary()
    {
        Raise(nameof(Summary), nameof(SummaryIsWarning));
        Changed?.Invoke();
    }

    // MARK: - Konfiguration

    private void ConfigurationChanged()
    {
        state.QueueSave();
        ApplyConfiguration();
    }

    private void ApplyConfiguration()
    {
        Issue = null;
        mismatchSince.Clear();
        Peers.RemoveAll(p => !p.Manual);
        Announce();
        RaiseSummary();
        if (IsActive) DebugLog.Write($"Verbund: Rolle {ClusterRoles.Key(Role)}, Verbund „{Group}“, Name „{DeviceName}“");
    }

    /// <summary>
    /// DNS-SD-Anzeige erneuern: nur im Verbund und wenn die Fernsteuerung von außen erreichbar ist
    /// (Mac: bonjourService über die Remote-API). Auch nach jedem Neustart der Remote-API.
    /// </summary>
    public void Announce()
    {
        if (IsActive && state.Remote?.RunningPort is int port && !state.RemoteLocalOnly)
            dnsSd.Register(DeviceName, port, new Dictionary<string, string>
            {
                ["id"] = InstanceId,
                ["role"] = ClusterRoles.Key(Role),
                ["group"] = Group,
                ["v"] = "1",
            });
        else
            dnsSd.Unregister();
    }

    private void RebuildManualPeers()
    {
        var keep = Peers.Where(p => !p.Manual || p.Discovered).ToList();
        foreach (var p in keep) p.Manual = false;
        foreach (var raw in ManualAddresses)
        {
            if (ParseAddress(raw) is not var (host, port)) continue;
            string key = $"{host}:{port}";
            if (keep.FirstOrDefault(p => p.Host == host && p.Port == port) is { } existing)
                existing.Manual = true;
            else
                keep.Add(Peers.FirstOrDefault(p => p.Manual && p.ManualKey == key)
                         ?? new ClusterPeer { Id = $"manual:{key}", Name = host, Host = host, Port = port, Manual = true });
        }
        Peers.Clear();
        Peers.AddRange(keep);
        Changed?.Invoke();
    }

    public static (string Host, int Port)? ParseAddress(string raw)
    {
        string t = raw.Trim();
        if (t.Length == 0) return null;
        var parts = t.Split(':');
        if (parts.Length == 2 && int.TryParse(parts[1], out int p) && p is > 0 and < 65536) return (parts[0], p);
        return (t, Remote.RemoteApi.DefaultPort);
    }

    public void AddManual(string raw)
    {
        if (ParseAddress(raw) is not var (host, port)) return;
        string key = $"{host}:{port}";
        if (!ManualAddresses.Contains(key)) ManualAddresses.Add(key);
        state.QueueSave();
        RebuildManualPeers();
    }

    public void RemoveManual(ClusterPeer peer)
    {
        ManualAddresses.RemoveAll(a => a == peer.ManualKey);
        if (!peer.Discovered) Peers.Remove(peer);
        state.QueueSave();
        RebuildManualPeers();
    }

    /// <summary>Diesen Rechner zum Master machen (wenn der bisherige Master ausfällt).</summary>
    public void BecomeMaster() => Role = ClusterRole.Master;

    // MARK: - Finden (DNS-SD)

    private async Task Discover()
    {
        if (browsing) return;
        browsing = true;
        try
        {
            var found = await DnsSd.BrowseAsync();
            var seen = new HashSet<string>();
            foreach (var s in found)
            {
                string id = s.Txt.GetValueOrDefault("id") ?? $"dnssd:{s.InstanceName}";
                if (id == InstanceId) continue;   // uns selbst nicht anzeigen
                seen.Add(id);
                var peer = Peers.FirstOrDefault(p => p.Id == id);
                if (peer == null)
                {
                    peer = new ClusterPeer { Id = id };
                    Peers.Add(peer);
                }
                peer.Name = s.InstanceName;
                peer.Role = ClusterRoles.Parse(s.Txt.GetValueOrDefault("role"));
                peer.Group = s.Txt.GetValueOrDefault("group") ?? "";
                peer.Discovered = true;
                if (s.Host != null)
                {
                    peer.Host = s.Host;
                    peer.Port = s.Port;
                }
                // manuell eingetragenen Doppelgänger zusammenführen
                if (Peers.FirstOrDefault(p => p.Manual && p != peer && p.Host == peer.Host && p.Port == peer.Port) is { } dup)
                {
                    peer.Manual = true;
                    Peers.Remove(dup);
                }
            }
            // verschwundene, nicht manuelle Einträge entfernen
            foreach (var p in Peers.Where(p => p.Discovered && !seen.Contains(p.Id)).ToList())
            {
                if (p.Manual) p.Discovered = false;
                else Peers.Remove(p);
            }
            RaiseSummary();
        }
        finally
        {
            browsing = false;
        }
    }

    // MARK: - Takt

    private async void Tick()
    {
        if (!IsActive) return;
        int n = tick++;
        // Master: Stand bei jeder Änderung sofort, sonst jede Sekunde an alle Slaves
        if (Role == ClusterRole.Master && !HoldSync && !pushing)
        {
            var sync = MakeSync();
            string sig = Signature(sync);
            if (sig != lastSyncSignature || (DateTime.Now - lastSyncSent).TotalSeconds >= 1)
            {
                lastSyncSignature = sig;
                lastSyncSent = DateTime.Now;
                pushing = true;
                try { await PushSync(sync); }
                finally { pushing = false; }
            }
        }
        // Status der übrigen Rechner alle 2 s abfragen, im Netz suchen alle 10 s
        if (n % 8 == 0) await PollOthers();
        if (AutoDiscovery && n % 40 == 0) _ = Discover();
        if (n % 4 == 0) RaiseSummary();
    }

    private static string Signature(ClusterSync s) =>
        $"{s.Live}|{s.Starting}|{s.File}|{s.Slide}|{s.Click}|{s.Blackout}|{s.Locked}|{s.Timer.State}|{(int)s.Timer.Total}|{s.Timer.Blink}|{s.Message.Visible}|{s.Message.Text}|{s.Message.Blink}";

    private ClusterSync MakeSync()
    {
        var t = state.Timer;
        return new ClusterSync
        {
            Group = Group,
            From = InstanceId,
            FromName = DeviceName,
            Live = state.IsShowRunning,
            Starting = state.IsStarting,
            File = state.LiveItem is { } live ? FileKey(live.Path) : null,
            Slide = state.CurrentSlide,
            Click = state.ClickInSlide,
            Blackout = state.IsBlackout,
            Locked = state.IsLocked,
            Timer = new ClusterSync.TimerState
            {
                Total = t.Total,
                Elapsed = t.Elapsed,
                State = t.RunState switch { TimerRunState.Running => "running", TimerRunState.Paused => "paused", _ => "stopped" },
                Blink = t.ManualBlink,
            },
            Message = new ClusterSync.MessageState { Visible = state.Message.IsVisible, Text = state.Message.Text, Blink = state.Message.Blink },
            ApiPort = state.RemotePort,
        };
    }

    /// <summary>Dateischlüssel im Verbund: Dateiname klein (Pfade unterscheiden sich zwischen Rechnern).</summary>
    public static string FileKey(string path) => Path.GetFileName(path).ToLowerInvariant();

    // MARK: - Senden (Master)

    /// <summary>Befehl an alle Slaves weiterreichen (nur als Master, nicht beim Ausführen fremder Befehle).</summary>
    public void Forward(string kind, string? file = null, int? slide = null, bool? on = null)
    {
        if (Role != ClusterRole.Master || ApplyingRemote) return;
        var cmd = new ClusterCommand
        {
            Group = Group,
            From = InstanceId,
            FromName = DeviceName,
            Cmd = kind,
            File = file != null ? FileKey(file) : null,
            Slide = slide,
            On = on,
            ApiPort = state.RemotePort,
        };
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(cmd, ClusterJson.Options);
        foreach (var p in Slaves)
            if (p.Host is { } host) _ = Post(host, p.Port, "cluster/command", body);
        // Stand gleich danach erneut senden, damit Slaves schnell nachziehen
        lastSyncSignature = "";
    }

    private async Task PushSync(ClusterSync sync)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(sync, ClusterJson.Options);
        var targets = Slaves.Where(p => p.Host != null).ToList();
        var results = await Task.WhenAll(targets.Select(async p => (p.Id, Info: ClusterJson.Read<ClusterInfo>(await Post(p.Host!, p.Port, "cluster/sync", body)))));
        foreach (var (id, info) in results) Apply(info, id, sync);
    }

    private async Task PollOthers()
    {
        var slaveIds = Role == ClusterRole.Master ? Slaves.Select(s => s.Id).ToHashSet() : new HashSet<string>();
        foreach (var p in Peers.Where(p => p.Host != null && !slaveIds.Contains(p.Id)).ToList())
        {
            var info = ClusterJson.Read<ClusterInfo>(await Get(p.Host!, p.Port, "cluster"));
            Apply(info, p.Id, null);
        }
    }

    private void Apply(ClusterInfo? info, string id, ClusterSync? sync)
    {
        var p = Peers.FirstOrDefault(x => x.Id == id);
        if (p == null) return;
        if (info != null)
        {
            p.Reachable = true;
            p.LastContact = DateTime.Now;
            p.Name = info.Name;
            p.Role = ClusterRoles.Parse(info.Role);
            p.Group = info.Group;
            p.Live = info.Live;
            p.File = info.File ?? "";
            p.Slide = info.Slide;
            p.Blackout = info.Blackout;
            p.Issue = info.Issue;
            if (info.Accepted == false && p.Issue == null)
                p.Issue = info.Role == "slave" ? "anderer Verbund" : "ist kein Slave";
            // abgekoppelter Slave bietet bearbeitete Präsentationen an → holen und übernehmen
            if (Role == ClusterRole.Master && info.Offers is { Count: > 0 } offers && p.Host != null)
                state.FileSync.Consider(offers, info.Name, p.Host, p.Port);
            p.InSync = sync == null ? null
                : info.Live == sync.Live && (!sync.Live || (info.File == sync.File && info.Slide == sync.Slide && info.Blackout == sync.Blackout));
            // manuelle Einträge bekommen die echte ID, sobald bekannt
            if (p.Id.StartsWith("manual:") && Peers.All(x => x.Id != info.Id)) p.Id = info.Id;
        }
        else
        {
            p.Reachable = false;
            p.InSync = null;
        }
    }

    private async Task<byte[]?> Post(string host, int port, string path, byte[] body)
    {
        try
        {
            using var content = new ByteArrayContent(body);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            using var r = await http.PostAsync($"http://{host}:{port}/api/{path}", content);
            return await r.Content.ReadAsByteArrayAsync();
        }
        catch
        {
            return null;
        }
    }

    public async Task<byte[]?> Get(string host, int port, string path)
    {
        try { return await http.GetByteArrayAsync($"http://{host}:{port}/api/{path}"); }
        catch { return null; }
    }

    /// <summary>Aktuelle Position (Folie, Klick) eines Slaves, der dieselbe Präsentation zeigt – frisch abgefragt.</summary>
    public async Task<(int Slide, int Click)?> SlavePosition(string file)
    {
        foreach (var p in Slaves.Where(p => p.Reachable && p.Live && p.File == file && p.Host != null))
        {
            var info = ClusterJson.Read<ClusterInfo>(await Get(p.Host!, p.Port, "cluster"));
            if (info is { Live: true, Slide: > 0 } && info.File == file) return (info.Slide, info.Click ?? 0);
        }
        return null;
    }

    // MARK: - Empfangen (Slave) – aufgerufen von der Remote-API auf dem UI-Thread

    public ClusterInfo Info(bool? accepted = null)
    {
        var fs = state.FileSync;
        var offers = fs.Offers();
        bool detached = fs.Detached && Role == ClusterRole.Slave;
        return new ClusterInfo
        {
            Id = InstanceId,
            Name = DeviceName,
            Role = ClusterRoles.Key(Role),
            Group = Group,
            Live = state.IsShowRunning || state.IsStarting,
            File = state.LiveItem is { } live ? FileKey(live.Path) : null,
            Slide = state.CurrentSlide,
            Blackout = state.IsBlackout,
            Issue = detached ? "zeitweise abgekoppelt" : Issue,
            Accepted = accepted,
            Click = state.ClickInSlide,
            Detached = detached ? true : null,
            Offers = offers.Count > 0 ? offers : null,
        };
    }

    private bool Accepts(string g) => Role == ClusterRole.Slave && g == Group;

    /// <summary>Schonfrist nach „Wieder in den Verbund“ mit laufender Präsentation (Mac: rejoinGrace).</summary>
    public bool RejoinGrace { get; set; }

    public ClusterInfo Receive(ClusterCommand c, string? fromHost)
    {
        if (!Accepts(c.Group)) return Info(accepted: false);
        NoteMaster(c.FromName, fromHost, c.ApiPort);
        if (state.FileSync.Detached)
        {
            // Abgekoppelt: Läuft hier eine Präsentation, folgen die Klicks des Masters trotzdem
            // (Clicker/Companion hängen am Master). Live schalten, Stopp und Sprünge nicht.
            if (!state.IsShowRunning || c.Cmd is not ("next" or "previous" or "blackout" or "unlock")) return Info(accepted: false);
            RunRemote(() =>
            {
                switch (c.Cmd)
                {
                    case "next": state.Next(); break;
                    case "previous": state.Previous(); break;
                    case "unlock": state.Unlock(); break;
                    case "blackout": state.SetBlackout(c.On ?? !state.IsBlackout); break;
                }
            });
            return Info(accepted: true);
        }
        if (c.Cmd is "golive" or "stop") RejoinGrace = false;
        RunRemote(() =>
        {
            switch (c.Cmd)
            {
                case "next": state.Next(); break;
                case "previous": state.Previous(); break;
                case "stop": _ = state.Stop(); break;
                case "unlock": state.Unlock(); break;
                case "blackout": state.SetBlackout(c.On ?? !state.IsBlackout); break;
                case "testpattern": state.SetTestPattern(c.On ?? !state.IsTestPattern); break;
                case "goto":
                    if (c.Slide is int slide) state.Jump(slide);
                    break;
                case "golive":
                    if (c.File is { } file)
                    {
                        if (ItemForFile(file) is { } match)
                        {
                            Issue = null;
                            state.Selected = match;
                            _ = state.GoLive(match, c.Slide);
                        }
                        else Issue = $"Präsentation „{file}“ fehlt";
                    }
                    break;
            }
        });
        return Info(accepted: true);
    }

    public ClusterInfo Receive(ClusterSync m, string? fromHost)
    {
        if (!Accepts(m.Group)) return Info(accepted: false);
        NoteMaster(m.FromName, fromHost, m.ApiPort);
        if (state.FileSync.Detached) return Info(accepted: false);
        var s = state;
        RunRemote(() =>
        {
            string? myFile = s.LiveItem is { } live ? FileKey(live.Path) : null;
            // Schonfrist nach dem Wiederverbinden: eigene laufende Präsentation nicht anfassen
            if (RejoinGrace)
            {
                if (!s.IsShowRunning && !s.IsStarting) RejoinGrace = false;
                else if (m.Live && m.File == myFile)
                {
                    RejoinGrace = false;
                    DebugLog.Write("Verbund: Master zeigt dieselbe Präsentation – Schonfrist beendet");
                }
            }
            if (RejoinGrace)
            {
                foreach (var k in new[] { "golive", "stop", "slide", "black" }) mismatchSince.Remove(k);
            }
            else
            {
                bool busy = s.IsStarting;
                // 1. Präsentation live / gestoppt
                if ((m.Live || m.Starting) && m.File is { } file)
                {
                    if (ItemForFile(file) is { } match)
                    {
                        if (Issue?.Contains("fehlt") == true) Issue = null;
                        if (!busy && (!s.IsShowRunning || myFile != file))
                        {
                            if (Persists("golive"))
                            {
                                DebugLog.Write($"Verbund: starte „{file}“ (Abgleich mit Master)");
                                s.Selected = match;
                                _ = s.GoLive(match, m.Slide > 0 ? m.Slide : null);
                            }
                        }
                        else mismatchSince.Remove("golive");
                    }
                    else Issue = $"Präsentation „{file}“ fehlt";
                }
                else if (!m.Live && !m.Starting)
                {
                    mismatchSince.Remove("golive");
                    if (s.IsShowRunning && !busy)
                    {
                        if (Persists("stop"))
                        {
                            DebugLog.Write("Verbund: stoppe (Master ist nicht live)");
                            _ = s.Stop();
                        }
                    }
                    else mismatchSince.Remove("stop");
                    if (Issue?.Contains("fehlt") == true) Issue = null;
                }

                // 2. Folie und Blackscreen (nur wenn dieselbe Präsentation läuft)
                if (m.Live && s.IsShowRunning && !busy && myFile == m.File)
                {
                    if (!s.IsLocked && m.Slide > 0 && s.CurrentSlide != m.Slide)
                    {
                        if (Persists("slide"))
                        {
                            DebugLog.Write($"Verbund: Folie {s.CurrentSlide} → {m.Slide} (Abgleich)");
                            s.Jump(m.Slide);
                        }
                    }
                    else mismatchSince.Remove("slide");
                    if (s.IsBlackout != m.Blackout)
                    {
                        if (Persists("black")) s.SetBlackout(m.Blackout);
                    }
                    else mismatchSince.Remove("black");
                }
            }

            // 3. Timer
            if (FollowTimer)
            {
                var t = s.Timer;
                var remote = m.Timer.State switch { "running" => TimerRunState.Running, "paused" => TimerRunState.Paused, _ => TimerRunState.Stopped };
                if (t.RunState != remote || Math.Abs(t.Total - m.Timer.Total) > 0.5 || Math.Abs(t.Elapsed - m.Timer.Elapsed) > 1.0 || t.ManualBlink != m.Timer.Blink)
                    t.ApplyRemote(m.Timer.Total, m.Timer.Elapsed, remote, m.Timer.Blink);
            }

            // 4. Regie-Nachricht
            if (FollowMessage)
            {
                var msg = s.Message;
                if (m.Message.Visible)
                {
                    if (!msg.IsVisible || msg.Text != m.Message.Text) msg.Send(m.Message.Text);
                }
                else if (msg.IsVisible) msg.Clear();
                if (msg.Blink != m.Message.Blink) msg.Blink = m.Message.Blink;
            }
        });
        return Info(accepted: true);
    }

    /// <summary>Abweichung muss 1,5 s bestehen, bevor korrigiert wird (Befehle haben Vorrang).</summary>
    private bool Persists(string key)
    {
        if (mismatchSince.TryGetValue(key, out var since))
        {
            if ((DateTime.Now - since).TotalSeconds < 1.5) return false;
            mismatchSince.Remove(key);
            return true;
        }
        mismatchSince[key] = DateTime.Now;
        return false;
    }

    private void NoteMaster(string name, string? host, int? port)
    {
        bool changed = masterName != name || !MasterReachable;
        masterName = name;
        lastMasterContact = DateTime.Now;
        if (host != null) MasterHost = host;
        if (port is int p) MasterPort = p;
        if (changed) RaiseSummary();
    }

    private void RunRemote(Action body)
    {
        ApplyingRemote = true;
        try { body(); }
        finally { ApplyingRemote = false; }
    }

    public PresentationItem? ItemForFile(string file)
    {
        string key = file.ToLowerInvariant();
        return state.Items.FirstOrDefault(i => FileKey(i.Path) == key)
               ?? state.Items.FirstOrDefault(i => i.Title.ToLowerInvariant() == Path.GetFileNameWithoutExtension(key));
    }

    public void Shutdown()
    {
        ticker.Stop();
        dnsSd.Dispose();
    }
}

/// <summary>Gespeicherte Verbund-Einstellungen (in state.json).</summary>
public sealed class SavedCluster
{
    public string? InstanceId { get; set; }
    public string? Role { get; set; }
    public string? Group { get; set; }
    public string? DeviceName { get; set; }
    public bool AutoDiscovery { get; set; } = true;
    public bool SlaveLocksLocalControl { get; set; } = true;
    public bool FollowTimer { get; set; } = true;
    public bool FollowMessage { get; set; } = true;
    public List<string>? ManualAddresses { get; set; }
}
