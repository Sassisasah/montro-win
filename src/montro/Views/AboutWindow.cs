using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Montro.Core;

namespace Montro.Views;

/// <summary>Info-Fenster „Über montro“ mit Version und Herkunftsangaben (Mac: AboutPanel).</summary>
public sealed class AboutWindow : Window
{
    public AboutWindow()
    {
        Title = "Über montro";
        Style = (Style)FindResource("Brand.Window");
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new StackPanel { Margin = new Thickness(32, 24, 32, 20), Width = 360 };
        var icon = new Image { Width = 96, Height = 96, Margin = new Thickness(0, 0, 0, 12) };
        try { icon.Source = BitmapFrame.Create(new Uri("pack://application:,,,/Resources/montro.ico")); } catch { }
        root.Children.Add(icon);
        root.Children.Add(Line("montro", 20, FontWeights.SemiBold));
        root.Children.Add(Line($"Version {AppInfo.Version} (Windows)", 12, FontWeights.Normal, secondary: true));
        root.Children.Add(new Border { Height = 14 });
        root.Children.Add(Line("Entwickelt für movidal", 12, FontWeights.SemiBold));
        root.Children.Add(Line("Konzept & Leitung: Sarah-Mathias Andrae", 12));
        root.Children.Add(Line("Programmiert mit Claude (Anthropic)", 12));
        root.Children.Add(new Border { Height = 10 });
        root.Children.Add(Line("Präsentations-Player für Bühne und Regie – funktioniert mit Microsoft PowerPoint. " +
                               "Referent*innenansicht, Redezeit-Timer, Regie-Nachrichten, Companion-Fernsteuerung und Verbund (Master/Slave).",
            11, FontWeights.Normal, secondary: true));
        root.Children.Add(new Border { Height = 12 });
        root.Children.Add(Line("© 2026 movidal · Sarah-Mathias Andrae", 11, FontWeights.Normal, secondary: true));
        var ok = new Button { Content = "OK", IsDefault = true, IsCancel = true, Style = (Style)FindResource("Brand.Compact"), Width = 80, Margin = new Thickness(0, 16, 0, 0) };
        ok.Click += (_, _) => Close();
        root.Children.Add(ok);
        Content = root;
    }

    private TextBlock Line(string text, double size, FontWeight? weight = null, bool secondary = false) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = weight ?? FontWeights.Normal,
        TextAlignment = TextAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
        HorizontalAlignment = HorizontalAlignment.Center,
        Foreground = secondary ? (Brush)FindResource("Brand.SecondaryText") : (Brush)FindResource("Brand.Text"),
        Margin = new Thickness(0, 1, 0, 1),
    };
}
