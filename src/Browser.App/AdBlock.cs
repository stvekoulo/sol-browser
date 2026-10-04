using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using Browser.Core;
using Microsoft.Web.WebView2.Core;

namespace Browser.App;

/// <summary>
/// Blocage des publicités et des traqueurs : uBlock Origin Lite (extension libre, GPLv3), livré avec le navigateur,
/// copié dans le dossier de données commun aux profils, installé dans chaque profil au démarrage et mis à jour depuis
/// son dépôt officiel. Le navigateur n'a pas de blocage à lui : sans l'extension, rien n'est bloqué.
/// </summary>
public static class AdBlock
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);

    // ---------- Moteur

    // Moteur commun à toutes les vues du navigateur, avec les extensions autorisées.
    // Toutes les vues d'un même dossier de données doivent être créées avec le même moteur.
    private static Task<CoreWebView2Environment>? s_environment;

    public static Task<CoreWebView2Environment> EnvironmentAsync() => s_environment ??= CreateEnvironmentAsync();

    private static async Task<CoreWebView2Environment> CreateEnvironmentAsync() =>
        await CoreWebView2Environment.CreateWithOptionsAsync(null, Environment.GetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER"),
            new CoreWebView2EnvironmentOptions { AreBrowserExtensionsEnabled = true });

    // ---------- uBlock Origin Lite

    // Un seul dossier, toujours le même : l'identité de l'extension (et donc ses réglages) dépend de son chemin.
    private static string Root => Path.Combine(AppPaths.BaseDir, "extensions");
    private static string Folder => Path.Combine(Root, "ubol");
    private static string Pending => Path.Combine(Root, "ubol-next");   // mise à jour téléchargée, posée au prochain démarrage
    private static string Registered => Path.Combine(AppPaths.DataDir, "ublock-path.txt"); // chemin installé dans ce profil
    private static string Bundled => Path.Combine(AppContext.BaseDirectory, "extensions", "ubol");

    private static readonly TaskCompletionSource<bool> s_installed = new();

    /// <summary>Vrai une fois l'extension en place dans le profil ; faux si elle manque ou a été refusée.</summary>
    public static Task<bool> ExtensionInstalled => s_installed.Task;
    public static string? ExtensionId { get; private set; }
    public static string? ExtensionVersion => VersionOf(Folder)?.ToString();

    private static Version? VersionOf(string folder)
    {
        try
        {
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "manifest.json")));
            return Version.TryParse(manifest.RootElement.GetProperty("version").GetString(), out var version) ? version : null;
        }
        catch (Exception e) when (e is IOException or JsonException or KeyNotFoundException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>
    /// Avant le démarrage du moteur : pose une mise à jour téléchargée, ou la version livrée avec le navigateur si elle
    /// est plus récente que celle du dossier (ou s'il est vide). Un autre profil ouvert peut tenir les fichiers : on
    /// réessaiera au prochain démarrage.
    /// </summary>
    public static void PrepareExtension()
    {
        try
        {
            var current = VersionOf(Folder);
            var source = VersionOf(Pending) is { } pending && (current is null || pending > current) ? Pending
                : VersionOf(Bundled) is { } bundled && (current is null || bundled > current) ? Bundled : null;
            if (source is not null)
            {
                if (Directory.Exists(Folder)) Directory.Delete(Folder, recursive: true);
                CopyFolder(source, Folder);
            }
            if (Directory.Exists(Pending)) Directory.Delete(Pending, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { DebugLog.Write($"extension non préparée : {e.Message}"); }
    }

    private static void CopyFolder(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: true);
        foreach (var folder in Directory.GetDirectories(from)) CopyFolder(folder, Path.Combine(to, Path.GetFileName(folder)));
    }

    /// <summary>Installe l'extension dans le profil si elle n'y est pas (ou y est sous un ancien chemin), puis l'active ou la coupe.</summary>
    public static async Task ApplyExtensionAsync(CoreWebView2Profile profile, bool enabled)
    {
        var active = false;
        try
        {
            var installed = (await profile.GetBrowserExtensionsAsync()).FirstOrDefault(e => e.Name.StartsWith("uBlock", StringComparison.OrdinalIgnoreCase));
            // Installée depuis un autre dossier (ancienne version du navigateur) : elle est réinstallée depuis le bon.
            if (installed is not null && !(File.Exists(Registered) && File.ReadAllText(Registered) == Folder))
            {
                await installed.RemoveAsync();
                installed = null;
            }
            if (installed is null)
            {
                if (VersionOf(Folder) is null) return;
                installed = await profile.AddBrowserExtensionAsync(Folder);
                File.WriteAllText(Registered, Folder);
            }
            if (installed.IsEnabled != enabled) await installed.EnableAsync(enabled);
            ExtensionId = installed.Id;
            active = true;
            DebugLog.Write($"extension {installed.Name} ({installed.Id}) active={enabled}");
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException or ArgumentException or IOException or NotImplementedException or UnauthorizedAccessException)
        {
            DebugLog.Write($"extension refusée : {e.Message}");
        }
        finally { s_installed.TrySetResult(active); }
    }

    // Mise à jour : uniquement depuis le dépôt officiel d'uBlock Origin Lite, en https, au plus une fois par semaine.
    private const string Releases = "https://api.github.com/repos/uBlockOrigin/uBOL-home/releases/latest";
    private const string DownloadPrefix = "https://github.com/uBlockOrigin/uBOL-home/releases/download/";
    private const int MaxPackage = 40_000_000;
    private static string Checked => Path.Combine(Root, "checked.txt");

    /// <summary>
    /// Cherche une version plus récente et la télécharge dans un dossier d'attente ; elle sera posée au prochain
    /// démarrage. Rien n'est installé qui ne ressemble pas exactement à ce qui est attendu (nom, taille, version, contenu).
    /// </summary>
    public static async Task UpdateExtensionAsync()
    {
        if (VersionOf(Folder) is not { } current) return;
        var download = Path.Combine(Root, "ubol-download");
        try
        {
            if (File.Exists(Checked) && DateTime.UtcNow - File.GetLastWriteTimeUtc(Checked) < MaxAge) return;
            File.WriteAllText(Checked, DateTime.UtcNow.ToString("O"));

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60), MaxResponseContentBufferSize = MaxPackage };
            http.DefaultRequestHeaders.Add("User-Agent", "Sol-browser");
            using var release = JsonDocument.Parse(await http.GetStringAsync(Releases));
            var tag = release.RootElement.GetProperty("tag_name").GetString() ?? "";
            if (!Regex.IsMatch(tag, @"^\d{4}\.\d{1,4}\.\d{1,4}$") || !Version.TryParse(tag, out var latest) || latest <= current) return;
            var asset = release.RootElement.GetProperty("assets").EnumerateArray().FirstOrDefault(a => a.GetProperty("name").GetString() == $"uBOLite_{tag}.edge.zip");
            if (asset.ValueKind != JsonValueKind.Object) return;
            var url = asset.GetProperty("browser_download_url").GetString() ?? "";
            if (!url.StartsWith(DownloadPrefix, StringComparison.Ordinal) || asset.GetProperty("size").GetInt64() > MaxPackage) return;

            var bytes = await http.GetByteArrayAsync(url);
            if (Directory.Exists(download)) Directory.Delete(download, recursive: true);
            using (var zip = new ZipArchive(new MemoryStream(bytes))) zip.ExtractToDirectory(download); // refuse tout chemin sortant du dossier
            // Le paquet doit être l'extension attendue, dans la version annoncée.
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(download, "manifest.json")));
            var root = manifest.RootElement;
            if (root.GetProperty("manifest_version").GetInt32() != 3 || root.GetProperty("version").GetString() != tag
                || !root.TryGetProperty("declarative_net_request", out _) || !File.Exists(Path.Combine(download, "LICENSE.txt"))) return;
            if (Directory.Exists(Pending)) Directory.Delete(Pending, recursive: true);
            Directory.Move(download, Pending);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or JsonException
            or KeyNotFoundException or InvalidOperationException or InvalidDataException)
        {
            DebugLog.Write($"mise à jour de l'extension abandonnée : {e.Message}");
        }
        finally
        {
            try { if (Directory.Exists(download)) Directory.Delete(download, recursive: true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}
