using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Montro.Display;

/// <summary>Bildschirmfoto eines Bereichs in physischen Pixeln (Standbild fürs Überblenden, Selbsttest).</summary>
public sealed class ScreenShot
{
    private readonly byte[] pixels;   // BGRA
    private readonly int stride;

    public int Width { get; }
    public int Height { get; }
    public BitmapSource Image { get; }

    private ScreenShot(BitmapSource image)
    {
        var bgra = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        Width = bgra.PixelWidth;
        Height = bgra.PixelHeight;
        stride = Width * 4;
        pixels = new byte[stride * Height];
        bgra.CopyPixels(pixels, stride, 0);
        Image = image;
    }

    public static ScreenShot Capture(PixelRect r)
    {
        IntPtr screen = Native.GetDC(IntPtr.Zero);
        IntPtr mem = Native.CreateCompatibleDC(screen);
        IntPtr bmp = Native.CreateCompatibleBitmap(screen, r.Width, r.Height);
        IntPtr old = Native.SelectObject(mem, bmp);
        try
        {
            Native.BitBlt(mem, 0, 0, r.Width, r.Height, screen, r.X, r.Y, Native.SRCCOPY | Native.CAPTUREBLT);
            Native.SelectObject(mem, old);
            var src = Imaging.CreateBitmapSourceFromHBitmap(bmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return new ScreenShot(src);
        }
        finally
        {
            Native.DeleteObject(bmp);
            Native.DeleteDC(mem);
            Native.ReleaseDC(IntPtr.Zero, screen);
        }
    }

    public Color Pixel(int x, int y)
    {
        int i = y * stride + x * 4;
        return Color.FromRgb(pixels[i + 2], pixels[i + 1], pixels[i]);
    }

    /// <summary>Die vier Ecken (2 px nach innen) und die Mitte der oberen und unteren Kante.</summary>
    public IEnumerable<(string Where, Color Color)> EdgeSamples()
    {
        int x1 = Width - 3, y1 = Height - 3, xm = Width / 2;
        yield return ("oben links", Pixel(2, 2));
        yield return ("oben rechts", Pixel(x1, 2));
        yield return ("unten links", Pixel(2, y1));
        yield return ("unten rechts", Pixel(x1, y1));
        yield return ("oben Mitte", Pixel(xm, 2));
        yield return ("unten Mitte", Pixel(xm, y1));
    }

    public static bool Near(Color a, Color b, int tolerance = 24) =>
        Math.Abs(a.R - b.R) <= tolerance && Math.Abs(a.G - b.G) <= tolerance && Math.Abs(a.B - b.B) <= tolerance;

    public void SavePng(string path)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(Image));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}
