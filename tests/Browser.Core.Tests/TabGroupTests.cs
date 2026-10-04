using Browser.Core;

public class TabGroupTests
{
    private static (TabManager M, Tab A, Tab B, Tab C, Tab D) Four()
    {
        var m = new TabManager();
        return (m, m.Open("https://a.test"), m.Open("https://b.test"), m.Open("https://c.test"), m.Open("https://d.test"));
    }

    private static string Layout(TabManager m) => string.Join(" ", m.Tabs.Select(t => t.Url[8..9] + (t.GroupId ?? "")));

    [Fact]
    public void CreateGroup_names_and_colours_the_group()
    {
        var (m, a, _, c, _) = Four();
        var g1 = m.CreateGroup(a.Id, "  Travail  ")!;
        var g2 = m.CreateGroup(c.Id, "", "#FF8800", icon: "📚")!;
        Assert.Equal(("Travail", "#8fa8e8"), (g1.Name, g1.Color));
        Assert.Equal(("Groupe", "#ff8800", "📚"), (g2.Name, g2.Color, g2.Icon));
        Assert.Equal("ag1 b cg2 d", Layout(m));
    }

    [Fact]
    public void A_pinned_tab_cannot_be_grouped_and_pinning_leaves_the_group()
    {
        var (m, a, b, _, _) = Four();
        m.SetPinned(a.Id, true);
        Assert.Null(m.CreateGroup(a.Id, "x"));
        var g = m.CreateGroup(b.Id, "x")!;
        Assert.False(m.AddToGroup(a.Id, g.Id));
        m.SetPinned(b.Id, true);
        Assert.Empty(m.Groups);
        Assert.All(m.Tabs, t => Assert.Null(t.GroupId));
    }

    [Fact]
    public void AddToGroup_moves_the_tab_to_the_end_of_the_group()
    {
        var (m, a, b, _, d) = Four();
        var g = m.CreateGroup(a.Id, "x")!;
        Assert.True(m.AddToGroup(d.Id, g.Id));
        Assert.Equal("ag1 dg1 b c", Layout(m));
        Assert.True(m.AddToGroup(b.Id, g.Id));
        Assert.Equal("ag1 dg1 bg1 c", Layout(m));
        Assert.False(m.AddToGroup(b.Id, "nope"));
    }

    [Fact]
    public void RemoveFromGroup_keeps_the_rest_of_the_group_together()
    {
        var (m, a, b, c, _) = Four();
        var g = m.CreateGroup(a.Id, "x")!;
        m.AddToGroup(b.Id, g.Id);
        m.AddToGroup(c.Id, g.Id);
        Assert.True(m.RemoveFromGroup(b.Id)); // au milieu : il sort après le groupe
        Assert.Equal("ag1 cg1 b d", Layout(m));
        Assert.True(m.RemoveFromGroup(a.Id)); // au bord : il reste sur place
        Assert.Equal("a cg1 b d", Layout(m));
        Assert.False(m.RemoveFromGroup(a.Id));
    }

    [Fact]
    public void An_empty_group_disappears()
    {
        var (m, a, _, _, _) = Four();
        m.CreateGroup(a.Id, "x");
        m.Close(a.Id);
        Assert.Empty(m.Groups);
    }

    [Fact]
    public void Moving_a_tab_between_two_members_joins_the_group()
    {
        var (m, a, b, _, d) = Four();
        var g = m.CreateGroup(a.Id, "x")!;
        m.AddToGroup(b.Id, g.Id);
        m.Move(d.Id, 1);
        Assert.Equal("ag1 dg1 bg1 c", Layout(m));
    }

    [Fact]
    public void Moving_a_tab_away_from_its_group_leaves_it()
    {
        var (m, a, b, _, _) = Four();
        var g = m.CreateGroup(a.Id, "x")!;
        m.AddToGroup(b.Id, g.Id);
        m.Move(a.Id, 3);
        Assert.Equal("bg1 c d a", Layout(m));
    }

    [Fact]
    public void Moving_inside_the_group_or_moving_a_one_tab_group_keeps_the_group()
    {
        var (m, a, b, c, _) = Four();
        var g = m.CreateGroup(a.Id, "x")!;
        m.AddToGroup(b.Id, g.Id);
        m.AddToGroup(c.Id, g.Id);
        m.Move(a.Id, 1);
        Assert.Equal("bg1 ag1 cg1 d", Layout(m));

        var solo = new TabManager();
        var s = solo.Open("https://s.test");
        solo.Open("https://t.test");
        solo.CreateGroup(s.Id, "seul");
        solo.Move(s.Id, 1);
        Assert.Equal("t sg1", Layout(solo));
    }

    [Fact]
    public void UpdateGroup_renames_recolours_and_rejects_unknown_values()
    {
        var (m, a, _, _, _) = Four();
        var g = m.CreateGroup(a.Id, "x")!;
        Assert.True(m.UpdateGroup(g.Id, name: "Lecture", color: "#12ab9c", icon: "🎬"));
        Assert.True(m.UpdateGroup(g.Id, name: "   ", color: "fuchsia fluo")); // ignorés
        Assert.Equal(new TabGroup(g.Id, "Lecture", "#12ab9c", Icon: "🎬"), m.Groups[0]);
        Assert.False(m.UpdateGroup("nope", name: "y"));
    }

