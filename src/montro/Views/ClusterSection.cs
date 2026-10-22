using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Montro.Cluster;
using Montro.Core;

namespace Montro.Views;

/// <summary>
/// Einstellungen › Verbund (mehrere Rechner) – Mac: ClusterSection. Rolle, Verbundname, Name,
/// automatische Suche, Slave-Optionen, Status, Rechner im Verbund und manuelle Adressen.
/// Wird bei jeder Änderung des Verbunds neu aufgebaut (Eingabefelder behalten dabei den Fokus nicht,
/// daher übernehmen Namensfelder erst bei Enter bzw. beim Verlassen).
/// </summary>
public sealed class ClusterSection : StackPanel
{
    private readonly AppState state;
    private ClusterManager C => state.Cluster;
    private readonly StackPanel settings = new();
    private readonly StackPanel members = new();
    private readonly StackPanel others = new();
    private readonly RadioButton[] roleButtons = new RadioButton[3];
    private readonly TextBlock roleHelp = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 6, 0, 10) };
    private readonly TextBox groupBox = new() { Width = 220 };
    private readonly TextBox nameBox = new() { Width = 220 };
    private readonly TextBox addressBox = new() { Width = 260 };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private bool syncing;

    public ClusterSection(AppState state)
    {
        this.state = state;
        var card = new StackPanel();
        card.Children.Add(Title("Verbund (mehrere Rechner)"));
        var roles = new UniformGrid3();
        string[] titles = ["Einzelplatz", "Master", "Slave"];
        ClusterRole[] values = [ClusterRole.Standalone, ClusterRole.Master, ClusterRole.Slave];
        for (int i = 0; i < 3; i++)
        {
            var rb = new RadioButton { Content = titles[i], GroupName = "ClusterRole", Style = (Style)FindResource("Brand.Segment") };
            var role = values[i];
            rb.Checked += (_, _) => { if (!syncing) C.Role = role; };
            roleButtons[i] = rb;
            roles.Children.Add(rb);
        }
        card.Children.Add(roles);
        roleHelp.Foreground = Secondary;
        card.Children.Add(roleHelp);
        card.Children.Add(settings);
        Children.Add(new Border { Style = (Style)FindResource("Brand.Card"), Child = card });
        Children.Add(new Border { Style = (Style)FindResource("Brand.Card"), Child = members });
        Children.Add(new Border { Style = (Style)FindResource("Brand.Card"), Child = others });

        groupBox.LostFocus += (_, _) => C.Group = groupBox.Text;
        groupBox.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) C.Group = groupBox.Text; };
        nameBox.LostFocus += (_, _) => C.DeviceName = nameBox.Text;
        nameBox.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) C.DeviceName = nameBox.Text; };
        addressBox.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) AddAddress(); };

        C.Changed += OnChanged;
        state.FileSync.PropertyChanged += (_, _) => Dispatcher.InvokeAsync(Rebuild);
        Unloaded += (_, _) => C.Changed -= OnChanged;
        Rebuild();
    }

    private void OnChanged() => Dispatcher.InvokeAsync(Rebuild);

    private Brush Secondary => (Brush)FindResource("Brand.SecondaryText");
    private Brush Res(string key) => (Brush)FindResource(key);

    private void Rebuild()
    {
        // Während der Eingabe in einem Feld nur den Status nachziehen (Neuaufbau nähme den Fokus)
        if (System.Windows.Input.Keyboard.FocusedElement is TextBox tb && IsAncestorOf(tb))
        {
            status.Text = (C.SummaryIsWarning ? "⚠ " : "✓ ") + C.Summary;
            return;
        }
        syncing = true;
        try
        {
            roleButtons[(int)C.Role].IsChecked = true;
            roleHelp.Text = C.Role switch
            {
                ClusterRole.Master => "Dieser Rechner steuert. Live schalten, Weiter/Zurück, Blackscreen, Timer und Regie-Nachricht werden an alle Slaves im Verbund übertragen. Companion und Clicker an den Master anschließen.",
                ClusterRole.Slave => "Dieser Rechner folgt dem Master Folie für Folie (Backup). Präsentationen werden über den Dateinamen zugeordnet – dieselben Dateien müssen in der Playlist sein.",
                _ => "Dieser Rechner arbeitet allein.",
            };
            settings.Children.Clear();
            members.Children.Clear();
            others.Children.Clear();
            ((Border)members.Parent).Visibility = C.IsActive ? Visibility.Visible : Visibility.Collapsed;
            ((Border)others.Parent).Visibility = C.IsActive && C.Others.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (!C.IsActive) return;

            if (!state.RemoteEnabled || state.RemoteLocalOnly)
            {
                var warn = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
                var on = Small("Einschalten", () => { state.RemoteLocalOnly = false; state.RemoteEnabled = true; });
                DockPanel.SetDock(on, Dock.Right);
                warn.Children.Add(on);
                warn.Children.Add(Caption("Für den Verbund muss die Netzwerk-Schnittstelle aktiv und von anderen Rechnern erreichbar sein.", Res("Brand.Warning")));
                settings.Children.Add(warn);
            }
            if (!groupBox.IsKeyboardFocusWithin) groupBox.Text = C.Group;
            if (!nameBox.IsKeyboardFocusWithin) nameBox.Text = C.DeviceName;
            settings.Children.Add(Row("Verbundname", groupBox));
            settings.Children.Add(Row("Name dieses Rechners", nameBox));
            settings.Children.Add(Check("Rechner automatisch finden (DNS-SD/Bonjour, gleiches Netz)", C.AutoDiscovery, v => C.AutoDiscovery = v));

            if (C.Role == ClusterRole.Slave)
            {
                var fs = state.FileSync;
                settings.Children.Add(Check("Playlist und Dateien vom Master übernehmen", fs.FollowPlaylist, v => fs.FollowPlaylist = v));
                settings.Children.Add(Caption("Fehlende oder geänderte Präsentationen werden automatisch vom Master geholt, die Reihenfolge wird angeglichen. Ersetzte Dateien landen vorher im Ordner „Dokumente\\montro\\Versionen“.", Secondary));
                if (fs.Detached)
                {
                    var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
                    var back = Small("Wieder in den Verbund", fs.Rejoin);
                    DockPanel.SetDock(back, Dock.Right);
                    row.Children.Add(back);
                    row.Children.Add(Caption("Zeitweise abgekoppelt", Res("Brand.Warning")));
                    settings.Children.Add(row);
                    settings.Children.Add(Caption("Bearbeiten (Stift in der Playlist) und selbst live schalten ist möglich. Eine geänderte Präsentation geht an den Master, sobald sie hier live war – spätestens beim Wiederverbinden.", Secondary));
                }
                else
                {
                    var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
                    var detach = Small("Zeitweise abkoppeln (zum Bearbeiten)", fs.Detach);
                    DockPanel.SetDock(detach, Dock.Left);
                    row.Children.Add(detach);
                    if (fs.PendingHandovers > 0)
                        row.Children.Add(Caption($"  {fs.PendingHandovers} Änderung(en) warten auf den Master", Res("Brand.Accent")));
                    settings.Children.Add(row);
                    if (fs.PendingHandovers > 0)
                        settings.Children.Add(Small("Eigene Änderungen verwerfen, Stand des Masters holen", fs.DiscardLocalChanges));
                }
                settings.Children.Add(Check("Bedienung an diesem Rechner sperren (nur der Master steuert)", C.SlaveLocksLocalControl, v => C.SlaveLocksLocalControl = v));
                settings.Children.Add(Check("Timer vom Master übernehmen", C.FollowTimer, v => C.FollowTimer = v));
                settings.Children.Add(Check("Regie-Nachricht vom Master übernehmen", C.FollowMessage, v => C.FollowMessage = v));
            }

            status.Text = (C.SummaryIsWarning ? "⚠ " : "✓ ") + C.Summary;
            status.Foreground = Res(C.SummaryIsWarning ? "Brand.Warning" : "Brand.Ok");
            settings.Children.Add(Row("Status", status));
            if (C.Role == ClusterRole.Slave && !C.MasterReachable)
            {
                var b = Small("Diesen Rechner zum Master machen", C.BecomeMaster);
                b.ToolTip = "Wenn der bisherige Master ausgefallen ist. Den alten Master danach auf „Slave“ stellen.";
                b.HorizontalAlignment = HorizontalAlignment.Left;
                settings.Children.Add(b);
            }

            // Rechner im Verbund
            members.Children.Add(Title(C.Role == ClusterRole.Master ? $"Slaves im Verbund „{C.Group}“" : $"Rechner im Verbund „{C.Group}“"));
            var list = C.Members;
            if (list.Count == 0)
                members.Children.Add(Caption(C.AutoDiscovery
                    ? $"Noch kein Rechner im Verbund „{C.Group}“ gefunden. Auf den anderen Rechnern denselben Verbundnamen und die passende Rolle einstellen – oder unten die IP-Adresse eintragen."
                    : "Keine Rechner eingetragen. Unten die IP-Adresse eintragen.", Secondary));
            foreach (var p in list) members.Children.Add(PeerRow(p));
            var add = new DockPanel { Margin = new Thickness(0, 10, 0, 4) };
            var addButton = Small("Hinzufügen", AddAddress);
            DockPanel.SetDock(addButton, Dock.Right);
            add.Children.Add(addButton);
            addressBox.ToolTip = "IP-Adresse (z. B. 192.168.1.20 oder 192.168.1.20:8765)";
            add.Children.Add(addressBox);
            members.Children.Add(add);
            members.Children.Add(Caption("Manuell eingetragene Rechner werden immer angesprochen – auch ohne automatische Suche, z. B. in Netzen, die DNS-SD blockieren. Ihre Rolle stellen sie selbst ein.", Secondary));

            // weitere Rechner im Netz
            others.Children.Add(Title("Weitere montro-Rechner im Netz"));
            foreach (var p in C.Others)
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                if (p.Role is ClusterRole.Master or ClusterRole.Slave)
                {
                    string g = p.Group;
                    var take = Small("Verbund übernehmen", () => C.Group = g);
                    take.ToolTip = $"Diesen Rechner in den Verbund „{g}“ aufnehmen";
                    DockPanel.SetDock(take, Dock.Right);
                    row.Children.Add(take);
                }
                var texts = new StackPanel();
                texts.Children.Add(new TextBlock { Text = p.Name });
                texts.Children.Add(Caption($"{ClusterRoles.Title(p.Role)} · Verbund „{p.Group}“ · {p.Address}", Secondary));
                row.Children.Add(texts);
                others.Children.Add(row);
            }
        }
        finally
        {
            syncing = false;
        }
    }

    private UIElement PeerRow(ClusterPeer p)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var dot = new Ellipse
        {
            Width = 9,
            Height = 9,
            Margin = new Thickness(0, 5, 8, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Fill = Res(!p.Reachable ? "Brand.Live" : p.Issue != null || p.InSync == false ? "Brand.Warning" : "Brand.Ok"),
        };
        DockPanel.SetDock(dot, Dock.Left);
        row.Children.Add(dot);
        if (p.Manual)
        {
            var remove = Small("Entfernen", () => C.RemoveManual(p));
            remove.ToolTip = "Manuellen Eintrag entfernen";
            DockPanel.SetDock(remove, Dock.Right);
            row.Children.Add(remove);
        }
        var texts = new StackPanel();
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(new TextBlock { Text = p.Name, FontWeight = FontWeights.Medium });
        head.Children.Add(new Border
        {
            Background = Res("Brand.Highlight"),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(5, 0, 5, 0),
            Margin = new Thickness(6, 0, 0, 0),
            Child = new TextBlock { Text = ClusterRoles.Title(p.Role), FontSize = 11 },
        });
        if (p.Manual) head.Children.Add(Caption("  manuell", Secondary));
        if (p.Discovered) head.Children.Add(Caption("  automatisch", Secondary));
        texts.Children.Add(head);
        var parts = new List<string> { p.Address };
        if (!p.Reachable) parts.Add("nicht erreichbar");
        else if (p.Live)
        {
            parts.Add($"live: {p.File} · Folie {p.Slide}{(p.Blackout ? " · Blackscreen" : "")}");
            if (C.Role == ClusterRole.Master && p.InSync is bool sync) parts.Add(sync ? "synchron" : "weicht ab");
        }
        else parts.Add("nicht live");
        texts.Children.Add(Caption(string.Join(" · ", parts), Secondary));
        if (p.Issue != null) texts.Children.Add(Caption("⚠ " + p.Issue, Res("Brand.Warning")));
        row.Children.Add(texts);
        return row;
    }

    private void AddAddress()
    {
        if (addressBox.Text.Trim().Length == 0) return;
        C.AddManual(addressBox.Text);
        addressBox.Text = "";
    }

    // MARK: - Bausteine

    private static TextBlock Title(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 0, 0, 10) };

    private static TextBlock Caption(string text, Brush brush) => new() { Text = text, FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = brush, VerticalAlignment = VerticalAlignment.Center };

    private CheckBox Check(string text, bool value, Action<bool> set)
    {
        var cb = new CheckBox { Content = text, IsChecked = value, Margin = new Thickness(0, 6, 0, 0) };
        cb.Click += (_, _) => set(cb.IsChecked == true);
        return cb;
    }

    private Button Small(string text, Action click)
    {
        var b = new Button { Content = text, Style = (Style)FindResource("Brand.Compact"), Margin = new Thickness(4, 0, 0, 0) };
        b.Click += (_, _) => click();
        return b;
    }

    private static UIElement Row(string label, UIElement control)
    {
        var dock = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        var l = new TextBlock { Text = label, Width = 150, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(l, Dock.Left);
        dock.Children.Add(l);
        if (control is FrameworkElement fe) fe.HorizontalAlignment = HorizontalAlignment.Left;
        dock.Children.Add(control);
        return dock;
    }

    private sealed class UniformGrid3 : System.Windows.Controls.Primitives.UniformGrid
    {
        public UniformGrid3() => Columns = 3;
    }
}
