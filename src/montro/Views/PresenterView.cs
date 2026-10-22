using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Montro.Core;

namespace Montro.Views;

/// <summary>
/// Inhalt der Referent*innenansicht (Mac: PresenterOutputView + PresenterView): nicht live die
/// Ruhe-Ansicht mit Uhr, live die Bausteine aus <see cref="PresenterLayout"/>, bei „Ende auf
/// Schwarz“ schwarz. Gezeichnet wird auf einer Fläche mit 1080 Einheiten Höhe (Mac: Schrift
/// skaliert mit Höhe/1080), deren Breite dem Seitenverhältnis des Platzes folgt.
/// </summary>
public sealed class PresenterOutputView : Border
{
    public const double ReferenceHeight = 1080;

    private readonly AppState state;
    private readonly Grid surface = new() { Height = ReferenceHeight, Width = ReferenceHeight * 16 / 9, ClipToBounds = true };
    private readonly Canvas canvas = new();
    private readonly Grid idle = new();
    private readonly TextBlock idleClock = new();
    private readonly Rectangle endBlack = new() { Fill = Brushes.Black };
    private readonly List<PresenterWidget> widgets = new();
    private bool subscribed;
    private readonly bool editing;

    /// <summary>Gezeigter Aufbau: im Baukasten der Entwurf, sonst die live geschaltete Ansicht.</summary>
    private PresenterLayout L => editing ? state.DraftLayout : state.Layout;

    public PresenterOutputView(AppState state, bool editing = false)
    {
        this.state = state;
        this.editing = editing;
        Background = Brushes.Black;
        ClipToBounds = true;
        surface.Children.Add(canvas);
        surface.Children.Add(idle);
        surface.Children.Add(endBlack);
        Child = new Viewbox { Stretch = Stretch.Fill, Child = surface };
        BuildIdle();
        BuildWidgets();
        SizeChanged += (_, _) => Relayout();
        Loaded += (_, _) => Subscribe(true);
        Unloaded += (_, _) => Subscribe(false);
        Refresh();
    }

    private void Subscribe(bool on)
    {
        if (on == subscribed) return;
        subscribed = on;
        if (on)
        {
            state.PropertyChanged += OnChanged;
            state.Timer.PropertyChanged += OnChanged;
            state.Message.PropertyChanged += OnChanged;
            Refresh();
        }
        else
        {
            state.PropertyChanged -= OnChanged;
            state.Timer.PropertyChanged -= OnChanged;
            state.Message.PropertyChanged -= OnChanged;
        }
    }

    /// <summary>Auf eigenem Bildschirm: nicht live schwarz statt Ruhe-Ansicht (Mac: Schwarzbild darüber).</summary>
    public bool BlackWhenIdle { get; init; }

    /// <summary>In der Karte: ohne Show die ausgewählte Präsentation zeigen (zum Gestalten), statt der Ruhe-Ansicht.</summary>
    public bool PreviewWhenIdle { get; init; }

