using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Montro.Views;

/// <summary>Farbauswahl als Menü mit den movidal-Farben (statt des Mac-Farbwählers).</summary>
public static class ColorMenu
{
    public static readonly (string Name, string Hex)[] Palette =
    [
        ("White Noise", "#F5F5F5"), ("Gelb", "#FFC71F"), ("Orange", "#FF8C1A"), ("Rot", "#F53838"),
        ("Bass Lime", "#B2F141"), ("Grün", "#38D675"), ("Cloud Purple", "#B088FB"), ("Static Lavender", "#E8DFFC"),
        ("Live-Rot", "#F2465A"), ("Warnung", "#FFB13D"), ("Dark Matter", "#202020"), ("Schwarz", "#000000"),
    ];

    public static void Show(UIElement target, string current, Action<string> apply)
    {
        var menu = new ContextMenu { PlacementTarget = target, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var (name, hex) in Palette)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new Border { Width = 18, Height = 12, CornerRadius = new CornerRadius(3), Background = Swatch(hex), Margin = new Thickness(0, 0, 8, 0), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1) });
            row.Children.Add(new TextBlock { Text = name });
            var mi = new MenuItem { Header = row, IsCheckable = true, IsChecked = string.Equals(hex, current, StringComparison.OrdinalIgnoreCase) };
            string h = hex;
            mi.Click += (_, _) => apply(h);
            menu.Items.Add(mi);
        }
        menu.IsOpen = true;
    }

    public static Brush Swatch(string hex)
    {
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        catch { return Brushes.Gray; }
    }
}
