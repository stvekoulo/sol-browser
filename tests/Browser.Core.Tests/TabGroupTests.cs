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
        var g2 = m.CreateGroup(c.Id, "", "pink")!;
        Assert.Equal(("Travail", "blue"), (g1.Name, g1.Color));
        Assert.Equal(("Groupe", "pink"), (g2.Name, g2.Color));
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
        Assert.True(m.UpdateGroup(g.Id, name: "Lecture", color: "green"));
        Assert.True(m.UpdateGroup(g.Id, name: "   ", color: "fuchsia fluo")); // ignorés
        Assert.Equal(new TabGroup(g.Id, "Lecture", "green"), m.Groups[0]);
        Assert.False(m.UpdateGroup("nope", name: "y"));
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
}
