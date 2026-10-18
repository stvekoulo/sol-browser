using Browser.Core;

public class BrowserDbTests
{
    [Fact]
    public void Frequent_sites_sort_by_count_then_recency_and_respect_limit()
    {
        using var db = new BrowserDb(":memory:");
        db.RecordVisit("https://a.test", "A", T0);
        db.RecordVisit("https://a.test", "A", T0);
        db.RecordVisit("https://b.test", "B", T0.AddMinutes(1));
        db.RecordVisit("https://c.test", "C", T0.AddMinutes(2));
        Assert.Equal(new[] { "https://a.test", "https://c.test" }, db.FrequentSites(2).Select(v => v.Url));
    }
    private static readonly DateTimeOffset T0 = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Revisiting_a_url_increments_count_and_updates_title_and_time()
    {
        using var db = new BrowserDb(":memory:");
        db.RecordVisit("https://a.test", "Old", T0);
        db.RecordVisit("https://a.test", "New", T0.AddMinutes(5));
        var v = Assert.Single(db.RecentHistory(10));
        Assert.Equal(new Visit("https://a.test", "New", 2, T0.AddMinutes(5)), v);
    }

    [Fact]
    public void RecentHistory_is_newest_first_and_limited()
    {
        using var db = new BrowserDb(":memory:");
        db.RecordVisit("https://a.test", "A", T0);
        db.RecordVisit("https://b.test", "B", T0.AddMinutes(1));
        db.RecordVisit("https://c.test", "C", T0.AddMinutes(2));
        Assert.Equal(new[] { "https://c.test", "https://b.test" }, db.RecentHistory(2).Select(v => v.Url));
    }

    [Fact]
    public void Suggest_matches_url_or_title_most_visited_first()
    {
        using var db = new BrowserDb(":memory:");
        db.RecordVisit("https://docs.test/react", "Docs", T0);
        db.RecordVisit("https://b.test", "React blog", T0);
        db.RecordVisit("https://b.test", "React blog", T0.AddMinutes(1));
        db.RecordVisit("https://other.test", "Other", T0);
        Assert.Equal(new[] { "https://b.test", "https://docs.test/react" }, db.Suggest("react", 5).Select(v => v.Url));
    }

    [Fact]
    public void Suggest_treats_wildcards_literally()
    {
        using var db = new BrowserDb(":memory:");
        db.RecordVisit("https://a.test", "A", T0);
        Assert.Empty(db.Suggest("%", 5));
        Assert.Empty(db.Suggest("_", 5));
    }

    [Fact]
    public void Bookmarks_add_remove_and_list()
    {
        using var db = new BrowserDb(":memory:");
        db.AddBookmark("https://a.test", "A");
        db.AddBookmark("https://a.test", "A renamed");
        Assert.True(db.IsBookmarked("https://a.test"));
        Assert.Equal(new Bookmark("https://a.test", "A renamed"), Assert.Single(db.Bookmarks()));
        db.RemoveBookmark("https://a.test");
        Assert.False(db.IsBookmarked("https://a.test"));
    }
}
