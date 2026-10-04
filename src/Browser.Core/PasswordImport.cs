using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Browser.Core;

public sealed record ImportedLogin(string OriginUrl, string ActionUrl, string UsernameElement, string Username,
    string PasswordElement, string Password, string SignonRealm);

/// <summary>
/// Reprise des mots de passe d'un autre navigateur bâti sur Chromium. Chacun range les siens dans une base
/// « Login Data », chiffrés (AES-256-GCM, préfixe « v10 ») avec une clé gardée dans « Local State » et protégée par
/// Windows pour le compte de l'utilisateur. Les entrées qu'un navigateur réserve à lui seul (chiffrement lié à
/// l'application, préfixe « v20 ») ne sont pas lisibles : elles sont comptées, pas contournées.
/// Le moteur de Sol garde sa propre base verrouillée tant qu'il tourne : les mots de passe lus sont mis en attente
/// dans un fichier protégé par Windows, puis écrits au démarrage suivant, avant que le moteur ne s'ouvre.
/// </summary>
public static class PasswordImport
{
    private static readonly string[] Databases = ["Login Data", "Login Data For Account"];
    private static readonly byte[] V10 = "v10"u8.ToArray();
    private const int Nonce = 12, Tag = 16;

    /// <summary>Les mots de passe lisibles d'un profil, et le nombre de ceux qui ne le sont pas.</summary>
    public static (IReadOnlyList<ImportedLogin> Logins, int Unreadable) Read(string userData, string profileDir, string tempDir)
    {
        var list = new List<ImportedLogin>();
        var unreadable = 0;
        var key = Key(userData);
        Directory.CreateDirectory(tempDir);
        foreach (var name in Databases)
        {
            var source = Path.Combine(profileDir, name);
            if (!File.Exists(source)) continue;
            // Copie avant lecture : le navigateur d'origine garde sa base verrouillée tant qu'il est ouvert.
            var copy = Path.Combine(tempDir, $"import-{Guid.NewGuid():N}.db");
            try
            {
                File.Copy(source, copy, overwrite: true);
                using var db = Open(copy, SqliteOpenMode.ReadOnly);
                using var cmd = db.CreateCommand();
                cmd.CommandText = "SELECT origin_url, action_url, username_element, username_value, password_element, password_value, signon_realm FROM logins WHERE blacklisted_by_user = 0";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var secret = r.IsDBNull(5) ? [] : (byte[])r[5];
                    if (secret.Length == 0) continue;
                    if (key is null || Decrypt(key, secret) is not { Length: > 0 } password) { unreadable++; continue; }
                    list.Add(new ImportedLogin(Text(r, 0), Text(r, 1), Text(r, 2), Text(r, 3), Text(r, 4), password, Text(r, 6)));
                }
            }
            catch (Exception e) when (e is IOException or SqliteException or UnauthorizedAccessException) { /* base illisible : rien à reprendre */ }
            finally
            {
                SqliteConnection.ClearAllPools();
                try { File.Delete(copy); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
        return (list, unreadable);
    }

    /// <summary>Met des mots de passe en attente (ajoutés à ceux qui attendent déjà), dans un fichier protégé par Windows.</summary>
    public static void Queue(string pendingFile, IReadOnlyList<ImportedLogin> logins)
    {
        if (logins.Count == 0 || !OperatingSystem.IsWindows()) return;
        var all = Pending(pendingFile).Concat(logins).ToList();
        Directory.CreateDirectory(Path.GetDirectoryName(pendingFile)!);
        File.WriteAllBytes(pendingFile, ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(all), null, DataProtectionScope.CurrentUser));
    }

    private static List<ImportedLogin> Pending(string pendingFile)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(pendingFile)) return [];
        try { return JsonSerializer.Deserialize<List<ImportedLogin>>(ProtectedData.Unprotect(File.ReadAllBytes(pendingFile), null, DataProtectionScope.CurrentUser)) ?? []; }
        catch (Exception e) when (e is CryptographicException or JsonException or IOException) { return []; }
    }

    /// <summary>
    /// Écrit les mots de passe en attente dans la base du moteur (<paramref name="engineData"/> : son dossier
    /// « EBWebView »), chiffrés avec sa clé. À appeler avant l'ouverture du moteur. Un identifiant déjà connu du moteur
    /// est laissé tel quel. Tant que le moteur n'a pas créé sa base, l'attente continue. Renvoie le nombre d'ajouts.
    /// </summary>
    public static int ApplyPending(string pendingFile, string engineData)
    {
        if (!File.Exists(pendingFile)) return 0;
        var target = Path.Combine(engineData, "Default", "Login Data");
        if (!File.Exists(target) || Key(engineData) is not { } key) return 0;
        var logins = Pending(pendingFile);
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
            if (!Known.All(k => columns.Any(c => c.Name == k))) return 0; // table inattendue : on n'y touche pas
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
        catch (Exception e) when (e is IOException or SqliteException or UnauthorizedAccessException) { return 0; } // base occupée : on réessaiera
        finally { SqliteConnection.ClearAllPools(); }
        File.Delete(pendingFile);
        return added;
    }

    private static readonly string[] Known =
        ["origin_url", "action_url", "username_element", "username_value", "password_element", "password_value", "signon_realm", "date_created"];

    private static SqliteConnection Open(string file, SqliteOpenMode mode)
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Mode = mode, Pooling = false }.ToString());
        db.Open();
        return db;
    }

    private static string Text(SqliteDataReader r, int i) => r.IsDBNull(i) ? "" : r.GetString(i);

    // La clé d'un navigateur : dans « Local State », en base 64, précédée de « DPAPI » et protégée par Windows.
    private static byte[]? Key(string userData)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(userData, "Local State")));
            var raw = Convert.FromBase64String(doc.RootElement.GetProperty("os_crypt").GetProperty("encrypted_key").GetString()!);
            if (raw.Length <= 5 || Encoding.ASCII.GetString(raw, 0, 5) != "DPAPI") return null;
            var key = ProtectedData.Unprotect(raw[5..], null, DataProtectionScope.CurrentUser);
            return key.Length == 32 ? key : null;
        }
        catch (Exception e) when (e is IOException or JsonException or FormatException or CryptographicException
            or KeyNotFoundException or InvalidOperationException or NullReferenceException or UnauthorizedAccessException) { return null; }
    }

    private static string? Decrypt(byte[] key, byte[] secret)
    {
        if (secret.Length < V10.Length + Nonce + Tag || !secret.AsSpan(0, V10.Length).SequenceEqual(V10)) return null;
        var cipher = secret.AsSpan(V10.Length + Nonce, secret.Length - V10.Length - Nonce - Tag);
        var plain = new byte[cipher.Length];
        try
        {
            using var aes = new AesGcm(key, Tag);
            aes.Decrypt(secret.AsSpan(V10.Length, Nonce), cipher, secret.AsSpan(secret.Length - Tag), plain);
            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException) { return null; }
    }

    private static byte[] Encrypt(byte[] key, string password)
    {
        var plain = Encoding.UTF8.GetBytes(password);
        var secret = new byte[V10.Length + Nonce + plain.Length + Tag];
        V10.CopyTo(secret, 0);
        RandomNumberGenerator.Fill(secret.AsSpan(V10.Length, Nonce));
        using var aes = new AesGcm(key, Tag);
        aes.Encrypt(secret.AsSpan(V10.Length, Nonce), plain, secret.AsSpan(V10.Length + Nonce, plain.Length), secret.AsSpan(secret.Length - Tag));
        return secret;
    }
}
