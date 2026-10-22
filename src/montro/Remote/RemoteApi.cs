using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;
using Montro.Core;
using Montro.Display;

namespace Montro.Remote;

/// <summary>
/// Fernsteuerung per HTTP/JSON (Mac: RemoteAPI.swift, docs/REMOTE-API.md im Repo Sassisasah/montro),
/// z. B. für das Companion-Modul / Stream Deck. Eigener kleiner Server auf <see cref="TcpListener"/>
/// – kein HttpListener, der bräuchte für „alle Adressen“ eine URL-Reservierung mit Adminrechten.
/// Befehle laufen auf dem UI-Thread; jede Befehls-Antwort enthält <c>{"ok": true, "state": {…}}</c>.
/// </summary>
public sealed class RemoteApi
{
    public const int DefaultPort = 8765;
    public const int ApiVersion = 1;

    private readonly AppState state;
    private readonly Dispatcher ui;
    private TcpListener? listener;
    private CancellationTokenSource? cts;

    public RemoteApi(AppState state)
    {
        this.state = state;
        ui = Dispatcher.CurrentDispatcher;
    }

    /// <summary>Port, auf dem gerade gelauscht wird (null = aus oder Fehler).</summary>
    public int? RunningPort { get; private set; }

    public string? LastError { get; private set; }

    public event Action? StatusChanged;

    /// <summary>(Neu) starten mit den Einstellungen aus dem Zustand; aus = nur stoppen.</summary>
    public async void Restart()
    {
        Stop();
        if (!state.RemoteEnabled) return;
        int port = state.RemotePort;
        // kurz warten, damit der alte Listener den Port freigibt (mehrfaches Tippen = ein Neustart)
        var my = cts = new CancellationTokenSource();
        try { await Task.Delay(300, my.Token); } catch (OperationCanceledException) { return; }
        for (int attempt = 1; attempt <= 10 && !my.IsCancellationRequested; attempt++)
        {
            try
            {
                var l = new TcpListener(state.RemoteLocalOnly ? IPAddress.Loopback : IPAddress.Any, port);
                l.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                l.Start();
                listener = l;
                RunningPort = port;
                LastError = null;
                DebugLog.Write($"Remote-API: lauscht auf Port {port}{(state.RemoteLocalOnly ? " (nur lokal)" : "")}");
                StatusChanged?.Invoke();
                _ = AcceptLoop(l, my.Token);
                return;
            }
            catch (SocketException e) when (e.SocketErrorCode == SocketError.AddressAlreadyInUse && attempt < 10)
            {
                LastError = $"Port {port} noch belegt – neuer Versuch ({attempt}/10) …";
                StatusChanged?.Invoke();
                try { await Task.Delay(500, my.Token); } catch (OperationCanceledException) { return; }
            }
            catch (Exception e)
            {
                LastError = $"Port {port}: {e.Message}";
                DebugLog.Write($"Remote-API: Start fehlgeschlagen – {e.Message}");
                StatusChanged?.Invoke();
                return;
            }
        }
    }

    public void Stop()
    {
        cts?.Cancel();
        cts = null;
        try { listener?.Stop(); } catch { }
        listener = null;
        if (RunningPort != null)
        {
            RunningPort = null;
            StatusChanged?.Invoke();
        }
    }

    /// <summary>IPv4-Adressen dieses Rechners im Netz (für die Anzeige in den Einstellungen).</summary>
    public static IReadOnlyList<string> LocalAddresses()
    {
        try
        {
            return System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up
                            && n.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(a => a.Address.ToString())
                .Distinct()
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    // MARK: - Verbindungen

    private async Task AcceptLoop(TcpListener l, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await l.AcceptTcpClientAsync(ct); }
            catch { break; }
            _ = Serve(client);
        }
    }

    private async Task Serve(TcpClient client)
    {
        using (client)
        {
            try
            {
                client.ReceiveTimeout = 5000;
                var stream = client.GetStream();
                var req = await HttpRequest.ReadAsync(stream);
                if (req == null) return;
                req.RemoteHost = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.MapToIPv4().ToString();
                if (req.Path.TrimEnd('/') == "/api/cluster/file")
                {
                    await ServeFile(stream, req);
                    return;
                }
                var (status, body) = await ui.InvokeAsync(() => Handle(req));
                await WriteJson(stream, status, body);
            }
            catch (Exception e)
            {
                DebugLog.Write($"Remote-API: Anfrage fehlgeschlagen – {e.Message}");
            }
        }
    }

