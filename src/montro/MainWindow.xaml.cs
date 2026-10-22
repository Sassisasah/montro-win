using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Montro.Core;
using Montro.Views;

namespace Montro;

/// <summary>Hauptfenster wie am Mac (MainView): Playlist links, Inspector rechts, Statusleiste unten.</summary>
public partial class MainWindow : Window
{
    private readonly AppState state = new();
    private PowerPointTestWindow? testWindow;
    private bool shutdownDone;
    private Point dragStart;
    private readonly Display.LiveThumbnail liveThumb;
    private PresentationItem? dragItem;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = state;
        try { EmptyIcon.Source = BitmapFrame.Create(new Uri("pack://application:,,,/Resources/montro.ico")); } catch { }

        state.PropertyChanged += OnStateChanged;
        state.Items.CollectionChanged += (_, _) => UpdateEmpty();
        state.Timer.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TalkTimer.ShouldBlink)) UpdateBlink();
        };
        state.ErrorRaised += msg => MessageBox.Show(this, msg, "Hinweis", MessageBoxButton.OK, MessageBoxImage.Information);
        state.FocusRequested += () =>
        {
            Activate();
            Focus();
        };
        SystemEvents.DisplaySettingsChanged += OnDisplaysChanged;

        (state.EndBehavior == EndBehavior.HoldLastSlide ? EndHold : EndBlack).IsChecked = true;
        LoadTransition();
        LoadTimerSettings();
        DurTotal.Changed += v => { state.Timer.Settings.Duration = Math.Max(v, 1); TimerSettingsChanged(); };
        DurWarn1.Changed += v => { state.Timer.Settings.Warning1 = v; TimerSettingsChanged(); };
        DurWarn2.Changed += v => { state.Timer.Settings.Warning2 = v; TimerSettingsChanged(); };
        UpdateEnd();
        UpdateEmpty();
        UpdateNavigatorHint();
        UpdateBlackoutButton();
        PresenterPreviewHost.Child = new PresenterOutputView(state);
        liveThumb = new Display.LiveThumbnail(LivePreview);

        PreviewKeyDown += OnKey;
        DragEnter += OnDragOver;
        DragOver += OnDragOver;
        DragLeave += (_, _) => DropFrame.Visibility = Visibility.Collapsed;
        Drop += OnDrop;
        Playlist.PreviewMouseLeftButtonDown += (_, e) =>
        {
            dragStart = e.GetPosition(null);
            dragItem = ItemAt(e.OriginalSource);
        };
        Playlist.PreviewMouseMove += OnPlaylistDragMove;
        DebugLog.Write($"Hauptfenster: {state.Items.Count} Einträge in der Playlist, {state.ExternalScreens.Count} externe(r) Bildschirm(e)");
    }

    /// <summary>Dateien in die Playlist (Kommandozeile, „Öffnen mit“).</summary>
    public void AddFiles(IEnumerable<string> paths)
    {
        var known = state.Items.Select(i => i.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        state.Add(paths.Select(Path.GetFullPath).Where(p => !known.Contains(p)));
    }

    // MARK: - Zustand → Oberfläche (was sich nicht direkt binden lässt)

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppState.IsBlackout):
            case nameof(AppState.IsShowRunning):
                UpdateBlackoutButton();
                UpdateNavigatorHint();
                UpdateVideoCard();
                break;
            case nameof(AppState.CanControl):
            case nameof(AppState.NavigatorItem):
            case nameof(AppState.IsLocked):
                UpdateNavigatorHint();
                break;
            case nameof(AppState.CurrentSlide):
                ScrollNavigatorToCurrent();
                break;
            case nameof(AppState.Video):
            case nameof(AppState.CurrentSlideMeta):
                UpdateVideoCard();
                break;
            case nameof(AppState.LiveSource):
                liveThumb.Source = state.LiveSource;
                break;
            case nameof(AppState.BlinkPhase):
                UpdateBlink();
                break;
        }
    }

    private void UpdateEmpty()
    {
        bool empty = state.Items.Count == 0;
        EmptyHint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        Playlist.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateBlackoutButton() =>
        BlackoutButton.Background = (Brush)FindResource(state.IsBlackout ? "Brand.Live" : "Brand.Line");

    private void UpdateNavigatorHint()
    {
        bool isLive = state.NavigatorItem != null && state.NavigatorItem == state.LiveItem && state.IsShowRunning;
        NavigatorHint.Text = isLive ? (state.IsLocked ? "gesperrt" : "Klick = springen") : "Doppelklick = ab hier live";
        NavigatorHint.Foreground = (Brush)FindResource(isLive && state.IsLocked ? "Brand.Warning" : "Brand.SecondaryText");
    }

    // MARK: - Video-Karte (Mac: VideoSection)

    private SlideMeta? videoCardSlide;
    private StackPanel? videoPlaying;
    private TextBlock? videoName, videoTimes, videoPauseGlyph;
    private ProgressBar? videoProgress;

    /// <summary>
    /// Laufendes Video mit Pause/Stopp und Fortschritt, darunter die Videos der Folie mit Start
    /// (automatisch / Klick n / Trigger) und „Abspielen“. Anders als am Mac meldet PowerPoint unter
    /// Windows die Position – daher kein Video-Timer zum Starten, sondern echtes Abspielen.
    /// </summary>
    private void UpdateVideoCard()
    {
        var slide = state.IsShowRunning ? state.CurrentSlideMeta : null;
        var videos = slide?.Videos ?? [];
        VideoCard.Visibility = state.Video != null || videos.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (VideoCard.Visibility != Visibility.Visible) return;
        if (videoPlaying == null || !ReferenceEquals(slide, videoCardSlide))
        {
            videoCardSlide = slide;
            VideoCardBody.Children.Clear();
            videoPlaying = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            var row = new DockPanel();
            var stop = new Button { Style = (Style)FindResource("Brand.Compact"), ToolTip = "Video stoppen", Margin = new Thickness(6, 0, 0, 0), Content = Glyph("") };
            stop.Click += (_, _) => state.StopVideo();
            videoPauseGlyph = Glyph("");
            var pause = new Button { Style = (Style)FindResource("Brand.Compact"), ToolTip = "Pause / Weiter", Content = videoPauseGlyph };
            pause.Click += (_, _) => state.ToggleVideoPause();
            DockPanel.SetDock(stop, Dock.Right);
            DockPanel.SetDock(pause, Dock.Right);
            row.Children.Add(stop);
            row.Children.Add(pause);
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            videoName = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
            videoTimes = new TextBlock { Style = (Style)FindResource("Brand.Caption") };
            texts.Children.Add(videoName);
            texts.Children.Add(videoTimes);
            row.Children.Add(texts);
            videoPlaying.Children.Add(row);
            videoProgress = new ProgressBar { Height = 4, Maximum = 1, Margin = new Thickness(0, 6, 0, 0), Foreground = (Brush)FindResource("Brand.Accent"), Background = (Brush)FindResource("Brand.Line"), BorderThickness = new Thickness(0) };
            videoPlaying.Children.Add(videoProgress);
            VideoCardBody.Children.Add(videoPlaying);
            foreach (var v in videos)
            {
                var line = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
                var play = new Button { Style = (Style)FindResource("Brand.Small"), Content = "Abspielen", Margin = new Thickness(8, 0, 0, 0) };
                string name = v.Name;
                play.Click += (_, _) => state.PlayVideo(name);
                DockPanel.SetDock(play, Dock.Right);
                line.Children.Add(play);
                var info = new TextBlock { Text = StartText(v), Style = (Style)FindResource("Brand.Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
                DockPanel.SetDock(info, Dock.Right);
                line.Children.Add(info);
                var icon = Glyph("");
                icon.Foreground = (Brush)FindResource("Brand.SecondaryText");
                icon.Margin = new Thickness(0, 0, 6, 0);
                DockPanel.SetDock(icon, Dock.Left);
                line.Children.Add(icon);
                line.Children.Add(new TextBlock { Text = v.Name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
                VideoCardBody.Children.Add(line);
            }
        }
        if (state.Video is { } p)
        {
            videoPlaying.Visibility = Visibility.Visible;
            videoName!.Text = p.Name;
            videoTimes!.Text = $"Gesamt {TimeFormat.Clock(p.Duration)} · Rest {TimeFormat.Clock(p.Remaining)}";
            videoPauseGlyph!.Text = p.Playing ? "" : "";
            videoProgress!.Value = p.Progress;
        }
        else videoPlaying.Visibility = Visibility.Collapsed;
    }

    private TextBlock Glyph(string glyph) => new() { Text = glyph, Style = (Style)FindResource("Brand.Icon"), FontSize = 11 };

    private static string StartText(VideoMeta v)
    {
        string length = v.Duration > 0 ? TimeFormat.Clock(v.Duration) : "Länge ?";
        return v.StartClick switch
        {
            null => $"{length} · Trigger",
            0 => $"{length} · automatisch",
            int c => $"{length} · Klick {c}",
        };
    }

    private void UpdateBlink() =>
        TimerText.Opacity = state.Timer.ShouldBlink && !state.BlinkPhase ? 0.2 : 1;

    // MARK: - Timer-Einstellungen (Mac: DisclosureGroup „Einstellungen“)

    private void LoadTimerSettings()
    {
        var s = state.Timer.Settings;
        DurTotal.Value = s.Duration;
        DurWarn1.Value = s.Warning1;
        DurWarn2.Value = s.Warning2;
        (s.CountUp ? ModeUp : ModeDown).IsChecked = true;
        OptOvertime.IsChecked = s.AllowOvertime;
        OptBlinkOver.IsChecked = s.BlinkInOvertime;
        OptBlinkWarn2.IsChecked = s.BlinkInWarning2;
        OptProgress.IsChecked = s.ShowProgress;
        OptAutoStart.IsChecked = s.AutoStartWithPresentation;
        ColNormal.Background = Swatch(s.NormalColor);
        ColWarn1.Background = Swatch(s.Warning1Color);
        ColWarn2.Background = Swatch(s.Warning2Color);
        ColOver.Background = Swatch(s.OvertimeColor);
        TimerProgress.Visibility = s.ShowProgress ? Visibility.Visible : Visibility.Collapsed;
    }

    private static Brush Swatch(string hex)
    {
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        catch { return Brushes.Gray; }
    }

    private void TimerSettingsChanged()
    {
        state.Timer.Refresh();
        state.QueueSave();
        LoadTimerSettings();
    }

    private void OnTimerSetting(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        var s = state.Timer.Settings;
        s.CountUp = ModeUp.IsChecked == true;
        s.AllowOvertime = OptOvertime.IsChecked == true;
        s.BlinkInOvertime = OptBlinkOver.IsChecked == true;
        s.BlinkInWarning2 = OptBlinkWarn2.IsChecked == true;
        s.ShowProgress = OptProgress.IsChecked == true;
        s.AutoStartWithPresentation = OptAutoStart.IsChecked == true;
        TimerSettingsChanged();
    }

    /// <summary>Farben wie die Mac-Voreinstellungen plus movidal-CD (Mac: ColorPicker).</summary>
    private static void PickColor(UIElement target, string current, Action<string> apply) =>
        ColorMenu.Show(target, current, apply);

    private void OnTimerColor(object sender, RoutedEventArgs e)
    {
        var s = state.Timer.Settings;
        switch ((sender as FrameworkElement)?.Tag as string)
        {
            case "normal": PickColor((UIElement)sender, s.NormalColor, h => { s.NormalColor = h; TimerSettingsChanged(); }); break;
            case "w1": PickColor((UIElement)sender, s.Warning1Color, h => { s.Warning1Color = h; TimerSettingsChanged(); }); break;
            case "w2": PickColor((UIElement)sender, s.Warning2Color, h => { s.Warning2Color = h; TimerSettingsChanged(); }); break;
            case "over": PickColor((UIElement)sender, s.OvertimeColor, h => { s.OvertimeColor = h; TimerSettingsChanged(); }); break;
        }
    }

    // MARK: - Regie-Nachricht

    private void OnMessageSend(object sender, RoutedEventArgs e) => state.Message.Send();
    private void OnMessageClear(object sender, RoutedEventArgs e) => state.Message.Clear();

    private void OnMessageKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            state.Message.Send();
            e.Handled = true;
        }
    }

    private void OnMessagePreset(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string t }) state.Message.Send(t);
    }

    private void OnMessagePresetRemove(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string t }) state.Message.Presets.Remove(t);
    }

    private void OnMessagePresetAdd(object sender, RoutedEventArgs e) => AddPreset();

    private void OnNewPresetKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        AddPreset();
        e.Handled = true;
    }

    private void AddPreset()
    {
        string t = NewPresetBox.Text.Trim();
        if (t.Length == 0 || state.Message.Presets.Contains(t)) return;
        state.Message.Presets.Add(t);
        NewPresetBox.Text = "";
    }

    private void OnMessageColor(object sender, RoutedEventArgs e)
    {
        var m = state.Message;
        if ((sender as FrameworkElement)?.Tag as string == "bg")
            PickColor((UIElement)sender, m.Background, h => m.Background = h);
        else
            PickColor((UIElement)sender, m.Foreground, h => m.Foreground = h);
    }

    // MARK: - Übergang

    private void LoadTransition() =>
        (state.Transition switch { ShowTransition.Cut => TrCut, ShowTransition.Crossfade => TrFade, _ => TrBlack }).IsChecked = true;

    private void OnTransition(object sender, RoutedEventArgs e)
    {
        state.Transition = TrCut.IsChecked == true ? ShowTransition.Cut
            : TrFade.IsChecked == true ? ShowTransition.Crossfade : ShowTransition.Black;
    }

    private void UpdateEnd()
    {
        EndDetail.Text = state.EndBehavior == EndBehavior.HoldLastSlide
            ? "Die letzte Folie bleibt stehen. Weitere Befehle (Clicker, Tastatur) werden ignoriert, bis entsperrt wird."
            : "Alle zugewiesenen Ausgaben inklusive Referent*innenansicht gehen auf Schwarz und die Steuerung wird gesperrt.";
    }

    private void ScrollNavigatorToCurrent()
    {
        if (state.LiveItem == null || state.NavigatorItem != state.LiveItem) return;
        var slide = state.LiveItem.Slide(state.CurrentSlide);
        if (slide != null) Navigator.ScrollIntoView(slide);
    }

    private void OnDisplaysChanged(object? sender, EventArgs e) =>
        Dispatcher.InvokeAsync(() =>
        {
            state.RefreshScreens();
            DebugLog.Write($"Bildschirme geändert: {state.ExternalScreens.Count} extern");
        });

    // MARK: - Befehle

    private void OnGoLive(object sender, RoutedEventArgs e) => state.GoLiveSelected();
    private async void OnStop(object sender, RoutedEventArgs e) => await state.Stop();
    private void OnNext(object sender, RoutedEventArgs e) => state.Next();
    private void OnPrevious(object sender, RoutedEventArgs e) => state.Previous();
    private void OnBlackout(object sender, RoutedEventArgs e) => state.ToggleBlackout();
    private void OnUnlock(object sender, RoutedEventArgs e) => state.Unlock();

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "PowerPoint-Dateien hinzufügen",
            Filter = "PowerPoint-Präsentationen (*.pptx;*.pptm;*.ppsx;*.ppsm)|*.pptx;*.pptm;*.ppsx;*.ppsm",
            Multiselect = true,
        };
        if (dlg.ShowDialog(this) == true) state.Add(dlg.FileNames);
    }

    // MARK: - Playlist

    private static PresentationItem? ItemAt(object source)
    {
        var d = source as DependencyObject;
        while (d != null && d is not ListBoxItem) d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        return (d as ListBoxItem)?.DataContext as PresentationItem;
    }

    private void OnPlaylistDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemAt(e.OriginalSource) is { } item) _ = state.GoLive(item);
    }

    private void OnPlaylistKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && state.Selected is { } item && item != state.LiveItem)
        {
            state.Remove(item);
            e.Handled = true;
        }
    }

    private void OnPlaylistMenu(object sender, ContextMenuEventArgs e)
    {
        var item = ItemAt(e.OriginalSource) ?? state.Selected;
        var menu = Playlist.ContextMenu!;
        menu.Items.Clear();
        if (item == null)
        {
            e.Handled = true;
            return;
        }
        state.Selected = item;
        MenuItem Entry(string text, Action action, bool enabled = true)
        {
            var mi = new MenuItem { Header = text, IsEnabled = enabled };
            mi.Click += (_, _) => action();
            return mi;
        }
        menu.Items.Add(Entry("Live schalten", () => _ = state.GoLive(item), !state.IsStarting));
        menu.Items.Add(Entry("Im Explorer zeigen", () =>
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{item.Path}\"") { UseShellExecute = true })));
        menu.Items.Add(Entry("Neu analysieren & Vorschaubilder erzeugen", () => state.Regenerate(item), item != state.LiveItem));
        menu.Items.Add(new Separator());
        menu.Items.Add(Entry("Aus Playlist entfernen", () => state.Remove(item), item != state.LiveItem));
    }

    /// <summary>Reihenfolge per Ziehen ändern (Mac: onMove).</summary>
    private void OnPlaylistDragMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || dragItem == null) return;
        var diff = e.GetPosition(null) - dragStart;
        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var item = dragItem;
        dragItem = null;
        DragDrop.DoDragDrop(Playlist, new DataObject(typeof(PresentationItem), item), DragDropEffects.Move);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        bool files = e.Data.GetDataPresent(DataFormats.FileDrop);
        bool move = e.Data.GetDataPresent(typeof(PresentationItem));
        e.Effects = files ? DragDropEffects.Copy : move ? DragDropEffects.Move : DragDropEffects.None;
        DropFrame.Visibility = files ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        DropFrame.Visibility = Visibility.Collapsed;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            state.Add(paths);
        }
        else if (e.Data.GetData(typeof(PresentationItem)) is PresentationItem moved)
        {
            var target = ItemAt(e.OriginalSource);
            int from = state.Items.IndexOf(moved);
            int to = target != null ? state.Items.IndexOf(target) : state.Items.Count - 1;
            state.Move(from, to);
            state.Selected = moved;
        }
        e.Handled = true;
    }

    // MARK: - Folienübersicht

    private static SlideMeta? TileAt(object source)
    {
        var d = source as DependencyObject;
        while (d != null && d is not SlideTile) d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        return (d as SlideTile)?.DataContext as SlideMeta;
    }

    private void OnTileClick(object sender, MouseButtonEventArgs e)
    {
        if (TileAt(e.OriginalSource) is not { } slide) return;
        bool isLive = state.NavigatorItem == state.LiveItem && state.IsShowRunning;
        if (isLive) state.Jump(slide.Index);
    }

    private void OnTileDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (TileAt(e.OriginalSource) is not { } slide || state.NavigatorItem is not { } item) return;
        bool isLive = item == state.LiveItem && state.IsShowRunning;
        if (!isLive) _ = state.GoLive(item, slide.Index);
    }

    private void OnGoto(object sender, RoutedEventArgs e) => ShowGoto();

    /// <summary>„Gehe zu Folie …“ – Nummer wie in „Folie x von n“ (Mac: GoToSlideSheet).</summary>
    private void ShowGoto()
    {
        if (!state.CanControl || state.LiveItem is not { } item) return;
        var box = new TextBox { Width = 100, Margin = new Thickness(0, 0, 8, 0) };
        var dlg = new Window
        {
            Title = "Gehe zu Folie",
            Owner = this,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Style = (Style)FindResource("Brand.Window"),
        };
        var ok = new Button { Content = "Springen", IsDefault = true, Style = (Style)FindResource("Brand.Compact") };
        var cancel = new Button { Content = "Abbrechen", IsCancel = true, Style = (Style)FindResource("Brand.Compact"), Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => { state.Jump(box.Text); dlg.Close(); };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        row.Children.Add(box);
        row.Children.Add(new TextBlock { Text = $"von {item.VisibleCount}", VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("Brand.SecondaryText") });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        var root = new StackPanel { Margin = new Thickness(20), Width = 260 };
        root.Children.Add(row);
        root.Children.Add(buttons);
        dlg.Content = root;
        dlg.Loaded += (_, _) => box.Focus();
        dlg.ShowDialog();
    }

    // MARK: - Ausgaben

    /// <summary>Bildschirmwahl wie am Mac (DisplayPickerButton): Popup-Fenster oder externer Bildschirm.</summary>
    private void OnAudiencePicker(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        var popup = new MenuItem { Header = "Popup-Fenster", IsCheckable = true, IsChecked = state.AudienceIsPopup };
        popup.Click += (_, _) => state.AudienceDevice = null;
        menu.Items.Add(popup);
        menu.Items.Add(new Separator());
        var screens = state.ExternalScreens;
        if (screens.Count == 0)
            menu.Items.Add(new MenuItem { Header = "Keine externen Bildschirme angeschlossen", IsEnabled = false });
        foreach (var m in screens)
        {
            var mi = new MenuItem
            {
                Header = $"{m.FriendlyName} ({m.Number}) – {m.Bounds.Width}×{m.Bounds.Height}, {m.Scale:P0}"
                         + (state.PresenterDevice == m.DeviceName ? " · Referent*innenansicht" : ""),
                IsCheckable = true,
                IsChecked = state.AudienceDevice == m.DeviceName,
            };
            string device = m.DeviceName;
            mi.Click += (_, _) => state.AudienceDevice = device;
            menu.Items.Add(mi);
        }
        menu.IsOpen = true;
    }

    // MARK: - Referent*innenansicht, Timer, Ende

    private LayoutBuilderWindow? builder;

    /// <summary>Baukasten der Referent*innenansicht öffnen (nur einmal).</summary>
    private void OnBuilder(object sender, RoutedEventArgs e)
    {
        if (builder is { IsLoaded: true })
        {
            builder.Activate();
            return;
        }
        builder = new LayoutBuilderWindow(state) { Owner = this };
        builder.Closed += (_, _) => builder = null;
        builder.Show();
    }

    /// <summary>Ausgabe der Referent*innenansicht wählen (Mac: Bildschirm-Menü in der Karte).</summary>
    private void OnPresenterPicker(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        var popup = new MenuItem { Header = "Popup-Fenster", IsCheckable = true, IsChecked = state.PresenterIsPopup };
        popup.Click += (_, _) => state.PresenterDevice = null;
        menu.Items.Add(popup);
        menu.Items.Add(new Separator());
        var screens = state.ExternalScreens;
        if (screens.Count == 0)
            menu.Items.Add(new MenuItem { Header = "Keine externen Bildschirme angeschlossen", IsEnabled = false });
        foreach (var m in screens)
        {
            bool isAudience = state.AudienceDevice == m.DeviceName;
            var mi = new MenuItem
            {
                Header = $"{m.FriendlyName} ({m.Number}) – {m.Bounds.Width}×{m.Bounds.Height}, {m.Scale:P0}" + (isAudience ? " · Publikum" : ""),
                IsCheckable = true,
                IsChecked = state.PresenterDevice == m.DeviceName && !state.PresenterIsPopup,
            };
            string device = m.DeviceName;
            mi.Click += (_, _) => state.PresenterDevice = device;
            menu.Items.Add(mi);
        }
        if (state.PresenterIsPopup)
        {
            menu.Items.Add(new Separator());
            var show = new MenuItem { Header = "Fenster zeigen" };
            show.Click += (_, _) => state.ShowPresenterWindow();
            menu.Items.Add(show);
        }
        menu.IsOpen = true;
    }

    private void OnNotesBigger(object sender, RoutedEventArgs e) => state.NotesFontScale += 0.1;
    private void OnNotesSmaller(object sender, RoutedEventArgs e) => state.NotesFontScale -= 0.1;

    private void OnTimerToggle(object sender, RoutedEventArgs e) => state.Timer.Toggle();
    private void OnTimerReset(object sender, RoutedEventArgs e) => state.Timer.Reset();

    private void OnTimerAdd(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string s } && double.TryParse(s, out var sec)) state.Timer.Add(sec);
    }

    private void OnTimerPreset(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string s } && double.TryParse(s, out var min))
        {
            state.Timer.SetDuration(min);
            state.QueueSave();
        }
    }

    private void OnEndBehavior(object sender, RoutedEventArgs e)
    {
        state.EndBehavior = EndBlack.IsChecked == true ? EndBehavior.BlackEverywhere : EndBehavior.HoldLastSlide;
        if (EndDetail != null) UpdateEnd();
    }

    // MARK: - Tastatur (Mac: → Bild↓ Leertaste Return N = weiter, ← Bild↑ P = zurück, B . = Blackscreen …)

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox) return;
        var mods = Keyboard.Modifiers;
        bool ctrl = mods.HasFlag(ModifierKeys.Control);
        bool shift = mods.HasFlag(ModifierKeys.Shift);
        switch (e.Key)
        {
            case Key.Enter when ctrl: state.GoLiveSelected(); break;
            case Key.OemPeriod when ctrl: _ = state.Stop(); break;
            case Key.T when ctrl: state.Timer.Toggle(); break;
            case Key.G when ctrl: ShowGoto(); break;
            case Key.OemPlus or Key.Add when ctrl: state.NotesFontScale += 0.1; break;
            case Key.OemMinus or Key.Subtract when ctrl: state.NotesFontScale -= 0.1; break;
            case Key.D0 or Key.NumPad0 when ctrl: state.NotesFontScale = 1; break;
            case Key.P when ctrl && shift: OpenTestWindow(); break;
            case Key.L when ctrl && shift: OpenLogFolder(); break;
            case Key.Right or Key.Next or Key.Space or Key.N when !ctrl && state.IsShowRunning: state.Next(); break;
            case Key.Enter when !ctrl && state.IsShowRunning: state.Next(); break;
            case Key.Left or Key.Prior or Key.P when !ctrl && state.IsShowRunning: state.Previous(); break;
            case Key.B or Key.OemPeriod when !ctrl && state.IsShowRunning: state.ToggleBlackout(); break;
            default: return;
        }
        e.Handled = true;
    }

    // MARK: - Werkzeuge

    /// <summary>Testfenster „PowerPoint testen“ (Strg+Umschalt+P), bis Meilenstein 4 fertig ist.</summary>
    private void OpenTestWindow()
    {
        if (testWindow != null)
        {
            testWindow.Activate();
            return;
        }
        testWindow = new PowerPointTestWindow { Owner = this };
        testWindow.Closed += (_, _) => testWindow = null;
        testWindow.Show();
    }

    private static void OpenLogFolder() =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.LocalDir}\"") { UseShellExecute = true });

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (shutdownDone)
        {
            base.OnClosing(e);
            return;
        }
        e.Cancel = true;
        IsEnabled = false;
        SystemEvents.DisplaySettingsChanged -= OnDisplaysChanged;
        testWindow?.Close();
        await state.ShutdownAsync();
        shutdownDone = true;
        Close();
    }
}
