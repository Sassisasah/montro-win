using System.Windows.Threading;
using Montro.Core;

namespace Montro.PowerPoint;

/// <summary>
/// Eigener STA-Thread mit Nachrichtenschleife (WPF-Dispatcher). Alle COM-Aufrufe an PowerPoint
/// laufen hier nacheinander – nie parallel, nie vom UI-Thread. COM-Objekte verlassen diesen
/// Thread nicht.
/// </summary>
public sealed class StaWorker : IDisposable
{
    private readonly Thread thread;
    private Dispatcher? dispatcher;

    public StaWorker(string name)
    {
        using var ready = new ManualResetEventSlim(false);
        thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            MessageFilter.Register();
            ready.Set();
            Dispatcher.Run();
            MessageFilter.Revoke();
        })
        {
            IsBackground = true,
            Name = name,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
    }

    /// <summary>
    /// Führt <paramref name="work"/> auf dem STA-Thread aus. Nach <paramref name="timeoutMs"/>
    /// kommt eine <see cref="TimeoutException"/> zurück (der Aufruf selbst läuft weiter, COM lässt
    /// sich nicht abbrechen – so friert aber wenigstens die Oberfläche nicht ein).
    /// </summary>
    public async Task<T> Run<T>(Func<T> work, int timeoutMs = 8000, string? label = null)
    {
        var d = dispatcher ?? throw new ObjectDisposedException(nameof(StaWorker));
        var op = d.InvokeAsync(work);
        var task = op.Task;
        var finished = await Task.WhenAny(task, Task.Delay(timeoutMs)).ConfigureAwait(true);
        if (finished != task)
        {
            if (label != null) DebugLog.Write($"PowerPoint: „{label}“ antwortet nicht nach {timeoutMs / 1000.0:0.#} s");
            // Ergebnis später beobachten, damit kein unbeobachteter Fehler entsteht
            _ = task.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            throw new TimeoutException($"PowerPoint antwortet nicht ({label ?? "Aufruf"}). Ist dort ein Dialog offen?");
        }
        try
        {
            return await task.ConfigureAwait(true);
        }
        catch (Exception e) when (label != null)
        {
            DebugLog.Write($"PowerPoint: „{label}“ fehlgeschlagen – {Describe(e)}");
            throw;
        }
    }

    public Task Run(Action work, int timeoutMs = 8000, string? label = null) =>
        Run(() => { work(); return true; }, timeoutMs, label);

    public static string Describe(Exception e)
    {
        var b = e.GetBaseException();
        return b is System.Runtime.InteropServices.COMException c
            ? $"{c.Message.Trim()} (0x{c.HResult:X8})"
            : b.Message;
    }

    public void Dispose()
    {
        try { dispatcher?.InvokeShutdown(); } catch { }
        dispatcher = null;
    }
}
