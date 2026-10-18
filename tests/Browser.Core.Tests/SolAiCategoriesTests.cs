using Browser.Core;

namespace Browser.Core.Tests;

public sealed class SolAiCategoriesTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

    [Fact]
    public void SendsOnlySiteAndTitleOfWebLinks()
    {
        var links = SolAiCategories.Links(
            [new("https://docs.example.com/guide?token=secret", "Guide"), new("javascript:alert(1)", "x"), new("https://user:pw@a.com/", "a")],
            [new("https://docs.example.com/guide?token=secret", "Guide", 3, Now), new("https://news.example.org/", "", 1, Now)]);
        Assert.Equal(2, links.Count);
        Assert.True(links[0].Bookmark);
        Assert.Equal("news.example.org", links[1].Title);
        var data = SolAiCategories.Data(links);
        Assert.DoesNotContain("secret", data);
        Assert.Contains("docs.example.com", data);
    }

    [Fact]
    public void ParsesUntrustedAnswer()
    {
        var links = SolAiCategories.Links([new("https://a.com/", "A"), new("https://b.com/", "B")], []);
        var categories = SolAiCategories.Parse("""Voici : {"categories":[{"name":"Travail","ids":[0,0,9,"x"]},{"name":"Autre","ids":[0,1]},{"name":"","ids":[1]}]}""", links);
        Assert.Equal(["Travail", "Autre"], categories.Select(c => c.Name));
        Assert.Equal(["https://a.com/"], categories[0].Links.Select(l => l.Url));
        Assert.Equal(["https://b.com/"], categories[1].Links.Select(l => l.Url));
        Assert.Empty(SolAiCategories.Parse("not json", links));
        Assert.Empty(SolAiCategories.Parse("""{"categories":"oops"}""", links));
    }

    [Fact]
    public void RunsOncePerDay()
    {
        Assert.True(SolAiCategories.Due(new CategoryIndex(), Now));
        Assert.False(SolAiCategories.Due(new CategoryIndex { UpdatedAt = Now.AddHours(-23) }, Now));
        Assert.True(SolAiCategories.Due(new CategoryIndex { UpdatedAt = Now.AddHours(-24) }, Now));
    }
}
