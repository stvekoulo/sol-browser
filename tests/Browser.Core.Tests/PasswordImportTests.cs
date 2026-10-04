using System.Security.Cryptography;
using System.Text;
using Browser.Core;
using Microsoft.Data.Sqlite;

// Navigateurs factices, avec leur propre clé : aucun vrai magasin de mots de passe n'est lu par ces tests.
public class PasswordImportTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("nav-passwords").FullName;
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    // La protection de Windows par compte (DPAPI) n'existe pas pour un compte de service sans profil chargé, comme
    // celui des machines d'intégration continue : ces tests n'y vérifient rien.
    private static readonly bool Dpapi = CanProtect();
    private static bool CanProtect()
    {
        try { ProtectedData.Protect([1], null, DataProtectionScope.CurrentUser); return true; }
        catch (CryptographicException) { return false; }
    }

    // Un dossier de navigateur : sa clé dans « Local State » et une base « Login Data » vide dans le profil « Default ».
    private (string UserData, byte[] Key) Browser(string name, string extraColumns = "")
    {
        var userData = Path.Combine(_dir, name);
        Directory.CreateDirectory(Path.Combine(userData, "Default"));
        var key = RandomNumberGenerator.GetBytes(32);
        var wrapped = Encoding.ASCII.GetBytes("DPAPI").Concat(ProtectedData.Protect(key, null, DataProtectionScope.CurrentUser)).ToArray();
        File.WriteAllText(Path.Combine(userData, "Local State"), $$"""{ "os_crypt": { "encrypted_key": "{{Convert.ToBase64String(wrapped)}}" } }""");
        Run(userData, $"""
            CREATE TABLE logins (origin_url VARCHAR NOT NULL, action_url VARCHAR, username_element VARCHAR, username_value VARCHAR,
              password_element VARCHAR, password_value BLOB, signon_realm VARCHAR NOT NULL, date_created INTEGER NOT NULL,
              blacklisted_by_user INTEGER NOT NULL, scheme INTEGER NOT NULL, times_used INTEGER, id INTEGER PRIMARY KEY AUTOINCREMENT{extraColumns},
              UNIQUE (origin_url, username_element, username_value, password_element, signon_realm))
            """);
        return (userData, key);
    }

    private static void Run(string userData, string sql, params (string, object)[] values)
    {
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(userData, "Default", "Login Data"), Pooling = false }.ToString());
        db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in values) cmd.Parameters.AddWithValue(name, value);
        cmd.ExecuteNonQuery();
    }

    private static byte[] Seal(byte[] key, string password, string prefix = "v10")
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plain = Encoding.UTF8.GetBytes(password);
        byte[] cipher = new byte[plain.Length], tag = new byte[16];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, plain, cipher, tag);
        return [.. Encoding.ASCII.GetBytes(prefix), .. nonce, .. cipher, .. tag];
    }

    private static void Save(string userData, string site, string user, byte[] secret, int never = 0) =>
        Run(userData, "INSERT INTO logins (origin_url, action_url, username_element, username_value, password_element, password_value, signon_realm, date_created, blacklisted_by_user, scheme) VALUES ($o, $o, 'u', $u, 'p', $s, $o, 1, $n, 0)",
            ("$o", site), ("$u", user), ("$s", secret), ("$n", never));

    [Fact]
    public void Passwords_travel_from_another_browser_to_the_engine_and_are_never_left_readable_on_disk()
    {
        if (!Dpapi) return;
        var (source, sourceKey) = Browser("source");
        Save(source, "https://exemple.test/", "ada", Seal(sourceKey, "mot de passe é1"));
        Save(source, "https://autre.test/", "bob", Seal(sourceKey, "s3cret"));
        Save(source, "https://lie.test/", "eve", Seal(sourceKey, "x", prefix: "v20"));          // réservé au navigateur d'origine
        Save(source, "https://jamais.test/", "", Seal(sourceKey, "x"), never: 1);                 // « ne jamais enregistrer »

        var (logins, unreadable) = PasswordImport.Read(source, Path.Combine(source, "Default"), Path.Combine(_dir, "tmp"));
        Assert.Equal(["ada:mot de passe é1", "bob:s3cret"], logins.Select(l => $"{l.Username}:{l.Password}").Order());
        Assert.Equal(1, unreadable);
        Assert.Empty(Directory.GetFiles(Path.Combine(_dir, "tmp")));

        var pending = Path.Combine(_dir, "pending.bin");
        PasswordImport.Queue(pending, logins);
        Assert.DoesNotContain("s3cret", Encoding.UTF8.GetString(File.ReadAllBytes(pending)));

        // Le moteur a une colonne obligatoire de plus, et connaît déjà « ada » : son mot de passe à lui est gardé.
        var (engine, engineKey) = Browser("engine", ", keychain_identifier BLOB NOT NULL, note VARCHAR");
        Run(engine, "INSERT INTO logins (origin_url, action_url, username_element, username_value, password_element, password_value, signon_realm, date_created, blacklisted_by_user, scheme, keychain_identifier) VALUES ('https://exemple.test/', '', 'u', 'ada', 'p', $s, 'https://exemple.test/', 1, 0, 0, x'')",
            ("$s", Seal(engineKey, "celui du moteur")));

        Assert.Equal(1, PasswordImport.ApplyPending(pending, engine));
        Assert.False(File.Exists(pending));
        var (stored, lost) = PasswordImport.Read(engine, Path.Combine(engine, "Default"), Path.Combine(_dir, "tmp"));
        Assert.Equal(["ada:celui du moteur", "bob:s3cret"], stored.Select(l => $"{l.Username}:{l.Password}").Order());
        Assert.Equal(0, lost);
    }

    [Fact]
    public void Pending_passwords_wait_until_the_engine_has_its_database()
    {
        if (!Dpapi) return;
        var (source, key) = Browser("source");
        Save(source, "https://exemple.test/", "ada", Seal(key, "a"));
        var pending = Path.Combine(_dir, "pending.bin");
        PasswordImport.Queue(pending, PasswordImport.Read(source, Path.Combine(source, "Default"), Path.Combine(_dir, "tmp")).Logins);
        Assert.Equal(0, PasswordImport.ApplyPending(pending, Path.Combine(_dir, "pas-encore")));
        Assert.True(File.Exists(pending));
    }
}
