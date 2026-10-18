using Browser.Core;

namespace Browser.Core.Tests;

public sealed class SolAiCategoriesTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

    [Fact]
    public void GroupsLinksBySiteAndSendsOnlyDomainAndTitle()
    {
        var links = SolAiCategories.Links(
            [new("https://www.docs.example.com/guide?token=secret", "Guide"), new("javascript:alert(1)", "x"), new("https://user:pw@a.com/", "a")],
            [new("https://www.docs.example.com/guide?token=secret", "old", 3, Now), new("https://docs.example.com/api", "API", 9, Now), new("https://news.example.org/", "", 1, Now)]);
        Assert.Equal(3, links.Count);
        Assert.True(links.Single(l => l.Url.Contains("guide")).Bookmark);
        var sites = SolAiCategories.Sites(links);
        Assert.Equal(["docs.example.com", "news.example.org"], sites.Select(s => s.Domain));
        Assert.Equal("Guide", sites[0].Title); // le titre du favori l'emporte
        Assert.Equal(17, sites[0].Weight);
        var data = SolAiCategories.Data(sites);
        Assert.DoesNotContain("secret", data);
        Assert.DoesNotContain("/guide", data);
    }

    [Fact]
    public void ClassifiesInBatchesAndKeepsExistingCategories()
    {
        var index = new CategoryIndex();
        CategorySite[] first = [new("a.com", "A", 1), new("b.com", "B", 1), new("c.com", "C", 1)];
        Assert.True(SolAiCategories.Apply(index, """Voici : {"categories":[{"name":"Travail","ids":[0,0,9,"x"]},{"name":"Autre","ids":[0,1]},{"name":"","ids":[2]}]}""", first, Now));
        Assert.Equal(["Travail", "Autre"], index.Names);
        Assert.Equal("Travail", index.Sites["a.com"]);
        Assert.Equal("Autre", index.Sites["b.com"]);
        Assert.Equal("", index.Sites["c.com"]); // traité, mais rangé nulle part : il n'est pas renvoyé
        Assert.Empty(SolAiCategories.Pending(index, first));

        CategorySite[] second = [new("d.com", "D", 1)];
        Assert.True(SolAiCategories.Apply(index, """{"categories":[{"name":"travail","ids":[0]}]}""", second, Now));
        Assert.Equal(["Travail", "Autre"], index.Names);
        Assert.Equal("Travail", index.Sites["d.com"]);
        Assert.False(SolAiCategories.Apply(index, "not json", second, Now));
        Assert.False(SolAiCategories.Apply(index, """{"categories":"oops"}""", second, Now));
    }

    [Fact]
    public void CapsCategoriesAndDailyBatches()
    {
        var index = new CategoryIndex { Names = Enumerable.Range(0, SolAiCategories.MaxCategories).Select(i => "C" + i).ToArray() };
        Assert.True(SolAiCategories.Apply(index, """{"categories":[{"name":"Nouvelle","ids":[0]}]}""", [new("x.com", "X", 1)], Now));
        Assert.Equal(SolAiCategories.MaxCategories, index.Names.Length);
        Assert.Equal("", index.Sites["x.com"]);
        Assert.Equal(SolAiCategories.BatchesPerDay, SolAiCategories.Budget(index, "2026-10-18"));
        index.Day = "2026-10-18"; index.BatchesToday = 8;
        Assert.Equal(2, SolAiCategories.Budget(index, "2026-10-18"));
        Assert.Equal(SolAiCategories.BatchesPerDay, SolAiCategories.Budget(index, "2026-10-19"));
    }

    [Fact]
    public void ViewListsBookmarksAndFrequentPagesFirst()
    {
        var index = new CategoryIndex { Names = ["Dev"], Sites = new(StringComparer.OrdinalIgnoreCase) { ["a.com"] = "Dev", ["b.com"] = "" } };
        CategoryLink[] links = [new("https://a.com/1", "Un", false, 2, Now), new("https://a.com/2", "Deux", true, 0, default), new("https://www.a.com/3", "Trois", false, 9, Now), new("https://b.com/", "B", true, 1, Now)];
        var view = Assert.Single(SolAiCategories.View(index, links, perCategory: 2));
        Assert.Equal(3, view.Total);
        Assert.Equal(["Deux", "Trois"], view.Links.Select(l => l.Title));
    }
}
