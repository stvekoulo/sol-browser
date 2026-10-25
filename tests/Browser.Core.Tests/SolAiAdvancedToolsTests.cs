using System.Text.Json;
using Browser.App;

namespace Browser.Core.Tests;

public sealed class SolAiAdvancedToolsTests
{
    [Fact]
    public async Task FindsTabsWithoutPrivateResultsAndDuplicatesOnlyRequestedTabs()
    {
        var browser = new BrowserController();
        var original = browser.SeedTab("https://example.org/research");
        browser.SeedTab("https://secret.invalid/research", isPrivate: true);
        var result = JsonDocument.Parse(await browser.Tool("find_tabs", """{"query":"research"}"""));
        Assert.Single(result.RootElement.GetProperty("tabs").EnumerateArray());
        var copy = JsonDocument.Parse(await browser.Tool("duplicate_tabs", JsonSerializer.Serialize(new { ids = new[] { original.Id } })));
        Assert.True(copy.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(3, browser.Tabs.Tabs.Count);
        Assert.NotEqual(original.Id, copy.RootElement.GetProperty("tabs")[0].GetProperty("Id").GetString());
        Assert.Equal(original.Url, browser.Tabs.Tabs.Last().Url);
    }

    [Fact]
    public async Task ValidatesDuplicateBatchBeforeOpeningAnyTab()
    {
        var browser = new BrowserController();
        var normal = browser.SeedTab("https://example.org");
        var internalTab = browser.SeedTab("file:///C:/secret");
        await Assert.ThrowsAsync<ArgumentException>(() => browser.Tool("duplicate_tabs", JsonSerializer.Serialize(new { ids = new[] { normal.Id, internalTab.Id } })));
        Assert.Equal(2, browser.Tabs.Tabs.Count);
    }

    [Fact]
    public async Task DuplicatePreservesIsolatedAndPrivateSessions()
    {
        var browser = new BrowserController();
        var normal = browser.SeedTab("https://example.org");
        var isolated = browser.SeedTab("https://example.org/isolated");
        browser.Tabs.SetSession(isolated.Id, "separate-session");
        var result = JsonDocument.Parse(await browser.Tool("duplicate_tabs", JsonSerializer.Serialize(new { ids = new[] { normal.Id, isolated.Id } })));
        Assert.False(result.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(2, browser.Tabs.Tabs.Count);
        var privateTab = browser.SeedTab("https://example.org/private", isPrivate: true);
        await Assert.ThrowsAsync<ArgumentException>(() => browser.Tool("duplicate_tabs", JsonSerializer.Serialize(new { ids = new[] { normal.Id, privateTab.Id } })));
        Assert.Equal(3, browser.Tabs.Tabs.Count);
    }

    [Fact]
    public async Task PinsBatchAndRejectsGroupedOrInvalidBatchBeforeChanges()
    {
        var browser = new BrowserController();
        var a = browser.SeedTab("https://example.org/a");
        var b = browser.SeedTab("https://example.org/b");
        var group = browser.Tabs.CreateGroup(b.Id, "Resources");
        var result = JsonDocument.Parse(await browser.Tool("pin_tabs", JsonSerializer.Serialize(new { ids = new[] { a.Id, b.Id }, pinned = true })));
        Assert.False(result.RootElement.GetProperty("ok").GetBoolean());
        Assert.All(browser.Tabs.Tabs, tab => Assert.False(tab.Pinned));
        browser.Tabs.RemoveFromGroup(b.Id);
        result = JsonDocument.Parse(await browser.Tool("pin_tabs", JsonSerializer.Serialize(new { ids = new[] { a.Id, b.Id }, pinned = true })));
        Assert.True(result.RootElement.GetProperty("ok").GetBoolean());
        Assert.All(browser.Tabs.Tabs, tab => Assert.True(tab.Pinned));
        await browser.Tool("pin_tabs", JsonSerializer.Serialize(new { ids = new[] { a.Id, b.Id }, pinned = false }));
        Assert.All(browser.Tabs.Tabs, tab => Assert.False(tab.Pinned));
    }
}
