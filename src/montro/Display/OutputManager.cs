using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Montro.Core;

namespace Montro.Display;

/// <summary>
/// Schwarzbild- und Übergangsfenster auf den Ausgabebildschirmen (Mac: OutputManager).
/// Randlos, ohne Fokus und ohne Mausklicks, in voller Bildschirmgröße. Das Schwarzbild liegt
/// immer auf dem Publikumsbildschirm, je nach Zustand über oder unter der Show.
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
                Place(existing, m, raise: false);   // Lage nachziehen, Stapelreihenfolge nicht ändern
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
    public async Task BeginTransition(ImageSource? stillImage, MonitorInfo screen, double fadeIn)
    {
        transitionWindow?.Close();
        var w = CreateCover(screen, stillImage);
        // Standbild = Bildschirmfoto des aktuellen Bilds: sofort voll zeigen, ohne Einblenden – ein
        // Einblenden über dasselbe Bild flackert nur (erstes Bild der Ebene, Zwischenstufen).
        // Nicht vorab per Left/Top/Width/Height legen: WPF rechnet die in DIPs und machte das Fenster
        // auf dem 150-%-Verbatim kurz 2880 px breit (Störungen am rechten Rand, 21.10.2026).
        bool instant = stillImage != null || fadeIn < 0.05;
        w.Opacity = 0;
        w.Show();
        Place(w, screen);
        transitionWindow = w;
        if (instant)
        {
            // erst sichtbar machen, wenn es an der richtigen Stelle liegt
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Render);
            w.Opacity = 1;
        }
        else
        {
            await FadeAsync(w, 1, fadeIn);
        }
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
    public void RaiseAll(bool black = true)
    {
        if (black)
            foreach (var w in blackWindows.Values) Raise(w);
        if (transitionWindow != null) Raise(transitionWindow);
        foreach (var w in testPatternWindows) Raise(w);
    }

    public void CloseAll()
    {
        ShowTestPattern(Array.Empty<MonitorInfo>(), _ => new Grid());
        foreach (var w in blackWindows.Values) w.Close();
        blackWindows.Clear();
        transitionWindow?.Close();
        transitionWindow = null;
        ClosePresenter();
    }

    // MARK: - Testbild (Mac: showTestPattern)

    private readonly List<Window> testPatternWindows = new();

    /// <summary>Testbild auf genau diesen Bildschirmen, leer = ausblenden. Liegt über Schwarzbild und Übergang.</summary>
    public void ShowTestPattern(IEnumerable<MonitorInfo> screens, Func<MonitorInfo, FrameworkElement> content)
    {
        foreach (var w in testPatternWindows) w.Close();
        testPatternWindows.Clear();
        foreach (var m in screens)
        {
            // „layered“ wie das Schwarzbild: volle Höhe, ohne das Verbatim-Problem
            var w = CreateCover(m, null);
            w.Content = content(m);
            w.Show();
            Place(w, m);
            testPatternWindows.Add(w);
        }
    }

    // MARK: - Ausgabe Referent*innenansicht (Mac: ensurePresenterWindow)

    private Window? presenterWindow;
    private string? presenterDevice;   // null = Popup-Fenster

    public bool PresenterPopupOpen => presenterWindow != null && presenterDevice == null;

    /// <summary>
    /// Ausgabefenster herstellen: randlos in voller Größe auf dem zugewiesenen Bildschirm, sonst
    /// ein normales Fenster „Referent*innenansicht“. <paramref name="showPopup"/> öffnet das Popup
    /// auch, wenn es gerade nicht offen ist (beim Live-Schalten, „Fenster zeigen“).
    /// </summary>
    public void EnsurePresenter(MonitorInfo? screen, bool showPopup, Func<FrameworkElement> content)
    {
        if (screen != null)
        {
            if (presenterWindow == null || presenterDevice != screen.DeviceName)
            {
                ClosePresenter();
                // „layered“ wie die Abdeckungen: läuft nie im Vollbild-Direktmodus, daher volle Größe
                // ohne das Verbatim-Problem (schwarz/Störungen bei randlosem Vollbild)
                var w = CreateCover(screen, null);
                w.Title = "montro Referent*innenansicht";
                w.Content = content();
                presenterWindow = w;
                presenterDevice = screen.DeviceName;
                w.Show();
                DebugLog.Write($"Referent*innenansicht auf {screen.FriendlyName} ({screen.Number})");
            }
            Place(presenterWindow, screen, raise: true);
            return;
        }
        if (presenterDevice != null) ClosePresenter();
        if (!showPopup) return;
        if (presenterWindow == null)
        {
            var w = new Window
            {
                Title = "Referent*innenansicht",
                Width = 1120,
                Height = 660,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = Brushes.Black,
                ShowActivated = false,
                Content = content(),
            };
            if (Application.Current?.MainWindow?.Icon is { } icon) w.Icon = icon;
            w.Closed += (_, _) =>
            {
                if (presenterWindow == w) presenterWindow = null;
            };
            presenterWindow = w;
            presenterDevice = null;
            w.Show();
        }
        if (presenterWindow.WindowState == WindowState.Minimized) presenterWindow.WindowState = WindowState.Normal;
        Raise(presenterWindow);
    }

    /// <summary>Popup der Referent*innenansicht an eine feste Stelle legen (physische Pixel).</summary>
    public void PlacePresenterPopup(PixelRect r)
    {
        if (presenterDevice != null || presenterWindow is not { } w) return;
        var hwnd = new WindowInteropHelper(w).Handle;
        if (hwnd == IntPtr.Zero) return;
        if (w.WindowState != WindowState.Normal) w.WindowState = WindowState.Normal;
        Monitors.MoveWindow(hwnd, r);
    }

    public void ClosePresenter()
    {
        var w = presenterWindow;
        presenterWindow = null;
        presenterDevice = null;
        w?.Close();
    }

    private static Window CreateCover(MonitorInfo m, ImageSource? image)
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
        if (image != null)
        {
            // Bildschirmfoto in Originalgröße: 1:1 auf den Bildschirm, ohne Umrechnen (sonst unscharf)
            var img = new Image { Source = image, Stretch = Stretch.Fill, SnapsToDevicePixels = true };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            w.Content = img;
        }
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
    /// <remarks>
    /// Volle Bildschirmhöhe: Diese Fenster sind „layered“ (AllowsTransparency) und laufen nie im
    /// Vollbild-Direktmodus, das Verbatim-Problem betrifft nur das PowerPoint-Fenster. So deckt das
    /// Schwarzbild auch die unterste Pixelzeile unter der Show ab.
    /// </remarks>
    private static void Place(Window w, MonitorInfo m, bool raise = true)
    {
        var hwnd = new WindowInteropHelper(w).Handle;
        if (hwnd == IntPtr.Zero) return;
        if (raise)
            Monitors.MoveWindow(hwnd, m.Bounds, topmost: true);
        else
            Monitors.MoveWindow(hwnd, m.Bounds);
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
