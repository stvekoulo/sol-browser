namespace Browser.Core;

public static class AppPaths
{
    public static string DataDir { get; } =
        Environment.GetEnvironmentVariable("NAVIGATEUR_DATA_DIR") is { Length: > 0 } dir
            ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "projet-navigateur");

    public static string Settings => Path.Combine(DataDir, "settings.json");
    public static string Session => Path.Combine(DataDir, "session.json");
    public static string Database => Path.Combine(DataDir, "browser.db");
}
