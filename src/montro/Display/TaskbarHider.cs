using System.Text;

namespace Montro.Display;

/// <summary>
/// Blendet die Taskleiste auf dem Publikumsbildschirm aus, solange montro ihn bespielt (Mac:
/// Menüleiste und Dock während der Show automatisch ausblenden). Windows legt die Taskleiste eines
/// Nebenbildschirms sonst immer wieder über Show und Schwarzbild – wegen des 1 Pixel niedrigeren
/// Fensters (Verbatim-Problem) gilt der Bildschirm nicht als „Vollbild“. Nur das Fenster wird
/// versteckt, keine Einstellung geändert; beim Beenden und beim Start zeigt montro alle wieder.
/// </summary>
public static class TaskbarHider
{
    private const int SW_HIDE = 0;
    private const int SW_SHOWNOACTIVATE = 4;
    private static readonly HashSet<IntPtr> Hidden = new();

    /// <summary>Taskleisten auf genau diesen Bildschirmen verstecken, alle anderen wieder zeigen.</summary>
    public static void Apply(IEnumerable<MonitorInfo> screens)
    {
        var targets = screens.ToList();
        foreach (var (hwnd, rect) in SecondaryTaskbars())
        {
            bool hide = targets.Any(m => Intersects(m.Bounds, rect));
            if (hide)
            {
                if (Native.IsWindowVisible(hwnd)) ShowWindow(hwnd, SW_HIDE);
                Hidden.Add(hwnd);
            }
            else if (Hidden.Remove(hwnd) || !Native.IsWindowVisible(hwnd))
            {
                ShowWindow(hwnd, SW_SHOWNOACTIVATE);
            }
        }
    }

    /// <summary>Alle Nebenbildschirm-Taskleisten wieder anzeigen (Beenden, Start nach Absturz).</summary>
    public static void RestoreAll()
    {
        foreach (var (hwnd, _) in SecondaryTaskbars())
        {
            if (!Native.IsWindowVisible(hwnd)) ShowWindow(hwnd, SW_SHOWNOACTIVATE);
        }
        Hidden.Clear();
    }

    private static List<(IntPtr Hwnd, PixelRect Rect)> SecondaryTaskbars()
    {
        var list = new List<(IntPtr, PixelRect)>();
        Native.EnumWindows((h, _) =>
        {
            var cls = new StringBuilder(64);
            Native.GetClassName(h, cls, cls.Capacity);
            if (cls.ToString() == "Shell_SecondaryTrayWnd" && Monitors.WindowRect(h) is { } r)
                list.Add((h, r));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    private static bool Intersects(PixelRect a, PixelRect b) =>
        a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int cmd);
}
