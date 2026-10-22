using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Montro.Core;
using Montro.Display;

namespace Montro.Testing;

/// <summary>
/// Prüft das Live-Bild per DWM-Miniatur in einem „layered“ Fenster (wie die Referent*innenansicht
/// auf eigenem Bildschirm): <c>montro.exe --miniaturtest &lt;bild.png&gt; [Prozessname]</c>.
/// Zeigt 2 s lang ein Fenster oben links auf dem Hauptbildschirm mit der Miniatur eines fremden
/// Fensters (Standard: Explorer-Taskleiste) und speichert ein Bildschirmfoto davon.
/// </summary>
public static class ThumbnailTest
{
    public static async Task<int> RunAsync(string[] args)
    {
        int i = Array.IndexOf(args, "--miniaturtest");
        var rest = args.Skip(i + 1).Where(a => !a.StartsWith("--")).ToList();
        string file = Path.GetFullPath(rest.FirstOrDefault() ?? "miniatur.png");
        string process = rest.Count > 1 ? rest[1] : "explorer";
        var source = Process.GetProcessesByName(process).Select(p => p.MainWindowHandle).FirstOrDefault(h => h != IntPtr.Zero);
        if (source == IntPtr.Zero) source = FindWindow("Shell_TrayWnd", null);

        var target = new Grid { Background = Brushes.DarkRed, Margin = new Thickness(20) };
        var w = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Black,
            Topmost = true,
            ShowInTaskbar = false,
            Width = 800,
            Height = 450,
            Left = 0,
            Top = 0,
            Content = target,
        };
        w.Show();
        var thumb = new LiveThumbnail(target) { Source = source };
        await Task.Delay(1500);
        thumb.Update();
        await Task.Delay(500);
        var px = new PixelRect(0, 0, (int)(800 * Scale(w)), (int)(450 * Scale(w)));
        ScreenShot.Capture(px).SavePng(file);
        bool shown = thumb.IsShowing;
        DebugLog.Write($"Miniaturtest: Quelle {source}, sichtbar {shown}, Bild {file}");
        w.Close();
        return shown ? 0 : 1;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? cls, string? title);

    private static double Scale(Visual v) => PresentationSource.FromVisual(v)?.CompositionTarget.TransformToDevice.M11 ?? 1;
}

