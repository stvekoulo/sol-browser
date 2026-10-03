using Browser.Core;

public class BlockListTests
{
    private static readonly BlockList List = new(["doubleclick.net", "ads.example.com", "Tracker.Test"]);

    [Theory]
    [InlineData("doubleclick.net", "doubleclick.net")]
    [InlineData("stats.g.doubleclick.net", "doubleclick.net")]
    [InlineData("ads.example.com", "ads.example.com")]
    [InlineData("eu.ads.example.com", "ads.example.com")]
    [InlineData("TRACKER.test", "tracker.test")]
    [InlineData("example.com", null)] // le site lui-même n'est pas dans la liste
    [InlineData("notdoubleclick.net", null)] // même fin de nom, autre domaine
    [InlineData("doubleclick.net.evil.org", null)]
    [InlineData("net", null)]
    public void Match_covers_a_domain_and_its_subdomains_only(string host, string? expected) =>
        Assert.Equal(expected, List.Match(host));

    [Theory]
    [InlineData("https://stats.g.doubleclick.net/pixel.gif", "https://journal.test/article", true)]
    [InlineData("wss://ads.example.com/socket", "https://journal.test/", true)]
    [InlineData("https://ads.example.com/banner.js", "https://example.com/", true)] // autre site que la régie
    [InlineData("https://doubleclick.net/style.css", "https://www.doubleclick.net/", false)] // on visite ce site
    [InlineData("https://cdn.journal.test/app.js", "https://journal.test/", false)]
    [InlineData("data:text/plain,doubleclick.net", "https://journal.test/", false)]
    [InlineData("pas une adresse", "https://journal.test/", false)]
    [InlineData("https://doubleclick.net/x", null, true)]
    public void Blocks_listed_domains_only_from_another_site(string request, string? page, bool expected) =>
        Assert.Equal(expected, List.Blocks(request, page));

    [Fact]
    public void Parse_reads_plain_hosts_and_filter_formats_and_ignores_the_rest()
    {
        var text = """
            # commentaire
            ! autre commentaire
            ads.one.test
            0.0.0.0 ads.two.test
            127.0.0.1   Ads.Three.Test   # en fin de ligne
            ||ads.four.test^
            127.0.0.1 localhost
            192.168.1.1 routeur.test
            pas un domaine
            <html>erreur</html>
            ..mauvais..test
            https://ads.five.test/chemin
            """;
        Assert.Equal(["ads.one.test", "ads.two.test", "ads.three.test", "ads.four.test"], BlockList.Parse(text));
    }

    [Fact]
    public void The_built_in_list_holds_only_plain_domains()
    {
        Assert.True(BlockList.BuiltIn.Count > 100);
        Assert.Equal(BlockList.BuiltIn.Count, BlockList.Parse(string.Join('\n', BlockList.BuiltIn)).Count());
    }
}
