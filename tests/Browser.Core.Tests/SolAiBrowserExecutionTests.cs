using System.Text.Json;
using Browser.App;
using Browser.Core;

namespace Browser.Core.Tests;

public sealed class SolAiBrowserExecutionTests
{
    private static bool Ok(string json) => JsonDocument.Parse(json).RootElement.GetProperty("ok").GetBoolean();
    private const string Workspace = """{"title":"Recherche","urls":["https://www.youtube.com/","https://example.org/article"],"note_title":"Mes notes","note_text":"","layout":"side_by_side"}""";

    [Fact]
    public async Task PreparesGroupedResourcesAndEditableNoteWithoutReplacingExistingTabs()
    {
        var browser = new BrowserController();
        var existing = browser.SeedTab("https://existing.example.org/");
        Assert.True(Ok(await browser.Tool("prepare_workspace", Workspace)));
        Assert.Contains(existing, browser.Tabs.Tabs);
        Assert.Equal(4, browser.Tabs.Tabs.Count);
        var group = Assert.Single(browser.Tabs.Groups);
        var members = browser.Tabs.Tabs.Where(t => t.GroupId == group.Id).ToArray();
        Assert.Equal(3, members.Length);
        Assert.All(members, tab => Assert.False(tab.Private));
        Assert.Equal("https://www.youtube.com/", members[0].Url);
        Assert.Equal(QuickNotes.PageUrl, members[2].Url);
        var note = Assert.Single(browser.Notes.Items);
        Assert.Equal("Mes notes", note.Title);
        Assert.Contains("# Mes notes", note.Text);
        Assert.Equal(members[0].Id, browser.Layout.PrimaryId);
        Assert.Equal(members[2].Id, browser.Layout.SecondaryId);
    }

    [Fact]
    public async Task InvalidWorkspaceOrUnsavedDraftHasNoPartialEffects()
    {
        var browser = new BrowserController();
        Assert.False(Ok(await browser.Tool("prepare_workspace", Workspace.Replace("side_by_side", "three_panes"))));
        Assert.Empty(browser.Tabs.Tabs); Assert.Empty(browser.Notes.Items);
        browser.NoteDraftChanged = true;
        Assert.False(Ok(await browser.Tool("prepare_workspace", Workspace)));
        Assert.False(Ok(await browser.Tool("open_notes", """{"id":""}""")));
        Assert.Empty(browser.Tabs.Tabs); Assert.Empty(browser.Notes.Items);
        await Assert.ThrowsAsync<ArgumentException>(() => browser.Tool("open_tabs", """{"urls":["https://example.org/","javascript:alert(1)"]}"""));
        Assert.Empty(browser.Tabs.Tabs);
    }

    [Fact]
    public async Task PrivateTabsAndPrivateContextAreUnavailable()
    {
        var browser = new BrowserController();
        var normal = browser.SeedTab("https://example.org/");
        var secret = browser.SeedTab("https://secret.example.org/", isPrivate: true);
        var listed = await browser.Tool("list_tabs");
        Assert.DoesNotContain("secret.example.org", listed);
        Assert.False(Ok(await browser.Tool("activate_tab", JsonSerializer.Serialize(new { id = secret.Id }))));
        browser.PrivateContext = true;
        Assert.False(Ok(await browser.Tool("open_tabs", """{"urls":["https://example.org/"]}""")));
        browser.PrivateContext = false; browser.Ready = false;
        Assert.False(Ok(await browser.Tool("prepare_workspace", Workspace)));
        Assert.Equal(2, browser.Tabs.Tabs.Count);
    }

    [Fact]
    public async Task KeepsSplitPairsTogetherAndPreservesPinnedTabs()
    {
        var browser = new BrowserController();
        var first = browser.SeedTab("https://example.org/one");
        var second = browser.SeedTab("https://example.org/two");
        var pinned = browser.SeedTab("https://example.org/pinned", pinned: true);
        Assert.True(Ok(await browser.Tool("split_tabs", JsonSerializer.Serialize(new { ids = new[] { first.Id, second.Id } }))));
        Assert.False(Ok(await browser.Tool("group_tabs", JsonSerializer.Serialize(new { ids = new[] { first.Id }, title = "incomplete" }))));
        Assert.False(Ok(await browser.Tool("group_tabs", JsonSerializer.Serialize(new { ids = new[] { pinned.Id }, title = "pinned" }))));
        Assert.True(Ok(await browser.Tool("group_tabs", JsonSerializer.Serialize(new { ids = new[] { first.Id, second.Id }, title = "Travail" }))));
        Assert.True(Ok(await browser.Tool("close_split")));
        Assert.False(browser.Layout.IsSplit); Assert.Equal(3, browser.Tabs.Tabs.Count);
    }

    [Fact]
    public async Task CancellationPreventsSideEffects()
    {
        var browser = new BrowserController();
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => browser.Tool("prepare_workspace", Workspace, cancelled.Token));
        Assert.Empty(browser.Tabs.Tabs); Assert.Empty(browser.Notes.Items);
    }
}
