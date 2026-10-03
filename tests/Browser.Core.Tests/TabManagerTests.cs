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
