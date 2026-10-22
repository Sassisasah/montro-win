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
        if (e.Args.Contains("--selbsttest"))
        {
            // Ohne Hauptfenster: Selbsttest laufen lassen, Bericht schreiben, beenden
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            base.OnStartup(e);
            Dispatcher.InvokeAsync(async () => Shutdown(await Testing.SelfTest.RunAsync(e.Args)));
            return;
        }
        if (e.Args.Contains("--showtest"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            base.OnStartup(e);
            Dispatcher.InvokeAsync(async () => Shutdown(await Testing.ShowFileTest.RunAsync(e.Args)));
            return;
        }
        if (e.Args.Contains("--miniaturtest"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            base.OnStartup(e);
            Dispatcher.InvokeAsync(async () => Shutdown(await Testing.ThumbnailTest.RunAsync(e.Args)));
            return;
        }
        if (e.Args.Contains("--referentenbild"))
        {
            // Testbilder der Referent*innenansicht schreiben und beenden (ohne PowerPoint, ohne Fenster)
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            base.OnStartup(e);
            Dispatcher.InvokeAsync(() => Shutdown(Testing.PresenterSnapshot.Run(e.Args)));
            return;
        }
        base.OnStartup(e);
        // Hauptfenster hier statt per StartupUri, damit der Selbsttest ohne Fenster starten kann
        var main = new MainWindow();
        main.Show();
        // „Öffnen mit montro“ / Dateien als Argumente: in die Playlist
        var files = e.Args.Where(a => !a.StartsWith("--") && File.Exists(a)).ToArray();
        if (files.FirstOrDefault(f => f.EndsWith(Core.ShowFile.Extension, StringComparison.OrdinalIgnoreCase)) is { } show)
            main.OpenShow(show);
        var presentations = files.Where(f => !f.EndsWith(Core.ShowFile.Extension, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (presentations.Length > 0) main.AddFiles(presentations);
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Während einer Show darf die App nicht wegen eines Bedienfehlers abstürzen.
        DebugLog.Write($"Fehler in der Oberfläche: {e.Exception}");
        e.Handled = true;
        // Beim Beenden (Fenster schon zu, Ressourcen weg) nur protokollieren, keine Meldung zeigen
        if (exiting || Current?.MainWindow is not { IsVisible: true }) return;
        MessageBox.Show(e.Exception.Message, "montro", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private static bool exiting;

    protected override void OnExit(ExitEventArgs e)
    {
        exiting = true;
        DebugLog.Write("montro beendet");
        base.OnExit(e);
    }
}
