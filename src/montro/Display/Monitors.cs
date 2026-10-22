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

        var edidNames = EdidNames();
        var result = new List<MonitorInfo>();
        for (var i = 0; i < ordered.Count; i++)
        {
            var (_, mi, dpi) = ordered[i];
            result.Add(new MonitorInfo(
                i + 1,
                mi.szDevice,
                edidNames.TryGetValue(mi.szDevice, out var edid) ? edid : FriendlyName(mi.szDevice),
                PixelRect.From(mi.rcMonitor),
                PixelRect.From(mi.rcWork),
                (mi.dwFlags & Native.MONITORINFOF_PRIMARY) != 0,
                dpi));
        }
        return result;
    }

    /// <summary>
    /// GDI-Gerätename (\\.\DISPLAY2) → Monitorname aus den EDID-Daten (z. B. „Verbatim MT15“).
    /// EnumDisplayDevices liefert dafür oft nur „Generic PnP Monitor“. Leer, wenn die API scheitert.
    /// </summary>
    private static Dictionary<string, string> EdidNames()
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (Native.GetDisplayConfigBufferSizes(Native.QDC_ONLY_ACTIVE_PATHS, out var np, out var nm) != 0) return names;
            var paths = new Native.DISPLAYCONFIG_PATH_INFO[np];
            var modes = new Native.DISPLAYCONFIG_MODE_INFO[nm];
            if (Native.QueryDisplayConfig(Native.QDC_ONLY_ACTIVE_PATHS, ref np, paths, ref nm, modes, IntPtr.Zero) != 0) return names;
            for (var i = 0; i < np; i++)
            {
                var p = paths[i];
                var source = new Native.DISPLAYCONFIG_SOURCE_DEVICE_NAME
                {
                    header = new Native.DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = Native.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME,
                        size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.DISPLAYCONFIG_SOURCE_DEVICE_NAME>(),
                        adapterId = p.sourceInfo.adapterId,
                        id = p.sourceInfo.id,
                    },
                };
                var target = new Native.DISPLAYCONFIG_TARGET_DEVICE_NAME
                {
                    header = new Native.DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = Native.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME,
                        size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.DISPLAYCONFIG_TARGET_DEVICE_NAME>(),
                        adapterId = p.targetInfo.adapterId,
                        id = p.targetInfo.id,
                    },
                };
                if (Native.DisplayConfigGetDeviceInfo(ref source) != 0 || Native.DisplayConfigGetDeviceInfo(ref target) != 0) continue;
                var name = target.monitorFriendlyDeviceName?.Trim();
                // Bei Duplizieren hängen mehrere Monitore an einer Quelle – den ersten Namen behalten
                if (!string.IsNullOrEmpty(name) && !names.ContainsKey(source.viewGdiDeviceName))
                    names[source.viewGdiDeviceName] = name;
            }
        }
        catch { }
        return names;
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

    /// <summary>Fenster auf eine Lage setzen (physische Pixel), ohne es zu aktivieren; optional über alle anderen.</summary>
    public static bool MoveWindow(IntPtr hwnd, PixelRect r, bool topmost = false, IntPtr insertAfter = default) =>
        Native.SetWindowPos(hwnd, insertAfter != IntPtr.Zero ? insertAfter : topmost ? Native.HWND_TOPMOST : IntPtr.Zero,
            r.X, r.Y, r.Width, r.Height,
            (topmost || insertAfter != IntPtr.Zero ? 0 : Native.SWP_NOZORDER) | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);

    /// <summary>Fenster wieder ganz nach oben (topmost) holen, ohne Lage, Größe oder Fokus zu ändern.</summary>
    public static bool BringToTop(IntPtr hwnd) =>
        Native.IsWindow(hwnd) && Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);

    /// <summary>
    /// Sichtbares Hauptfenster eines Prozesses (Name ohne .exe) mit dieser Fensterklasse, dessen
    /// Titel <paramref name="titleContains"/> enthält (leer = egal). Zero = nicht gefunden.
    /// </summary>
    public static IntPtr FindProcessWindow(string processName, string className, string titleContains)
    {
        var pids = System.Diagnostics.Process.GetProcessesByName(processName).Select(p => (uint)p.Id).ToHashSet();
        IntPtr found = IntPtr.Zero, fallback = IntPtr.Zero;
        Native.EnumWindows((h, _) =>
        {
            Native.GetWindowThreadProcessId(h, out var pid);
            if (!pids.Contains(pid) || !Native.IsWindowVisible(h)) return true;
            var cls = new System.Text.StringBuilder(64);
            Native.GetClassName(h, cls, cls.Capacity);
            if (cls.ToString() != className) return true;
            var title = new System.Text.StringBuilder(512);
            Native.GetWindowText(h, title, title.Capacity);
            if (titleContains.Length == 0 || title.ToString().Contains(titleContains, StringComparison.OrdinalIgnoreCase))
            {
                found = h;
                return false;
            }
            if (fallback == IntPtr.Zero) fallback = h;
            return true;
        }, IntPtr.Zero);
        return found != IntPtr.Zero ? found : fallback;
    }

    public static uint SystemDpi()
    {
        try { return Native.GetDpiForSystem(); } catch { return 96; }
    }

    public static uint WindowDpi(IntPtr hwnd)
    {
        try { return Native.GetDpiForWindow(hwnd); } catch { return 0; }
    }
}
