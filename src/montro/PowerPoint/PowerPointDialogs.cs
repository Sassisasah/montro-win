using System.Text;
using Montro.Display;

namespace Montro.PowerPoint;

/// <summary>
/// Findet offene Dialoge von PowerPoint (z. B. „Änderungen speichern?“), die jede Fernsteuerung
/// blockieren, und holt sie auf den Hauptbildschirm (Mac 0.34: Dialog benennen, PowerPoint nach
/// vorne). Ein Dialog auf einem Publikumsbildschirm unter dem Schwarzbild wäre sonst unsichtbar.
/// </summary>
public static class PowerPointDialogs
{
    /// <summary>Text der offenen Dialoge, z. B. „Microsoft PowerPoint: Möchten Sie … speichern?“; leer = keiner.</summary>
    public static List<string> Describe()
    {
        var result = new List<string>();
        foreach (var hwnd in Find())
        {
            var title = new StringBuilder(256);
            Native.GetWindowText(hwnd, title, title.Capacity);
            var texts = new List<string>();
            EnumChildWindows(hwnd, (c, _) =>
            {
                var cls = new StringBuilder(32);
                Native.GetClassName(c, cls, cls.Capacity);
                if (cls.ToString() is "Static" or "DirectUIHWND")
                {
                    var t = new StringBuilder(512);
                    Native.GetWindowText(c, t, t.Capacity);
                    if (t.Length > 0) texts.Add(t.ToString().Replace("\r", " ").Replace("\n", " ").Trim());
                }
                return true;
            }, IntPtr.Zero);
            string text = string.Join(" ", texts.Where(x => x.Length > 0));
            result.Add(text.Length > 0 ? $"{title}: {text}" : title.ToString());
        }
        return result;
    }

    /// <summary>Offene Dialoge auf den Hauptbildschirm holen und nach vorne bringen.</summary>
    public static void BringToFront()
    {
        var main = Monitors.All().FirstOrDefault(m => m.IsPrimary);
        foreach (var hwnd in Find())
        {
            if (main != null && Monitors.WindowRect(hwnd) is { } r && !Intersects(main.Bounds, r))
            {
                var wa = main.WorkArea;
                var target = new PixelRect(wa.X + (wa.Width - r.Width) / 2, wa.Y + (wa.Height - r.Height) / 2, r.Width, r.Height);
                Monitors.MoveWindow(hwnd, target, topmost: true);
            }
            else
            {
                Monitors.BringToTop(hwnd);
            }
            SetForegroundWindow(hwnd);
        }
    }

    private static List<IntPtr> Find()
    {
        var pids = System.Diagnostics.Process.GetProcessesByName("POWERPNT").Select(p => (uint)p.Id).ToHashSet();
        var list = new List<IntPtr>();
        Native.EnumWindows((h, _) =>
        {
            Native.GetWindowThreadProcessId(h, out var pid);
            if (!pids.Contains(pid) || !Native.IsWindowVisible(h)) return true;
            var cls = new StringBuilder(64);
            Native.GetClassName(h, cls, cls.Capacity);
            // klassische Dialoge (#32770) und die neueren Office-Dialoge (NUIDialog)
            if (cls.ToString() is "#32770" or "NUIDialog") list.Add(h);
            return true;
        }, IntPtr.Zero);
        return list;
    }

    private static bool Intersects(PixelRect a, PixelRect b) =>
        a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom;

    private delegate bool ChildProc(IntPtr hwnd, IntPtr data);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parent, ChildProc proc, IntPtr data);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);
}
