using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Montro.Core;
using Montro.Remote;

namespace Montro.Views;

/// <summary>Einstellungen (Mac: SettingsView), bisher der Abschnitt „Fernsteuerung (Bitfocus Companion)“.</summary>
public sealed class SettingsWindow : Window
{
    private readonly AppState state;
    private readonly CheckBox enabled = new() { Content = "Netzwerk-Schnittstelle aktiv", Margin = new Thickness(0, 0, 0, 6) };
    private readonly CheckBox localOnly = new() { Content = "Nur von diesem Rechner erreichbar (127.0.0.1)", Margin = new Thickness(0, 0, 0, 10) };
    private readonly TextBox port = new() { Width = 80, TextAlignment = TextAlignment.Right };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBox hint = new()
    {
        IsReadOnly = true,
        BorderThickness = new Thickness(0),
        Background = Brushes.Transparent,
        TextWrapping = TextWrapping.Wrap,
        FontSize = 11,
        Margin = new Thickness(0, 10, 0, 0),
    };

    public SettingsWindow(AppState state)
    {
        this.state = state;
        Title = "Einstellungen";
        Style = (Style)FindResource("Brand.Window");
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var card = new StackPanel();
        card.Children.Add(new TextBlock { Text = "Fernsteuerung (Bitfocus Companion)", FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 0, 0, 10) });
        card.Children.Add(enabled);
        card.Children.Add(localOnly);
        card.Children.Add(Row("Port", port));
        card.Children.Add(Row("Status", status));
        hint.Foreground = (Brush)FindResource("Brand.SecondaryText");
        card.Children.Add(hint);
        card.Children.Add(new TextBlock
        {
            Text = "Keine Anmeldung: Ohne „Nur von diesem Rechner“ kann jeder im selben Netz montro steuern. " +
                   "In fremden Netzen (Hotel, Kongresszentrum) „Nur von diesem Rechner“ einschalten oder ein eigenes Technik-Netz nutzen. " +
                   "Fragt die Windows-Firewall beim ersten Start, Zugriff im privaten Netz erlauben.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Foreground = (Brush)FindResource("Brand.SecondaryText"),
            Margin = new Thickness(0, 8, 0, 0),
        });
        var close = new Button { Content = "Fertig", IsDefault = true, IsCancel = true, Style = (Style)FindResource("Brand.Compact"), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        close.Click += (_, _) => Close();
        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new Border { Style = (Style)FindResource("Brand.Card"), Child = card });
        root.Children.Add(new ClusterSection(state));
        root.Children.Add(close);
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };
        Width = 600;
        MaxHeight = SystemParameters.WorkArea.Height - 40;

        enabled.Click += (_, _) => state.RemoteEnabled = enabled.IsChecked == true;
        localOnly.Click += (_, _) => state.RemoteLocalOnly = localOnly.IsChecked == true;
        port.LostFocus += (_, _) => ApplyPort();
        port.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) ApplyPort(); };
        state.PropertyChanged += OnState;
        if (state.Remote != null) state.Remote.StatusChanged += OnRemote;
        Closed += (_, _) =>
        {
            state.PropertyChanged -= OnState;
            if (state.Remote != null) state.Remote.StatusChanged -= OnRemote;
        };
        Sync();
    }

    private void ApplyPort()
    {
        if (int.TryParse(port.Text.Trim(), out int p) && p > 0 && p < 65536) state.RemotePort = p;
        port.Text = state.RemotePort.ToString();
    }

    private void OnState(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppState.RemoteEnabled) or nameof(AppState.RemotePort) or nameof(AppState.RemoteLocalOnly)) Sync();
    }

    private void OnRemote() => Dispatcher.InvokeAsync(Sync);

    private void Sync()
    {
        enabled.IsChecked = state.RemoteEnabled;
        localOnly.IsChecked = state.RemoteLocalOnly;
        localOnly.IsEnabled = state.RemoteEnabled;
        if (!port.IsKeyboardFocusWithin) port.Text = state.RemotePort.ToString();
        var r = state.Remote;
        if (r?.RunningPort is int p)
        {
            status.Text = $"läuft auf Port {p}";
            status.Foreground = (Brush)FindResource("Brand.Ok");
        }
        else if (r?.LastError is { } err)
        {
            status.Text = err;
            status.Foreground = (Brush)FindResource("Brand.Live");
        }
        else
        {
            status.Text = state.RemoteEnabled ? "startet …" : "aus";
            status.Foreground = (Brush)FindResource("Brand.SecondaryText");
        }
        var ips = state.RemoteLocalOnly ? [] : RemoteApi.LocalAddresses();
        hint.Text = state.RemoteLocalOnly
            ? "Nur Companion auf diesem Rechner kann steuern – dort als Host 127.0.0.1 eintragen. Für andere Rechner diesen Schalter ausschalten."
            : ips.Count == 0
                ? "In Companion als Host die IP dieses Rechners eintragen (auf demselben Rechner: 127.0.0.1)."
                : $"In Companion als Host eintragen: {string.Join(" oder ", ips)} – auf demselben Rechner: 127.0.0.1 (Port {state.RemotePort})";
    }

    private static UIElement Row(string label, UIElement control)
    {
        var dock = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var l = new TextBlock { Text = label, Width = 70, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(l, Dock.Left);
        dock.Children.Add(l);
        if (control is FrameworkElement fe) fe.HorizontalAlignment = HorizontalAlignment.Left;
        dock.Children.Add(control);
        return dock;
    }
}