    private bool refreshQueued;

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == (editing ? nameof(AppState.DraftLayout) : nameof(AppState.Layout))) BuildWidgets();
        // Viele Meldungen pro Takt (Timer, Uhr, Blinken) → einmal gesammelt neu zeichnen
        if (refreshQueued) return;
        refreshQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            refreshQueued = false;
            Refresh();
        }, System.Windows.Threading.DispatcherPriority.Render);
    }

    private Brush TextBrush => PresenterWidget.Hex(L.TextColor, Brushes.White);

    private void BuildIdle()
    {
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(new TextBlock
        {
            Text = "",   // Bildschirm (Mac: rectangle.on.rectangle.slash)
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 90,
            Opacity = 0.35,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Keine Präsentation live",
            FontSize = 44,
            Opacity = 0.6,
            Margin = new Thickness(0, 26, 0, 26),
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        idleClock.FontSize = 66;
        idleClock.FontWeight = FontWeights.Medium;
        idleClock.Opacity = 0.4;
        idleClock.HorizontalAlignment = HorizontalAlignment.Center;
        panel.Children.Add(idleClock);
        idle.Children.Add(panel);
    }

    private void BuildWidgets()
    {
        canvas.Children.Clear();
        widgets.Clear();
        foreach (var cfg in L.Widgets.Where(w => w.Enabled))
        {
            PresenterWidget w = cfg.Kind switch
            {
                WidgetKind.CurrentSlide => new CurrentSlideWidget(cfg),
                WidgetKind.NextSlide => new NextSlideWidget(cfg),
                WidgetKind.Notes => new NotesWidget(cfg),
                WidgetKind.SlideCounter => new SlideCounterWidget(cfg),
                WidgetKind.Timer => new TimerWidget(cfg),
                WidgetKind.Clock => new ClockWidget(cfg),
                WidgetKind.VideoTime => new VideoTimeWidget(cfg),
                _ => new MessageWidget(cfg),
            };
            w.Editing = editing;
            widgets.Add(w);
            canvas.Children.Add(w);
        }
        Relayout();
    }

    private void Relayout()
    {
        if (ActualWidth > 0 && ActualHeight > 0)
            surface.Width = Math.Round(ReferenceHeight * ActualWidth / ActualHeight);
        double w = surface.Width, h = surface.Height;
        foreach (var widget in widgets)
        {
            var f = widget.Config.Frame;
            Canvas.SetLeft(widget, f.X * w);
            Canvas.SetTop(widget, f.Y * h);
            widget.Width = Math.Max(f.W * w, 1);
            widget.Height = Math.Max(f.H * h, 1);
        }
    }

    /// <summary>Alle Anzeigen aus dem Zustand neu setzen (Takt alle 250 ms und bei Änderungen).</summary>
    public void Refresh()
    {
        bool live = editing || state.IsShowRunning || state.IsStarting || (PreviewWhenIdle && state.PresenterItem != null);
        var bg = PresenterWidget.Hex(L.Background, Brushes.Black);
        surface.Background = bg;
        System.Windows.Documents.TextElement.SetForeground(surface, TextBrush);
        canvas.Visibility = live ? Visibility.Visible : Visibility.Collapsed;
        idle.Visibility = live ? Visibility.Collapsed : Visibility.Visible;
        endBlack.Visibility = !editing && (state.IsEndBlack || (BlackWhenIdle && !live)) ? Visibility.Visible : Visibility.Collapsed;
        if (!live)
        {
            idleClock.Text = state.Clock.ToString("HH:mm:ss");
            return;
        }
        foreach (var w in widgets) w.Update(state);
    }
}

/// <summary>Gemeinsame Grundlage der Bausteine (Mac: WidgetView), Schriftgrößen in Einheiten der 1080er-Fläche.</summary>
public abstract class PresenterWidget : Grid
{
    private static readonly PathToImage Images = new();

    public WidgetConfig Config { get; }
    protected double Fs => Config.FontScale;

    /// <summary>Im Baukasten: Platzhalter statt leerer Bausteine, kein Live-Bild.</summary>
    public bool Editing { get; set; }

    protected PresenterWidget(WidgetConfig config)
    {
        Config = config;
        ClipToBounds = true;
    }

    /// <summary>Grauer Platzhalter im Baukasten (Mac: PlaceholderBox).</summary>
    protected Border Placeholder(string text) => new()
    {
        Background = new SolidColorBrush(Color.FromArgb(10, 255, 255, 255)),
        CornerRadius = new CornerRadius(8 * Fs),
        Child = new TextBlock
        {
            Text = text,
            FontSize = 20 * Fs,
            Opacity = 0.4,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8),
        },
    };

    public abstract void Update(AppState state);

    /// <summary>Überschrift (Mac: WidgetTitle) über dem Inhalt, wenn eingeschaltet.</summary>
    protected UIElement WithTitle(string title, UIElement content, double spacing = 6)
    {
        if (!Config.ShowTitle) return content;
        var dock = new DockPanel { LastChildFill = true };
        var t = new TextBlock
        {
            Text = title.ToUpper(CultureInfo.GetCultureInfo("de-DE")),
            FontSize = 17 * Fs,
            FontWeight = FontWeights.SemiBold,
            Opacity = 0.55,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 0, spacing * Fs),
        };
        DockPanel.SetDock(t, Dock.Top);
        dock.Children.Add(t);
        dock.Children.Add(content);
        return dock;
    }

    protected static ImageSource? Image(string? path) =>
        Images.Convert(path, typeof(ImageSource), null, CultureInfo.InvariantCulture) as ImageSource;

    public static Brush Hex(string? hex, Brush fallback)
    {
        try { return hex == null ? fallback : new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        catch { return fallback; }
    }

    protected static readonly Brush Live = new SolidColorBrush(Color.FromRgb(0xF2, 0x46, 0x5A));
    protected static readonly Brush Warning = new SolidColorBrush(Color.FromRgb(0xFF, 0xB1, 0x3D));
    protected static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0xB0, 0x88, 0xFB));
}

