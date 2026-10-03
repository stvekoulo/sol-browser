using Browser.Core;

public class TabManagerTests
{
    [Fact]
    public void Open_adds_tab_and_activates_it()
    {
        var m = new TabManager();
        var t = m.Open("https://a.test");
        Assert.Single(m.Tabs);
        Assert.Equal(t.Id, m.ActiveId);
    }

    [Fact]
    public void Open_in_background_keeps_active_tab()
    {
        var m = new TabManager();
        var a = m.Open("https://a.test");
        m.Open("https://b.test", activate: false);
        Assert.Equal(a.Id, m.ActiveId);
    }

    [Fact]
    public void Closing_active_tab_activates_the_one_that_takes_its_place()
    {
        var m = new TabManager();
        var a = m.Open("https://a.test");
        var b = m.Open("https://b.test");
        var c = m.Open("https://c.test");
        m.Activate(b.Id);
        Assert.True(m.Close(b.Id));
        Assert.Equal(c.Id, m.ActiveId);
        m.Close(c.Id);
        Assert.Equal(a.Id, m.ActiveId);
    }

    [Fact]
    public void Closing_last_tab_leaves_no_active_tab()
    {
        var m = new TabManager();
        m.Close(m.Open("https://a.test").Id);
        Assert.Null(m.ActiveId);
    }

    [Fact]
    public void Unknown_ids_are_rejected()
    {
        var m = new TabManager();
        Assert.False(m.Close("nope"));
        Assert.False(m.Activate("nope"));
        Assert.False(m.Move("nope", 0));
        Assert.False(m.Update("nope", title: "x"));
    }

    [Fact]
    public void Move_reorders_and_rejects_out_of_range()
    {
        var m = new TabManager();
        var a = m.Open("https://a.test");
        var b = m.Open("https://b.test");
        Assert.True(m.Move(b.Id, 0));
        Assert.Equal(new[] { b.Id, a.Id }, m.Tabs.Select(t => t.Id));
        Assert.False(m.Move(b.Id, 2));
        Assert.False(m.Move(b.Id, -1));
    }

    [Fact]
    public void Pinning_moves_the_tab_to_the_end_of_the_pinned_zone()
    {
        var m = new TabManager();
        var a = m.Open("https://a.test");
        var b = m.Open("https://b.test");
        var c = m.Open("https://c.test");
        Assert.True(m.SetPinned(c.Id, true));
        Assert.True(m.SetPinned(b.Id, true));
        Assert.Equal(new[] { c.Id, b.Id, a.Id }, m.Tabs.Select(t => t.Id));
        Assert.Equal(2, m.PinnedCount);
    }

    [Fact]
    public void Unpinning_moves_the_tab_to_the_start_of_the_other_tabs()
    {
        var m = new TabManager();
        var a = m.Open("https://a.test");
        var b = m.Open("https://b.test");
        var c = m.Open("https://c.test");
        m.SetPinned(a.Id, true);
        m.SetPinned(b.Id, true);
        Assert.True(m.SetPinned(a.Id, false));
        Assert.Equal(new[] { b.Id, a.Id, c.Id }, m.Tabs.Select(t => t.Id));
        Assert.False(m.SetPinned("nope", true));
    }

    [Fact]
    public void Move_keeps_tabs_inside_their_zone()
    {
        var m = new TabManager();
        var a = m.Open("https://a.test");
        var b = m.Open("https://b.test");
        var c = m.Open("https://c.test");
        m.SetPinned(a.Id, true);
        m.Move(a.Id, 2); // un épinglé ne sort pas de la tête
        Assert.Equal(new[] { a.Id, b.Id, c.Id }, m.Tabs.Select(t => t.Id));
        m.Move(c.Id, 0); // un onglet ordinaire ne passe pas devant les épinglés
        Assert.Equal(new[] { a.Id, c.Id, b.Id }, m.Tabs.Select(t => t.Id));
    }

    [Fact]
    public void Opening_a_pinned_tab_places_it_after_the_pinned_ones()
    {
        var m = new TabManager();
        var a = m.Open("https://a.test");
        var p = m.Open("https://p.test", activate: false, pinned: true);
        Assert.Equal(new[] { p.Id, a.Id }, m.Tabs.Select(t => t.Id));
        Assert.True(m.Tabs[0].Pinned);
    }

    [Fact]
    public void ReopenClosed_restores_last_closed_url()
    {
        var m = new TabManager();
        m.Open("https://keep.test");
        m.Close(m.Open("https://gone.test").Id);
        Assert.Equal("https://gone.test", m.ReopenClosed()!.Url);
        Assert.Null(m.ReopenClosed());
    }

    [Fact]
    public void Update_changes_url_and_title_and_raises_Changed()
    {
        var m = new TabManager();
        var t = m.Open("https://a.test");
        var raised = 0;
        m.Changed += () => raised++;
        Assert.True(m.Update(t.Id, url: "https://b.test", title: "B"));
        Assert.Equal(new Tab(t.Id, "https://b.test", "B"), m.Tabs[0]);
        Assert.Equal(1, raised);
    }
}
