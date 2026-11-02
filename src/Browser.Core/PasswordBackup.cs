using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Browser.Core;

public sealed record PasswordBackupProfile(string SessionId, IReadOnlyList<ImportedLogin> Logins);
public sealed record PasswordBackupDocument(int Version, IReadOnlyList<PasswordBackupProfile> Profiles, int Unreadable = 0)
{
    [JsonIgnore] public int Exported => Profiles.Sum(p => p.Logins.Count);
}

/// <summary>Mots de passe portables, rattachés à l'identité stable de leur session ; jamais aux profils privés.</summary>
public static class PasswordBackup
{
    public static PasswordBackupDocument Collect(string engineData, IEnumerable<TabSession> sessions, string tempDir, string? pendingFile = null)
    {
        var profiles = new List<PasswordBackupProfile>();
        var unreadable = 0;
        foreach (var id in sessions.Where(s => s is not null).Select(s => s.Id).Where(TabSessions.ValidId).Distinct().Prepend(""))
        {
            var read = PasswordImport.Read(engineData, ProfileDirectory(engineData, id), tempDir);
            profiles.Add(new(id, read.Logins));
            unreadable += read.Unreadable;
        }
        var result = new PasswordBackupDocument(1, profiles, unreadable);
        if (pendingFile is null) return result;
        var queued = Pending(pendingFile);
        var known = profiles.Select(p => p.SessionId).ToHashSet();
        return Merge(result, queued with { Profiles = queued.Profiles.Where(p => known.Contains(p.SessionId)).ToArray() }) with { Unreadable = unreadable };
    }

    public static string ProfileDirectory(string engineData, string sessionId)
    {
        if (sessionId.Length > 0 && !TabSessions.ValidId(sessionId)) throw new ArgumentException("Session invalide.", nameof(sessionId));
        return Path.Combine(engineData, sessionId.Length == 0 ? "Default" : "WV2Profile_sol-" + sessionId);
    }

    /// <summary>Les sauvegardes historiques sont un tableau réservé au profil principal.</summary>
    public static PasswordBackupDocument Decode(byte[] json)
    {
        using var doc = JsonDocument.Parse(json);
        var result = doc.RootElement.ValueKind == JsonValueKind.Array
            ? new PasswordBackupDocument(1, [new("", JsonSerializer.Deserialize<List<ImportedLogin>>(json) ?? [])])
            : JsonSerializer.Deserialize<PasswordBackupDocument>(json);
        if (result is null || result.Version != 1 || result.Unreadable < 0 || result.Profiles is null
            || result.Profiles.Any(p => p is null || p.SessionId is null || p.SessionId.Length > 0 && !TabSessions.ValidId(p.SessionId)
                || p.Logins is null || p.Logins.Any(l => l is null || l.Password is null || l.OriginUrl is null || l.SignonRealm is null
                    || l.Username is null || l.UsernameElement is null || l.PasswordElement is null || l.ActionUrl is null))
            || result.Profiles.Select(p => p.SessionId).Distinct().Count() != result.Profiles.Count)
            throw new JsonException("Format des mots de passe invalide.");
        return result;
    }

    /// <summary>Prépare l'attente chiffrée pour qu'elle soit validée et appliquée avec la transaction de restauration.</summary>
    public static byte[] PreparePending(string pendingFile, byte[] json) => Protect(Merge(Pending(pendingFile), Decode(json)));

    internal static byte[] MergePending(string pendingFile, byte[] prepared) => Protect(Merge(Pending(pendingFile), Unprotect(prepared)));

    internal static PasswordBackupDocument Pending(string pendingFile) =>
        File.Exists(pendingFile) ? Unprotect(File.ReadAllBytes(pendingFile)) : new(1, []);

    private static PasswordBackupDocument Unprotect(byte[] bytes)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("La protection des mots de passe nécessite Windows.");
        // Une attente corrompue ne doit pas être remplacée silencieusement par une nouvelle importation.
        return Decode(ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser));
    }

    internal static void SavePending(string pendingFile, byte[] bytes)
    {
        var temp = pendingFile + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temp, pendingFile, true);
    }

    internal static byte[] Protect(PasswordBackupDocument document)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("La protection des mots de passe nécessite Windows.");
        return ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(document), null, DataProtectionScope.CurrentUser);
    }

    internal static PasswordBackupDocument Merge(PasswordBackupDocument first, PasswordBackupDocument second) => new(1,
        first.Profiles.Concat(second.Profiles).GroupBy(p => p.SessionId)
            .Select(g => new PasswordBackupProfile(g.Key, g.SelectMany(p => p.Logins).Distinct().ToArray())).ToArray());
}
