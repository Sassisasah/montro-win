using System.Diagnostics;
using System.Windows.Media;

namespace Montro.Core;

/// <summary>Einstellungen des Redezeit-Timers (Mac: TimerSettings).</summary>
public sealed class TimerSettings
{
    public double Duration { get; set; } = 15 * 60;
    /// <summary>1. Warnzeit = Restzeit in Sekunden, ab der Warnfarbe 1 greift</summary>
    public double Warning1 { get; set; } = 5 * 60;
    public double Warning2 { get; set; } = 60;
    public string NormalColor { get; set; } = "#F5F5F5";
    public string Warning1Color { get; set; } = "#FFC71F";
    public string Warning2Color { get; set; } = "#FF8C1A";
    public string OvertimeColor { get; set; } = "#F53838";
    public bool AllowOvertime { get; set; } = true;
    public bool BlinkInOvertime { get; set; } = true;
    public bool BlinkInWarning2 { get; set; }
    public bool CountUp { get; set; }
    public bool AutoStartWithPresentation { get; set; }
    public bool ShowProgress { get; set; } = true;
}

public enum TimerRunState { Stopped, Running, Paused }
public enum TimerPhase { Normal, Warning1, Warning2, Overtime }

/// <summary>
/// Redezeit-Timer, portiert aus <c>TalkTimer.swift</c>: Redezeit, zwei Warnphasen mit eigenen
/// Farben, Überziehen ins Minus, Blinken auf Kommando. <see cref="Tick"/> ruft die Oberfläche
/// regelmäßig auf.
/// </summary>
public sealed class TalkTimer : Observable
{
    private readonly Stopwatch watch = new();
    private double accumulated;
    private double elapsed;
    private double adjustment;
    private TimerRunState runState = TimerRunState.Stopped;
    private bool manualBlink;

    public TimerSettings Settings { get; set; } = new();

    public TimerRunState RunState { get => runState; private set { if (Set(ref runState, value)) RaiseAll(); } }
    public double Elapsed => elapsed;
    public double Adjustment => adjustment;
    public double Total => Settings.Duration + adjustment;
    public double Remaining => Total - elapsed;
    public bool IsRunning => RunState == TimerRunState.Running;

    public bool ManualBlink { get => manualBlink; set { if (Set(ref manualBlink, value)) RaiseAll(); } }

    public void Start()
    {
        if (IsRunning) return;
        if (Remaining <= 0 && !Settings.AllowOvertime) return;
        watch.Restart();
        RunState = TimerRunState.Running;
    }

    public void Pause()
    {
        if (!IsRunning) return;
        Tick();
        accumulated = elapsed;
        watch.Reset();
        RunState = TimerRunState.Paused;
    }

    public void Toggle()
    {
        if (IsRunning) Pause(); else Start();
    }

    public void Reset()
    {
        watch.Reset();
        accumulated = 0;
        elapsed = 0;
        adjustment = 0;
        RunState = TimerRunState.Stopped;
        RaiseAll();
    }

    public void Add(double seconds)
    {
        adjustment += seconds;
        RaiseAll();
    }

    public void SetDuration(double minutes)
    {
        Settings.Duration = minutes * 60;
        Reset();
    }

    public void Tick()
    {
        if (!IsRunning) return;
        double e = accumulated + watch.Elapsed.TotalSeconds;
        if (!Settings.AllowOvertime && e >= Total)
        {
            elapsed = Math.Max(Total, 0);
            accumulated = elapsed;
            watch.Reset();
            RunState = TimerRunState.Paused;
            return;
        }
        elapsed = e;
        RaiseAll();
    }

    public TimerPhase Phase
    {
        get
        {
            double r = Remaining;
            if (r <= 0) return TimerPhase.Overtime;
            if (r <= Settings.Warning2) return TimerPhase.Warning2;
            if (r <= Settings.Warning1) return TimerPhase.Warning1;
            return TimerPhase.Normal;
        }
    }

    public string ColorHex => Phase switch
    {
        TimerPhase.Warning1 => Settings.Warning1Color,
        TimerPhase.Warning2 => Settings.Warning2Color,
        TimerPhase.Overtime => Settings.OvertimeColor,
        _ => Settings.NormalColor,
    };

    public Brush ColorBrush => new SolidColorBrush((Color)ColorConverter.ConvertFromString(ColorHex));

    public bool ShouldBlink
    {
        get
        {
            if (ManualBlink) return true;
            if (RunState == TimerRunState.Stopped) return false;
            if (Phase == TimerPhase.Overtime && Settings.BlinkInOvertime) return true;
            return Phase == TimerPhase.Warning2 && Settings.BlinkInWarning2 && IsRunning;
        }
    }

    public string DisplayText => Settings.CountUp ? TimeFormat.Clock(elapsed, roundUp: false) : TimeFormat.Clock(Remaining);

    public double Progress => Total <= 0 ? 1 : Math.Clamp(elapsed / Total, 0, 1);

    public string StateLabel => RunState switch
    {
        TimerRunState.Running => Phase == TimerPhase.Overtime ? "Überzogen" : "Läuft",
        TimerRunState.Paused => "Pausiert",
        _ => "Bereit",
    };

    public string TotalText => $"Redezeit {TimeFormat.Clock(Total)}";

    public string PlayGlyph => IsRunning ? "⏸" : "▶";

    /// <summary>Nach Änderungen an <see cref="Settings"/> alle Anzeigen neu berechnen.</summary>
    public void Refresh() => RaiseAll();

    private void RaiseAll() => Raise(nameof(RunState), nameof(Elapsed), nameof(Remaining), nameof(Total), nameof(IsRunning),
        nameof(Phase), nameof(ColorHex), nameof(ColorBrush), nameof(ShouldBlink), nameof(DisplayText), nameof(Progress),
        nameof(StateLabel), nameof(TotalText), nameof(PlayGlyph), nameof(ManualBlink));
}

/// <summary>mm:ss bzw. h:mm:ss, negative Werte mit Minus (Mac: TimeFormat.clock).</summary>
public static class TimeFormat
{
    public static string Clock(double seconds, bool roundUp = true)
    {
        bool negative = seconds < 0;
        double magnitude = Math.Abs(seconds);
        int total = (int)(roundUp || negative ? Math.Ceiling(magnitude) : Math.Floor(magnitude));
        int h = total / 3600, m = total % 3600 / 60, s = total % 60;
        string body = h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m:00}:{s:00}";
        return (negative ? "−" : "") + body;
    }
}