    /// <summary>
    /// <c>GET /api/cluster/file?name=&lt;datei&gt;&amp;group=&lt;verbund&gt;</c> – Präsentation aus der
    /// Playlist an einen anderen Rechner des Verbunds ausliefern (gestreamt, auch große Dateien).
    /// </summary>
    private async Task ServeFile(NetworkStream stream, HttpRequest req)
    {
        string? path = await ui.InvokeAsync(() => state.FileSync.ServableFile(req.Query.GetValueOrDefault("name") ?? "", req.Query.GetValueOrDefault("group")));
        if (path == null)
        {
            await WriteJson(stream, 404, Error("Datei nicht in der Playlist"));
            return;
        }
        await using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20, useAsync: true);
        DebugLog.Write($"Verbund: sende „{Path.GetFileName(path)}“ an {req.RemoteHost ?? "?"} ({f.Length / 1_048_576} MB)");
        string head = "HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\n" +
                      $"Content-Length: {f.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
        await f.CopyToAsync(stream, 1 << 20);
        await stream.FlushAsync();
    }

    private static async Task WriteJson(NetworkStream stream, int status, object body)
    {
        byte[] payload = status == 204 ? [] : JsonSerializer.SerializeToUtf8Bytes(body, Json);
        var head = new StringBuilder()
            .Append($"HTTP/1.1 {status} {(status == 200 ? "OK" : status == 204 ? "No Content" : "Error")}\r\n")
            .Append("Content-Type: application/json; charset=utf-8\r\n")
            .Append("Access-Control-Allow-Origin: *\r\n")
            .Append("Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n")
            .Append("Access-Control-Allow-Headers: Content-Type\r\n")
            .Append("Cache-Control: no-store\r\n")
            .Append("Connection: close\r\n")
            .Append($"Content-Length: {payload.Length}\r\n\r\n");
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head.ToString()));
        await stream.WriteAsync(payload);
        await stream.FlushAsync();
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        // Verbund-Antworten (ClusterInfo, SyncManifest) wie am Mac: Feldnamen klein, null weglassen
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    // MARK: - Befehle (Mac: handle)

    private (int, object) Handle(HttpRequest req)
    {
        if (req.Method == "OPTIONS") return (204, new { });
        var parts = req.Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return (200, new Dictionary<string, object> { ["app"] = "montro", ["api"] = ApiVersion, ["state"] = "/api/state" });
        if (parts[0] != "api") return (404, Error("unbekannter Pfad"));
        var cmd = parts.Skip(1).ToArray();
        string Arg(int i) => cmd.Length > i ? cmd[i] : "";
        (int, object) Ok() => (200, new Dictionary<string, object> { ["ok"] = true, ["state"] = Snapshot() });
        (int, object) Bad(string m) => (400, Error(m));

        switch (Arg(0))
        {
            case "" or "state":
                return (200, Snapshot());
            case "diag":
                return (200, Diagnostics());
            case "cluster":
            {
                // Verbund (intern, Master ↔ Slave), Antworten wie am Mac
                var c = state.Cluster;
                switch (Arg(1))
                {
                    case "":
                        return (200, c.Info());
                    case "command":
                        if (Cluster.ClusterJson.Read<Cluster.ClusterCommand>(req.Body) is not { } cmdIn) return Bad("ungültiger Befehl");
                        return (200, c.Receive(cmdIn, req.RemoteHost));
                    case "sync":
                        if (Cluster.ClusterJson.Read<Cluster.ClusterSync>(req.Body) is not { } syncIn) return Bad("ungültiger Abgleich");
                        return (200, c.Receive(syncIn, req.RemoteHost));
                    case "manifest":
                        return (200, state.FileSync.Manifest());
                    default:
                        return Bad("unbekannter Verbund-Befehl");
                }
            }
            case "next":
                state.Next(); return Ok();
            case "previous" or "prev":
                state.Previous(); return Ok();
            case "stop":
                _ = state.Stop(); return Ok();
            case "unlock":
                state.Unlock(); return Ok();
            case "golive" or "live":
            {
                var items = state.Items;
                string a = Arg(1);
                PresentationItem target;
                if (a is "next" or "previous" or "prev")
                {
                    // relativ zur Live-Präsentation (sonst zur Auswahl)
                    if (items.Count == 0) return Bad("Playlist ist leer");
                    int baseIndex = items.IndexOf(state.LiveItem ?? state.Selected!);
                    int t = a == "next" ? (baseIndex < 0 ? 0 : baseIndex + 1) : (baseIndex < 0 ? 0 : baseIndex - 1);
                    if (t < 0 || t >= items.Count) return Bad($"keine {(a == "next" ? "nächste" : "vorherige")} Präsentation");
                    target = items[t];
                }
                else if (int.TryParse(a, out int n))
                {
                    if (n < 1 || n > items.Count) return Bad($"Playlist-Position {n} gibt es nicht");
                    target = items[n - 1];
                }
                else
                {
                    if (state.Selected is not { } sel) return Bad("keine Präsentation ausgewählt");
                    target = sel;
                }
                state.Selected = target;
                _ = state.GoLive(target);
                return Ok();
            }
            case "select":
            {
                var items = state.Items;
                if (items.Count == 0) return Bad("Playlist ist leer");
                int current = state.Selected is { } s ? items.IndexOf(s) : -1;
                int? t = Arg(1) switch
                {
                    "next" => Math.Min(current + 1, items.Count - 1),
                    "previous" or "prev" => Math.Max((current < 0 ? items.Count : current) - 1, 0),
                    var x => int.TryParse(x, out int n) ? n - 1 : null,
                };
                if (t is not int ti || ti < 0 || ti >= items.Count) return Bad("ungültige Position");
                state.Selected = items[ti];
                return Ok();
            }
            case "goto" or "slide":
            {
                // Position unter den sichtbaren Folien (so wie „Folie n von m“)
                if (state.LiveItem is not { } item) return Bad("keine Präsentation live");
                var visible = item.Slides.Where(x => !x.Hidden).ToList();
                if (!int.TryParse(Arg(1), out int n) || n < 1 || n > visible.Count) return Bad("ungültige Foliennummer");
                state.Jump(visible[n - 1].Index);
                return Ok();
            }
            case "transition":
                if (Arg(1) == "duration")
                {
                    if (!TryDouble(Arg(2), out double sec) || sec < 0.3 || sec > 3) return Bad("Dauer 0.3–3 s");
                    state.TransitionDuration = sec;
                }
                else
                {
                    if (ShowFile.ParseTransition(Arg(1)) is not { } tr) return Bad("Übergang: cut, black oder crossfade");
                    state.Transition = tr;
                }
                return Ok();
            case "blackout" or "black":
                switch (Arg(1))
                {
                    case "on": state.SetBlackout(true); break;
                    case "off": state.SetBlackout(false); break;
                    default: state.ToggleBlackout(); break;
                }
                return Ok();
            case "timer":
            {
                var t = state.Timer;
                switch (Arg(1))
                {
                    case "start": t.Start(); break;
                    case "pause" or "stop": t.Pause(); break;
                    case "toggle": t.Toggle(); break;
                    case "reset": t.Reset(); break;
                    case "add":
                        if (!TryDouble(Arg(2), out double s)) return Bad("Sekunden fehlen");
                        t.Add(s);
                        break;
                    case "duration":
                        if (!TryDouble(Arg(2), out double m) || m <= 0) return Bad("Minuten fehlen");
                        t.SetDuration(m);
                        state.QueueSave();
                        break;
                    case "blink":
                        t.ManualBlink = Arg(2) switch { "on" => true, "off" => false, _ => !t.ManualBlink };
                        break;
                    default:
                        return Bad("unbekannter Timer-Befehl");
                }
                return Ok();
            }
            case "message":
            {
                var m = state.Message;
                switch (Arg(1))
                {
                    case "hide" or "clear":
                        m.Clear();
                        break;
                    case "preset":
                        if (!int.TryParse(Arg(2), out int n) || n < 1 || n > m.Presets.Count) return Bad("ungültige Vorlage");
                        m.Send(m.Presets[n - 1]);
                        break;
                    case "blink":
                        m.Blink = Arg(2) switch { "on" => true, "off" => false, _ => !m.Blink };
                        break;
                    case "" or "send" or "show":
                        string? text = req.Query.GetValueOrDefault("text") ?? req.JsonText()
                                       ?? (cmd.Length > 2 ? string.Join("/", cmd.Skip(2)) : null);
                        if (string.IsNullOrWhiteSpace(text)) return Bad("Text fehlt");
                        m.Send(text);
                        break;
                    default:
                        return Bad("unbekannter Nachrichten-Befehl");
                }
                return Ok();
            }
            default:
                return (404, Error($"unbekannter Befehl {Arg(0)}"));
        }
    }

    private static Dictionary<string, object> Error(string message) => new() { ["ok"] = false, ["error"] = message };

    private static bool TryDouble(string s, out double v) =>
        double.TryParse(s.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v);

    // MARK: - Status (Mac: snapshot)

    private Dictionary<string, object> Snapshot()
    {
        var s = state;
        var live = s.LiveItem;
        var items = s.Items;
        int liveIndex = live != null ? items.IndexOf(live) + 1 : 0;
        int selIndex = s.Selected != null ? items.IndexOf(s.Selected) + 1 : 0;
        int pos = live?.VisiblePosition(s.CurrentSlide) ?? 0;
        int nextPos = live != null && live.NextVisible(s.CurrentSlide) is int n ? live.VisiblePosition(n) ?? 0 : 0;
        var t = s.Timer;
        var v = s.Video;
        int baseIdx = items.IndexOf(s.LiveItem ?? s.Selected!);
        string nextPresentation = baseIdx + 1 < items.Count && baseIdx + 1 >= 0 ? items[baseIdx + 1].Title : "";
        return new Dictionary<string, object>
        {
            ["app"] = "montro",
            ["api"] = ApiVersion,
            ["live"] = s.IsShowRunning,
            ["starting"] = s.IsStarting,
            ["locked"] = s.IsLocked,
            ["blackout"] = s.IsBlackout,
            ["endBlack"] = s.IsEndBlack,
            ["status"] = s.StatusMessage,
            ["presentation"] = live?.Title ?? "",
            ["presentationFile"] = live != null ? Path.GetFileName(live.Path).ToLowerInvariant() : "",
            ["cluster"] = new Dictionary<string, object>
            {
                ["role"] = Cluster.ClusterRoles.Key(s.Cluster.Role),
                ["group"] = s.Cluster.Group,
                ["name"] = s.Cluster.DeviceName,
                ["summary"] = s.Cluster.Summary,
                ["warning"] = s.Cluster.SummaryIsWarning,
            },
            ["presentationIndex"] = liveIndex,
            ["selected"] = s.Selected?.Title ?? "",
            ["selectedIndex"] = selIndex,
            ["slide"] = pos,
            ["slideCount"] = live?.VisibleCount ?? 0,
            ["nextSlide"] = nextPos,
            ["animation"] = s.ClickInSlide,
            ["animationCount"] = s.CurrentSlideMeta?.ClickCount ?? 0,
            ["playlistCount"] = items.Count,
            ["nextPresentation"] = nextPresentation,
            ["transition"] = new Dictionary<string, object>
            {
                ["style"] = ShowFile.TransitionName(s.Transition),
                ["duration"] = s.TransitionDuration,
            },
            ["playlist"] = items.Select((it, i) => new Dictionary<string, object>
            {
                ["index"] = i + 1,
                ["title"] = it.Title,
                ["slides"] = it.VisibleCount,
                ["live"] = it == live,
                ["selected"] = it == s.Selected,
                ["ready"] = it.State == PrepState.Ready,
            }).ToList(),
            ["timer"] = new Dictionary<string, object>
            {
                ["text"] = t.DisplayText,
                ["remaining"] = t.Remaining,
                ["elapsed"] = t.Elapsed,
                ["duration"] = t.Total,
                ["state"] = t.RunState switch { TimerRunState.Running => "running", TimerRunState.Paused => "paused", _ => "stopped" },
                ["running"] = t.IsRunning,
                ["phase"] = t.Phase switch { TimerPhase.Warning1 => "warning1", TimerPhase.Warning2 => "warning2", TimerPhase.Overtime => "overtime", _ => "normal" },
                ["blink"] = t.ShouldBlink,
                ["color"] = t.ColorHex.Length == 9 ? "#" + t.ColorHex[3..] : t.ColorHex,
            },
            ["message"] = new Dictionary<string, object>
            {
                ["visible"] = s.Message.IsVisible,
                ["text"] = s.Message.Text,
                ["blink"] = s.Message.Blink,
                ["presets"] = s.Message.Presets.ToList(),
            },
            ["video"] = new Dictionary<string, object>
            {
                ["playing"] = v != null,
                ["name"] = v?.Name ?? "",
                ["duration"] = v?.Duration ?? 0,
                ["remaining"] = v?.Remaining ?? 0,
                ["remainingText"] = v == null ? "" : TimeFormat.Clock(v.Remaining),
            },
        };
    }

    /// <summary>Diagnose: Version, Windows, Bildschirme, Zuordnung, Live-Bild, letzte 60 Log-Zeilen.</summary>
    private Dictionary<string, object> Diagnostics() => new()
    {
        ["version"] = AppInfo.Version,
        ["windows"] = Environment.OSVersion.VersionString,
        ["screens"] = Monitors.All().Select(m => new Dictionary<string, object>
        {
            ["number"] = m.Number,
            ["device"] = m.DeviceName,
            ["name"] = m.FriendlyName,
            ["bounds"] = $"{m.Bounds.X},{m.Bounds.Y} {m.Bounds.Width}x{m.Bounds.Height}",
            ["workArea"] = $"{m.WorkArea.X},{m.WorkArea.Y} {m.WorkArea.Width}x{m.WorkArea.Height}",
            ["scale"] = m.Scale,
            ["main"] = m.IsPrimary,
        }).ToList(),
        ["audience"] = state.AudienceScreen is { } a ? $"{a.Number} {a.FriendlyName}" : "Popup",
        ["presenter"] = state.PresenterScreen is { } p ? $"{p.Number} {p.FriendlyName}" : "Popup",
        ["liveImage"] = state.LiveSource != IntPtr.Zero,
        ["log"] = DebugLog.Tail(60),
    };
}

