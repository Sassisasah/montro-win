using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Montro.Views;

/// <summary>
/// Testbild für die Ausgaben, gleich aufgebaut wie am Mac (<c>Views/TestPatternView.swift</c>), damit
/// man am Mischer oder Vorschaumonitor vergleichen kann, welcher Rechner abweicht. Alle Werte sind
/// 8-Bit-sRGB, wie sie in einer Folie stehen würden. Gezeichnet auf 1920×1080 und auf den Bildschirm gestreckt.
/// </summary>
public static class TestPattern
{
    private sealed record Swatch(string Name, uint Hex, bool Light);

    private static readonly Swatch[] Bars =
    [
        new("Weiß", 0xFFFFFF, true), new("Gelb", 0xFFFF00, true), new("Cyan", 0x00FFFF, true), new("Grün", 0x00FF00, true),
        new("Magenta", 0xFF00FF, false), new("Rot", 0xFF0000, false), new("Blau", 0x0000FF, false), new("Schwarz", 0x000000, false),
    ];

    /// <summary>Graukeil in 11 Stufen (0 %, 10 % … 100 %)</summary>
    private static readonly int[] Grays = Enumerable.Range(0, 11).Select(i => (int)Math.Round(i * 25.5, MidpointRounding.AwayFromZero)).ToArray();

    /// <summary>16 bzw. 235 = Grenzen des begrenzten Videobereichs.</summary>
    private static readonly int[] Blacks = [0, 4, 8, 16, 24, 32];
    private static readonly int[] Whites = [215, 225, 235, 245, 250, 255];

    private static readonly Swatch[] BrandColors =
    [
        new("Cloud Purple", 0xB088FB, true), new("Lavender", 0xE8DFFC, true), new("Noir Royale", 0x35294B, false),
        new("Bass Lime", 0xB2F141, true), new("Dark Camo", 0x314412, false), new("Dark Matter", 0x202020, false),
    ];

    private const double W = 1920, H = 1080, TextSize = H * 0.022;

    /// <param name="caption">Rechnername, Bildschirm, Auflösung und Farbangaben (unten links)</param>
    public static FrameworkElement Create(string caption)
    {
        var root = new Grid { Width = W, Height = H, Background = Gray(0) };
        foreach (var share in new[] { 0.40, 0.18, 0.18, 0.14, 0.10 })
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(share, GridUnitType.Star) });

        AddRow(root, 0, Bars.Select(s => Cell(Rgb(s.Hex), s.Name, s.Light)));
        AddRow(root, 1, Grays.Select(v => Cell(Gray(v), v.ToString(), v > 128)));
        AddRow(root, 2, Blacks.Select(v => Patch(v, 0)).Concat(Whites.Select(v => Patch(v, 255))));
        AddRow(root, 3, BrandColors.Select(s => Cell(Rgb(s.Hex), $"{s.Name}\n#{s.Hex:X6}", s.Light)));

        var footer = new DockPanel { Margin = new Thickness(H * 0.02, 0, H * 0.02, 0), Background = Gray(0) };
        var right = Caption("16 und 235 müssen sich vom Rand abheben, sonst ist der Signalbereich begrenzt", Gray(200), FontWeights.Medium);
        DockPanel.SetDock(right, Dock.Right);
        footer.Children.Add(right);
        footer.Children.Add(Caption($"montro Testbild · {caption}", Gray(200), FontWeights.Medium));
        Grid.SetRow(footer, 4);
        root.Children.Add(footer);

        return new Viewbox { Stretch = Stretch.Fill, Child = root };
    }

    private static void AddRow(Grid root, int row, IEnumerable<FrameworkElement> cells)
    {
        var g = new UniformGrid { Rows = 1 };
        foreach (var c in cells) g.Children.Add(c);
        Grid.SetRow(g, row);
        root.Children.Add(g);
    }

    private static FrameworkElement Cell(Brush color, string label, bool light)
    {
        var t = Caption(label, light ? Gray(0) : Gray(255), FontWeights.SemiBold);
        t.TextAlignment = TextAlignment.Center;
        t.VerticalAlignment = VerticalAlignment.Bottom;
        t.HorizontalAlignment = HorizontalAlignment.Center;
        t.Margin = new Thickness(0, 0, 0, 6);
        return new Border { Background = color, Child = t };
    }

    /// <summary>Feld mit Wert <paramref name="v"/> mittig auf Hintergrund <paramref name="bg"/>.</summary>
    private static FrameworkElement Patch(int v, int bg)
    {
        var g = new Grid { Background = Gray(bg) };
        g.Children.Add(new Border
        {
            Background = Gray(v),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(W / 12 * 0.2, H * 0.18 * 0.2, W / 12 * 0.2, H * 0.18 * 0.2),
        });
        var t = Caption(v.ToString(), bg == 0 ? Gray(160) : Gray(90), FontWeights.SemiBold);
        t.VerticalAlignment = VerticalAlignment.Bottom;
        t.HorizontalAlignment = HorizontalAlignment.Center;
        t.Margin = new Thickness(0, 0, 0, 2);
        g.Children.Add(t);
        return g;
    }

    private static TextBlock Caption(string text, Brush color, FontWeight weight) => new()
    {
        Text = text,
        Foreground = color,
        FontSize = TextSize,
        FontWeight = weight,
        FontFamily = new FontFamily("Segoe UI"),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static SolidColorBrush Rgb(uint hex) =>
        Frozen(Color.FromRgb((byte)(hex >> 16), (byte)(hex >> 8), (byte)hex));

    private static SolidColorBrush Gray(int v) => Frozen(Color.FromRgb((byte)v, (byte)v, (byte)v));

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
