using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Browser.Core;

public static partial class PasswordImport
{
    /// <summary>Ajoute une importation au profil principal sans modifier les attentes des sessions nommées.</summary>
    public static void Queue(string pendingFile, IReadOnlyList<ImportedLogin> logins)
    {
        if (logins.Count == 0 || !OperatingSystem.IsWindows()) return;
        var bytes = PasswordBackup.PreparePending(pendingFile, JsonSerializer.SerializeToUtf8Bytes(logins));
        Directory.CreateDirectory(Path.GetDirectoryName(pendingFile)!);
        PasswordBackup.SavePending(pendingFile, bytes);
    }

    /// <summary>Écrit avant l'ouverture du moteur ; chaque session conserve ses mots de passe et son attente.</summary>
    public static int ApplyPending(string pendingFile, string engineData)
    {
        if (!File.Exists(pendingFile)) return 0;
        var pending = PasswordBackup.Pending(pendingFile);
        // Sur un nouveau PC, réserve les sessions avant même que le moteur ne crée sa clé et ses bases.
        foreach (var profile in pending.Profiles.Where(p => p.SessionId.Length > 0 && p.Logins.Count > 0))
            Directory.CreateDirectory(PasswordBackup.ProfileDirectory(engineData, profile.SessionId));
        if (Key(engineData) is not { } key) return 0;
        var remaining = new List<PasswordBackupProfile>();
        var added = 0;
        foreach (var profile in pending.Profiles)
        {
            if (profile.Logins.Count == 0) continue;
            var target = Path.Combine(PasswordBackup.ProfileDirectory(engineData, profile.SessionId), "Login Data");
            if (!File.Exists(target))
            {
                // Réserve aussi les anciennes sessions sans site, sinon leur nettoyage les prendrait pour inutilisées.
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                remaining.Add(profile); continue;
            }
            var result = ApplyProfile(target, key, profile.Logins);
            added += result.Added;
            if (!result.Applied) remaining.Add(profile);
        }
        if (remaining.Count == 0) File.Delete(pendingFile);
        else PasswordBackup.SavePending(pendingFile, PasswordBackup.Protect(new(1, remaining)));
        return added;
    }

    private static (bool Applied, int Added) ApplyProfile(string target, byte[] key, IReadOnlyList<ImportedLogin> logins)
    {
        var added = 0;
        try
        {
            using var db = Open(target, SqliteOpenMode.ReadWrite);
            // Les colonnes de cette table changent d'une version du moteur à l'autre : on remplit celles qu'on connaît,
            // et on donne une valeur neutre aux autres colonnes obligatoires.
            var columns = new List<(string Name, string Type)>();
            using (var info = db.CreateCommand())
            {
                info.CommandText = "PRAGMA table_info(logins)";
                using var r = info.ExecuteReader();
                while (r.Read())
                    if (r.GetInt32(5) == 0 && (Known.Contains(r.GetString(1)) || (r.GetInt32(3) == 1 && r.IsDBNull(4))))
                        columns.Add((r.GetString(1), r.GetString(2).ToUpperInvariant()));
            }
            if (!Known.All(k => columns.Any(c => c.Name == k))) return (false, 0); // table inattendue : on n'y touche pas
            var now = (DateTimeOffset.UtcNow - new DateTimeOffset(1601, 1, 1, 0, 0, 0, TimeSpan.Zero)).Ticks / 10;
            using var tx = db.BeginTransaction();
            foreach (var login in logins)
            {
                using var cmd = db.CreateCommand();
                cmd.Transaction = tx;
                // La table interdit deux fois le même identifiant pour le même formulaire : celui du moteur est gardé.
                cmd.CommandText = $"INSERT OR IGNORE INTO logins ({string.Join(", ", columns.Select(c => c.Name))}) VALUES ({string.Join(", ", columns.Select((_, i) => $"$p{i}"))})";
                for (var i = 0; i < columns.Count; i++)
                    cmd.Parameters.AddWithValue($"$p{i}", columns[i].Name switch
                    {
                        "origin_url" => login.OriginUrl,
                        "action_url" => login.ActionUrl,
                        "username_element" => login.UsernameElement,
                        "username_value" => login.Username,
                        "password_element" => login.PasswordElement,
                        "password_value" => Encrypt(key, login.Password),
                        "signon_realm" => login.SignonRealm,
                        "date_created" => now,
                        _ => columns[i].Type switch { "BLOB" => Array.Empty<byte>(), "INTEGER" => 0L, _ => "" },
                    });
                added += cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        catch (Exception e) when (e is IOException or SqliteException or UnauthorizedAccessException) { return (false, 0); } // base occupée : on réessaiera
        finally { SqliteConnection.ClearAllPools(); }
        return (true, added);
    }

}
