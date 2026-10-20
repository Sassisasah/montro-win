using System.Reflection;

namespace Montro.Core;

/// <summary>Version und Herkunft (für Info-Fenster, Log und später /api/diag).</summary>
public static class AppInfo
{
    public static string Version
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v == null ? "?" : $"{v.Major}.{v.Minor}";
        }
    }
}
