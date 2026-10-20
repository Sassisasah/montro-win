namespace Montro.Display;

/// <summary>Rechteck in physischen Pixeln (virtueller Desktop).</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;

    public bool RoughlyEquals(PixelRect o, int tolerance = 2) =>
        Math.Abs(X - o.X) <= tolerance && Math.Abs(Y - o.Y) <= tolerance
        && Math.Abs(Width - o.Width) <= tolerance && Math.Abs(Height - o.Height) <= tolerance;

    public override string ToString() => $"({X},{Y}) {Width}×{Height}";

    internal static PixelRect From(Native.RECT r) => new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
}

/// <summary>Ein Monitor, wie Windows ihn gerade meldet.</summary>
public sealed record MonitorInfo(
    int Number,
    string DeviceName,   // z. B. \\.\DISPLAY2 – stabil für die Sitzung
    string FriendlyName, // z. B. „DELL U2720Q“, sonst „Generic PnP Monitor“
    PixelRect Bounds,
    PixelRect WorkArea,
    bool IsPrimary,
    uint Dpi)
{
    public double Scale => Dpi / 96.0;

    public string Label =>
        $"{Number}: {FriendlyName} – {Bounds.Width}×{Bounds.Height}, {Scale:P0}{(IsPrimary ? ", Hauptbildschirm" : "")} [{DeviceName} @ {Bounds.X},{Bounds.Y}]";

    public override string ToString() => Label;
}

public static class Monitors
{
    /// <summary>Alle Monitore, Hauptbildschirm zuerst, dann von links nach rechts.</summary>
    public static IReadOnlyList<MonitorInfo> All()
    {
        var raw = new List<(IntPtr h, Native.MONITORINFOEX mi, uint dpi)>();
        Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr hdc, ref Native.RECT clip, IntPtr data) =>
        {
            var mi = new Native.MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFOEX>() };
            if (Native.GetMonitorInfo(h, ref mi))
            {
                uint dpi = 96;
                try
                {
                    if (Native.GetDpiForMonitor(h, Native.MDT_EFFECTIVE_DPI, out var dx, out _) == 0) dpi = dx;
                }
                catch { }
                raw.Add((h, mi, dpi));
            }
            return true;
        }, IntPtr.Zero);

        var ordered = raw
            .OrderByDescending(r => (r.mi.dwFlags & Native.MONITORINFOF_PRIMARY) != 0)
            .ThenBy(r => r.mi.rcMonitor.Left)
            .ThenBy(r => r.mi.rcMonitor.Top)
            .ToList();

        var result = new List<MonitorInfo>();
        for (var i = 0; i < ordered.Count; i++)
        {
            var (_, mi, dpi) = ordered[i];
            result.Add(new MonitorInfo(
                i + 1,
                mi.szDevice,
                FriendlyName(mi.szDevice),
                PixelRect.From(mi.rcMonitor),
                PixelRect.From(mi.rcWork),
                (mi.dwFlags & Native.MONITORINFOF_PRIMARY) != 0,
                dpi));
        }
        return result;
    }

    private static string FriendlyName(string device)
    {
        try
        {
            var dd = new Native.DISPLAY_DEVICE { cb = System.Runtime.InteropServices.Marshal.SizeOf<Native.DISPLAY_DEVICE>() };
            if (Native.EnumDisplayDevices(device, 0, ref dd, 0) && !string.IsNullOrWhiteSpace(dd.DeviceString))
                return dd.DeviceString.Trim();
        }
        catch { }
        return device;
    }

    /// <summary>Aktuelle Lage eines Fensters in physischen Pixeln.</summary>
    public static PixelRect? WindowRect(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !Native.IsWindow(hwnd)) return null;
        return Native.GetWindowRect(hwnd, out var r) ? PixelRect.From(r) : null;
    }

    /// <summary>Fenster auf eine Lage setzen (physische Pixel), ohne es zu aktivieren.</summary>
    public static bool MoveWindow(IntPtr hwnd, PixelRect r) =>
        Native.SetWindowPos(hwnd, IntPtr.Zero, r.X, r.Y, r.Width, r.Height,
            Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);

    public static uint SystemDpi()
    {
        try { return Native.GetDpiForSystem(); } catch { return 96; }
    }

    public static uint WindowDpi(IntPtr hwnd)
    {
        try { return Native.GetDpiForWindow(hwnd); } catch { return 0; }
    }
}
