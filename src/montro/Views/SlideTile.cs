using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Montro.Core;

namespace Montro.Views;

/// <summary>
/// Kachel der Folienübersicht (Mac: SlideTile): Vorschaubild 140×79, ausgeblendete Folien
/// abgedunkelt, aktuelle Live-Folie rot umrandet, darunter Nummer („Folie x“ ohne
/// ausgeblendete) und Symbole für Animationen, Video und Notizen. DataContext = SlideMeta.
/// </summary>
public sealed class SlideTile : StackPanel
{
    private static readonly PathToImage Images = new();
    private readonly Border frame = new() { Width = 140, Height = 79, CornerRadius = new CornerRadius(4), Background = Brushes.Black, ClipToBounds = true };
    private readonly Image image = new() { Stretch = Stretch.Uniform };
    private readonly Grid hiddenOverlay = new() { Background = new SolidColorBrush(Color.FromArgb(140, 0, 0, 0)) };
    private readonly TextBlock fallback = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock number = new() { FontSize = 11 };
    private readonly TextBlock icons = new() { FontSize = 10, Margin = new Thickness(4, 0, 0, 0) };
    private AppState? state;

    public SlideTile()
    {
        var grid = new Grid();
        grid.Children.Add(fallback);
        grid.Children.Add(image);
        hiddenOverlay.Children.Add(new TextBlock
        {
            Text = "",
            Style = (Style)Application.Current.FindResource("Brand.Icon"),
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        grid.Children.Add(hiddenOverlay);
        frame.Child = grid;
        Children.Add(frame);

        var line = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 0) };
        icons.FontFamily = (FontFamily)Application.Current.FindResource("Brand.IconFont");
        icons.Foreground = Res("Brand.SecondaryText");
        line.Children.Add(number);
        line.Children.Add(icons);
        Children.Add(line);

        Cursor = System.Windows.Input.Cursors.Hand;
        DataContextChanged += (_, _) => Update();
        Loaded += (_, _) =>
        {
            state = Window.GetWindow(this)?.DataContext as AppState;
            if (state != null) state.PropertyChanged += OnState;
            Update();
        };
        Unloaded += (_, _) =>
        {
            if (state != null) state.PropertyChanged -= OnState;
        };
    }

    /// <summary>Farbe aus den App-Ressourcen; beim Beenden sind die schon weg (dann grau statt Fehler).</summary>
    private static Brush Res(string key) =>
        Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;

    private void OnState(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppState.CurrentSlide) or nameof(AppState.NavigatorItem) or nameof(AppState.CanControl))
            Update();
    }

    private void Update()
    {
        if (DataContext is not SlideMeta slide) return;
        var item = state?.NavigatorItem;
        bool isLive = item != null && item == state!.LiveItem && state.IsShowRunning;
        bool isCurrent = isLive && slide.Index == state!.CurrentSlide;
        int? pos = item?.VisiblePosition(slide.Index);

        image.Source = Images.Convert(item?.Thumbnail(slide.Index), typeof(ImageSource), "280", System.Globalization.CultureInfo.InvariantCulture) as ImageSource;
        fallback.Text = slide.Index.ToString();
        fallback.Foreground = Res("Brand.SecondaryText");
        hiddenOverlay.Visibility = slide.Hidden ? Visibility.Visible : Visibility.Collapsed;
        frame.BorderBrush = Res(isCurrent ? "Brand.Live" : "Brand.Line");
        frame.BorderThickness = new Thickness(isCurrent ? 3 : 1);
        number.Text = pos?.ToString() ?? "–";
        number.FontWeight = isCurrent ? FontWeights.Bold : FontWeights.Normal;
        var glyphs = new List<string>();
        if (slide.ClickCount > 0) glyphs.Add("");   // Klick-Animationen
        if (slide.Videos.Count > 0) glyphs.Add("");  // Video
        if (slide.Notes.Length > 0) glyphs.Add("");  // Notizen
        icons.Text = string.Join(" ", glyphs);
        Opacity = isLive && state is { CanControl: false } && !isCurrent ? 0.5 : 1;
        ToolTip = slide.Hidden ? "Ausgeblendete Folie" : $"Folie {pos ?? slide.Index}";
    }
}
