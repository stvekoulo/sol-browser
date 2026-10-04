using System.Text;
using Browser.Core;

public class BackupTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("nav-backup").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static readonly Dictionary<string, byte[]> Files = new()
    {
        ["settings.json"] = "{\"Theme\":\"dark\"}"u8.ToArray(),
        ["browser.db"] = [1, 2, 3, 0, 255],
        ["passwords.json"] = "[\"s3cret\"]"u8.ToArray(),
        ["..\\ailleurs.txt"] = [9],
    };

    [Fact]
    public void A_backup_opens_only_with_its_key_and_hides_its_content()
    {
        var file = Backup.Seal(Files, "clé de sauvegarde");
        Assert.DoesNotContain("s3cret", Encoding.Latin1.GetString(file));
        Assert.Equal(Files["browser.db"], Backup.Open(file, "clé de sauvegarde")!["browser.db"]);
        Assert.Null(Backup.Open(file, "autre clé"));
        file[^1] ^= 1; // un seul octet modifié
        Assert.Null(Backup.Open(file, "clé de sauvegarde"));
        Assert.Null(Backup.Open("pas une sauvegarde"u8.ToArray(), "clé de sauvegarde"));
    }

    [Fact]
    public void Restoring_replaces_the_profile_files_at_the_next_start_and_writes_nothing_else()
    {
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "ancien");
        File.WriteAllText(Path.Combine(_dir, "browser.db-wal"), "journal de l'ancienne base");
        Assert.False(Backup.ApplyStaged(_dir));

        Backup.Stage(_dir, Backup.Open(Backup.Seal(Files, "clé de sauvegarde"), "clé de sauvegarde")!);
        Assert.Equal("ancien", File.ReadAllText(Path.Combine(_dir, "settings.json")));

        Assert.True(Backup.ApplyStaged(_dir));
        Assert.Equal("{\"Theme\":\"dark\"}", File.ReadAllText(Path.Combine(_dir, "settings.json")));
        Assert.Equal(["browser.db", "settings.json"], Directory.GetFileSystemEntries(_dir, "*", SearchOption.AllDirectories).Select(Path.GetFileName).Order());
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(_dir)!, "ailleurs.txt"));
    }

    [Fact]
    public void Passwords_are_read_from_an_exported_csv()
    {
        var csv = "name,url,username,password,note\r\nExemple,https://exemple.test/login,ada,\"a,b \"\"c\"\"\",\r\nSans,https://vide.test/,bob,,\r\nApp,android://x/,eve,z,\r\n\"Multi\nligne\",http://autre.test:8080/a?b=1,,p2,note\n";
        Assert.Equal(
            [new ImportedLogin("https://exemple.test/login", "", "", "ada", "", "a,b \"c\"", "https://exemple.test/"),
             new ImportedLogin("http://autre.test:8080/a?b=1", "", "", "", "", "p2", "http://autre.test:8080/")],
            PasswordImport.ReadCsv(csv));
        Assert.Empty(PasswordImport.ReadCsv("a,b\n1,2\n"));
        Assert.Empty(PasswordImport.ReadCsv(""));
    }
}
