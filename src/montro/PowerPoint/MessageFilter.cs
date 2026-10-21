using System.Runtime.InteropServices;

namespace Montro.PowerPoint;

/// <summary>
/// COM-Nachrichtenfilter für den STA-Thread. Ist PowerPoint beschäftigt, lehnt es Aufrufe mit
/// RPC_E_CALL_REJECTED (0x80010001) bzw. SERVERCALL_RETRYLATER ab. Ohne Filter käme sofort eine
/// Ausnahme; so wiederholt COM den Aufruf alle 100 ms, höchstens 6 s lang.
/// </summary>
[ComImport, Guid("00000016-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IOleMessageFilter
{
    [PreserveSig]
    int HandleInComingCall(int dwCallType, IntPtr hTaskCaller, int dwTickCount, IntPtr lpInterfaceInfo);

    [PreserveSig]
    int RetryRejectedCall(IntPtr hTaskCallee, int dwTickCount, int dwRejectType);

    [PreserveSig]
    int MessagePending(IntPtr hTaskCallee, int dwTickCount, int dwPendingType);
}

internal sealed class MessageFilter : IOleMessageFilter
{
    private const int ServerCallIsHandled = 0;
    private const int ServerCallRetryLater = 2;
    private const int PendingMsgWaitDefProcess = 2;
    private const int MaxRetryMs = 6000;

    [DllImport("ole32.dll")]
    private static extern int CoRegisterMessageFilter(IOleMessageFilter? newFilter, out IOleMessageFilter? oldFilter);

    public static void Register()
    {
        try { CoRegisterMessageFilter(new MessageFilter(), out _); } catch { }
    }

    public static void Revoke()
    {
        try { CoRegisterMessageFilter(null, out _); } catch { }
    }

    int IOleMessageFilter.HandleInComingCall(int dwCallType, IntPtr hTaskCaller, int dwTickCount, IntPtr lpInterfaceInfo)
        => ServerCallIsHandled;

    int IOleMessageFilter.RetryRejectedCall(IntPtr hTaskCallee, int dwTickCount, int dwRejectType)
    {
        // -1 = aufgeben, ≥ 0 = nach so vielen ms erneut versuchen
        if (dwRejectType == ServerCallRetryLater && dwTickCount < MaxRetryMs) return 100;
        return -1;
    }

    int IOleMessageFilter.MessagePending(IntPtr hTaskCallee, int dwTickCount, int dwPendingType)
        => PendingMsgWaitDefProcess;
}
