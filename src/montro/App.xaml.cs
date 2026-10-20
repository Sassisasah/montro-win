using System.Windows;
using System.Windows.Threading;
using Montro.Core;

namespace Montro;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DebugLog.Write($"montro {AppInfo.Version} startet ({Environment.OSVersion}, {(Environment.Is64BitProcess ? "64" : "32")} Bit)");
        DispatcherUnhandledException += OnUnhandled;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            DebugLog.Write($"Unbehandelter Fehler: {args.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            DebugLog.Write($"Unbeobachteter Fehler: {args.Exception.GetBaseException().Message}");
            args.SetObserved();
        };
        base.OnStartup(e);
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Während einer Show darf die App nicht wegen eines Bedienfehlers abstürzen.
        DebugLog.Write($"Fehler in der Oberfläche: {e.Exception}");
        MessageBox.Show(e.Exception.Message, "montro", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DebugLog.Write("montro beendet");
        base.OnExit(e);
    }
}
