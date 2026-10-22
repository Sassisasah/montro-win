using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Montro.Core;

namespace Montro.Views;

/// <summary>
/// Baukasten der Referent*innenansicht (Mac: LayoutBuilderView): links die Ansicht mit
/// verschiebbaren Bausteinen (Ecke unten rechts = Größe), rechts Preset, Widgets, Eigenschaften,
/// Darstellung. Ohne Präsentation wirken Änderungen sofort, während einer Präsentation erst per
/// „Jetzt übernehmen“ oder nach dem Ende.
/// </summary>
public sealed class LayoutBuilderWindow : Window
{
    private const double CanvasW = 1600, CanvasH = 900;

    private readonly AppState state;
    private readonly Canvas overlay = new() { Width = CanvasW, Height = CanvasH, Background = Brushes.Transparent };
    private readonly Dictionary<WidgetKind, EditableFrame> frames = new();
    private WidgetKind? selected;
    private bool snap = true;
    private bool syncing;

    // Bedienelemente, die bei Änderungen nachgezogen werden
    private readonly TextBlock hint = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Border draftBar = new();
    private readonly TextBlock draftTitle = new() { FontWeight = FontWeights.SemiBold };
    private readonly TextBlock draftDetail = new() { FontSize = 11 };
    private readonly TextBlock draftIcon = new();
    private readonly Button draftDiscard, draftAfter, draftNow;
    private readonly ComboBox targetBox = new() { MinWidth = 160 };
    private readonly StackPanel unsavedRow = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Button saveToTarget;
    private readonly Button renameButton, deleteButton;
    private readonly StackPanel assignRow = new() { Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBlock assignTitle = new() { FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly ComboBox assignBox = new() { MinWidth = 160 };
    private readonly Dictionary<WidgetKind, (CheckBox Box, TextBlock Chevron, Border Row)> widgetRows = new();
    private readonly Border propsCard;
    private readonly TextBlock propsTitle = new() { FontWeight = FontWeights.SemiBold, FontSize = 13, Margin = new Thickness(0, 0, 0, 10) };
    private readonly Slider fontSlider = new() { Minimum = 0.4, Maximum = 4, Width = 150, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock fontText = new() { Width = 44, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox titleBox = new() { Content = "Überschrift zeigen", Margin = new Thickness(0, 8, 0, 8) };
    private readonly Dictionary<string, TextBox> frameBoxes = new();
    private readonly Button bgButton = new() { Width = 34, Height = 20 };
    private readonly Button fgButton = new() { Width = 34, Height = 20 };

    public LayoutBuilderWindow(AppState state)
    {
        this.state = state;
        Title = "Referent*innenansicht konfigurieren";
        Style = (Style)FindResource("Brand.Window");
        Width = 1200;
        Height = 720;
        MinWidth = 980;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        draftDiscard = SmallButton("Verwerfen", () => state.DiscardDraft());
        draftAfter = SmallButton("Nach der Präsentation übernehmen", () => state.ApplyDraftAfterPresentation());
        draftNow = SmallButton("Jetzt übernehmen", () => state.ApplyDraftNow());
        draftNow.Style = (Style)FindResource("Brand.Prominent");
        saveToTarget = SmallButton("Speichern", () => state.SaveDraftToTarget());
        saveToTarget.Style = (Style)FindResource("Brand.Prominent");
        renameButton = SmallButton("Umbenennen …", () => SaveDialog(rename: state.BuilderTarget));
        deleteButton = SmallButton("Löschen", DeletePreset);

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 560 });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });
        root.Children.Add(BuildCanvasSide());
        var side = BuildSidebar();
        propsCard = (Border)((StackPanel)((ScrollViewer)side).Content).Children[2];
        Grid.SetColumn(side, 1);
        root.Children.Add(side);
        Content = root;

        state.PropertyChanged += OnState;
        state.LayoutPresets.CollectionChanged += (_, _) => Sync();
        Closed += (_, _) => state.PropertyChanged -= OnState;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control && state.HasUnsavedChanges)
            {
                state.SaveDraftToTarget();
                e.Handled = true;
            }
        };
        Sync();
    }

    private void OnState(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppState.DraftLayout) or nameof(AppState.HasDraftChanges) or nameof(AppState.HasUnsavedChanges)
            or nameof(AppState.BuilderTarget) or nameof(AppState.ApplyDraftAfterShow) or nameof(AppState.IsShowRunning)
            or nameof(AppState.IsStarting) or nameof(AppState.NavigatorItem) or nameof(AppState.Layout))
            Sync();
    }

    // MARK: - Linke Seite: Ansicht mit verschiebbaren Bausteinen

    private UIElement BuildCanvasSide()
    {
        var dock = new DockPanel { Margin = new Thickness(16) };
        var hintRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        hintRow.Children.Add(Glyph("", secondary: true));   // Hand / Ziehen
        hint.Foreground = Res("Brand.SecondaryText");
        hint.Margin = new Thickness(6, 0, 0, 0);
        hintRow.Children.Add(hint);
        DockPanel.SetDock(hintRow, Dock.Top);
        dock.Children.Add(hintRow);

        // Entwurfsleiste (Mac: DraftBar)
        var bar = new DockPanel();
        draftIcon.FontFamily = (FontFamily)FindResource("Brand.IconFont");
        draftIcon.FontSize = 16;
        draftIcon.VerticalAlignment = VerticalAlignment.Center;
        draftIcon.Margin = new Thickness(0, 0, 10, 0);
        DockPanel.SetDock(draftIcon, Dock.Left);
        bar.Children.Add(draftIcon);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        draftAfter.Margin = draftDiscard.Margin = new Thickness(0, 0, 6, 0);
        buttons.Children.Add(draftDiscard);
        buttons.Children.Add(draftAfter);
        buttons.Children.Add(draftNow);
        DockPanel.SetDock(buttons, Dock.Right);
        bar.Children.Add(buttons);
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(draftTitle);
        draftDetail.Foreground = Res("Brand.SecondaryText");
        texts.Children.Add(draftDetail);
        bar.Children.Add(texts);
        draftBar.Child = bar;
        draftBar.Padding = new Thickness(10);
        draftBar.CornerRadius = new CornerRadius(8);
        draftBar.Margin = new Thickness(0, 0, 0, 8);
        draftBar.Background = new SolidColorBrush(Color.FromArgb(31, 0xFF, 0xB1, 0x3D));
        draftBar.BorderBrush = new SolidColorBrush(Color.FromArgb(102, 0xFF, 0xB1, 0x3D));
        draftBar.BorderThickness = new Thickness(1);
        DockPanel.SetDock(draftBar, Dock.Top);
        dock.Children.Add(draftBar);

        // Ansicht 16:9 mit Bausteinen, darüber die bearbeitbaren Rahmen
        var stage = new Grid { Width = CanvasW, Height = CanvasH };
        stage.Children.Add(new PresenterOutputView(state, editing: true));
        stage.Children.Add(overlay);
        overlay.MouseLeftButtonDown += (_, _) => Select(null);
        var frame = new Border
        {
            CornerRadius = new CornerRadius(6),
            BorderBrush = Res("Brand.Line"),
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            Child = new Viewbox { Stretch = Stretch.Uniform, Child = stage },
            VerticalAlignment = VerticalAlignment.Top,
        };
        dock.Children.Add(frame);
        return dock;
    }

    /// <summary>Rahmen der eingeschalteten Bausteine an den Entwurf anpassen (ohne laufendes Ziehen zu stören).</summary>
    private void SyncFrames()
    {
        var enabled = state.DraftLayout.Widgets.Where(w => w.Enabled).Select(w => w.Kind).ToHashSet();
        foreach (var kind in frames.Keys.Where(k => !enabled.Contains(k)).ToList())
        {
            overlay.Children.Remove(frames[kind]);
            frames.Remove(kind);
        }
        foreach (var kind in enabled)
        {
            if (!frames.TryGetValue(kind, out var f))
            {
                f = new EditableFrame(this, kind);
                frames[kind] = f;
                overlay.Children.Add(f);
            }
            var r = state.DraftLayout.Config(kind)!.Frame;
            Canvas.SetLeft(f, r.X * CanvasW);
            Canvas.SetTop(f, r.Y * CanvasH);
            f.Width = Math.Max(r.W * CanvasW, 1);
            f.Height = Math.Max(r.H * CanvasH, 1);
            f.SetSelected(selected == kind);
        }
    }

    /// <summary>Für Testbilder: Baustein auswählen.</summary>
    internal void SelectForTest(WidgetKind kind) => Select(kind);

    private void Select(WidgetKind? kind)
    {
        if (selected == kind) return;
        selected = kind;
        Sync();
    }

    private void Apply(WidgetKind kind, NormRect rect)
    {
        var r = (snap ? rect.Snapped(0.01) : rect).Clamped();
        state.DraftLayout.Update(kind, c => c.Frame = r);
        state.DraftChanged();
    }

    private void Hide(WidgetKind kind)
    {
        state.DraftLayout.Update(kind, c => c.Enabled = false);
        if (selected == kind) selected = null;
        state.DraftChanged();
    }

    /// <summary>Bearbeitbarer Rahmen eines Bausteins (Mac: EditableFrame).</summary>
    private sealed class EditableFrame : Grid
    {
        private readonly LayoutBuilderWindow owner;
        private readonly WidgetKind kind;
        private readonly Rectangle outline = new();
        private readonly Border fill = new();
        private Point dragStart;
        private NormRect? startRect;
        private bool resizing;

        public EditableFrame(LayoutBuilderWindow owner, WidgetKind kind)
        {
            this.owner = owner;
            this.kind = kind;
            Children.Add(fill);
            Children.Add(outline);
            var label = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(166, 0, 0, 0)),
                Padding = new Thickness(8, 3, 8, 3),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                IsHitTestVisible = false,
                Child = new TextBlock { Text = WidgetConfig.Title(kind), FontSize = 20, Foreground = Brushes.White },
            };
            Children.Add(label);
            // Schließen: Baustein ausblenden (über „Widgets“ wieder einblendbar)
            var close = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(15),
                Background = new SolidColorBrush(Color.FromArgb(178, 0, 0, 0)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(5),
                Cursor = Cursors.Hand,
                ToolTip = $"„{WidgetConfig.Title(kind)}“ ausblenden",
                Child = new TextBlock
                {
                    Text = "",
                    FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                    FontSize = 13,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            close.MouseLeftButtonDown += (_, e) =>
            {
                owner.Hide(kind);
                e.Handled = true;
            };
            Children.Add(close);
            // Griff zum Skalieren
            var grip = new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Color.FromRgb(0xB0, 0x88, 0xFB)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(4),
                Cursor = Cursors.SizeNWSE,
            };
            grip.MouseLeftButtonDown += (_, e) => Begin(e, resize: true);
            Children.Add(grip);
            Cursor = Cursors.SizeAll;
            MouseLeftButtonDown += (_, e) => Begin(e, resize: false);
            MouseMove += OnMove;
            MouseLeftButtonUp += (_, _) => End();
            LostMouseCapture += (_, _) => startRect = null;
        }

        public void SetSelected(bool on)
        {
            var accent = Color.FromRgb(0xB0, 0x88, 0xFB);
            fill.Background = new SolidColorBrush(on ? Color.FromArgb(31, accent.R, accent.G, accent.B) : Color.FromArgb(1, 0, 0, 0));
            outline.Stroke = on ? new SolidColorBrush(accent) : new SolidColorBrush(Color.FromArgb(102, 255, 255, 255));
            outline.StrokeThickness = on ? 4 : 2;
            outline.StrokeDashArray = on ? null : new DoubleCollection { 5, 4 };
            Panel.SetZIndex(this, on ? 1 : 0);
        }

        private void Begin(MouseButtonEventArgs e, bool resize)
        {
            owner.Select(kind);
            startRect = owner.state.DraftLayout.Config(kind)?.Frame;
            dragStart = e.GetPosition(owner.overlay);
            resizing = resize;
            CaptureMouse();
            e.Handled = true;
        }

        private void OnMove(object sender, MouseEventArgs e)
        {
            if (startRect is not { } s || e.LeftButton != MouseButtonState.Pressed) return;
            var p = e.GetPosition(owner.overlay);
            double dx = (p.X - dragStart.X) / CanvasW, dy = (p.Y - dragStart.Y) / CanvasH;
            owner.Apply(kind, resizing ? s with { W = s.W + dx, H = s.H + dy } : s with { X = s.X + dx, Y = s.Y + dy });
        }

        private void End()
        {
            startRect = null;
            ReleaseMouseCapture();
        }
    }

    // MARK: - Rechte Seite

    private UIElement BuildSidebar()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 16, 16, 16) };
        panel.Children.Add(PresetCard());
        panel.Children.Add(WidgetsCard());
        panel.Children.Add(PropertiesCard());
        panel.Children.Add(AppearanceCard());
        panel.Children.Add(ActionsCard());
        return new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = panel };
    }

    private Border PresetCard()
    {
        var p = new StackPanel();
        p.Children.Add(CardTitle("Preset"));
        targetBox.SelectionChanged += (_, _) =>
        {
            if (syncing || targetBox.SelectedItem is not ComboBoxItem { Tag: var tag }) return;
            state.SelectBuilderTarget(tag as Guid?);
        };
        p.Children.Add(Row("Bearbeiten", targetBox));

        unsavedRow.Children.Add(new TextBlock { Text = "", FontFamily = (FontFamily)FindResource("Brand.IconFont"), Foreground = Res("Brand.Warning"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        unsavedRow.Children.Add(new TextBlock { Text = "Nicht gespeichert", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        var revert = SmallButton("Verwerfen", () => state.RevertDraftToTarget());
        revert.Margin = new Thickness(0, 0, 6, 0);
        unsavedRow.Children.Add(revert);
        unsavedRow.Children.Add(saveToTarget);
        p.Children.Add(new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = unsavedRow });

        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var saveAs = SmallButton("Als Preset speichern …", () => SaveDialog(rename: null));
        saveAs.Margin = renameButton.Margin = new Thickness(0, 0, 6, 6);
        deleteButton.Margin = new Thickness(0, 0, 0, 6);
        actions.Children.Add(saveAs);
        actions.Children.Add(renameButton);
        actions.Children.Add(deleteButton);
        p.Children.Add(actions);

        var label = new StackPanel();
        label.Children.Add(new TextBlock { Text = "Zugewiesen an" });
        assignTitle.Foreground = Res("Brand.SecondaryText");
        assignTitle.MaxWidth = 120;
        label.Children.Add(assignTitle);
        var row = new DockPanel();
        DockPanel.SetDock(assignBox, Dock.Right);
        row.Children.Add(assignBox);
        row.Children.Add(label);
        assignRow.Children.Add(row);
        assignBox.SelectionChanged += (_, _) =>
        {
            if (syncing || state.NavigatorItem is not { } item || assignBox.SelectedItem is not ComboBoxItem { Tag: var tag }) return;
            state.AssignPreset(tag as Guid?, item);
        };
        p.Children.Add(assignRow);
        return Card(p);
    }

    private Border WidgetsCard()
    {
        var p = new StackPanel();
        p.Children.Add(CardTitle("Widgets"));
        foreach (var kind in Enum.GetValues<WidgetKind>())
        {
            var box = new CheckBox { Content = WidgetConfig.Title(kind), VerticalAlignment = VerticalAlignment.Center };
            box.Click += (_, _) =>
            {
                bool on = box.IsChecked == true;
                state.DraftLayout.Update(kind, c => c.Enabled = on);
                if (on) selected = kind;
                else if (selected == kind) selected = null;
                state.DraftChanged();
            };
            var chevron = new TextBlock { Text = "", FontFamily = (FontFamily)FindResource("Brand.IconFont"), FontSize = 10, Foreground = Res("Brand.SecondaryText"), VerticalAlignment = VerticalAlignment.Center };
            var dock = new DockPanel();
            DockPanel.SetDock(chevron, Dock.Right);
            dock.Children.Add(chevron);
            dock.Children.Add(box);
            var row = new Border { Padding = new Thickness(6, 4, 6, 4), CornerRadius = new CornerRadius(4), Background = Brushes.Transparent, Child = dock, Cursor = Cursors.Hand };
            row.MouseLeftButtonUp += (_, _) => Select(kind);
            widgetRows[kind] = (box, chevron, row);
            p.Children.Add(row);
        }
        return Card(p);
    }

    private Border PropertiesCard()
    {
        var p = new StackPanel();
        p.Children.Add(propsTitle);
        var font = new StackPanel { Orientation = Orientation.Horizontal };
        font.Children.Add(fontSlider);
        font.Children.Add(fontText);
        fontSlider.ValueChanged += (_, _) =>
        {
            fontText.Text = $"{Math.Round(fontSlider.Value * 100)} %";
            if (syncing || selected is not { } k) return;
            double v = Math.Round(fontSlider.Value * 20) / 20;
            state.DraftLayout.Update(k, c => c.FontScale = v);
            state.DraftChanged();
        };
        p.Children.Add(Row("Schriftgröße", font));
        titleBox.Click += (_, _) =>
        {
            if (selected is not { } k) return;
            bool on = titleBox.IsChecked == true;
            state.DraftLayout.Update(k, c => c.ShowTitle = on);
            state.DraftChanged();
        };
        p.Children.Add(titleBox);
        foreach (var (label, key) in new[] { ("X", "x"), ("Y", "y"), ("Breite", "w"), ("Höhe", "h") })
            p.Children.Add(Row(label, PercentEditor(key)));
        return Card(p);
    }

    /// <summary>Lage in Prozent: Eingabefeld mit Pfeilen (Mac: TextField + Stepper).</summary>
    private UIElement PercentEditor(string key)
    {
        var box = new TextBox { Width = 56, TextAlignment = TextAlignment.Right, VerticalContentAlignment = VerticalAlignment.Center };
        frameBoxes[key] = box;
        void Commit(double? value = null)
        {
            if (selected is not { } k || state.DraftLayout.Config(k) is not { } c) return;
            double v = value ?? (double.TryParse(box.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var t) ? t : Get(c.Frame, key) * 100);
            var f = key switch
            {
                "x" => c.Frame with { X = v / 100 },
                "y" => c.Frame with { Y = v / 100 },
                "w" => c.Frame with { W = v / 100 },
                _ => c.Frame with { H = v / 100 },
            };
            state.DraftLayout.Update(k, cfg => cfg.Frame = f.Clamped());
            state.DraftChanged();
        }
        box.LostFocus += (_, _) => { if (!syncing) Commit(); };
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) Commit();
            else if (e.Key == Key.Up) Step(+1);
            else if (e.Key == Key.Down) Step(-1);
        };
        void Step(int d)
        {
            if (selected is not { } k || state.DraftLayout.Config(k) is not { } c) return;
            Commit(Math.Round(Get(c.Frame, key) * 100) + d);
        }
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(box);
        row.Children.Add(new TextBlock { Text = "%", Foreground = Res("Brand.SecondaryText"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 8, 0) });
        var down = SmallButton("−", () => Step(-1));
        var up = SmallButton("+", () => Step(+1));
        down.Margin = new Thickness(0, 0, 4, 0);
        row.Children.Add(down);
        row.Children.Add(up);
        return row;
    }

    private static double Get(NormRect r, string key) => key switch { "x" => r.X, "y" => r.Y, "w" => r.W, _ => r.H };

    private Border AppearanceCard()
    {
        var p = new StackPanel();
        p.Children.Add(CardTitle("Darstellung"));
        bgButton.Style = fgButton.Style = (Style)FindResource("Brand.Small");
        bgButton.Click += (_, _) => ColorMenu.Show(bgButton, state.DraftLayout.Background, h => { state.DraftLayout.Background = h; state.DraftChanged(); });
        fgButton.Click += (_, _) => ColorMenu.Show(fgButton, state.DraftLayout.TextColor, h => { state.DraftLayout.TextColor = h; state.DraftChanged(); });
        p.Children.Add(Row("Hintergrund", bgButton));
        p.Children.Add(Row("Schriftfarbe", fgButton));
        var snapBox = new CheckBox { Content = "Am Raster ausrichten (1 %)", IsChecked = snap, Margin = new Thickness(0, 6, 0, 0) };
        snapBox.Click += (_, _) => snap = snapBox.IsChecked == true;
        p.Children.Add(snapBox);
        return Card(p);
    }

    private Border ActionsCard()
    {
        var p = new StackPanel();
        var all = SmallButton("Alle Widgets einblenden", () =>
        {
            foreach (var k in Enum.GetValues<WidgetKind>()) state.DraftLayout.Update(k, c => c.Enabled = true);
            state.DraftChanged();
        });
        var reset = SmallButton("Standard wiederherstellen (Windows-Referentenansicht)", () => state.DraftLayout = PresenterLayout.WindowsDefault());
        var done = SmallButton("Fertig", Close);
        done.IsDefault = true;
        done.Style = (Style)FindResource("Brand.Prominent");
        foreach (var b in new[] { all, reset, done })
        {
            b.HorizontalAlignment = HorizontalAlignment.Stretch;
            b.Margin = new Thickness(0, 0, 0, 6);
            p.Children.Add(b);
        }
        return Card(p);
    }

    // MARK: - Abgleich

    private void Sync()
    {
        syncing = true;
        try
        {
            bool live = state.IsShowRunning || state.IsStarting;
            hint.Text = live ? "Entwurf – Widgets ziehen, Ecke unten rechts zum Skalieren"
                             : "Vorschau – Änderungen wirken sofort";

            // Entwurfsleiste
            bool changes = state.HasDraftChanges;
            draftBar.Visibility = live || changes ? Visibility.Visible : Visibility.Collapsed;
            draftIcon.Text = changes ? "" : "";
            draftIcon.Foreground = Res(changes ? "Brand.Warning" : "Brand.Ok");
            draftTitle.Text = live ? "Präsentation läuft – Änderungen sind noch nicht sichtbar"
                                   : "Entwurf aus der letzten Präsentation noch nicht übernommen";
            draftDetail.Text = state.ApplyDraftAfterShow ? "Wird nach dem Ende der Präsentation übernommen."
                : changes ? "Ungespeicherter Entwurf." : "Keine Änderungen.";
            draftDiscard.IsEnabled = changes;
            draftAfter.Visibility = live ? Visibility.Visible : Visibility.Collapsed;
            draftAfter.IsEnabled = changes && !state.ApplyDraftAfterShow;
            draftNow.IsEnabled = changes;

            // Preset
            targetBox.Items.Clear();
            targetBox.Items.Add(new ComboBoxItem { Content = "Standard", Tag = null });
            foreach (var preset in state.LayoutPresets) targetBox.Items.Add(new ComboBoxItem { Content = preset.Name, Tag = preset.Id });
            targetBox.SelectedItem = targetBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (i.Tag as Guid?) == state.BuilderTarget);
            unsavedRow.Visibility = state.HasUnsavedChanges ? Visibility.Visible : Visibility.Collapsed;
            saveToTarget.Content = $"In „{state.TargetName(state.BuilderTarget)}“ speichern";
            bool isPreset = state.BuilderTarget != null;
            renameButton.Visibility = deleteButton.Visibility = isPreset ? Visibility.Visible : Visibility.Collapsed;
            if (state.NavigatorItem is { } item)
            {
                assignRow.Visibility = Visibility.Visible;
                assignTitle.Text = item.Title;
                assignBox.Items.Clear();
                assignBox.Items.Add(new ComboBoxItem { Content = "Standard", Tag = null });
                foreach (var preset in state.LayoutPresets) assignBox.Items.Add(new ComboBoxItem { Content = preset.Name, Tag = preset.Id });
                var current = state.Preset(item.PresetId)?.Id;
                assignBox.SelectedItem = assignBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (i.Tag as Guid?) == current);
            }
            else assignRow.Visibility = Visibility.Collapsed;

            // Widgets
            if (selected is { } s && state.DraftLayout.Config(s) is not { Enabled: true }) selected = null;
            foreach (var (kind, (box, chevron, row)) in widgetRows)
            {
                box.IsChecked = state.DraftLayout.Config(kind)?.Enabled == true;
                chevron.Visibility = selected == kind ? Visibility.Visible : Visibility.Hidden;
                row.Background = selected == kind ? Res("Brand.Highlight") : Brushes.Transparent;
            }

            // Eigenschaften
            if (selected is { } k && state.DraftLayout.Config(k) is { } c)
            {
                propsCard.Visibility = Visibility.Visible;
                propsTitle.Text = $"Eigenschaften: {WidgetConfig.Title(k)}";
                fontSlider.Value = c.FontScale;
                fontText.Text = $"{Math.Round(c.FontScale * 100)} %";
                titleBox.IsChecked = c.ShowTitle;
                foreach (var (key, tb) in frameBoxes)
                    if (!tb.IsKeyboardFocusWithin) tb.Text = Math.Round(Get(c.Frame, key) * 100).ToString(CultureInfo.InvariantCulture);
            }
            else propsCard.Visibility = Visibility.Collapsed;

            // Darstellung
            bgButton.Background = ColorMenu.Swatch(state.DraftLayout.Background);
            fgButton.Background = ColorMenu.Swatch(state.DraftLayout.TextColor);

            SyncFrames();
        }
        finally
        {
            syncing = false;
        }
    }

    // MARK: - Preset speichern / umbenennen / löschen (Mac: PresetSaveSheet)

    private void SaveDialog(Guid? rename)
    {
        var item = state.NavigatorItem;
        bool renaming = rename != null;
        var dlg = new Window
        {
            Title = renaming ? "Preset umbenennen" : "Referent*innenansicht als Preset speichern",
            Owner = this,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Style = (Style)FindResource("Brand.Window"),
        };
        var name = new TextBox
        {
            Width = 360,
            Text = renaming ? state.Preset(rename)?.Name ?? "" : item?.Title ?? $"Preset {state.LayoutPresets.Count + 1}",
            Margin = new Thickness(0, 0, 0, 12),
        };
        var assign = new CheckBox { IsChecked = item != null, Margin = new Thickness(0, 0, 0, 14) };
        if (item != null) assign.Content = new TextBlock { Text = $"Der Präsentation „{item.Title}“ zuweisen", TextWrapping = TextWrapping.Wrap, MaxWidth = 340 };
        var ok = new Button { Content = renaming ? "Umbenennen" : "Speichern", IsDefault = true, Style = (Style)FindResource("Brand.Prominent") };
        var cancel = new Button { Content = "Abbrechen", IsCancel = true, Style = (Style)FindResource("Brand.Compact"), Margin = new Thickness(0, 0, 8, 0) };
        name.TextChanged += (_, _) => ok.IsEnabled = name.Text.Trim().Length > 0;
        ok.Click += (_, _) =>
        {
            if (renaming) state.RenamePreset(rename!.Value, name.Text);
            else state.SaveDraftAsPreset(name.Text, assign.IsChecked == true ? item : null);
            dlg.Close();
        };
        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(name);
        if (!renaming && item != null) root.Children.Add(assign);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        root.Children.Add(buttons);
        dlg.Content = root;
        dlg.Loaded += (_, _) => { name.Focus(); name.SelectAll(); };
        dlg.ShowDialog();
    }

    private void DeletePreset()
    {
        if (state.BuilderTarget is not { } id) return;
        var r = MessageBox.Show(this, $"Preset „{state.TargetName(id)}“ löschen? Präsentationen mit diesem Preset nutzen dann „Standard“.",
            "Preset löschen", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (r == MessageBoxResult.OK) state.DeletePreset(id);
    }

    // MARK: - Bausteine der Oberfläche

    private Brush Res(string key) => TryFindResource(key) as Brush ?? Brushes.Gray;

    private TextBlock Glyph(string glyph, bool secondary = false) => new()
    {
        Text = glyph,
        FontFamily = (FontFamily)FindResource("Brand.IconFont"),
        Foreground = secondary ? Res("Brand.SecondaryText") : Res("Brand.Accent"),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private Border Card(UIElement content) => new()
    {
        Style = (Style)FindResource("Brand.Card"),
        Child = content,
    };

    private static TextBlock CardTitle(string text) => new()
    {
        Text = text,
        FontWeight = FontWeights.SemiBold,
        FontSize = 13,
        Margin = new Thickness(0, 0, 0, 10),
    };

    private static UIElement Row(string label, UIElement control)
    {
        var dock = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(control, Dock.Right);
        dock.Children.Add(control);
        dock.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        return dock;
    }

    private Button SmallButton(string text, Action click)
    {
        var b = new Button { Content = text, Style = (Style)FindResource("Brand.Compact") };
        b.Click += (_, _) => click();
        return b;
    }
}
