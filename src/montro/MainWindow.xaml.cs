using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using Montro.Core;
using Montro.Views;

namespace Montro;

public partial class MainWindow : Window
{
    private PowerPointTestWindow? testWindow;

    public MainWindow()
    {
        InitializeComponent();
        VersionLine.Text = $"Version {AppInfo.Version} · Entwickelt für movidal · Konzept & Leitung Sarah-Mathias Andrae · programmiert mit Claude (Anthropic) · © movidal";
    }

    private void OnTestPowerPoint(object sender, RoutedEventArgs e)
    {
        if (testWindow != null)
        {
            testWindow.Activate();
            return;
        }
        testWindow = new PowerPointTestWindow { Owner = this };
        testWindow.Closed += (_, _) => testWindow = null;
        testWindow.Show();
    }

    private void OnOpenLog(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.LocalDir}\"") { UseShellExecute = true });

    protected override void OnClosing(CancelEventArgs e)
    {
        // Testfenster zuerst schließen lassen (beendet laufende Shows), dann die App
        if (testWindow != null)
        {
            e.Cancel = true;
            testWindow.Closed += (_, _) => Dispatcher.InvokeAsync(Close);
            testWindow.Close();
            return;
        }
        base.OnClosing(e);
    }
}