/// <summary>Folienbild in 16:9, schwarz hinterlegt, dünner Rahmen (Mac: SlideFrame).</summary>
internal sealed class SlideFrame : Panel
{
    private readonly Image image = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock placeholder = new()
    {
        Foreground = new SolidColorBrush(Color.FromArgb(102, 255, 255, 255)),
        TextAlignment = TextAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(16),
    };
    private readonly Border badge;

    public SlideFrame(double fs)
    {
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        placeholder.FontSize = 22 * fs;
        badge = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xF2, 0x46, 0x5A)),
            CornerRadius = new CornerRadius(100),
            Padding = new Thickness(10 * fs, 4 * fs, 10 * fs, 4 * fs),
            Margin = new Thickness(10 * fs),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock { Text = "SCHWARZ", FontSize = 20 * fs, FontWeight = FontWeights.Heavy, Foreground = Brushes.White },
            Visibility = Visibility.Collapsed,
        };
        var inner = new Grid { Background = Brushes.Black };
        inner.Children.Add(placeholder);
        inner.Children.Add(image);
        inner.Children.Add(badge);
        frame = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromArgb(38, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Child = inner,
        };
        Children.Add(frame);
        live = new Display.LiveThumbnail(inner);
    }

    private readonly Border frame;
    private readonly Display.LiveThumbnail live;

    /// <summary>Show-Fenster als Live-Bild über dem Vorschaubild (Zero = nur Vorschaubild).</summary>
    public void SetLive(IntPtr showWindow) => live.Source = showWindow;

    public void Set(ImageSource? source, string placeholderText, bool black = false)
    {
        image.Source = source;
        image.Visibility = source != null ? Visibility.Visible : Visibility.Collapsed;
        placeholder.Text = source == null ? placeholderText : "";
        badge.Visibility = black ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Größtes 16:9-Rechteck im Platz, zentriert (wie aspectRatio(16/9, .fit)).</summary>
    private static Rect Fit(Size space)
    {
        double w = double.IsInfinity(space.Width) ? 1600 : space.Width;
        double h = double.IsInfinity(space.Height) ? w * 9 / 16 : space.Height;
        double fw = Math.Min(w, h * 16 / 9), fh = fw * 9 / 16;
        return new Rect((w - fw) / 2, (h - fh) / 2, Math.Max(fw, 0), Math.Max(fh, 0));
    }

    protected override Size MeasureOverride(Size available)
    {
        var r = Fit(available);
        frame.Measure(r.Size);
        return new Size(double.IsInfinity(available.Width) ? r.Width : available.Width,
            double.IsInfinity(available.Height) ? r.Height : available.Height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        frame.Arrange(Fit(final));
        return final;
    }
}

internal sealed class CurrentSlideWidget : PresenterWidget
{
    private readonly SlideFrame frame;

    public CurrentSlideWidget(WidgetConfig c) : base(c)
    {
        frame = new SlideFrame(Fs);
        Children.Add(WithTitle("Aktuelle Folie", frame));
    }

    public override void Update(AppState s)
    {
        frame.Set(s.PresenterSlideSource, "Keine Folie live", s.IsBlackout || s.IsEndBlack);
        frame.SetLive(Editing ? IntPtr.Zero : s.LiveSource);
    }
}

internal sealed class NextSlideWidget : PresenterWidget
{
    private readonly SlideFrame frame;

    public NextSlideWidget(WidgetConfig c) : base(c)
    {
        frame = new SlideFrame(Fs);
        Children.Add(WithTitle("Nächste Folie", frame));
    }

    public override void Update(AppState s)
    {
        bool atEnd = s.PresenterAtEnd;
        frame.Set(atEnd ? null : Image(s.PresenterNextImage), atEnd ? "Ende der Präsentation" : "–");
    }
}

internal sealed class NotesWidget : PresenterWidget
{
    private readonly TextBlock text = new() { TextWrapping = TextWrapping.Wrap };

    public NotesWidget(WidgetConfig c) : base(c)
    {
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = text,
            Focusable = false,
        };
        Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(13, 255, 255, 255)),
            CornerRadius = new CornerRadius(8 * Fs),
            Padding = new Thickness(10 * Fs),
            Child = WithTitle("Notizen", scroll),
        });
    }

    public override void Update(AppState s)
    {
        string notes = s.PresenterNotes;
        double size = 30 * Fs;
        if (text.Text != (notes.Length == 0 ? "Keine Notizen" : notes)) text.Text = notes.Length == 0 ? "Keine Notizen" : notes;
        text.Opacity = notes.Length == 0 ? 0.35 : 1;
        text.FontSize = size;
        text.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        text.LineHeight = size * 1.2 + 6 * Fs;
    }
}

