using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Browser.Core;

public static partial class Backup
{
    public const string Manifest = "profile-manifest.json";
    private const string PendingPasswords = "passwords.pending";
    private sealed record ProfileManifest(int Version, string[] Files, string[] Folders, string[] Directories);
    private sealed record Ready(string[] Targets, Dictionary<string, string> Hashes);
    private static string Staging(string dataDir) => Path.Combine(dataDir, "restore");
    private static string LocalPath(string root, string name) => Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar));
    private static bool RestoreTarget(string name) => ProfileFiles.Contains(name) || ProfileFolders.Contains(name)
        || name is PendingPasswords or "browser.db-wal" or "browser.db-shm";

    /// <summary>Publie uniquement une préparation complète ; une préparation interrompue ne remplace jamais la précédente.</summary>
    public static void Stage(string dataDir, IReadOnlyDictionary<string, byte[]> files)
    {
        if (Directory.Exists(Path.Combine(dataDir, "restore-transaction")))
            throw new IOException("Une restauration doit être récupérée au démarrage avant d'en préparer une autre.");
        var generation = "stage-" + Guid.NewGuid().ToString("N");
        var temporary = Path.Combine(dataDir, ".restore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var targets = new HashSet<string>(StringComparer.Ordinal);
            var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (files.TryGetValue(Manifest, out var manifestBytes))
            {
                var manifest = JsonSerializer.Deserialize<ProfileManifest>(manifestBytes);
                if (manifest is not { Version: 1, Files: not null, Folders: not null, Directories: not null })
                    throw new InvalidDataException("Manifeste de sauvegarde invalide.");
                targets.UnionWith(manifest.Files.Where(ProfileFiles.Contains));
                targets.UnionWith(manifest.Folders.Where(ProfileFolders.Contains));
                foreach (var folder in targets.Where(ProfileFolders.Contains)) Directory.CreateDirectory(LocalPath(temporary, folder));
                foreach (var directory in manifest.Directories)
                {
                    if (!Allowed(directory + "/.directory") || !targets.Contains(directory.Split('/')[0]))
                        throw new InvalidDataException("Dossier de sauvegarde invalide.");
                    Directory.CreateDirectory(LocalPath(temporary, directory));
                }
            }
            foreach (var (name, bytes) in files)
            {
                if (!Allowed(name) || name is Passwords or Manifest) continue;
                targets.Add(name.Split('/')[0]);
                WritePrepared(temporary, name, bytes, hashes);
            }
            if (files.TryGetValue(Passwords, out var passwords))
            {
                WritePrepared(temporary, PendingPasswords, PasswordBackup.PreparePending(Path.Combine(dataDir, PendingPasswords), passwords), hashes);
                targets.Add(PendingPasswords);
            }
            // Ces journaux font partie de la même transaction uniquement si la base elle-même est remplacée.
            if (targets.Contains("browser.db")) targets.UnionWith(["browser.db-wal", "browser.db-shm"]);
            DurableWrite(Path.Combine(temporary, "ready.json"), JsonSerializer.SerializeToUtf8Bytes(new Ready(targets.ToArray(), hashes)));
            var staging = Staging(dataDir);
            Directory.CreateDirectory(staging);
            Directory.Move(temporary, Path.Combine(staging, generation));
            var pointer = Path.Combine(staging, "current.tmp");
            DurableWrite(pointer, Encoding.UTF8.GetBytes(generation));
            File.Move(pointer, Path.Combine(staging, "current"), overwrite: true);
        }
        finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
    }

    private static void WritePrepared(string root, string name, byte[] bytes, Dictionary<string, string> hashes)
    {
        if (!hashes.TryAdd(name, Convert.ToHexString(SHA256.HashData(bytes)))) throw new InvalidDataException("Entrée dupliquée.");
        var target = LocalPath(root, name);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        DurableWrite(target, bytes);
    }

    private static void DurableWrite(string path, byte[] bytes)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    private static (string Directory, Ready Plan) ReadPrepared(string dataDir)
    {
        var staging = Staging(dataDir);
        var pointer = Path.Combine(staging, "current");
        if (!File.Exists(pointer)) throw new InvalidDataException("Restauration incomplète : aucune préparation validée.");
        var generation = File.ReadAllText(pointer);
        if (!generation.StartsWith("stage-", StringComparison.Ordinal) || !Guid.TryParseExact(generation[6..], "N", out _))
            throw new InvalidDataException("Préparation de restauration invalide.");
        var root = Path.Combine(staging, generation);
        Ready? ready;
        try { ready = JsonSerializer.Deserialize<Ready>(File.ReadAllBytes(Path.Combine(root, "ready.json"))); }
        catch (JsonException e) { throw new InvalidDataException("Préparation de restauration invalide.", e); }
        if (ready is not { Targets: not null, Hashes: not null } || ready.Targets.Any(name => !RestoreTarget(name)))
            throw new InvalidDataException("Cibles de restauration invalides.");
        foreach (var (name, hash) in ready.Hashes)
        {
            if (!(Allowed(name) && name is not (Passwords or Manifest) || name == PendingPasswords)
                || !ready.Targets.Contains(name.Split('/')[0])) throw new InvalidDataException("Fichier préparé invalide.");
            using var content = File.OpenRead(LocalPath(root, name));
            if (Convert.ToHexString(SHA256.HashData(content)) != hash) throw new InvalidDataException("Préparation de restauration endommagée.");
        }
        return (root, ready);
    }
}
