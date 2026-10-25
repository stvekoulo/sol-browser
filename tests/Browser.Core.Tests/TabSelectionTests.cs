using Browser.Core;

namespace Browser.Core.Tests;

public sealed class TabSelectionTests
{
    [Fact]
    public void GroupsScatteredTabsInTheirExistingOrderInOneChange()
    {
        var manager = new TabManager();
        var tabs = Enumerable.Range(0, 6).Select(i => manager.Open($"https://example.org/{i}")).ToArray();
        var active = manager.ActiveId;
        var changes = 0;
        manager.Changed += () => changes++;
        var group = manager.CreateGroupFromTabs([tabs[4].Id, tabs[1].Id, tabs[4].Id], "Recherche", "#ABCDEF", "📚");
        Assert.NotNull(group);
        Assert.Equal([tabs[0].Id, tabs[1].Id, tabs[4].Id, tabs[2].Id, tabs[3].Id, tabs[5].Id], manager.Tabs.Select(t => t.Id));
        Assert.Equal([tabs[1].Id, tabs[4].Id], manager.Tabs.Where(t => t.GroupId == group.Id).Select(t => t.Id));
        Assert.Equal("#abcdef", group.Color);
        Assert.Equal("📚", group.Icon);
        Assert.Equal(active, manager.ActiveId);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void MovingMiddleMembersDoesNotSplitTheOriginalGroup()
    {
        var manager = new TabManager();
        var tabs = Enumerable.Range(0, 7).Select(i => manager.Open($"https://example.org/{i}")).ToArray();
        var original = manager.CreateGroup(tabs[0].Id, "Original")!;
        foreach (var tab in tabs.Skip(1).Take(4)) manager.AddToGroup(tab.Id, original.Id);
        var created = manager.CreateGroupFromTabs([tabs[1].Id, tabs[3].Id, tabs[6].Id], "Choisis")!;
        Assert.Equal([tabs[1].Id, tabs[3].Id, tabs[6].Id], manager.Tabs.Where(t => t.GroupId == created.Id).Select(t => t.Id));
        Assert.Equal([tabs[0].Id, tabs[2].Id, tabs[4].Id], manager.Tabs.Where(t => t.GroupId == original.Id).Select(t => t.Id));
        foreach (var group in manager.Groups)
        {
            var positions = manager.Tabs.Select((t, i) => (t, i)).Where(x => x.t.GroupId == group.Id).Select(x => x.i).ToArray();
            Assert.Equal(positions.Length, positions[^1] - positions[0] + 1);
        }
    }

    [Fact]
    public void RejectsPinnedUnknownAndMixedPrivacySelectionsWithoutMutation()
    {
        var manager = new TabManager();
        var normal = manager.Open("https://example.org/a");
        var pinned = manager.Open("https://example.org/b", pinned: true);
        var privateTab = manager.Open("https://example.org/c", isPrivate: true);
        var before = manager.Tabs.ToArray();
        Assert.Null(manager.CreateGroupFromTabs([normal.Id, pinned.Id], "Choix"));
        Assert.Null(manager.CreateGroupFromTabs([normal.Id, "missing"], "Choix"));
        Assert.Null(manager.CreateGroupFromTabs([normal.Id, privateTab.Id], "Choix"));
        Assert.Null(manager.CreateGroupFromTabs([], "Choix"));
        Assert.Equal(before, manager.Tabs);
        Assert.Empty(manager.Groups);
    }

    [Fact]
    public void RegroupingEveryMemberPrunesOnlyTheEmptiedGroup()
    {
        var manager = new TabManager();
        var a = manager.Open("https://example.org/a");
        var b = manager.Open("https://example.org/b");
        var old = manager.CreateGroup(a.Id, "Ancien")!;
        manager.AddToGroup(b.Id, old.Id);
        var replacement = manager.CreateGroupFromTabs([b.Id, a.Id], "Nouveau")!;
        Assert.Equal(replacement, Assert.Single(manager.Groups));
        Assert.All(manager.Tabs, t => Assert.Equal(replacement.Id, t.GroupId));
    }
}
