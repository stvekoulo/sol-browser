using Browser.Core;
using Microsoft.Data.Sqlite;

public class BrowserImportTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("nav-import").FullName;
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    private string Profile(string browserPath, string folder)
    {
        var dir = Path.Combine(_dir, "Local", browserPath, folder);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private const string Bookmarks = """
        { "roots": {
            "bookmark_bar": { "type": "folder", "children": [
              { "type": "url", "name": "Exemple", "url": "https://exemple.test/" },
              { "type": "folder", "name": "Travail", "children": [
                { "type": "url", "name": "Docs", "url": "https://docs.test/a" },
                { "type": "url", "name": "Script", "url": "javascript:alert(1)" },
                { "type": "url", "name": "Fichier", "url": "file:///C:/secret.txt" } ] } ] },
            "other": { "type": "folder", "children": [ { "type": "url", "name": "", "url": "http://autre.test/" } ] } } }
        """;

    [Fact]
    public void Bookmarks_are_read_from_every_folder_and_only_web_addresses_are_kept()
    {
        var dir = Profile(@"Google\Chrome\User Data", "Default");
        File.WriteAllText(Path.Combine(dir, "Bookmarks"), Bookmarks);
        Assert.Equal(
            [new Bookmark("https://exemple.test/", "Exemple"), new Bookmark("https://docs.test/a", "Docs"), new Bookmark("http://autre.test/", "")],
            BrowserImport.ReadBookmarks(dir));
        Assert.Empty(BrowserImport.ReadBookmarks(Path.Combine(_dir, "absent")));
    }

    [Fact]
    public void A_broken_bookmarks_file_gives_nothing_instead_of_failing()
    {
        var dir = Profile(@"Google\Chrome\User Data", "Default");
        File.WriteAllText(Path.Combine(dir, "Bookmarks"), "{ pas du json");
        Assert.Empty(BrowserImport.ReadBookmarks(dir));
    }

    [Fact]
    public void History_is_read_from_a_copy_most_recent_first_with_real_dates()
    {
        var dir = Profile(@"Microsoft\Edge\User Data", "Default");
        var when = new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
        var chromeTime = (when - new DateTimeOffset(1601, 1, 1, 0, 0, 0, TimeSpan.Zero)).Ticks / 10;
        using (var db = new SqliteConnection($"Data Source={Path.Combine(dir, "History")}"))
        {
            db.Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = $"""
                CREATE TABLE urls (id INTEGER PRIMARY KEY, url TEXT, title TEXT, visit_count INTEGER, typed_count INTEGER, last_visit_time INTEGER, hidden INTEGER);
                INSERT INTO urls (url, title, visit_count, typed_count, last_visit_time, hidden) VALUES
                  ('https://ancien.test/', 'Ancien', 2, 0, {chromeTime - 1_000_000}, 0),
                  ('https://recent.test/', 'Récent', 7, 0, {chromeTime}, 0),
                  ('chrome://settings/', 'Réglages', 1, 0, {chromeTime + 5}, 0),
                  ('https://cache.test/', 'Caché', 1, 0, {chromeTime + 9}, 1);
                """;
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        var visits = BrowserImport.ReadHistory(dir, limit: 10, Path.Combine(_dir, "tmp"));
        Assert.Equal(["https://recent.test/", "https://ancien.test/"], visits.Select(v => v.Url));
        Assert.Equal(("Récent", 7, when), (visits[0].Title, visits[0].Count, visits[0].Last));
        Assert.Empty(Directory.GetFiles(Path.Combine(_dir, "tmp"))); // la copie de travail est effacée
        Assert.Empty(BrowserImport.ReadHistory(Path.Combine(_dir, "absent"), 10, Path.Combine(_dir, "tmp")));
    }

    [Fact]
    public void Detect_lists_installed_browsers_with_their_named_profiles()
    {
        var chrome = Path.Combine(_dir, "Local", @"Google\Chrome\User Data");
        File.WriteAllText(Path.Combine(Profile(@"Google\Chrome\User Data", "Default"), "Bookmarks"), "{}");
        File.WriteAllText(Path.Combine(Profile(@"Google\Chrome\User Data", "Profile 2"), "History"), "");
        Profile(@"Google\Chrome\User Data", "Profile 3"); // vide : rien à reprendre
        Profile(@"Google\Chrome\User Data", "Crashpad");  // pas un profil
        File.WriteAllText(Path.Combine(chrome, "Local State"), """{ "profile": { "info_cache": { "Default": { "name": "Steven" }, "Profile 2": { "name": "Travail" } } } }""");
        var opera = Path.Combine(_dir, "Roaming", @"Opera Software\Opera Stable");
        Directory.CreateDirectory(opera);
        File.WriteAllText(Path.Combine(opera, "Bookmarks"), "{}");

        var found = BrowserImport.Detect(Path.Combine(_dir, "Local"), Path.Combine(_dir, "Roaming"));
        Assert.Equal(["chrome", "opera"], found.Select(s => s.Id));
        Assert.Equal([new ImportProfile("Default", "Steven"), new ImportProfile("Profile 2", "Travail")], found[0].Profiles);
        Assert.Equal([new ImportProfile("", "Opera")], found[1].Profiles);

        Assert.Equal(Path.Combine(chrome, "Profile 2"), BrowserImport.ProfileDir(found, "chrome", "Profile 2"));
        Assert.Null(BrowserImport.ProfileDir(found, "chrome", @"..\..\ailleurs")); // seul un profil détecté est accepté
        Assert.Null(BrowserImport.ProfileDir(found, "edge", "Default"));
    }

    [Fact]
    public void Older_imported_pages_are_searchable_and_available_after_the_first_history_page()
    {
        using var db = new BrowserDb(":memory:");
        var when = DateTimeOffset.UtcNow.AddDays(-30);
        db.Import([new Bookmark("https://import.test/favorite", "Imported favorite")],
            Enumerable.Range(0, 125).Select(i => new ImportedVisit($"https://import.test/{i}", i == 0 ? "Older imported 100% page" : $"Imported {i}", 1, when.AddMinutes(i))));
        db.RecordVisit("https://sol.test/new", "New page", when.AddDays(30));
        Assert.Equal(126, db.HistoryCount(""));
        Assert.Equal(100, db.History("", 100, 0).Count);
        Assert.Equal(26, db.History("", 100, 100).Count);
        Assert.Equal("https://import.test/0", Assert.Single(db.History("100%", 100, 0)).Url);
        Assert.Equal(125, db.HistoryCount("import.test"));
        Assert.True(db.IsBookmarked("https://import.test/favorite"));
    }

    [Fact]
    public void Imported_visits_merge_with_the_existing_history()
    {
        using var db = new BrowserDb(Path.Combine(_dir, "sol.db"));
        var recent = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        db.RecordVisit("https://a.test/", "A ici", recent);
        db.Import([new Bookmark("https://fav.test/", "Favori")],
            [new ImportedVisit("https://a.test/", "A ailleurs", 4, recent.AddDays(-3)), new ImportedVisit("https://b.test/", "B", 2, recent.AddDays(-1))]);
        Assert.True(db.IsBookmarked("https://fav.test/"));
        var history = db.RecentHistory(10);
        Assert.Equal(["https://a.test/", "https://b.test/"], history.Select(v => v.Url));
        Assert.Equal(("A ici", 5, recent), (history[0].Title, history[0].Count, history[0].Last)); // la visite la plus récente garde son titre et sa date
    }

    [Fact]
    public void Extensions_of_another_browser_are_found_with_their_real_name_and_latest_version()
    {
        var id = "abcdefghijklmnopabcdefghijklmnop";
        var userData = Path.Combine(_dir, "Local", "Navigateur", "User Data");
        foreach (var version in new[] { "1.2.0_0", "1.10.0_0" })
        {
            var dir = Path.Combine(userData, "Default", "Extensions", id, version);
            Directory.CreateDirectory(Path.Combine(dir, "_locales", "en"));
            Directory.CreateDirectory(Path.Combine(dir, "_metadata"));
            File.WriteAllText(Path.Combine(dir, "manifest.json"), $$"""{ "name": "__MSG_extName__", "version": "{{version.Split('_')[0]}}", "default_locale": "en", "action": { "default_popup": "popup.html" }, "options_ui": { "page": "options.html" } }""");
            File.WriteAllText(Path.Combine(dir, "_locales", "en", "messages.json"), """{ "extName": { "message": "Coffre" } }""");
            File.WriteAllText(Path.Combine(dir, "_metadata", "verified_contents.json"), "{}");
        }
        // Ni un thème, ni un dossier qui n'a pas la forme d'un identifiant, ne sont des extensions à proposer.
        var theme = Path.Combine(userData, "Default", "Extensions", "ponmlkjihgfedcbaponmlkjihgfedcba", "1.0_0");
        Directory.CreateDirectory(theme);
        File.WriteAllText(Path.Combine(theme, "manifest.json"), """{ "name": "Nuit", "version": "1.0", "theme": {} }""");
        Directory.CreateDirectory(Path.Combine(userData, "Default", "Extensions", "Temp"));

        var found = Assert.Single(ExtensionCatalog.Scan(userData, "Navigateur"));
        Assert.Equal((id, "Coffre", "1.10.0", "popup.html", "options.html", "Navigateur"), (found.Id, found.Name, found.Version, found.Popup, found.Options, found.From));

        var copy = ExtensionCatalog.CopyTo(found.Folder, Path.Combine(_dir, "sol", id));
        Assert.True(File.Exists(Path.Combine(copy, "manifest.json")));
        Assert.False(Directory.Exists(Path.Combine(copy, "_metadata")));
        Assert.Empty(ExtensionCatalog.Scan(Path.Combine(_dir, "absent"), "Rien"));
    }
}
