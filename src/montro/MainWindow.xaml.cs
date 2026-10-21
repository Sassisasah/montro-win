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
        SystemEvents.DisplaySettingsChanged += OnDisplaysChanged;

        (state.EndBehavior == EndBehavior.HoldLastSlide ? EndHold : EndBlack).IsChecked = true;
        UpdateEnd();
        UpdateEmpty();
        UpdateNavigatorHint();
        UpdateBlackoutButton();
        UpdateNotesFont();

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
                break;
            case nameof(AppState.CanControl):
            case nameof(AppState.NavigatorItem):
            case nameof(AppState.IsLocked):
                UpdateNavigatorHint();
                break;
            case nameof(AppState.CurrentSlide):
                ScrollNavigatorToCurrent();
                break;
            case nameof(AppState.NotesFontScale):
                UpdateNotesFont();
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

    private void UpdateNotesFont() => PresenterNotes.FontSize = 34 * state.NotesFontScale;

    private void UpdateBlink() =>
        TimerText.Opacity = state.Timer.ShouldBlink && !state.BlinkPhase ? 0.2 : 1;

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
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender };
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
                Header = $"{m.FriendlyName} ({m.Number}) – {m.Bounds.Width}×{m.Bounds.Height}, {m.Scale:P0}",
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