    [Theory]
    [InlineData("#A1B2C3", "#a1b2c3")]
    [InlineData("blue", "#8fa8e8")] // nom d'une ancienne session
    [InlineData("#12345", null)]
    [InlineData("#12345g", null)]
    [InlineData("red; background:url(x)", null)]
    [InlineData("rgb(1,2,3)", null)]
    [InlineData(null, null)]
    public void NormalizeColor_accepts_only_plain_hex_colours(string? input, string? expected) =>
        Assert.Equal(expected, TabManager.NormalizeColor(input));

    [Theory]
    [InlineData("📚", "📚")]
    [InlineData("  ⭐  ", "⭐")]
    [InlineData("👨‍👩‍👧‍👦", "👨‍👩‍👧‍👦")]
    [InlineData("un texte beaucoup trop long pour une icône", "")]
    [InlineData(null, "")]
    public void NormalizeIcon_keeps_short_icons_only(string? input, string expected) =>
        Assert.Equal(expected, TabManager.NormalizeIcon(input));

    [Fact]
    public void An_icon_can_be_removed()
    {
        var (m, a, _, _, _) = Four();
        var g = m.CreateGroup(a.Id, "x", icon: "🎮")!;
        m.UpdateGroup(g.Id, icon: "un texte beaucoup trop long pour une icône"); // ignoré
        Assert.Equal("🎮", m.Groups[0].Icon);
        m.UpdateGroup(g.Id, icon: "");
        Assert.Equal("", m.Groups[0].Icon);
    }

    [Fact]
    public void Collapsing_the_group_of_the_active_tab_activates_a_tab_outside_it()
    {
        var (m, a, b, c, _) = Four();
        var g = m.CreateGroup(a.Id, "x")!;
        m.AddToGroup(b.Id, g.Id);
        m.Activate(b.Id);
        Assert.True(m.UpdateGroup(g.Id, collapsed: true));
        Assert.True(m.Groups[0].Collapsed);
        Assert.Equal(c.Id, m.ActiveId);
    }

    [Fact]
    public void A_group_holding_every_tab_cannot_collapse()
    {
        var m = new TabManager();
        var a = m.Open("https://a.test");
        var g = m.CreateGroup(a.Id, "x")!;
        Assert.False(m.UpdateGroup(g.Id, collapsed: true));
        Assert.False(m.Groups[0].Collapsed);
    }

    [Fact]
    public void Ungroup_keeps_the_tabs_open()
    {
        var (m, a, b, _, _) = Four();
        var g = m.CreateGroup(a.Id, "x")!;
        m.AddToGroup(b.Id, g.Id);
        Assert.True(m.Ungroup(g.Id));
        Assert.Empty(m.Groups);
        Assert.Equal("a b c d", Layout(m));
    }

    [Fact]
    public void MatchGroup_copies_the_group_without_moving()
    {
        var (m, a, b, _, _) = Four();
        m.CreateGroup(a.Id, "x");
        m.MatchGroup(b.Id, a.Id);
        Assert.Equal("ag1 bg1 c d", Layout(m));
        m.MatchGroup(a.Id, m.Tabs[2].Id); // vers un onglet sans groupe
        Assert.Equal("a bg1 c d", Layout(m));
    }

    [Fact]
    public void Dragging_a_tab_past_a_grouped_tab_joins_its_group_and_past_a_loose_tab_leaves_it()
    {
        var m = new TabManager();
        var (a, b, c) = (m.Open("https://a.test"), m.Open("https://b.test"), m.Open("https://c.test"));
        var group = m.CreateGroup(b.Id, "Seul")!.Id; // un groupe d'un seul onglet
        string? GroupOf(Tab tab) => m.Tabs.First(t => t.Id == tab.Id).GroupId;

        Assert.True(m.Move(a.Id, 1)); // a passe sous b : il entre dans le groupe, par le bas
        Assert.Equal([b.Id, a.Id, c.Id], m.Tabs.Select(t => t.Id));
        Assert.Equal(group, GroupOf(a));

        Assert.True(m.Move(a.Id, 2)); // a passe sous c, sans groupe : il sort
        Assert.Null(GroupOf(a));

        Assert.True(m.Move(c.Id, 0)); // c remonte au-dessus de b : il entre dans le groupe, par le haut
        Assert.Equal(group, GroupOf(c));
        Assert.True(m.Move(c.Id, 2)); // puis redescend sous a
        Assert.Null(GroupOf(c));

        Assert.True(m.Move(b.Id, 1)); // b, seul dans son groupe, dépasse a : le groupe se déplace avec lui
        Assert.Equal(group, GroupOf(b));
        Assert.Null(GroupOf(a));
    }
}
