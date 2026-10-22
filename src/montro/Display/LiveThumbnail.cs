using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Montro.Core;

namespace Montro.Display;

/// <summary>
/// Live-Bild der Show als DWM-Miniatur (wie die Vorschau in der Taskleiste): Windows spiegelt das
/// Show-Fenster von PowerPoint mit voller Bildrate in den Bereich eines montro-Elements, ohne
/// Bildschirmfotos und ohne Rechenlast (Mac: LiveCapture mit ScreenCaptureKit).
/// Die Miniatur liegt über dem WPF-Inhalt des Fensters; sie wird auf den sichtbaren Teil des
/// Elements (Bildlauf, Fensterrand) zugeschnitten.
/// </summary>
public sealed class LiveThumbnail
{
    private readonly FrameworkElement target;
    private IntPtr thumb;
    private IntPtr dest;
    private IntPtr source;
    private bool visible;

    public LiveThumbnail(FrameworkElement target)
    {
        this.target = target;
        target.LayoutUpdated += (_, _) => Update();
        target.IsVisibleChanged += (_, _) => Update();
        target.Unloaded += (_, _) => Unregister();
    }

    /// <summary>Show-Fenster, das gezeigt werden soll; Zero = keins (dann sieht man das Element darunter).</summary>
    public IntPtr Source
    {
        get => source;
        set
        {
            if (source == value) return;
            source = value;
            Unregister();
            Update();
        }
    }

    /// <summary>Ob die Miniatur gerade zu sehen ist (sonst gilt das Bild darunter).</summary>
    public bool IsShowing => thumb != IntPtr.Zero && visible;

    public void Update()
    {
        if (source == IntPtr.Zero || !target.IsVisible || target.ActualWidth < 2 || target.ActualHeight < 2
            || PresentationSource.FromVisual(target) is not HwndSource hs)
        {
            Hide();
            return;
        }
        var window = hs.RootVisual as Visual;
        if (window == null) { Hide(); return; }
        if (thumb == IntPtr.Zero || dest != hs.Handle)
        {
            Unregister();
            dest = hs.Handle;
            if (DwmRegisterThumbnail(dest, source, out thumb) != 0)
            {
                thumb = IntPtr.Zero;
                return;
            }
        }
        if (DwmQueryThumbnailSourceSize(thumb, out var size) != 0 || size.cx <= 0 || size.cy <= 0)
        {
            Hide();
            return;
        }

        // Ziel in Gerätepixeln relativ zum Fenster
        Rect r;
        try { r = target.TransformToAncestor(window).TransformBounds(new Rect(0, 0, target.ActualWidth, target.ActualHeight)); }
        catch (InvalidOperationException) { Hide(); return; }
        var toDevice = hs.CompositionTarget.TransformToDevice;
        r = Rect.Transform(r, toDevice);

        // Quelle ins Ziel einpassen (Seitenverhältnis halten, zentriert)
        double scale = Math.Min(r.Width / size.cx, r.Height / size.cy);
        double w = size.cx * scale, h = size.cy * scale;
        var fit = new Rect(r.X + (r.Width - w) / 2, r.Y + (r.Height - h) / 2, w, h);

        // Auf den sichtbaren Bereich zuschneiden (ScrollViewer, Fenster)
        var clip = VisibleArea(target, window, toDevice);
        var shown = Rect.Intersect(fit, clip);
        if (shown.IsEmpty || shown.Width < 1 || shown.Height < 1)
        {
            Hide();
            return;
        }
        double sx = size.cx / fit.Width, sy = size.cy / fit.Height;
        var props = new DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = DWM_TNP_RECTDESTINATION | DWM_TNP_RECTSOURCE | DWM_TNP_VISIBLE | DWM_TNP_OPACITY | DWM_TNP_SOURCECLIENTAREAONLY,
            rcDestination = new RECT((int)Math.Round(shown.Left), (int)Math.Round(shown.Top), (int)Math.Round(shown.Right), (int)Math.Round(shown.Bottom)),
            rcSource = new RECT((int)Math.Round((shown.Left - fit.Left) * sx), (int)Math.Round((shown.Top - fit.Top) * sy),
                (int)Math.Round((shown.Right - fit.Left) * sx), (int)Math.Round((shown.Bottom - fit.Top) * sy)),
            opacity = 255,
            fVisible = true,
            fSourceClientAreaOnly = true,
        };
        if (DwmUpdateThumbnailProperties(thumb, ref props) == 0) visible = true;
    }

    /// <summary>Sichtbarer Bereich des Elements: Fensterinhalt geschnitten mit allen umgebenden ScrollViewern.</summary>
    private static Rect VisibleArea(FrameworkElement target, Visual window, Matrix toDevice)
    {
        var area = window is FrameworkElement fw
            ? Rect.Transform(new Rect(0, 0, fw.ActualWidth, fw.ActualHeight), toDevice)
            : new Rect(-1e6, -1e6, 2e6, 2e6);
        DependencyObject? p = VisualTreeHelper.GetParent(target);
        while (p != null && p != window)
        {
            if (p is ScrollContentPresenter scp)
            {
                var vr = scp.TransformToAncestor(window).TransformBounds(new Rect(0, 0, scp.ActualWidth, scp.ActualHeight));
                area = Rect.Intersect(area, Rect.Transform(vr, toDevice));
            }
            p = VisualTreeHelper.GetParent(p);
        }
        return area;
    }

    private void Hide()
    {
        if (thumb == IntPtr.Zero || !visible) return;
        var props = new DWM_THUMBNAIL_PROPERTIES { dwFlags = DWM_TNP_VISIBLE, fVisible = false };
        DwmUpdateThumbnailProperties(thumb, ref props);
        visible = false;
    }

    private void Unregister()
    {
        if (thumb != IntPtr.Zero) DwmUnregisterThumbnail(thumb);
        thumb = IntPtr.Zero;
        dest = IntPtr.Zero;
        visible = false;
    }

    // MARK: - DWM

    private const int DWM_TNP_RECTDESTINATION = 0x1;
    private const int DWM_TNP_RECTSOURCE = 0x2;
    private const int DWM_TNP_OPACITY = 0x4;
    private const int DWM_TNP_VISIBLE = 0x8;
    private const int DWM_TNP_SOURCECLIENTAREAONLY = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
        public RECT(int l, int t, int r, int b) { Left = l; Top = t; Right = r; Bottom = b; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int cx, cy; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DWM_THUMBNAIL_PROPERTIES
    {
        public int dwFlags;
        public RECT rcDestination;
        public RECT rcSource;
        public byte opacity;
        [MarshalAs(UnmanagedType.Bool)] public bool fVisible;
        [MarshalAs(UnmanagedType.Bool)] public bool fSourceClientAreaOnly;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmRegisterThumbnail(IntPtr dest, IntPtr src, out IntPtr thumb);

    [DllImport("dwmapi.dll")]
    private static extern int DwmUnregisterThumbnail(IntPtr thumb);

    [DllImport("dwmapi.dll")]
    private static extern int DwmUpdateThumbnailProperties(IntPtr thumb, ref DWM_THUMBNAIL_PROPERTIES props);

    [DllImport("dwmapi.dll")]
    private static extern int DwmQueryThumbnailSourceSize(IntPtr thumb, out SIZE size);
}
