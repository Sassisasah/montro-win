using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Montro.Core;

namespace Montro.Display;

/// <summary>
/// Schwarzbild- und Übergangsfenster auf den Ausgabebildschirmen (Mac: OutputManager).
/// Randlos, über allem, ohne Fokus und ohne Mausklicks. Wie das PowerPoint-Fenster 1 Pixel
/// niedriger als der Bildschirm (Vollbild-Direktmodus, siehe ARCHITEKTUR.md).
/// </summary>
public sealed class OutputManager
{
    private readonly Dictionary<string, Window> blackWindows = new();
    private Window? transitionWindow;

    public bool TransitionActive => transitionWindow != null;

    /// <summary>Fenster-Handle der Übergangsabdeckung (Zero = keine).</summary>
    public IntPtr TransitionHandle => transitionWindow != null ? new WindowInteropHelper(transitionWindow).Handle : IntPtr.Zero;

    /// <summary>Genau diese Bildschirme (Gerätenamen) schwarz, alle anderen frei.</summary>
    public void ApplyBlack(ISet<string> devices)
    {
        foreach (var (device, w) in blackWindows.ToList())
        {
            if (devices.Contains(device)) continue;
            blackWindows.Remove(device);
            Fade(w, 0, 0.2, () => w.Close());
        }
        var monitors = Monitors.All();
        foreach (var device in devices)
        {
            var m = monitors.FirstOrDefault(x => x.DeviceName == device);
            if (m == null) continue;
            if (blackWindows.TryGetValue(device, out var existing))
            {
                Place(existing, m);
                continue;
            }
            var w = CreateCover(m, null);
            blackWindows[device] = w;
            w.Opacity = 0;
            w.Show();
            Place(w, m);
            Fade(w, 1, 0.2);
        }
    }

    /// <summary>Publikumsbildschirm abdecken: Standbild (Überblenden) oder Schwarz.</summary>
    public async Task BeginTransition(string? stillImage, MonitorInfo screen, double fadeIn)
    {
        transitionWindow?.Close();
        var w = CreateCover(screen, stillImage);
        w.Opacity = 0;
        w.Show();
        Place(w, screen);
        transitionWindow = w;
        await FadeAsync(w, 1, fadeIn);
    }

    /// <summary>Abdeckung ausblenden und die neue Präsentation freigeben.</summary>
    public async Task EndTransition(double fadeOut)
    {
        if (transitionWindow is not { } w) return;
        await FadeAsync(w, 0, fadeOut);
        w.Close();
        if (transitionWindow == w) transitionWindow = null;
    }

    /// <summary>Abdeckungen wieder ganz nach oben holen (über das PowerPoint-Fenster).</summary>
    public void RaiseAll()
    {
        foreach (var w in blackWindows.Values) Raise(w);
        if (transitionWindow != null) Raise(transitionWindow);
    }

    public void CloseAll()
    {
        foreach (var w in blackWindows.Values) w.Close();
        blackWindows.Clear();
        transitionWindow?.Close();
        transitionWindow = null;
    }

    private static Window CreateCover(MonitorInfo m, string? image)
    {
        var w = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,   // nötig für das Ein- und Ausblenden (Opacity)
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            Background = Brushes.Black,
            Focusable = false,
            IsHitTestVisible = false,
            Title = "montro Ausgabe",
        };
        if (image != null && new Views.PathToImage().Convert(image, typeof(ImageSource), null, System.Globalization.CultureInfo.InvariantCulture) is ImageSource src)
            w.Content = new Image { Source = src, Stretch = Stretch.Uniform };
        w.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            int ex = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
            Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE,
                ex | Native.WS_EX_NOACTIVATE | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW);
            // WM_DPICHANGED selbst behandeln: Sonst vergrößert WPF das Fenster beim Wechsel auf einen
            // Bildschirm mit 150 % um den Faktor 1,5 (am Test-PC 2880 statt 1920 px breit, ragte auf
            // den nächsten Verbatim). Die Lage setzt montro immer selbst in physischen Pixeln.
            HwndSource.FromHwnd(hwnd)?.AddHook((IntPtr h, int msg, IntPtr wp, IntPtr lp, ref bool handled) =>
            {
                if (msg == Native.WM_DPICHANGED) handled = true;
                return IntPtr.Zero;
            });
        };
        return w;
    }

    /// <summary>In physischen Pixeln platzieren (WPF-Koordinaten wären DPI-abhängig).</summary>
    private static void Place(Window w, MonitorInfo m)
    {
        var hwnd = new WindowInteropHelper(w).Handle;
        if (hwnd == IntPtr.Zero) return;
        var b = m.Bounds;
        Monitors.MoveWindow(hwnd, new PixelRect(b.X, b.Y, b.Width, b.Height - 1), topmost: true);
    }

    private static void Raise(Window w)
    {
        var hwnd = new WindowInteropHelper(w).Handle;
        if (hwnd != IntPtr.Zero) Monitors.BringToTop(hwnd);
    }

    private static void Fade(Window w, double to, double seconds, Action? done = null)
    {
        var a = new DoubleAnimation(to, TimeSpan.FromSeconds(Math.Max(seconds, 0.01)));
        if (done != null) a.Completed += (_, _) => done();
        w.BeginAnimation(UIElement.OpacityProperty, a);
    }

    private static Task FadeAsync(Window w, double to, double seconds)
    {
        var tcs = new TaskCompletionSource();
        Fade(w, to, seconds, () => tcs.TrySetResult());
        return tcs.Task;
    }
}
