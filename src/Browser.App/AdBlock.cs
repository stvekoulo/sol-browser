using Browser.Core;
using Microsoft.Web.WebView2.Core;

namespace Browser.App;

/// <summary>
/// Liste de blocage des publicités et des traqueurs : la liste livrée avec le navigateur, complétée par une liste
/// publique de domaines, téléchargée au plus une fois par semaine et gardée sur l'ordinateur (commune aux profils).
/// </summary>
public static class AdBlock
{
    // Liste de Peter Lowe (pgl.yoyo.org) : quelques milliers de domaines de publicité et de pistage, un par ligne.
    private const string Source = "https://pgl.yoyo.org/adservers/serverlist.php?hostformat=nohtml&showintro=0&mimetype=plaintext";
    private const int MinDomains = 1000, MaxBytes = 5_000_000;
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);
    private static string Cache => Path.Combine(AppPaths.BaseDir, "blocklist.txt");

    public static BlockList Load()
    {
        IEnumerable<string> downloaded = [];
        try { if (File.Exists(Cache)) downloaded = BlockList.Parse(File.ReadAllText(Cache)).ToList(); }
        catch (IOException) { /* fichier illisible : la liste livrée suffit */ }
        return new BlockList(BlockList.BuiltIn.Concat(downloaded));
    }

    // Moteur commun à toutes les vues du navigateur, avec les extensions autorisées : uBlock Origin Lite en est une.
    // Toutes les vues d'un même dossier de données doivent être créées avec le même moteur.
    private static Task<CoreWebView2Environment>? s_environment;

    public static Task<CoreWebView2Environment> EnvironmentAsync() => s_environment ??= CreateEnvironmentAsync();

    private static async Task<CoreWebView2Environment> CreateEnvironmentAsync() =>
        await CoreWebView2Environment.CreateWithOptionsAsync(null, Environment.GetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER"),
            new CoreWebView2EnvironmentOptions { AreBrowserExtensionsEnabled = true });

    /// <summary>
    /// Installe uBlock Origin Lite (livré à côté de l'exécutable) dans le profil s'il n'y est pas, et l'active ou le
    /// coupe selon le réglage. Sans lui, ou s'il est refusé, le blocage par domaines du navigateur reste seul.
    /// </summary>
    public static async Task ApplyExtensionAsync(CoreWebView2Profile profile, bool enabled)
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "extensions", "ubol");
        try
        {
            var installed = (await profile.GetBrowserExtensionsAsync()).FirstOrDefault(e => e.Name.StartsWith("uBlock", StringComparison.OrdinalIgnoreCase));
            if (installed is null)
            {
                if (!enabled || !File.Exists(Path.Combine(folder, "manifest.json"))) return;
                installed = await profile.AddBrowserExtensionAsync(folder);
            }
            DebugLog.Write($"extension {installed.Name} ({installed.Id}) active={installed.IsEnabled} voulu={enabled}");
            if (installed.IsEnabled != enabled) await installed.EnableAsync(enabled);
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException or ArgumentException or IOException or NotImplementedException)
        {
            DebugLog.Write($"extension refusée : {e.Message}");
        }
    }

    /// <summary>Télécharge la liste si celle de l'ordinateur manque ou a plus d'une semaine. Null : rien de nouveau.</summary>
    public static async Task<BlockList?> UpdateAsync()
    {
        try
        {
            if (File.Exists(Cache) && DateTime.UtcNow - File.GetLastWriteTimeUtc(Cache) < MaxAge) return null;
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20), MaxResponseContentBufferSize = MaxBytes };
            var text = await http.GetStringAsync(Source);
            // Une réponse qui ne ressemble pas à la liste attendue (page d'erreur, liste vidée) ne remplace rien.
            var domains = BlockList.Parse(text).Distinct().ToList();
            if (domains.Count < MinDomains) return null;
            Directory.CreateDirectory(AppPaths.BaseDir);
            File.WriteAllLines(Cache, domains);
            return Load();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            return null; // hors ligne ou disque refusé : on garde la liste en place
        }
    }
}
