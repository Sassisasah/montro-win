using System.Runtime.InteropServices;
using Montro.Core;

namespace Montro.Cluster;

/// <summary>
/// DNS-SD (Bonjour/mDNS) mit den Windows-eigenen Funktionen aus dnsapi.dll (Windows 10 1809+):
/// Dienst <c>_montro._tcp</c> ankündigen (Name, Port, TXT id/role/group/v wie am Mac) und andere
/// montro-Rechner im Netz suchen. Ohne Fremdbibliothek. Geht etwas schief, bleibt der Verbund
/// über manuell eingetragene Adressen nutzbar.
/// </summary>
public sealed class DnsSd : IDisposable
{
    public const string ServiceType = "_montro._tcp";
    private const string Domain = ".local";
    private const uint RequestVersion1 = 1;
    private const int DnsRequestPending = 9506;
    private const ushort TypePtr = 12;

    /// <summary>Gefundener Dienst (aufgelöst).</summary>
    public sealed record Service(string InstanceName, string? Host, int Port, IReadOnlyDictionary<string, string> Txt);

    // MARK: - Ankündigen

    private IntPtr registeredInstance;
    private IntPtr registerRequest;
    private DnsServiceCancel registerCancel;

    /// <summary>Dienst ankündigen (ersetzt eine frühere Ankündigung).</summary>
    public void Register(string name, int port, IDictionary<string, string> txt)
    {
        Unregister();
        try
        {
            string instance = $"{Sanitize(name)}.{ServiceType}{Domain}";
            string host = Environment.MachineName + Domain;
            var keys = txt.Keys.ToArray();
            var values = keys.Select(k => txt[k]).ToArray();
            uint ip = FirstIPv4();
            IntPtr ipPtr = Marshal.AllocHGlobal(4);
            Marshal.WriteInt32(ipPtr, (int)ip);
            registeredInstance = DnsServiceConstructInstance(instance, host, ip != 0 ? ipPtr : IntPtr.Zero, IntPtr.Zero,
                (ushort)port, 0, 0, (uint)keys.Length, keys, values);
            Marshal.FreeHGlobal(ipPtr);
            if (registeredInstance == IntPtr.Zero)
            {
                DebugLog.Write("Verbund: DNS-SD-Anzeige nicht möglich (Instanz)");
                return;
            }
            var req = new DnsServiceRegisterRequest
            {
                Version = RequestVersion1,
                InterfaceIndex = 0,
                pServiceInstance = registeredInstance,
                pRegisterCompletionCallback = Marshal.GetFunctionPointerForDelegate(RegisterDone),
            };
            registerRequest = Marshal.AllocHGlobal(Marshal.SizeOf<DnsServiceRegisterRequest>());
            Marshal.StructureToPtr(req, registerRequest, false);
            int r = DnsServiceRegister(registerRequest, out registerCancel);
            if (r != DnsRequestPending) DebugLog.Write($"Verbund: DNS-SD-Anzeige fehlgeschlagen ({r})");
            else DebugLog.Write($"Verbund: angekündigt als „{name}“ ({ServiceType}, Port {port})");
        }
        catch (Exception e)
        {
            DebugLog.Write($"Verbund: DNS-SD-Anzeige nicht verfügbar – {e.Message}");
        }
    }

    public void Unregister()
    {
        if (registerRequest == IntPtr.Zero) return;
        try { DnsServiceDeRegister(registerRequest, IntPtr.Zero); } catch { }
        // Speicher bewusst erst beim nächsten Register/Dispose freigeben wäre sauberer; die Abmeldung
        // läuft asynchron, daher Anfrage und Instanz hier nicht sofort freigeben (kleines, einmaliges Leck).
        registerRequest = IntPtr.Zero;
        registeredInstance = IntPtr.Zero;
    }

    private static readonly RegisterCompleteCallback RegisterDone = (status, ctx, instance) =>
    {
        if (status != 0) DebugLog.Write($"Verbund: DNS-SD-Anzeige meldet {status}");
    };

    // MARK: - Suchen

