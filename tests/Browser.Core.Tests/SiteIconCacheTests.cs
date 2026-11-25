using Browser.Core;
using Microsoft.Data.Sqlite;

public sealed class SiteIconCacheTests
{
    [Fact]
    public void Expired_icons_survive_reopening_and_are_available_during_refresh()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sol-icons-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "browser.db");
        try {
            using (var db = new BrowserDb(file)) { db.CacheSiteIcon("a.test", "data:image/png;base64,AAAA"); db.CacheSiteIcon("missing.test", null); }
            using (var db = new BrowserDb(file)) {
                Assert.Equal((true, "data:image/png;base64,AAAA"), db.CachedSiteIcon("a.test"));
                Assert.Equal((true, (string?)null), db.CachedSiteIcon("missing.test"));
                Assert.False(db.CachedSiteIcon("other.test").Found);
            }
            using (var connection = new SqliteConnection($"Data Source={file}")) {
                connection.Open(); using var command = connection.CreateCommand();
                command.CommandText = "UPDATE site_icons SET checked=0"; command.ExecuteNonQuery();
            }
            using (var db = new BrowserDb(file)) {
                Assert.Equal((false, "data:image/png;base64,AAAA"), db.CachedSiteIcon("a.test"));
                Assert.Equal((false, (string?)null), db.CachedSiteIcon("missing.test"));
            }
        } finally { SqliteConnection.ClearAllPools(); Directory.Delete(directory, true); }
    }

    [Fact]
    public void Cache_is_bounded_and_start_bookmark_query_respects_limit()
    {
        using var db = new BrowserDb(":memory:");
        for (var index = 0; index < 520; index++) { db.CacheSiteIcon($"site{index}.test", null); db.AddBookmark($"https://site{index}.test", $"Site {index}"); }
        Assert.Equal(512, Enumerable.Range(0, 520).Count(index => db.CachedSiteIcon($"site{index}.test").Found));
        Assert.Equal(48, db.Bookmarks(48).Count);
        Assert.Equal(520, db.Bookmarks().Count);
    }

    [Fact]
    public void Bookmarked_icon_survives_cache_eviction_and_an_empty_refresh()
    {
        using var db = new BrowserDb(":memory:");
        const string icon = "data:image/png;base64,AAAA";
        db.AddBookmark("https://www.saved.test/paper", "Saved link");
        db.CacheSiteIcon("saved.test", icon);
        for (var index = 0; index < 520; index++) db.CacheSiteIcon($"visit{index}.test", null);

        Assert.Equal(icon, db.CachedSiteIcon("saved.test").Icon);
        db.CacheSiteIcon("saved.test", null);
        Assert.Equal((true, icon), db.CachedSiteIcon("saved.test"));
    }
}
