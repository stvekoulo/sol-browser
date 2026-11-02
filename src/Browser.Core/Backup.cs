using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Browser.Core;

/// <summary>
/// Fichier de sauvegarde d'un profil : ses fichiers, compressés puis chiffrés (AES-256-GCM) avec une clé tirée de la
/// clé de sauvegarde choisie par l'utilisateur (PBKDF2-SHA256). Sans cette clé, le fichier est illisible ; Sol ne la
/// garde nulle part. La restauration est d'abord mise de côté, puis appliquée au démarrage suivant, quand aucun
/// fichier du profil n'est encore ouvert.
/// </summary>
public static partial class Backup
{
    /// <summary>Les fichiers d'un profil qu'une sauvegarde contient et qu'une restauration a le droit de remplacer.</summary>
    public static readonly string[] ProfileFiles = ["settings.json", "session.json", "permissions.json", "browser.db", "notes.json", "notes-assets.json",
        "tab-sessions.json", "tab-sessions.json.sites.json", "workspaces.json", "downloads.json", "extension-state.json", "ai-categories.json", "wallpaper.jpg"];
    /// <summary>Dossiers repris en entier : les extensions installées et les autorisations propres à chaque session d'onglets.</summary>
    public static readonly string[] ProfileFolders = ["extensions", "session-permissions"];

    /// <summary>Un nom de la sauvegarde qu'une restauration a le droit d'écrire : un fichier connu, ou un chemin simple dans un dossier connu.</summary>
    public static bool Allowed(string name)
    {
        if (ProfileFiles.Contains(name) || name is Passwords or Manifest) return true;
        var parts = name.Split('/');
        return parts.Length > 1 && ProfileFolders.Contains(parts[0])
            && parts.All(p => p.Length > 0 && p is not ("." or "..") && p.IndexOfAny(Path.GetInvalidFileNameChars()) < 0);
    }

    /// <summary>Les fichiers du profil à sauvegarder, sauf la base (copiée à part pendant qu'elle est ouverte) et les mots de passe.</summary>
    public static Dictionary<string, byte[]> Collect(string dataDir)
    {
        var files = new Dictionary<string, byte[]>();
        var directories = new List<string>();
        foreach (var name in ProfileFiles)
            if (name != "browser.db" && File.Exists(Path.Combine(dataDir, name))) files[name] = File.ReadAllBytes(Path.Combine(dataDir, name));
        foreach (var folder in ProfileFolders)
        {
            var root = Path.Combine(dataDir, folder);
            if (!Directory.Exists(root)) continue;
            directories.AddRange(Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                .Select(path => folder + "/" + Path.GetRelativePath(root, path).Replace('\\', '/')));
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                files[folder + "/" + Path.GetRelativePath(root, file).Replace('\\', '/')] = File.ReadAllBytes(file);
        }
        files[Manifest] = JsonSerializer.SerializeToUtf8Bytes(new ProfileManifest(1, ProfileFiles, ProfileFolders, directories.ToArray()));
        return files;
    }
    public const string Passwords = "passwords.json";
    public const int MinKeyLength = 8;
    public const int MaxSealedBytes = 256 * 1024 * 1024;
    public const int MaxEntryBytes = 256 * 1024 * 1024;
    public const int MaxExpandedBytes = 512 * 1024 * 1024;
    public const int MaxEntries = 10_000;

    private static readonly byte[] Magic = "SOLBACKUP1"u8.ToArray();
    private const int Salt = 16, Nonce = 12, Tag = 16, Iterations = 600_000;

    public static byte[] Seal(IReadOnlyDictionary<string, byte[]> files, string key)
    {
        long expanded = 0;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (files.Count > MaxEntries) throw new InvalidDataException("La sauvegarde contient trop de fichiers.");
        foreach (var (name, bytes) in files)
        {
            if (bytes.Length > MaxEntryBytes || bytes.Length > MaxExpandedBytes - expanded)
                throw new InvalidDataException("Le contenu de la sauvegarde dépasse la taille restaurable.");
            if (Allowed(name) && !names.Add(name)) throw new InvalidDataException("La sauvegarde contient des chemins ambigus.");
            expanded += bytes.Length;
        }
        using var plain = new MemoryStream();
        using (var zip = new ZipArchive(plain, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, bytes) in files)
            {
                using var entry = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
                entry.Write(bytes);
            }
        if (plain.Length > MaxSealedBytes - Magic.Length - Salt - Nonce - Tag)
            throw new InvalidDataException("La sauvegarde dépasse 256 Mo.");
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
        if (file.Length < header || file.Length > MaxSealedBytes || !file.AsSpan(0, Magic.Length).SequenceEqual(Magic)) return null;
        var data = new byte[file.Length - header];
        try
        {
            using var aes = new AesGcm(Derive(key, file.AsSpan(Magic.Length, Salt)), Tag);
            aes.Decrypt(file.AsSpan(Magic.Length + Salt, Nonce), file.AsSpan(header), file.AsSpan(Magic.Length + Salt + Nonce, Tag), data, Magic);
            if (!BoundedZipDirectory(data)) return null;
            var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            using var zip = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read);
            if (zip.Entries.Count > MaxEntries) return null;
            long declared = 0;
            foreach (var entry in zip.Entries.Where(entry => Allowed(entry.FullName)))
            {
                if (entry.Length > MaxEntryBytes || entry.Length > MaxExpandedBytes - declared) return null;
                declared += entry.Length;
            }
            long total = 0;
            var buffer = new byte[81920];
            foreach (var entry in zip.Entries)
            {
                if (!Allowed(entry.FullName)) continue;
                if (entry.Length > MaxEntryBytes || entry.Length > MaxExpandedBytes - total || files.ContainsKey(entry.FullName)) return null;
                using var content = new MemoryStream();
                using var stream = entry.Open();
                int count;
                while ((count = stream.Read(buffer)) != 0)
                {
                    total += count;
                    if (content.Length + count > MaxEntryBytes || total > MaxExpandedBytes) return null;
                    content.Write(buffer, 0, count);
                }
                if (content.Length != entry.Length) return null;
                files.Add(entry.FullName, content.ToArray());
            }
            return files;
        }
        catch (Exception e) when (e is CryptographicException or InvalidDataException) { return null; }
    }

    private static byte[] Derive(string key, ReadOnlySpan<byte> salt) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(key), salt, Iterations, HashAlgorithmName.SHA256, 32);

}
