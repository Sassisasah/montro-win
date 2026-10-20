using Microsoft.Win32;
using Montro.Core;

namespace Montro.PowerPoint;

/// <summary>
/// PowerPoints eigene Einstellung „Bildschirmpräsentation › Monitor“ liegt unter
/// HKCU\Software\Microsoft\Office\16.0\PowerPoint\Options, Wert „DisplayMonitor“
/// (Gerätename wie \\.\DISPLAY2). Vor dem Start gesetzt, öffnet PowerPoint die Show gleich dort.
/// Ob jede Office-Version den Wert so liest, ist ein Testpunkt (docs/STATUS.md).
/// </summary>
public static class RegistryMonitor
{
    private const string KeyPath = @"Software\Microsoft\Office\16.0\PowerPoint\Options";
    private const string ValueName = "DisplayMonitor";

    public static string? Get()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            return key?.GetValue(ValueName) as string;
        }
        catch { return null; }
    }

    public static void Set(string deviceName)
    {
        try
        {
            var old = Get();
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
            key.SetValue(ValueName, deviceName, RegistryValueKind.String);
            DebugLog.Write($"PowerPoint-Monitor (Registry): „{old ?? "–"}“ → „{deviceName}“");
        }
        catch (Exception e)
        {
            DebugLog.Write($"PowerPoint-Monitor (Registry) nicht gesetzt: {e.Message}");
        }
    }
}
