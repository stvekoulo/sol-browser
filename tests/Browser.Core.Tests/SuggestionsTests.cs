using Browser.Core;

public class SuggestionsTests
{
    private const string Search = "https://www.google.com/search?q={0}";
    private static readonly DateTimeOffset T0 = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Empty_query_gives_nothing() => Assert.Empty(Suggestions.Build("  ", [], Search));

    [Fact]
    public void With_no_history_offers_search_then_known_sites()
    {
        var s = Suggestions.Build("you", [], Search);
        Assert.Equal(new Suggestion("https://www.google.com/search?q=you", "you", "search"), s[0]);
        Assert.Equal(new Suggestion("https://youtube.com", "YouTube", "site"), s[1]);
        Assert.Equal(2, s.Count);
    }

    [Fact]
    public void A_typed_address_comes_first_as_go()
    {
        var s = Suggestions.Build("github.com/WebKit", [], Search);
        Assert.Equal(new Suggestion("https://github.com/WebKit", "github.com/WebKit", "go"), s[0]);
    }

    [Fact]
    public void History_comes_before_known_sites_and_hides_the_duplicate_site()
    {
        var history = new[] { new Visit("https://www.youtube.com/watch?v=1", "Une vidéo", 3, T0) };
        var s = Suggestions.Build("you", history, Search);
        Assert.Equal(new[] { "search", "history" }, s.Select(x => x.Kind));
    }

    [Fact]
    public void Matches_known_sites_by_name_and_respects_the_limit()
    {
        Assert.Contains(Suggestions.Build("gma", [], Search), x => x.Url == "https://mail.google.com");
        Assert.Equal(3, Suggestions.Build("g", [], Search, limit: 3).Count);
    }
}
