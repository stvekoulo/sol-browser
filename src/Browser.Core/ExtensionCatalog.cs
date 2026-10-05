using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;

namespace Browser.Core;

/// <summary>Une extension décompressée : son dossier, et ce que son manifeste annonce.</summary>
/// <param name="Popup">Page ouverte par son bouton, s'il y en a une.</param>
/// <param name="Options">Page de ses réglages, s'il y en a une.</param>
/// <param name="From">Navigateur où elle a été trouvée ; vide pour une extension déjà dans Sol.</param>
public sealed record ExtensionInfo(string Id, string Name, string Version, string Folder, string? Popup, string? Options, string From = "", string[]? Permissions = null);

/// <summary>
/// Extensions pour navigateurs Chromium. Le moteur de Sol les charge depuis un dossier ; il n'a ni boutique ni mise à
/// jour. Sol reprend donc celles que l'utilisateur a déjà dans ses autres navigateurs (elles y sont rangées sous
/// « Extensions/&lt;identifiant&gt;/&lt;version&gt; ») ou un dossier qu'il désigne, et en garde sa propre copie.
/// </summary>
public static class ExtensionCatalog
{
    /// <summary>Lit le manifeste d'un dossier d'extension ; null si ce n'en est pas un.</summary>
    public static ExtensionInfo? Read(string folder, string id, string from = "")
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "manifest.json")), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || Text(root, "name") is not { Length: > 0 } name) return null;
            if (root.TryGetProperty("theme", out _)) return null; // un thème n'est pas une extension
            var action = root.TryGetProperty("action", out var a) ? a : root.TryGetProperty("browser_action", out var b) ? b : default;
            var options = Text(root, "options_page") ?? (root.TryGetProperty("options_ui", out var ui) ? Text(ui, "page") : null);
            return new ExtensionInfo(id, Localized(folder, root, name), Text(root, "version") ?? "", folder,
                action.ValueKind == JsonValueKind.Object ? Text(action, "default_popup") : null, options, from,
                new[] { "permissions", "host_permissions" }.SelectMany(field => root.TryGetProperty(field, out var list) && list.ValueKind == JsonValueKind.Array
                    ? list.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!).ToArray() : []).Distinct().ToArray());
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    private static string? Text(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    // Un nom écrit « __MSG_clé__ » est dans les traductions de l'extension : celle de sa langue par défaut, sinon l'anglais.
    private static string Localized(string folder, JsonElement manifest, string name)
    {
        if (!name.StartsWith("__MSG_", StringComparison.Ordinal) || !name.EndsWith("__", StringComparison.Ordinal)) return name;
        var key = name[6..^2];
        foreach (var locale in new[] { Text(manifest, "default_locale"), "en", "en_US" }.OfType<string>().Distinct())
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "_locales", locale, "messages.json")));
                foreach (var entry in doc.RootElement.EnumerateObject())
                    if (entry.Name.Equals(key, StringComparison.OrdinalIgnoreCase) && Text(entry.Value, "message") is { Length: > 0 } message) return message;
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        }
        return name;
    }

    /// <summary>Les extensions installées dans les profils d'un navigateur (son dossier « User Data »), dans leur version la plus récente.</summary>
    public static IReadOnlyList<ExtensionInfo> Scan(string userData, string browserName)
    {
        var found = new Dictionary<string, ExtensionInfo>();
        if (!Directory.Exists(userData)) return [];
        foreach (var profile in Folders(userData).Append(userData))
        {
            var extensions = Path.Combine(profile, "Extensions");
            if (!Directory.Exists(extensions)) continue;
            foreach (var dir in Folders(extensions))
            {
                var id = Path.GetFileName(dir);
                if (!IsId(id) || found.ContainsKey(id) || BuiltIn.Contains(id)) continue;
                var latest = Folders(dir).OrderByDescending(v => VersionOf(Path.GetFileName(v))).FirstOrDefault();
                if (latest is not null && Read(latest, id, browserName) is { } info && !info.Name.StartsWith("__MSG_", StringComparison.Ordinal)) found[id] = info;
            }
        }
        return found.Values.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string[] Folders(string path)
    {
        try { return Directory.GetDirectories(path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return []; }
    }

    // Composants que les navigateurs installent pour eux-mêmes : ils n'ont rien à faire dans un autre navigateur.
    private static readonly HashSet<string> BuiltIn =
    [
        "nmmhkkegccagdldgiimedpiccmgmieda", // Chrome Web Store Payments
        "jmjflgjpcpepeafmmgdpfkogkghcpiha", // Edge relevant text changes
        "mhjfbmdgcfjbbpaeojofohoefgiehjai", // lecteur PDF
        "ahfgeienlihckogmohjhadlkjgocpleb", // Web Store
    ];

    /// <summary>Un identifiant d'extension : 32 lettres de « a » à « p ».</summary>
    public static bool IsId(string id) => id.Length == 32 && id.All(c => c is >= 'a' and <= 'p');

    private static Version VersionOf(string folderName) =>
        Version.TryParse(folderName.Split('_')[0], out var v) ? v : new Version(0, 0);

    /// <summary>Copie une extension dans le dossier de Sol (sans les fichiers de vérification du navigateur d'origine) et renvoie la copie.</summary>
    public static string CopyTo(string source, string target)
    {
        var sourcePath = Path.GetFullPath(source);
        var targetPath = Path.GetFullPath(target);
        if (sourcePath.Equals(targetPath, StringComparison.OrdinalIgnoreCase)) return target;
        if (targetPath.StartsWith(sourcePath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("La destination ne peut pas se trouver dans l’extension source.");
        var stage = target + ".stage-" + Guid.NewGuid().ToString("N");
        var backup = target + ".backup-" + Guid.NewGuid().ToString("N");
        try
        {
            Copy(source, stage);
            if (Directory.Exists(target)) Directory.Move(target, backup);
            try { Directory.Move(stage, target); }
            catch { if (Directory.Exists(backup)) Directory.Move(backup, target); throw; }
            if (Directory.Exists(backup)) Directory.Delete(backup, true);
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
        return target;
    }

    public static void EnsureKey(string folder)
    {
        var file = Path.Combine(folder, "manifest.json");
        var manifest = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        if (manifest["key"] is not null) return;
        using var rsa = RSA.Create(2048);
        manifest["key"] = Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo());
        File.WriteAllText(file, manifest.ToJsonString());
    }

    public static string? Icon(string folder)
    {
        try
        {
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "manifest.json")));
            if (!manifest.RootElement.TryGetProperty("icons", out var icons) || icons.ValueKind != JsonValueKind.Object) return null;
            var root = Path.GetFullPath(folder) + Path.DirectorySeparatorChar;
            foreach (var icon in icons.EnumerateObject().OrderByDescending(i => int.TryParse(i.Name, out var size) ? size : 0))
            {
                if (icon.Value.ValueKind != JsonValueKind.String) continue;
                var file = Path.GetFullPath(Path.Combine(folder, icon.Value.GetString()!));
                if (!file.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(file) || new FileInfo(file).Length > 512 * 1024) continue;
                var mime = Path.GetExtension(file).ToLowerInvariant() switch { ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".svg" => "image/svg+xml", ".webp" => "image/webp", _ => null };
                if (mime is not null) return $"data:{mime};base64," + Convert.ToBase64String(File.ReadAllBytes(file));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { }
        return null;
    }

    private static void Copy(string from, string to)
    {
        if (File.GetAttributes(from).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Les liens de dossiers ne sont pas autorisés dans une extension.");
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from))
        {
            if (File.GetAttributes(file).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Les liens de fichiers ne sont pas autorisés dans une extension.");
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: true);
        }
        foreach (var dir in Directory.GetDirectories(from))
            if (Path.GetFileName(dir) != "_metadata") Copy(dir, Path.Combine(to, Path.GetFileName(dir)));
    }
}
