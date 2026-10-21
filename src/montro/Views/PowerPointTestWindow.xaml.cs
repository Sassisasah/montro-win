using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using Montro.Core;
using Montro.Display;
using Montro.PowerPoint;

namespace Montro.Views;

/// <summary>
/// Testfenster für den PowerPoint-Kern (Meilenstein 1): öffnen, Show auf Bildschirm X,
/// weiter/zurück/gehe zu, Schwarz, Lage prüfen, beenden, Vorschaubilder.
/// </summary>
public partial class PowerPointTestWindow : Window
{
    private readonly PowerPointHost host = new();
    private readonly Action<string> logHandler;
    private bool isOpen;
    private bool openedWithWindow;
    private bool busy;
    private bool shutdownDone;

    public PowerPointTestWindow()
    {
        InitializeComponent();
        logHandler = line => Dispatcher.InvokeAsync(() => AddLog(line));
        foreach (var line in DebugLog.Tail(40)) AddLog(line);
        DebugLog.LineWritten += logHandler;

        host.StateChanged += ShowStateChanged;
        host.ShowEnded += () => Dispatcher.InvokeAsync(() =>
        {
            DetailText.Text = "Show beendet";
            UpdateButtons();
        });

        LoadMonitors();
        UpdateButtons();
        Loaded += async (_, _) => await Connect();
    }

    // MARK: - Verbindung, Monitore

    private async Task Connect()
    {
        try
        {
            VersionText.Text = $"{await host.ConnectAsync()} · montro {AppInfo.Version}";
        }
        catch (Exception e)
        {
            VersionText.Text = $"PowerPoint: {StaWorker.Describe(e)}";
        }
    }

    private void LoadMonitors()
    {
        var previous = (MonitorBox.SelectedItem as MonitorInfo)?.DeviceName;
        var list = Monitors.All();
        MonitorBox.ItemsSource = list;
        MonitorInfo? pick = list.FirstOrDefault(m => m.DeviceName == previous)
                            ?? list.FirstOrDefault(m => !m.IsPrimary)
                            ?? list.FirstOrDefault();
        MonitorBox.SelectedItem = pick;
        DebugLog.Write($"Monitore ({list.Count}), System-DPI {Monitors.SystemDpi()}:");
        foreach (var m in list)
            DebugLog.Write($"  {m.Label}, Arbeitsbereich {m.WorkArea}");
    }

    private void OnRefreshMonitors(object sender, RoutedEventArgs e) => LoadMonitors();

    private MonitorInfo? SelectedMonitor => MonitorBox.SelectedItem as MonitorInfo;

    // MARK: - Datei

