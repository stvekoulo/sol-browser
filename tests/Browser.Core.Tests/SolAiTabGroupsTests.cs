using Browser.Core;

namespace Browser.Core.Tests;

public sealed class SolAiTabGroupsTests
{
    private static readonly AiTab[] Tabs = [new("t1", "Issues", "github.com"), new("t2", "PR", "github.com"), new("t3", "Docs", "learn.microsoft.com"), new("t4", "Mail", "mail.example.com")];

    [Fact]
    public void ParsesUntrustedGroups()
    {
        var groups = SolAiTabGroups.Parse("""Voici {"groups":[{"name":"Dev","color":"#22AA66","icon":"💻","ids":[0,1,2,0,9]},{"name":"Courrier","color":"red","icon":"<b>","ids":[3]},{"name":"Reste","ids":[2,3]}]}""", Tabs);
        var dev = Assert.Single(groups);
        Assert.Equal(new[] { "t1", "t2", "t3" }, dev.TabIds);
        Assert.Equal("#22aa66", dev.Color);
        Assert.Equal("💻", dev.Icon);
        Assert.Empty(SolAiTabGroups.Parse("not json", Tabs));
    }

    [Fact]
    public void ParsesOneGroupSuggestion()
    {
        var group = SolAiTabGroups.ParseOne("""{"name":"Recherche","color":"#ff00aa","icon":"abc"}""");
        Assert.Equal("Recherche", group!.Name);
        Assert.Equal("", group.Icon);
        Assert.Null(SolAiTabGroups.ParseOne("""{"name":""}"""));
    }

    [Fact]
    public void SendsOnlySiteAndTitle()
    {
        Assert.Null(SolAiTabGroups.Tab("x", "https://start.sol/index.html", "Accueil"));
        var tab = SolAiTabGroups.Tab("x", "https://www.github.com/org/repo?token=secret", "Repo")!;
        Assert.DoesNotContain("secret", SolAiTabGroups.Data([tab]));
        Assert.Contains("github.com", SolAiTabGroups.Data([tab]));
    }
}
