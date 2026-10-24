using System.Runtime.InteropServices;

namespace Montro.Display;

/// <summary>
/// Farb-Check einer Ausgabe (Mac: <c>DisplayColor.swift</c>): HDR bzw. „Erweiterte Farben“ (auch Auto
/// Color Management unter Windows 11), Signalformat (RGB oder YCbCr) und Bittiefe, wie Windows sie
/// für den Monitor meldet. Ein Videomischer erwartet sRGB, RGB, ohne HDR.
/// </summary>
public sealed record DisplayColor(bool AdvancedColor, bool WideColorEnforced, string Encoding, uint BitsPerChannel)
{
    public bool IsRgb => Encoding == "RGB";

    /// <summary>Hinweis für die Ausgabe-Zeile, null = alles in Ordnung.</summary>
    public string? Warning
    {
        get
        {
            var parts = new List<string>();
            if (AdvancedColor)
                parts.Add(WideColorEnforced
                    ? "Automatische Farbverwaltung ist an: Windows rechnet die Folienfarben um. Einstellungen › System › Bildschirm › diesen Bildschirm › Farbe automatisch verwalten ausschalten."
                    : "HDR ist an: Folien wirken blass. Einstellungen › System › Bildschirm › HDR ausschalten.");
            if (!IsRgb)
                parts.Add($"Der Ausgang sendet {Encoding} statt RGB, Schwarz und Farben können abweichen. Im Grafiktreiber RGB mit vollem Bereich einstellen.");
            return parts.Count == 0 ? null : string.Join(" ", parts);
        }
    }

    /// <summary>Kurztext für das Testbild, z. B. „RGB 8 Bit“.</summary>
    public string Summary =>
        $"{Encoding}{(BitsPerChannel > 0 ? $" {BitsPerChannel} Bit" : "")}{(AdvancedColor ? (WideColorEnforced ? ", Farbverwaltung an" : ", HDR an") : "")}";

    /// <summary>GDI-Gerätename (\\.\DISPLAY2) → Farbangaben. Leer, wenn die API scheitert.</summary>
    public static Dictionary<string, DisplayColor> All()
    {
        var result = new Dictionary<string, DisplayColor>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (Native.GetDisplayConfigBufferSizes(Native.QDC_ONLY_ACTIVE_PATHS, out var np, out var nm) != 0) return result;
            var paths = new Native.DISPLAYCONFIG_PATH_INFO[np];
            var modes = new Native.DISPLAYCONFIG_MODE_INFO[nm];
            if (Native.QueryDisplayConfig(Native.QDC_ONLY_ACTIVE_PATHS, ref np, paths, ref nm, modes, IntPtr.Zero) != 0) return result;
            for (var i = 0; i < np; i++)
            {
                var p = paths[i];
                var source = new Native.DISPLAYCONFIG_SOURCE_DEVICE_NAME
                {
                    header = new Native.DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = Native.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME,
                        size = (uint)Marshal.SizeOf<Native.DISPLAYCONFIG_SOURCE_DEVICE_NAME>(),
                        adapterId = p.sourceInfo.adapterId,
                        id = p.sourceInfo.id,
                    },
                };
                var color = new ADVANCED_COLOR_INFO
                {
                    header = new Native.DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO,
                        size = (uint)Marshal.SizeOf<ADVANCED_COLOR_INFO>(),
                        adapterId = p.targetInfo.adapterId,
                        id = p.targetInfo.id,
                    },
                };
                if (Native.DisplayConfigGetDeviceInfo(ref source) != 0 || DisplayConfigGetDeviceInfo(ref color) != 0) continue;
                if (result.ContainsKey(source.viewGdiDeviceName)) continue;
                result[source.viewGdiDeviceName] = new DisplayColor(
                    AdvancedColor: (color.value & 0x2) != 0,
                    WideColorEnforced: (color.value & 0x4) != 0,
                    Encoding: color.colorEncoding switch
                    {
                        0 => "RGB",
                        1 => "YCbCr 4:4:4",
                        2 => "YCbCr 4:2:2",
                        3 => "YCbCr 4:2:0",
                        _ => "Intensität",
                    },
                    BitsPerChannel: color.bitsPerColorChannel);
            }
        }
        catch (Exception ex)
        {
            Core.DebugLog.Write($"Farb-Check: {ex.Message}");
        }
        return result;
    }

    private const uint DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO = 9;

    /// <summary>DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO: Bit 0 unterstützt, Bit 1 an, Bit 2 erzwungen (ACM), Bit 3 gesperrt.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct ADVANCED_COLOR_INFO
    {
        public Native.DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint value;
        public uint colorEncoding;
        public uint bitsPerColorChannel;
    }

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref ADVANCED_COLOR_INFO info);
}
