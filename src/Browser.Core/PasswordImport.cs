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
public static partial class PasswordImport
{
    private static readonly string[] Databases = ["Login Data", "Login Data For Account"];
    private static readonly byte[] V10 = "v10"u8.ToArray();
    private const int Nonce = 12, Tag = 16;

    /// <summary>Les mots de passe lisibles d'un profil, et le nombre de ceux qui ne le sont pas.</summary>
    public static (IReadOnlyList<ImportedLogin> Logins, int Unreadable) Read(string userData, string profileDir, string tempDir)
    {
        var list = new List<ImportedLogin>();
        var unreadable = 0;
        Exception? failure = null;
        var key = Key(userData);
        Directory.CreateDirectory(tempDir);
        foreach (var name in Databases)
        {
            var source = Path.Combine(profileDir, name);
            if (!File.Exists(source)) continue;
            // Une sauvegarde SQLite inclut aussi les mots de passe encore dans le journal WAL du navigateur ouvert.
            var copy = Path.Combine(tempDir, $"import-{Guid.NewGuid():N}.db");
            try
            {
                Snapshot(source, copy);
                using var db = Open(copy, SqliteOpenMode.ReadOnly);
                using var cmd = db.CreateCommand();
                cmd.CommandText = "SELECT origin_url, action_url, username_element, username_value, password_element, password_value, signon_realm FROM logins WHERE blacklisted_by_user = 0";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var secret = r.IsDBNull(5) ? [] : (byte[])r[5];
                    if (secret.Length == 0) continue;
                    if (Decrypt(key, secret) is not { Length: > 0 } password) { unreadable++; continue; }
                    list.Add(new ImportedLogin(Text(r, 0), Text(r, 1), Text(r, 2), Text(r, 3), Text(r, 4), password, Text(r, 6)));
                }
            }
            catch (Exception e) when (e is IOException or SqliteException or UnauthorizedAccessException) { failure = e; }
            finally
            {
                SqliteConnection.ClearAllPools();
                foreach (var part in new[] { copy, copy + "-wal", copy + "-journal", copy + "-shm" })
                    try { File.Delete(part); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
        if (failure is not null) throw new IOException("Cannot read the saved passwords. Close the source browser and retry, or import its CSV export.", failure);
        return (list, unreadable);
    }

    // Le moteur en cours d'exécution garde sa base verrouillée pour SQLite : les fichiers restent lisibles directement,
    // journal compris, et SQLite le rejoue sur la copie pour retrouver un état cohérent.
    private static void Snapshot(string source, string copy)
    {
        try
        {
            using var original = Open(source, SqliteOpenMode.ReadOnly);
            using var snapshot = Open(copy, SqliteOpenMode.ReadWriteCreate);
            original.BackupDatabase(snapshot);
        }
        catch (SqliteException)
        {
            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-wal", "-journal" })
            {
                if (!File.Exists(source + suffix)) continue;
                using var from = new FileStream(source + suffix, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var to = new FileStream(copy + suffix, FileMode.Create, FileAccess.Write);
                from.CopyTo(to);
            }
        }
    }

    /// <summary>
    /// Les mots de passe d'un fichier CSV exporté par un navigateur ou un gestionnaire de mots de passe : une ligne
    /// d'en-tête avec au moins une colonne d'adresse, d'identifiant et de mot de passe.
    /// </summary>
    public static IReadOnlyList<ImportedLogin> ReadCsv(string text)
    {
        var rows = Rows(text);
        if (rows.Count < 2) return [];
        var head = rows[0].Select(h => h.Trim().TrimStart('\uFEFF').ToLowerInvariant()).ToList();
        int url = head.FindIndex(h => h is "url" or "login_uri" or "origin" or "website" or "adresse"),
            user = head.FindIndex(h => h is "username" or "login_username" or "login" or "identifiant" or "nom d'utilisateur"),
            pass = head.FindIndex(h => h is "password" or "login_password" or "mot de passe");
        if (url < 0 || user < 0 || pass < 0) return [];
        var list = new List<ImportedLogin>();
        foreach (var row in rows.Skip(1))
        {
            if (row.Count <= Math.Max(url, Math.Max(user, pass)) || row[pass].Length == 0) continue;
            if (!Uri.TryCreate(row[url].Trim(), UriKind.Absolute, out var site) || site.Scheme is not ("https" or "http")) continue;
            // Sans nom de champ : le moteur propose alors l'identifiant sur tout formulaire de connexion du site.
            list.Add(new ImportedLogin(site.AbsoluteUri, "", "", row[user], "", row[pass], site.GetLeftPart(UriPartial.Authority) + "/"));
        }
        return list;
    }

    // Le premier séparateur de l'en-tête détermine le format ; les séparateurs entre guillemets restent du texte.
    private static List<List<string>> Rows(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        char? separator = null;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c != '"') field.Append(c);
                else if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else quoted = false;
            }
            else if (c == '"') quoted = true;
            else if (c == separator || separator is null && (c is ',' or ';' or '\t')) { separator ??= c; row.Add(field.ToString()); field.Clear(); }
            else if (c is '\n' or '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(field.ToString()); field.Clear();
                if (row.Count > 1 || row[0].Length > 0) rows.Add(row);
                row = [];
            }
            else field.Append(c);
        }
        if (quoted) return [];
        row.Add(field.ToString());
        if (row.Count > 1 || row[0].Length > 0) rows.Add(row);
        return rows;
    }

    private static readonly string[] Known =
        ["origin_url", "action_url", "username_element", "username_value", "password_element", "password_value", "signon_realm", "date_created"];

    private static SqliteConnection Open(string file, SqliteOpenMode mode)
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Mode = mode, Pooling = false, DefaultTimeout = 3 }.ToString());
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

    private static string? Decrypt(byte[]? key, byte[] secret)
    {
        if (!secret.AsSpan().StartsWith(V10))
        {
            if (!OperatingSystem.IsWindows() || secret[0] == (byte)'v') return null;
            try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(secret, null, DataProtectionScope.CurrentUser)); }
            catch (CryptographicException) { return null; }
        }
        if (key is null || secret.Length < V10.Length + Nonce + Tag) return null;
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
