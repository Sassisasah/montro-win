using System.Windows;
using System.Windows.Controls;

namespace Montro.Views;

/// <summary>Eingabe für Minuten + Sekunden (Mac: DurationField).</summary>
public sealed class DurationBox : StackPanel
{
    private readonly TextBox minutes = new() { Width = 48, TextAlignment = TextAlignment.Right };
    private readonly TextBox seconds = new() { Width = 38, TextAlignment = TextAlignment.Right };
    private bool updating;
    private double value;

    /// <summary>Wert geändert (nach Verlassen des Feldes oder Enter).</summary>
    public event Action<double>? Changed;

    public DurationBox()
    {
        Orientation = Orientation.Horizontal;
        Margin = new Thickness(0, 3, 8, 3);
        var secondary = (System.Windows.Media.Brush)Application.Current.FindResource("Brand.SecondaryText");
        Children.Add(minutes);
        Children.Add(new TextBlock { Text = "min", Foreground = secondary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 8, 0) });
        Children.Add(seconds);
        Children.Add(new TextBlock { Text = "s", Foreground = secondary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) });
        foreach (var box in new[] { minutes, seconds })
        {
            box.LostFocus += (_, _) => Commit();
            box.KeyDown += (_, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter) Commit();
            };
        }
    }

    public double Value
    {
        get => value;
        set
        {
            this.value = Math.Max(0, value);
            updating = true;
            minutes.Text = ((int)this.value / 60).ToString();
            seconds.Text = ((int)this.value % 60).ToString();
            updating = false;
        }
    }

    private void Commit()
    {
        if (updating) return;
        int.TryParse(minutes.Text.Trim(), out int m);
        int.TryParse(seconds.Text.Trim(), out int s);
        double v = Math.Max(0, m) * 60 + Math.Clamp(s, 0, 59);
        if (v == value) return;
        Value = v;
        Changed?.Invoke(v);
    }
}
