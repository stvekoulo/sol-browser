using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Browser.Core;

public sealed record ImportProfile(string Folder, string Name);
public sealed record ImportSource(string Id, string Name, string UserData, IReadOnlyList<ImportProfile> Profiles);
public sealed record ImportedVisit(string Url, string Title, int Count, DateTimeOffset Last);

/// <summary>
/// Reprise des favoris et de l'historique d'un autre navigateur installé sur l'ordinateur. Tous ceux visés ici sont
/// bâtis sur Chromium et rangent leurs données de la même façon : un fichier « Bookmarks » (JSON) et une base
/// « History » par profil. Les mots de passe et les cookies ne sont pas repris : ces navigateurs les chiffrent pour
/// eux seuls.
/// </summary>
public static class BrowserImport
{
    // Identifiant, nom affiché, dossier des données sous AppData\Local (ou Roaming pour Opera).
    private static readonly (string Id, string Name, string Path, bool Roaming)[] Known =
    [
        ("chrome", "Google Chrome", @"Google\Chrome\User Data", false),
        ("edge", "Microsoft Edge", @"Microsoft\Edge\User Data", false),
        ("brave", "Brave", @"BraveSoftware\Brave-Browser\User Data", false),
        ("comet", "Comet", @"Perplexity\Comet\User Data", false),
        ("arc", "Arc", @"Packages\TheBrowserCompany.Arc_ttt1ap7aakyb4\LocalCache\Local\Arc\User Data", false),
        ("vivaldi", "Vivaldi", @"Vivaldi\User Data", false),
        ("opera", "Opera", @"Opera Software\Opera Stable", true),
        ("chromium", "Chromium", @"Chromium\User Data", false),
    ];

    /// <summary>Les navigateurs trouvés sur l'ordinateur, avec leurs profils qui ont quelque chose à reprendre.</summary>
    public static IReadOnlyList<ImportSource> Detect(string localAppData, string roamingAppData)
    {
        var found = new List<ImportSource>();
        foreach (var (id, name, path, roaming) in Known)
        {
            var userData = Path.Combine(roaming ? roamingAppData : localAppData, path);
            if (!Directory.Exists(userData)) continue;
            var names = ProfileNames(userData);
            var profiles = new List<ImportProfile>();
            // Opera range son seul profil à la racine ; les autres dans « Default », « Profile 1 »…
            if (HasData(userData)) profiles.Add(new ImportProfile("", name));
            foreach (var dir in Directory.GetDirectories(userData).Order(StringComparer.OrdinalIgnoreCase))
            {
                var folder = Path.GetFileName(dir);
                if ((folder == "Default" || folder.StartsWith("Profile ", StringComparison.Ordinal)) && HasData(dir))
                    profiles.Add(new ImportProfile(folder, names.GetValueOrDefault(folder, folder)));
            }
            if (profiles.Count > 0) found.Add(new ImportSource(id, name, userData, profiles));
        }
        return found;
    }

    private static bool HasData(string dir) => File.Exists(Path.Combine(dir, "Bookmarks")) || File.Exists(Path.Combine(dir, "History"));

    // Noms donnés par l'utilisateur à ses profils, rangés dans « Local State ».
    private static Dictionary<string, string> ProfileNames(string userData)
    {
        var names = new Dictionary<string, string>();
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(userData, "Local State")));
            if (doc.RootElement.TryGetProperty("profile", out var profile) && profile.TryGetProperty("info_cache", out var cache))
                foreach (var entry in cache.EnumerateObject())
                    if (entry.Value.TryGetProperty("name", out var name) && name.GetString() is { Length: > 0 } text) names[entry.Name] = text;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { /* pas de noms : ceux des dossiers */ }
        return names;
    }

    /// <summary>Le dossier d'un profil détecté ; null si le navigateur ou le profil n'est pas de ceux qui ont été trouvés.</summary>
    public static string? ProfileDir(IReadOnlyList<ImportSource> sources, string id, string folder) =>
        sources.FirstOrDefault(s => s.Id == id) is { } source && source.Profiles.Any(p => p.Folder == folder)
            ? Path.Combine(source.UserData, folder) : null;

    /// <summary>Les favoris d'un profil, dans l'ordre de ses dossiers. Seules les adresses web sont gardées.</summary>
    public static IReadOnlyList<Bookmark> ReadBookmarks(string profileDir)
    {
        var list = new List<Bookmark>();
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(profileDir, "Bookmarks")));
            if (doc.RootElement.TryGetProperty("roots", out var roots))
                foreach (var root in roots.EnumerateObject()) Walk(root.Value, list, depth: 0);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { /* pas de favoris lisibles */ }
        return list;
    }

    private static void Walk(JsonElement node, List<Bookmark> list, int depth)
    {
        if (node.ValueKind != JsonValueKind.Object || depth > 32) return;
        if (node.TryGetProperty("type", out var type) && type.GetString() == "url"
            && node.TryGetProperty("url", out var url) && url.GetString() is { } address && IsWeb(address))
            list.Add(new Bookmark(address, node.TryGetProperty("name", out var name) ? name.GetString() ?? address : address));
        if (node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
            foreach (var child in children.EnumerateArray()) Walk(child, list, depth + 1);
    }

    /// <summary>
    /// Les pages les plus récemment visitées d'un profil. La base est copiée avant d'être lue : le navigateur
    /// d'origine la garde verrouillée tant qu'il est ouvert.
    /// </summary>
    public static IReadOnlyList<ImportedVisit> ReadHistory(string profileDir, int limit, string tempDir)
    {
        var list = new List<ImportedVisit>();
        var source = Path.Combine(profileDir, "History");
        if (!File.Exists(source)) return list;
        Directory.CreateDirectory(tempDir);
        var copy = Path.Combine(tempDir, $"import-{Guid.NewGuid():N}.db");
        try
        {
            File.Copy(source, copy, overwrite: true);
            using (var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = copy, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
            {
                db.Open();
                using var cmd = db.CreateCommand();
                cmd.CommandText = "SELECT url, title, visit_count, last_visit_time FROM urls WHERE hidden = 0 ORDER BY last_visit_time DESC LIMIT $limit";
                cmd.Parameters.AddWithValue("$limit", limit);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var url = r.GetString(0);
                    if (!IsWeb(url)) continue;
                    list.Add(new ImportedVisit(url, r.IsDBNull(1) ? url : r.GetString(1), Math.Max(1, r.GetInt32(2)), FromChromeTime(r.GetInt64(3))));
                }
            }
        }
        catch (Exception e) when (e is IOException or SqliteException or UnauthorizedAccessException) { /* base illisible : pas d'historique repris */ }
        finally
        {
            try { File.Delete(copy); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return list;
    }

    private static bool IsWeb(string url) =>
        url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase);

    // Chromium compte en microsecondes depuis le 1er janvier 1601.
    private static readonly DateTimeOffset ChromeEpoch = new(1601, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static DateTimeOffset FromChromeTime(long microseconds)
    {
        try { return microseconds <= 0 ? DateTimeOffset.UnixEpoch : ChromeEpoch.AddTicks(microseconds * 10); }
        catch (ArgumentOutOfRangeException) { return DateTimeOffset.UnixEpoch; }
    }
}