internal sealed class SlideCounterWidget : PresenterWidget
{
    private readonly TextBlock position = new() { FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel dots = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock locked = new() { FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };

    public SlideCounterWidget(WidgetConfig c) : base(c)
    {
        position.FontSize = 30 * Fs;
        locked.FontSize = 20 * Fs;
        locked.Foreground = Warning;
        locked.Inlines.Add(new System.Windows.Documents.Run(" ") { FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets") });
        locked.Inlines.Add(new System.Windows.Documents.Run("gesperrt"));
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(position);
        row.Children.Add(dots);
        row.Children.Add(locked);
        // zu lang für den Platz → verkleinern (Mac: minimumScaleFactor)
        Children.Add(WithTitle("Foliennummer / Folienanzahl", new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Child = row }));
    }

    public override void Update(AppState s)
    {
        position.Text = s.PresenterPositionText;
        int clicks = s.PresenterSlideMeta?.ClickCount ?? 0;
        if (dots.Children.Count != clicks)
        {
            dots.Children.Clear();
            for (int i = 0; i < clicks; i++)
                dots.Children.Add(new Ellipse { Width = 10 * Fs, Height = 10 * Fs, Margin = new Thickness(i == 0 ? 18 * Fs : 5 * Fs, 0, 0, 0) });
        }
        for (int i = 0; i < dots.Children.Count; i++)
            ((Ellipse)dots.Children[i]).Fill = new SolidColorBrush(Color.FromArgb((byte)(i < s.ClickInSlide ? 255 : 64), 255, 255, 255));
        locked.Visibility = s.IsLocked ? Visibility.Visible : Visibility.Collapsed;
        locked.Margin = new Thickness(18 * Fs, 0, 0, 0);
    }
}

internal sealed class TimerWidget : PresenterWidget
{
    private readonly TextBlock text = new() { FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly ProgressLine progress;
    private readonly TextBlock title = new() { FontWeight = FontWeights.SemiBold, Opacity = 0.55, HorizontalAlignment = HorizontalAlignment.Center };

    public TimerWidget(WidgetConfig c) : base(c)
    {
        text.FontSize = 110 * Fs;
        title.FontSize = 17 * Fs;
        progress = new ProgressLine(Math.Max(3, 6 * Fs)) { Margin = new Thickness(0, 4 * Fs, 0, 0) };
        var dock = new DockPanel { Margin = new Thickness(4 * Fs) };
        DockPanel.SetDock(title, Dock.Top);
        DockPanel.SetDock(progress, Dock.Bottom);
        if (Config.ShowTitle) dock.Children.Add(title);
        dock.Children.Add(progress);
        dock.Children.Add(new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Child = text });
        Children.Add(dock);
    }

    public override void Update(AppState s)
    {
        var t = s.Timer;
        var color = Hex(t.ColorHex, Brushes.White);
        title.Text = t.Settings.CountUp ? "LAUFZEIT" : "RESTZEIT";
        text.Text = t.DisplayText;
        text.Foreground = color;
        text.Opacity = t.ShouldBlink && !s.BlinkPhase ? 0.1 : 1;
        progress.Visibility = t.Settings.ShowProgress ? Visibility.Visible : Visibility.Collapsed;
        progress.Set(t.Progress, color);
    }
}

internal sealed class ClockWidget : PresenterWidget
{
    private readonly TextBlock text = new() { FontWeight = FontWeights.Medium, Opacity = 0.8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

    public ClockWidget(WidgetConfig c) : base(c)
    {
        text.FontSize = 60 * Fs;
        var box = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Child = text };
        if (Config.ShowTitle)
        {
            var dock = new DockPanel();
            var title = new TextBlock { Text = "UHRZEIT", FontSize = 17 * Fs, FontWeight = FontWeights.SemiBold, Opacity = 0.55, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 2 * Fs) };
            DockPanel.SetDock(title, Dock.Top);
            dock.Children.Add(title);
            dock.Children.Add(box);
            Children.Add(dock);
        }
        else Children.Add(box);
    }

    public override void Update(AppState s) => text.Text = s.Clock.ToString("HH:mm:ss");
}

/// <summary>Video-Laufzeit: erscheint, sobald ein Video läuft (Video-Erkennung folgt mit der Video-Karte).</summary>
internal sealed class VideoTimeWidget : PresenterWidget
{
    private Border? placeholder;
    private readonly Border box;
    private readonly TextBlock glyph = new() { FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock name = new() { Opacity = 0.7, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock total = new() { Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock rest = new() { FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
    private readonly ProgressLine progress;

    /// <summary>Name, „Gesamt“, „Rest“ (rot ab 10 s) und Fortschritt (Mac: VideoTimeWidget).</summary>
    public VideoTimeWidget(WidgetConfig c) : base(c)
    {
        foreach (var t in new[] { glyph, name, total, rest }) t.FontSize = 30 * Fs;
        glyph.FontSize = 24 * Fs;
        var row = new DockPanel { LastChildFill = true };
        glyph.Margin = new Thickness(0, 0, 14 * Fs, 0);
        total.Margin = new Thickness(8 * Fs, 0, 14 * Fs, 0);
        DockPanel.SetDock(glyph, Dock.Left);
        DockPanel.SetDock(rest, Dock.Right);
        DockPanel.SetDock(total, Dock.Right);
        row.Children.Add(glyph);
        row.Children.Add(rest);
        row.Children.Add(total);
        row.Children.Add(name);
        progress = new ProgressLine(Math.Max(3, 6 * Fs)) { Margin = new Thickness(0, 4 * Fs, 0, 0) };
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Child = row, HorizontalAlignment = HorizontalAlignment.Stretch });
        stack.Children.Add(progress);
        box = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)),
            CornerRadius = new CornerRadius(8 * Fs),
            Padding = new Thickness(12 * Fs, 0, 12 * Fs, 0),
            Child = stack,
            Visibility = Visibility.Collapsed,
        };
        Children.Add(box);
        // Zeile so breit wie der Baustein, damit „Rest“ rechts steht
        SizeChanged += (_, _) => row.Width = Math.Max(ActualWidth - 24 * Fs, 10);
    }

