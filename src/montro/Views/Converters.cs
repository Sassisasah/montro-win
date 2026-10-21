using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Montro.Views;

/// <summary>true → sichtbar; mit Parameter „not“ umgekehrt.</summary>
public sealed class BoolToVisibility : IValueConverter
{
    public object Convert(object value, Type t, object? p, CultureInfo c)
    {
        bool v = value is true;
        if (p as string == "not") v = !v;
        return v ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>null/leer → ausgeblendet; mit Parameter „not“ umgekehrt.</summary>
public sealed class NullToVisibility : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        bool has = value is string s ? s.Length > 0 : value != null;
        if (p as string == "not") has = !has;
        return has ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Bilddatei → ImageSource, ohne die Datei zu sperren (Vorschaubilder werden neu erzeugt).</summary>
public sealed class PathToImage : IValueConverter
{
    private static readonly Dictionary<string, (DateTime Time, ImageSource Image)> Cache = new();

    public object? Convert(object? value, Type t, object? p, CultureInfo c)
    {
        if (value is not string path || path.Length == 0 || !File.Exists(path)) return null;
        try
        {
            var time = File.GetLastWriteTimeUtc(path);
            if (Cache.TryGetValue(path, out var hit) && hit.Time == time) return hit.Image;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bmp.UriSource = new Uri(path);
            if (p is string w && int.TryParse(w, out int width)) bmp.DecodePixelWidth = width;
            bmp.EndInit();
            bmp.Freeze();
            Cache[path] = (time, bmp);
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    public object ConvertBack(object value, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>true → Farbe aus dem Parameter (Ressourcenschlüssel), sonst transparent.</summary>
public sealed class BoolToBrush : IValueConverter
{
    public object Convert(object value, Type t, object? p, CultureInfo c) =>
        value is true && p is string key && Application.Current.TryFindResource(key) is Brush b ? b : Brushes.Transparent;

    public object ConvertBack(object value, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}
