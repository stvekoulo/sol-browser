using Browser.Core;

namespace Browser.App;

/// <summary>Journal de diagnostic, écrit seulement si NAVIGATEUR_DEBUG=1 : ce que la vraie souris et le pavé tactile envoient.</summary>
public static class DebugLog
{
    private static readonly bool Enabled = Environment.GetEnvironmentVariable("NAVIGATEUR_DEBUG") == "1";
    private static readonly string File = Path.Combine(AppPaths.DataDir, "debug.log");

    public static void Write(string line)
    {
        if (!Enabled) return;
        try { System.IO.File.AppendAllText(File, $"{DateTime.Now:HH:mm:ss.fff} {line}\n"); }
        catch (IOException) { /* le journal ne doit jamais gêner le navigateur */ }
    }
}
