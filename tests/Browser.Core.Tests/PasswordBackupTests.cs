using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Browser.Core;
using Microsoft.Data.Sqlite;

public sealed class PasswordBackupTests : IDisposable
{
    private const string Session = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private readonly string _dir = Directory.CreateTempSubdirectory("sol-password-backup-").FullName;
    public void Dispose() { SqliteConnection.ClearAllPools(); Directory.Delete(_dir, true); }
    private string Temp => Path.Combine(_dir, "temp");
    private static ImportedLogin Login(string password) => new("https://site.test/", "", "u", "ada", "p", password, "https://site.test/");
    private static readonly bool Dpapi = CanProtect();
    private static bool CanProtect()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try { ProtectedData.Protect([1], null, DataProtectionScope.CurrentUser); return true; }
        catch (CryptographicException) { return false; }
    }

    private string Engine(string name)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var engine = Directory.CreateDirectory(Path.Combine(_dir, name)).FullName;
        var key = Encoding.ASCII.GetBytes("DPAPI").Concat(ProtectedData.Protect(RandomNumberGenerator.GetBytes(32), null, DataProtectionScope.CurrentUser)).ToArray();
        File.WriteAllText(Path.Combine(engine, "Local State"), JsonSerializer.Serialize(new { os_crypt = new { encrypted_key = Convert.ToBase64String(key) } }));
        Database(Path.Combine(engine, "Default"));
        return engine;
    }

    private static void Database(string profile, bool unreadable = false)
    {
        Directory.CreateDirectory(profile);
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(profile, "Login Data"), Pooling = false }.ToString());
        db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE logins (origin_url TEXT, action_url TEXT, username_element TEXT, username_value TEXT,
              password_element TEXT, password_value BLOB, signon_realm TEXT, date_created INTEGER NOT NULL,
              blacklisted_by_user INTEGER NOT NULL, UNIQUE(origin_url, username_element, username_value, password_element, signon_realm));
            """;
        cmd.ExecuteNonQuery();
        if (!unreadable) return;
        cmd.CommandText = "INSERT INTO logins VALUES ('https://site.test/','','u','ada','p',$p,'https://site.test/',1,0)";
        cmd.Parameters.AddWithValue("$p", "v20opaque"u8.ToArray());
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public void Collection_counts_unreadable_named_session_entries_and_excludes_private_or_unknown_profiles()
    {
        var engine = Directory.CreateDirectory(Path.Combine(_dir, "source")).FullName;
        Database(Path.Combine(engine, "Default"), true);
        Database(PasswordBackup.ProfileDirectory(engine, Session), true);
        Database(Path.Combine(engine, "WV2Profile_sol-private-" + Session), true);
        Database(Path.Combine(engine, "WV2Profile_sol-bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"), true);
        var document = PasswordBackup.Collect(engine, [new(Session, "Travail"), new("../invalid", "Invalide")], Temp);
        Assert.Equal(2, document.Unreadable);
        Assert.Equal(0, document.Exported);
        Assert.Equal(["", Session], document.Profiles.Select(p => p.SessionId));
        Assert.Empty(Directory.GetFiles(Temp));
    }

    [Fact]
    public void Backup_round_trip_preserves_password_isolation_and_retries_a_missing_session_database()
    {
        if (!OperatingSystem.IsWindows() || !Dpapi) return;
        var engine = Engine("target");
        var pending = Path.Combine(_dir, "passwords.pending");
        PasswordImport.Queue(pending, [Login("principal")]);
        var document = new PasswordBackupDocument(1, [new(Session, [Login("travail")])], 1);
        var sealedFile = Backup.Seal(new Dictionary<string, byte[]> { [Backup.Passwords] = JsonSerializer.SerializeToUtf8Bytes(document) }, "cle-secrete");
        var opened = Backup.Open(sealedFile, "cle-secrete")!;
        File.WriteAllBytes(pending, PasswordBackup.PreparePending(pending, opened[Backup.Passwords]));
        Assert.DoesNotContain("travail", Encoding.UTF8.GetString(File.ReadAllBytes(pending)));
        Assert.Equal(1, PasswordImport.ApplyPending(pending, engine));
        Assert.True(File.Exists(pending));
        Assert.True(Directory.Exists(PasswordBackup.ProfileDirectory(engine, Session)));
        var waiting = PasswordBackup.Collect(engine, [new(Session, "Travail")], Temp, pending);
        Assert.Equal(2, waiting.Exported);
        Assert.Equal("travail", Assert.Single(waiting.Profiles.Single(p => p.SessionId == Session).Logins).Password);
        Assert.Equal("principal", Assert.Single(PasswordImport.Read(engine, Path.Combine(engine, "Default"), Temp).Logins).Password);
        Database(PasswordBackup.ProfileDirectory(engine, Session));
        Assert.Equal(1, PasswordImport.ApplyPending(pending, engine));
        Assert.False(File.Exists(pending));
        Assert.Equal("travail", Assert.Single(PasswordImport.Read(engine, PasswordBackup.ProfileDirectory(engine, Session), Temp).Logins).Password);
        var exported = PasswordBackup.Collect(engine, [new(Session, "Travail")], Temp);
        Assert.Equal(2, exported.Exported);
        Assert.Equal(0, exported.Unreadable);
        Assert.Equal("principal", Assert.Single(exported.Profiles.Single(p => p.SessionId == "").Logins).Password);
        Assert.Equal("travail", Assert.Single(exported.Profiles.Single(p => p.SessionId == Session).Logins).Password);
    }

    [Fact]
    public void Legacy_backups_and_legacy_pending_files_target_only_the_default_profile()
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new[] { Login("ancien") });
        var decoded = PasswordBackup.Decode(bytes);
        Assert.Equal("", Assert.Single(decoded.Profiles).SessionId);
        if (!OperatingSystem.IsWindows() || !Dpapi) return;
        var engine = Engine("legacy");
        Database(PasswordBackup.ProfileDirectory(engine, Session));
        var pending = Path.Combine(_dir, "legacy.pending");
        File.WriteAllBytes(pending, ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser));
        Assert.Equal(1, PasswordImport.ApplyPending(pending, engine));
        Assert.Single(PasswordImport.Read(engine, Path.Combine(engine, "Default"), Temp).Logins);
        Assert.Empty(PasswordImport.Read(engine, PasswordBackup.ProfileDirectory(engine, Session), Temp).Logins);
    }

    [Fact]
    public void Passwords_imported_after_staging_survive_the_restore_transaction()
    {
        if (!OperatingSystem.IsWindows() || !Dpapi) return;
        var engine = Engine("late-import");
        var data = Directory.CreateDirectory(Path.Combine(_dir, "profile")).FullName;
        var pending = Path.Combine(data, "passwords.pending");
        Backup.Stage(data, new Dictionary<string, byte[]>
        {
            [Backup.Passwords] = JsonSerializer.SerializeToUtf8Bytes(new[] { Login("sauvegarde") }),
        });
        PasswordImport.Queue(pending, [Login("csv-tardif") with { Username = "bob" }]);
        Assert.True(Backup.ApplyStaged(data));
        Assert.Equal(2, PasswordImport.ApplyPending(pending, engine));
        Assert.Equal(new[] { "csv-tardif", "sauvegarde" }, PasswordImport.Read(engine, Path.Combine(engine, "Default"), Temp).Logins.Select(l => l.Password).Order());
        Assert.False(File.Exists(pending));
    }

    [Theory]
    [InlineData("{\"Version\":2,\"Profiles\":[]}")]
    [InlineData("{\"Version\":1,\"Profiles\":[{\"SessionId\":\"../Default\",\"Logins\":[]}]}")]
    [InlineData("{\"Version\":1,\"Profiles\":[{\"SessionId\":\"sol-private-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"Logins\":[]}]}")]
    [InlineData("{\"Version\":1,\"Profiles\":[{\"SessionId\":\"\",\"Logins\":[]},{\"SessionId\":\"\",\"Logins\":[]}]}")]
    [InlineData("{\"Version\":1,\"Profiles\":null}")]
    [InlineData("[null]")]
    public void Malformed_or_ambiguous_profile_maps_are_rejected(string json) =>
        Assert.Throws<JsonException>(() => PasswordBackup.Decode(Encoding.UTF8.GetBytes(json)));
}