    public override void Update(AppState s)
    {
        if (s.Video is not { } v)
        {
            box.Visibility = Visibility.Collapsed;
            if (Editing && placeholder == null)
            {
                placeholder = Placeholder("Video-Laufzeit (sichtbar, sobald ein Video läuft)");
                Children.Add(placeholder);
            }
            if (placeholder != null) placeholder.Visibility = Visibility.Visible;
            return;
        }
        if (placeholder != null) placeholder.Visibility = Visibility.Collapsed;
        box.Visibility = Visibility.Visible;
        glyph.Text = v.Playing ? "" : "";   // Play / Pause
        name.Text = s.IsLooping(v.Name) ? $"{v.Name}  ↻" : v.Name;   // ↻ = Schleife
        total.Text = v.Duration > 0 ? $"Gesamt {TimeFormat.Clock(v.Duration)}" : "";
        rest.Text = v.Duration > 0 ? $"Rest {TimeFormat.Clock(v.Remaining)}" : TimeFormat.Clock(v.Position, roundUp: false);
        rest.Foreground = v.Duration > 0 && v.Remaining <= 10 ? Live : null;
        if (rest.Foreground == null) rest.ClearValue(TextBlock.ForegroundProperty);
        progress.Set(v.Progress, Accent);
    }
}