    private async void OnChooseFile(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Präsentation wählen",
            Filter = "PowerPoint-Präsentationen (*.pptx;*.pptm;*.ppsx;*.ppsm)|*.pptx;*.pptm;*.ppsx;*.ppsm",
        };
        if (dlg.ShowDialog(this) != true) return;
        FileBox.Text = dlg.FileName;
        await Do("Öffnen", OpenCurrent, longRunning: true);
    }

    private async Task OpenCurrent()
    {
        isOpen = false;
        FileDetails.Text = "öffne …";
        bool withWindow = WithWindowBox.IsChecked == true;
        string file = FileBox.Text;
        var parse = Task.Run(() => Pptx.PptxParser.Parse(file));
        var r = await host.OpenAsync(file, withWindow);
        isOpen = true;
        openedWithWindow = withWindow;
        FileDetails.Text = $"„{r.Title}“ – {r.Slides}, Seitenformat {r.SlideWidthPt:0}×{r.SlideHeightPt:0} pt";
        try
        {
            var meta = await parse;
            int notes = meta.Slides.Count(s => s.Notes.Length > 0);
            int clicks = meta.Slides.Sum(s => s.ClickCount);
            FileDetails.Text += $" · Parser: {meta.Subtitle}, {clicks} Klick-Animationen, {notes} Folien mit Notizen";
            DebugLog.Write($"Parser: {meta.Subtitle}, {clicks} Klicks, {notes} Folien mit Notizen");
        }
        catch (Exception e)
        {
            DebugLog.Write($"Parser: {e.Message}");
        }
    }

    // MARK: - Show

    private async void OnStart(object sender, RoutedEventArgs e) => await Do("Show starten", async () =>
    {
        if (string.IsNullOrEmpty(FileBox.Text)) return;
        // Option „Mit Dokumentfenster“ geändert oder Präsentation geschlossen → neu öffnen
        if (!isOpen || openedWithWindow != (WithWindowBox.IsChecked == true))
            await OpenCurrent();

        var target = SelectedMonitor;
        await host.StartShowAsync(new ShowOptions(
            Kiosk: KioskBox.IsChecked == true,
            Target: target,
            PreselectMonitorInRegistry: RegistryBox.IsChecked == true));
        if (target != null)
            ShowPlacement(await host.PlaceAsync(target), target);
    }, longRunning: true);

    private async void OnPlace(object sender, RoutedEventArgs e) => await Do("Lage prüfen", async () =>
    {
        var target = SelectedMonitor;
        if (target == null) return;
        ShowPlacement(await host.PlaceAsync(target), target);
    });

    private void ShowPlacement(PlacementResult p, MonitorInfo target)
    {
        MessageText.Text = p.Ok
            ? (p.Corrections > 0 ? $"Fenster liegt nach {p.Corrections} Korrektur(en) auf Bildschirm {target.Number}." : "")
            : $"Show-Fenster liegt nicht exakt auf Bildschirm {target.Number}: {p.Detail}";
        if (p.Ok) DetailText.Text = $"Fenster liegt auf Bildschirm {target.Number} ({target.Bounds})";
    }

    private async void OnNext(object sender, RoutedEventArgs e) => await Do("Weiter", host.NextAsync);

    private async void OnPrevious(object sender, RoutedEventArgs e) => await Do("Zurück", host.PreviousAsync);

    private async void OnGoto(object sender, RoutedEventArgs e) => await GotoFromBox();

    private async void OnGotoKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return) await GotoFromBox();
    }

    private Task GotoFromBox() => Do("Gehe zu", async () =>
    {
        var map = host.Slides;
        if (map == null) return;
        if (!int.TryParse(GotoBox.Text.Trim(), out var n) || map.IndexForVisible(n) is not int index)
        {
            MessageText.Text = $"Folie „{GotoBox.Text}“ gibt es nicht (1–{map.VisibleCount}, gezählt ohne ausgeblendete).";
            return;
        }
        await host.GotoAsync(index);
    });

    private async void OnBlack(object sender, RoutedEventArgs e) =>
        await Do("Schwarz", () => host.SetBlackAsync(!host.LastState.IsBlack));

    private async void OnStop(object sender, RoutedEventArgs e) => await Do("Beenden", async () =>
    {
        await host.StopAsync();
        isOpen = false;
        FileDetails.Text = "geschlossen – „Show starten“ öffnet die Datei neu";
    });

    private async void OnOrphans(object sender, RoutedEventArgs e) => await Do("Verwaiste Shows", async () =>
    {
        var n = await host.ExitOrphanShowsAsync();
        MessageText.Text = n == 0 ? "Keine laufenden Shows gefunden." : $"{n} Show(s) beendet.";
    });

    private async void OnExport(object sender, RoutedEventArgs e) => await Do("Export", async () =>
    {
        if (!isOpen) await OpenCurrent();
        var dir = AppPaths.ThumbsFor("test");
        foreach (var old in Directory.GetFiles(dir, "slide-*.png"))
            File.Delete(old);
        var progress = new Progress<(int done, int total)>(p => MessageText.Text = $"Exportiere Folie {p.done} von {p.total} …");
        var sw = Stopwatch.StartNew();
        var count = await host.ExportThumbnailsAsync(dir, progress);
        MessageText.Text = $"{count} Vorschaubilder in {sw.Elapsed.TotalSeconds:0.0} s exportiert.";
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
    }, longRunning: true);

    // MARK: - Status

    private void ShowStateChanged(ShowState s)
    {
        // kommt auf dem UI-Thread an (Abfrage-Schleife wurde von hier gestartet)
        var map = host.Slides;
        if (!s.HasShow)
        {
            StatusText.Text = "Keine Show";
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("Brand.Text");
        }
        else
        {
            int visible = map?.VisiblePosition(s.SlideIndex) ?? 0;
            StatusText.Text = s.AtEnd ? "Ende der Bildschirmpräsentation"
                : visible > 0 ? $"Folie {visible} von {map?.VisibleCount ?? 0}"
                : $"Folie {s.SlideIndex} (ausgeblendet)";
            var brush = s.AtEnd ? "Brand.Live" : s.IsBlack ? "Brand.Warning" : "Brand.Ok";
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource(brush);
            DetailText.Text = $"PowerPoint-Index {s.SlideIndex} · Show-Position {s.ShowPosition} · "
                            + $"Klick {s.Click} von {s.ClickCount} · {s.RunStateText}";
        }
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        bool show = host.LastState.HasShow;
        bool file = !string.IsNullOrEmpty(FileBox.Text);
        StartButton.IsEnabled = !busy && file;
        ChooseButton.IsEnabled = !busy;
        PrevButton.IsEnabled = NextButton.IsEnabled = GotoButton.IsEnabled = BlackButton.IsEnabled = show;
        PlaceButton.IsEnabled = show && !busy;
        StopButton.IsEnabled = !busy && (show || isOpen);
        ExportButton.IsEnabled = !busy && file;
        OrphanButton.IsEnabled = !busy;
        BlackButton.Content = host.LastState.IsBlack ? "Schwarz aus" : "Schwarz";
    }

    // MARK: - Hilfen

    private async Task Do(string what, Func<Task> action, bool longRunning = false)
    {
        if (longRunning)
        {
            busy = true;
            UpdateButtons();
        }
        MessageText.Text = "";
        try
        {
            await action();
        }
        catch (Exception e)
        {
            var msg = StaWorker.Describe(e);
            DebugLog.Write($"{what}: {msg}");
            MessageText.Text = $"{what} fehlgeschlagen: {msg}";
        }
        finally
        {
            if (longRunning) busy = false;
            UpdateButtons();
        }
    }

    private void AddLog(string line)
    {
        LogList.Items.Add(line);
        while (LogList.Items.Count > 400) LogList.Items.RemoveAt(0);
        LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (shutdownDone)
        {
            base.OnClosing(e);
            return;
        }
        // erst Shows beenden und Präsentation schließen, dann wirklich schließen
        e.Cancel = true;
        DebugLog.LineWritten -= logHandler;
        IsEnabled = false;
        StatusText.Text = "Beende …";
        await host.ShutdownAsync();
        shutdownDone = true;
        Close();
    }
}