/// <summary>Minimaler HTTP-Request-Parser (nur was die API braucht, Mac: HTTPRequest).</summary>
public sealed class HttpRequest
{
    public string Method { get; init; } = "GET";
    public string Path { get; init; } = "/";
    public Dictionary<string, string> Query { get; init; } = new();
    public byte[] Body { get; init; } = [];

    /// <summary>IP-Adresse der Gegenstelle (Verbund: Adresse des Masters)</summary>
    public string? RemoteHost { get; set; }

    public string? JsonText()
    {
        if (Body.Length == 0) return null;
        try
        {
            using var doc = JsonDocument.Parse(Body);
            return doc.RootElement.TryGetProperty("text", out var t) ? t.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Kopf bis zur Leerzeile lesen, dann Content-Length Bytes Körper (max. 1 MB).</summary>
    public static async Task<HttpRequest?> ReadAsync(NetworkStream stream)
    {
        var buffer = new List<byte>();
        var chunk = new byte[8192];
        int headEnd = -1;
        while (headEnd < 0)
        {
            int n = await stream.ReadAsync(chunk);
            if (n <= 0 || buffer.Count > 1_000_000) return null;
            buffer.AddRange(chunk.AsSpan(0, n).ToArray());
            headEnd = IndexOf(buffer, "\r\n\r\n"u8.ToArray());
        }
        string head = Encoding.UTF8.GetString(buffer.GetRange(0, headEnd).ToArray());
        var lines = head.Split("\r\n");
        var requestLine = lines[0].Split(' ');
        if (requestLine.Length < 2) return null;
        int length = 0;
        foreach (var line in lines.Skip(1))
        {
            int c = line.IndexOf(':');
            if (c > 0 && line[..c].Trim().Equals("content-length", StringComparison.OrdinalIgnoreCase))
                int.TryParse(line[(c + 1)..].Trim(), out length);
        }
        length = Math.Clamp(length, 0, 1_000_000);
        int bodyStart = headEnd + 4;
        while (buffer.Count - bodyStart < length)
        {
            int n = await stream.ReadAsync(chunk);
            if (n <= 0) break;
            buffer.AddRange(chunk.AsSpan(0, n).ToArray());
        }
        byte[] body = buffer.Skip(bodyStart).Take(length).ToArray();

        string target = requestLine[1];
        int q = target.IndexOf('?');
        string path = q >= 0 ? target[..q] : target;
        var query = new Dictionary<string, string>();
        if (q >= 0)
        {
            foreach (var pair in target[(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int e = pair.IndexOf('=');
                string k = Uri.UnescapeDataString((e >= 0 ? pair[..e] : pair).Replace('+', ' '));
                string v = e >= 0 ? Uri.UnescapeDataString(pair[(e + 1)..].Replace('+', ' ')) : "";
                query[k] = v;
            }
        }
        return new HttpRequest
        {
            Method = requestLine[0].ToUpperInvariant(),
            Path = Uri.UnescapeDataString(path),
            Query = query,
            Body = body,
        };
    }

    private static int IndexOf(List<byte> data, byte[] pattern)
    {
        for (int i = 0; i <= data.Count - pattern.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length && match; j++) match = data[i + j] == pattern[j];
            if (match) return i;
        }
        return -1;
    }
}