internal sealed class MessageWidget : PresenterWidget
{
    private readonly TextBlock text = new() { FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly Border box;
    private readonly Viewbox fit;

    public MessageWidget(WidgetConfig c) : base(c)
    {
        text.FontSize = 44 * Fs;
        fit = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Child = text, VerticalAlignment = VerticalAlignment.Center };
        box = new Border { CornerRadius = new CornerRadius(10 * Fs), Padding = new Thickness(14 * Fs), Child = fit };
        Children.Add(box);
        SizeChanged += (_, _) => text.MaxWidth = Math.Max(ActualWidth - 28 * Fs, 10);
    }

    private Border? placeholder;

    private Border AddPlaceholder()
    {
        var p = Placeholder("Regie-Nachricht (sichtbar, sobald gesendet)");
        Children.Add(p);
        return p;
    }

    public override void Update(AppState s)
    {
        var m = s.Message;
        box.Visibility = m.IsVisible ? Visibility.Visible : Visibility.Collapsed;
        if (Editing)
        {
            placeholder ??= AddPlaceholder();
            placeholder.Visibility = m.IsVisible ? Visibility.Collapsed : Visibility.Visible;
        }
        if (!m.IsVisible) return;
        text.Text = m.Text;
        text.Foreground = m.ForegroundBrush;
        box.Background = m.BackgroundBrush;
        box.Opacity = (m.Blink || m.IsFlashing) && !s.BlinkPhase ? 0.25 : 1;
    }
}

/// <summary>Dünner Fortschrittsbalken mit runden Enden (Mac: ProgressLine).</summary>
internal sealed class ProgressLine : Grid
{
    private readonly Border fill;

    public ProgressLine(double height)
    {
        Height = height;
        Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(38, 255, 255, 255)), CornerRadius = new CornerRadius(height / 2) });
        fill = new Border { CornerRadius = new CornerRadius(height / 2), HorizontalAlignment = HorizontalAlignment.Left };
        Children.Add(fill);
        SizeChanged += (_, _) => fill.Width = ActualWidth * value;
    }

    private double value;

    public void Set(double v, Brush color)
    {
        value = Math.Clamp(v, 0, 1);
        fill.Background = color;
        fill.Width = ActualWidth * value;
    }
}
