namespace Montro.Core;

/// <summary>
/// Log-Datei %LOCALAPPDATA%\montro\montro.log (threadsicher). Die letzten Zeilen bleiben im
/// Speicher – für das Testfenster und später für GET /api/diag (letzte 60 Zeilen).
/// </summary>
public static class DebugLog
{
    private const long MaxBytes = 2 * 1024 * 1024;
    private static readonly object Gate = new();
    private static readonly LinkedList<string> Recent = new();
    private static readonly System.Text.Encoding Utf8WithBom = new System.Text.UTF8Encoding(true);

    /// <summary>Neue Zeile (wird auf dem Thread des Schreibers ausgelöst).</summary>
    public static event Action<string>? LineWritten;

    public static void Write(string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}";
        lock (Gate)
        {
            Recent.AddLast(line);
            while (Recent.Count > 500) Recent.RemoveFirst();
            try
            {
                var path = AppPaths.LogFile;
                var info = new FileInfo(path);
                if (info.Exists && info.Length > MaxBytes)
                {
                    // einfache Rotation: alte Datei als .1 behalten
                    File.Copy(path, path + ".1", overwrite: true);
                    File.WriteAllText(path, "");
                }
                // UTF-8 mit BOM: sonst zeigen Windows PowerShell 5.1 und manche Editoren Umlaute falsch
                File.AppendAllText(path, line + Environment.NewLine, Utf8WithBom);
            }
            catch
            {
                // Log darf nie die App stören
            }
        }
        try { LineWritten?.Invoke(line); } catch { }
    }

    public static string[] Tail(int count)
    {
        lock (Gate)
        {
            return Recent.Skip(Math.Max(0, Recent.Count - count)).ToArray();
        }
    }
}
