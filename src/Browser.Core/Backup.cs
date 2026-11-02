using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Browser.Core;

/// <summary>
/// Fichier de sauvegarde d'un profil : ses fichiers, compressés puis chiffrés (AES-256-GCM) avec une clé tirée de la
/// clé de sauvegarde choisie par l'utilisateur (PBKDF2-SHA256). Sans cette clé, le fichier est illisible ; Sol ne la
/// garde nulle part. La restauration est d'abord mise de côté, puis appliquée au démarrage suivant, quand aucun
/// fichier du profil n'est encore ouvert.
/// </summary>
public static class Backup
{
    /// <summary>Les fichiers d'un profil qu'une sauvegarde contient et qu'une restauration a le droit de remplacer.</summary>
    public static readonly string[] ProfileFiles = ["settings.json", "session.json", "permissions.json", "browser.db", "notes.json", "notes-assets.json",
        "tab-sessions.json", "tab-sessions.json.sites.json", "workspaces.json", "downloads.json", "extension-state.json", "ai-categories.json", "wallpaper.jpg"];
    /// <summary>Dossiers repris en entier : les extensions installées et les autorisations propres à chaque session d'onglets.</summary>
    public static readonly string[] ProfileFolders = ["extensions", "session-permissions"];

    /// <summary>Un nom de la sauvegarde qu'une restauration a le droit d'écrire : un fichier connu, ou un chemin simple dans un dossier connu.</summary>
    public static bool Allowed(string name)
    {
        if (ProfileFiles.Contains(name) || name == Passwords) return true;
        var parts = name.Split('/');
        return parts.Length > 1 && ProfileFolders.Contains(parts[0])
            && parts.All(p => p.Length > 0 && p is not ("." or "..") && p.IndexOfAny(Path.GetInvalidFileNameChars()) < 0);
    }

    /// <summary>Les fichiers du profil à sauvegarder, sauf la base (copiée à part pendant qu'elle est ouverte) et les mots de passe.</summary>
    public static Dictionary<string, byte[]> Collect(string dataDir)
    {
        var files = new Dictionary<string, byte[]>();
        foreach (var name in ProfileFiles)
            if (name != "browser.db" && File.Exists(Path.Combine(dataDir, name))) files[name] = File.ReadAllBytes(Path.Combine(dataDir, name));
        foreach (var folder in ProfileFolders)
        {
            var root = Path.Combine(dataDir, folder);
            if (!Directory.Exists(root)) continue;
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                files[folder + "/" + Path.GetRelativePath(root, file).Replace('\\', '/')] = File.ReadAllBytes(file);
        }
        return files;
    }
    public const string Passwords = "passwords.json";
    public const int MinKeyLength = 8;

    private static readonly byte[] Magic = "SOLBACKUP1"u8.ToArray();
    private const int Salt = 16, Nonce = 12, Tag = 16, Iterations = 600_000;

    public static byte[] Seal(IReadOnlyDictionary<string, byte[]> files, string key)
    {
        using var plain = new MemoryStream();
        using (var zip = new ZipArchive(plain, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, bytes) in files)
            {
                using var entry = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
                entry.Write(bytes);
            }
        var data = plain.ToArray();
        var output = new byte[Magic.Length + Salt + Nonce + Tag + data.Length];
        Magic.CopyTo(output, 0);
        var salt = output.AsSpan(Magic.Length, Salt);
        var nonce = output.AsSpan(Magic.Length + Salt, Nonce);
        RandomNumberGenerator.Fill(salt);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(Derive(key, salt), Tag);
        // L'en-tête est authentifié avec le contenu : un fichier modifié est refusé en entier.
        aes.Encrypt(nonce, data, output.AsSpan(Magic.Length + Salt + Nonce + Tag), output.AsSpan(Magic.Length + Salt + Nonce, Tag), Magic);
        return output;
    }

    /// <summary>Le contenu d'une sauvegarde ; null si la clé n'est pas la bonne ou si le fichier n'en est pas une.</summary>
    public static Dictionary<string, byte[]>? Open(byte[] file, string key)
    {
        var header = Magic.Length + Salt + Nonce + Tag;
        if (file.Length < header || !file.AsSpan(0, Magic.Length).SequenceEqual(Magic)) return null;
        var data = new byte[file.Length - header];
        try
        {
            using var aes = new AesGcm(Derive(key, file.AsSpan(Magic.Length, Salt)), Tag);
            aes.Decrypt(file.AsSpan(Magic.Length + Salt, Nonce), file.AsSpan(header), file.AsSpan(Magic.Length + Salt + Nonce, Tag), data, Magic);
            var files = new Dictionary<string, byte[]>();
            using var zip = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read);
            foreach (var entry in zip.Entries)
            {
                using var content = new MemoryStream();
                using (var stream = entry.Open()) stream.CopyTo(content);
                files[entry.FullName] = content.ToArray();
            }
            return files;
        }
        catch (Exception e) when (e is CryptographicException or InvalidDataException) { return null; }
    }

    private static byte[] Derive(string key, ReadOnlySpan<byte> salt) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(key), salt, Iterations, HashAlgorithmName.SHA256, 32);

    private static string Staging(string dataDir) => Path.Combine(dataDir, "restore");

    /// <summary>Met de côté les fichiers de profil d'une sauvegarde ouverte ; ils remplaceront ceux du profil au prochain démarrage.</summary>
    public static void Stage(string dataDir, IReadOnlyDictionary<string, byte[]> files)
    {
        var dir = Staging(dataDir);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        // Seuls les noms connus sont écrits : le contenu d'une sauvegarde ne choisit pas où il va.
        foreach (var (name, bytes) in files)
        {
            if (name == Passwords || !Allowed(name)) continue;
            var target = Path.Combine(dir, name.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, bytes);
        }
    }

    /// <summary>Applique une restauration mise de côté. À appeler au démarrage, avant d'ouvrir le moindre fichier du profil.</summary>
    public static bool ApplyStaged(string dataDir)
    {
        var dir = Staging(dataDir);
        if (!Directory.Exists(dir)) return false;
        try
        {
            foreach (var name in ProfileFiles)
                if (File.Exists(Path.Combine(dir, name))) File.Move(Path.Combine(dir, name), Path.Combine(dataDir, name), overwrite: true);
            // Journaux de l'ancienne base : ils ne vont pas avec la base restaurée.
            foreach (var journal in new[] { "browser.db-wal", "browser.db-shm" }) File.Delete(Path.Combine(dataDir, journal));
            // Un dossier sauvegardé remplace le dossier en place ; absent de la sauvegarde, celui du profil reste.
            foreach (var folder in ProfileFolders)
            {
                var staged = Path.Combine(dir, folder);
                if (!Directory.Exists(staged)) continue;
                var live = Path.Combine(dataDir, folder);
                if (Directory.Exists(live)) Directory.Delete(live, recursive: true);
                Directory.Move(staged, live);
            }
            Directory.Delete(dir, recursive: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; } // profil occupé : au prochain démarrage
    }
}