    /// <summary>
    /// Einmal suchen: Browse für <paramref name="seconds"/> Sekunden, dann jeden gefundenen Dienst
    /// auflösen (Adresse, Port, TXT). Läuft im Hintergrund.
    /// </summary>
    public static async Task<List<Service>> BrowseAsync(double seconds = 2.5)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var gate = new object();
        BrowseCallback cb = (status, ctx, record) =>
        {
            for (IntPtr r = record; r != IntPtr.Zero; r = Marshal.ReadIntPtr(r))
            {
                // DNS_RECORDW: pNext (0), pName (8), wType (16) … Data ab 32; PTR: pNameHost
                ushort type = (ushort)Marshal.ReadInt16(r, IntPtr.Size * 2);
                if (type != TypePtr) continue;
                string? host = Marshal.PtrToStringUni(Marshal.ReadIntPtr(r, DataOffset));
                if (host != null) lock (gate) names.Add(host);
            }
            if (record != IntPtr.Zero) DnsRecordListFree(record, 1);
        };
        var handle = GCHandle.Alloc(cb);
        IntPtr req = IntPtr.Zero;
        try
        {
            var request = new DnsServiceBrowseRequest
            {
                Version = RequestVersion1,
                InterfaceIndex = 0,
                QueryName = Marshal.StringToHGlobalUni(ServiceType + Domain),
                pBrowseCallback = Marshal.GetFunctionPointerForDelegate(cb),
            };
            req = Marshal.AllocHGlobal(Marshal.SizeOf<DnsServiceBrowseRequest>());
            Marshal.StructureToPtr(request, req, false);
            int r = DnsServiceBrowse(req, out var cancel);
            if (r != DnsRequestPending) return new();
            await Task.Delay(TimeSpan.FromSeconds(seconds));
            DnsServiceBrowseCancel(ref cancel);
            await Task.Delay(200);
            Marshal.FreeHGlobal(request.QueryName);
        }
        catch (Exception e)
        {
            DebugLog.Write($"Verbund: DNS-SD-Suche nicht verfügbar – {e.Message}");
            return new();
        }
        finally
        {
            handle.Free();
            if (req != IntPtr.Zero) Marshal.FreeHGlobal(req);
        }
        List<string> found;
        lock (gate) found = names.ToList();
        var result = new List<Service>();
        foreach (var n in found)
            if (await ResolveAsync(n) is { } s) result.Add(s);
        return result;
    }

    private static readonly int DataOffset = IntPtr.Size == 8 ? 32 : 24;

    /// <summary>Dienst-Instanz auflösen (DnsServiceResolve), Zeitlimit 2 s.</summary>
    private static async Task<Service?> ResolveAsync(string instanceName)
    {
        var tcs = new TaskCompletionSource<Service?>(TaskCreationOptions.RunContinuationsAsynchronously);
        ResolveCompleteCallback cb = (status, ctx, inst) =>
        {
            try
            {
                if (status != 0 || inst == IntPtr.Zero)
                {
                    tcs.TrySetResult(null);
                    return;
                }
                int ps = IntPtr.Size;
                // DNS_SERVICE_INSTANCE: Name, HostName, ip4*, ip6*, wPort, wPriority, wWeight, dwPropertyCount, keys, values, dwInterfaceIndex
                string name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(inst, 0)) ?? instanceName;
                IntPtr ip4 = Marshal.ReadIntPtr(inst, ps * 2);
                int portOffset = ps * 4;
                int port = (ushort)Marshal.ReadInt16(inst, portOffset);
                int countOffset = portOffset + 8;
                int count = Marshal.ReadInt32(inst, countOffset);
                int keysOffset = (countOffset + 4 + ps - 1) / ps * ps;
                IntPtr keys = Marshal.ReadIntPtr(inst, keysOffset);
                IntPtr values = Marshal.ReadIntPtr(inst, keysOffset + ps);
                var txt = new Dictionary<string, string>();
                for (int i = 0; i < count; i++)
                {
                    string? k = Marshal.PtrToStringUni(Marshal.ReadIntPtr(keys, i * ps));
                    string? v = Marshal.PtrToStringUni(Marshal.ReadIntPtr(values, i * ps));
                    if (k != null) txt[k] = v ?? "";
                }
                string? host = null;
                if (ip4 != IntPtr.Zero)
                {
                    var b = BitConverter.GetBytes(Marshal.ReadInt32(ip4));
                    host = $"{b[0]}.{b[1]}.{b[2]}.{b[3]}";
                }
                DnsServiceFreeInstance(inst);
                tcs.TrySetResult(new Service(InstanceLabel(name), host, port, txt));
            }
            catch
            {
                tcs.TrySetResult(null);
            }
        };
        var handle = GCHandle.Alloc(cb);
        IntPtr req = IntPtr.Zero, query = IntPtr.Zero;
        try
        {
            query = Marshal.StringToHGlobalUni(instanceName);
            var request = new DnsServiceResolveRequest
            {
                Version = RequestVersion1,
                InterfaceIndex = 0,
                QueryName = query,
                pResolveCompletionCallback = Marshal.GetFunctionPointerForDelegate(cb),
            };
            req = Marshal.AllocHGlobal(Marshal.SizeOf<DnsServiceResolveRequest>());
            Marshal.StructureToPtr(request, req, false);
            int r = DnsServiceResolve(req, out var cancel);
            if (r != DnsRequestPending) return null;
            var done = await Task.WhenAny(tcs.Task, Task.Delay(2000));
            if (done != tcs.Task)
            {
                DnsServiceResolveCancel(ref cancel);
                await Task.Delay(100);
                return null;
            }
            return tcs.Task.Result;
        }
        catch
        {
            return null;
        }
        finally
        {
            handle.Free();
            if (req != IntPtr.Zero) Marshal.FreeHGlobal(req);
            if (query != IntPtr.Zero) Marshal.FreeHGlobal(query);
        }
    }

    /// <summary>„Saal 1 Mac._montro._tcp.local“ → „Saal 1 Mac“</summary>
    private static string InstanceLabel(string full)
    {
        int i = full.IndexOf("." + ServiceType, StringComparison.OrdinalIgnoreCase);
        return (i > 0 ? full[..i] : full).Replace("\\032", " ").Replace("\\ ", " ");
    }

    /// <summary>Punkte trennen in DNS-Namen Ebenen – im Instanznamen durch Leerzeichen ersetzen.</summary>
    private static string Sanitize(string name) => name.Replace('.', ' ').Trim() is { Length: > 0 } s ? s : "montro";

    private static uint FirstIPv4()
    {
        var ip = Remote.RemoteApi.LocalAddresses().FirstOrDefault();
        if (ip == null || !System.Net.IPAddress.TryParse(ip, out var a)) return 0;
        return BitConverter.ToUInt32(a.GetAddressBytes(), 0);
    }

    public void Dispose() => Unregister();

    // MARK: - dnsapi.dll

    [StructLayout(LayoutKind.Sequential)]
    private struct DnsServiceCancel { public IntPtr Reserved; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DnsServiceRegisterRequest
    {
        public uint Version;
        public uint InterfaceIndex;
        public IntPtr pServiceInstance;
        public IntPtr pRegisterCompletionCallback;
        public IntPtr pQueryContext;
        public IntPtr hCredentials;
        public int unicastEnabled;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DnsServiceBrowseRequest
    {
        public uint Version;
        public uint InterfaceIndex;
        public IntPtr QueryName;
        public IntPtr pBrowseCallback;
        public IntPtr pQueryContext;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DnsServiceResolveRequest
    {
        public uint Version;
        public uint InterfaceIndex;
        public IntPtr QueryName;
        public IntPtr pResolveCompletionCallback;
        public IntPtr pQueryContext;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void RegisterCompleteCallback(uint status, IntPtr context, IntPtr instance);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void BrowseCallback(uint status, IntPtr context, IntPtr record);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void ResolveCompleteCallback(uint status, IntPtr context, IntPtr instance);

    [DllImport("dnsapi.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DnsServiceConstructInstance(string serviceName, string hostName, IntPtr ip4, IntPtr ip6,
        ushort port, ushort priority, ushort weight, uint propertiesCount, string[] keys, string[] values);

    [DllImport("dnsapi.dll")]
    private static extern void DnsServiceFreeInstance(IntPtr instance);

    [DllImport("dnsapi.dll")]
    private static extern int DnsServiceRegister(IntPtr request, out DnsServiceCancel cancel);

    [DllImport("dnsapi.dll")]
    private static extern int DnsServiceDeRegister(IntPtr request, IntPtr cancel);

    [DllImport("dnsapi.dll")]
    private static extern int DnsServiceBrowse(IntPtr request, out DnsServiceCancel cancel);

    [DllImport("dnsapi.dll")]
    private static extern int DnsServiceBrowseCancel(ref DnsServiceCancel cancel);

    [DllImport("dnsapi.dll")]
    private static extern int DnsServiceResolve(IntPtr request, out DnsServiceCancel cancel);

    [DllImport("dnsapi.dll")]
    private static extern int DnsServiceResolveCancel(ref DnsServiceCancel cancel);

    [DllImport("dnsapi.dll")]
    private static extern void DnsRecordListFree(IntPtr record, int freeType);
}
