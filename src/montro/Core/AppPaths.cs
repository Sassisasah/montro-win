namespace Montro.Core;

/// <summary>
/// Ablageorte (entsprechen am Mac ~/Library/Application Support/montro):
/// Zustand unter %APPDATA%\montro, Vorschaubilder und Log unter %LOCALAPPDATA%\montro.
/// </summary>
public static class AppPaths
{
    public static string RoamingDir => Ensure(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "montro"));

    public static string LocalDir => Ensure(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "montro"));

    public static string StateFile => Path.Combine(RoamingDir, "state.json");

    public static string LogFile => Path.Combine(LocalDir, "montro.log");

    public static string ThumbsDir => Ensure(Path.Combine(LocalDir, "thumbs"));

    public static string ThumbsFor(string id) => Ensure(Path.Combine(ThumbsDir, id));

    private static string Ensure(string dir)
    {
        Directory.CreateDirectory(dir);
        return dir;
    }
}
