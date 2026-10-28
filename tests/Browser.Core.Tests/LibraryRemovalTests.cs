using Browser.Core;

public class LibraryRemovalTests
{
    [Fact]
    public void Removing_history_for_one_url_updates_all_history_queries_and_preserves_bookmarks()
    {
        using var db = new BrowserDb(":memory:");
        const string removed = "https://a.test/page?text=' OR 1=1 --";
        const string kept = "https://b.test/kept";
        db.RecordVisit(removed, "Removed", DateTimeOffset.UtcNow);
        db.RecordVisit(removed, "Removed again", DateTimeOffset.UtcNow);
        db.RecordVisit(kept, "Kept", DateTimeOffset.UtcNow);
        db.AddBookmark(removed, "Favorite remains");

        db.RemoveHistory(removed);
        db.RemoveHistory("https://missing.test");

        Assert.Equal(kept, Assert.Single(db.RecentHistory(10)).Url);
        Assert.Equal(kept, Assert.Single(db.FrequentSites(10)).Url);
        Assert.Empty(db.Suggest("Removed", 10));
        Assert.Empty(db.History("Removed", 10, 0));
        Assert.Equal(1, db.HistoryCount(""));
        Assert.True(db.IsBookmarked(removed));
    }

    [Theory]
    [InlineData("start.bookmark.remove")]
    [InlineData("start.history.remove")]
    public void Start_removal_requires_a_url_input(string type)
    {
        Assert.Equal(new Command(type, Input: "https://a.test"),
            CommandParser.Parse($$"""{"type":"{{type}}","input":"https://a.test"}"""));
        Assert.Null(CommandParser.Parse($$"""{"type":"{{type}}"}"""));
    }

    [Fact]
    public void Library_removal_requires_the_item_kind_url_and_current_search_page()
    {
        Assert.Equal(new Command("library.remove", Url: "https://a.test", Input: "history", Query: "a", Index: 2),
            CommandParser.Parse("""{"type":"library.remove","url":"https://a.test","input":"history","query":"a","index":2}"""));
        Assert.Null(CommandParser.Parse("""{"type":"library.remove","url":"https://a.test"}"""));
    }
}
