namespace Browser.Core;

public static class AppPaths
{
    /// <summary>Dossier commun à tous les profils.</summary>
    public static string BaseDir { get; } =
        Environment.GetEnvironmentVariable("NAVIGATEUR_DATA_DIR") is { Length: > 0 } dir
            ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "projet-navigateur");

    /// <summary>Profil de cette fenêtre, choisi au lancement par <c>--profile=nom</c>.</summary>
    public static string Profile { get; } = Profiles.FromArgs(Environment.GetCommandLineArgs());

    /// <summary>Données du profil : rien n'est partagé entre deux profils.</summary>
    public static string DataDir { get; } = Profiles.DataDir(BaseDir, Profile);

    public static string Settings => Path.Combine(DataDir, "settings.json");
    public static string Session => Path.Combine(DataDir, "session.json");
    public static string Permissions => Path.Combine(DataDir, "permissions.json");
    public static string Database => Path.Combine(DataDir, "browser.db");
    public static string WebData => Path.Combine(DataDir, "webview2");
}
