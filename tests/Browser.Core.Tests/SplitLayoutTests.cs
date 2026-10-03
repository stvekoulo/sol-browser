using Browser.Core;

public class SplitLayoutTests
{
    [Fact]
    public void Open_sets_secondary_unless_it_is_the_active_tab()
    {
        var s = new SplitLayout();
        Assert.False(s.Open("t1", activeId: "t1"));
        Assert.False(s.IsSplit);
        Assert.True(s.Open("t2", activeId: "t1"));
        Assert.Equal("t2", s.SecondaryId);
    }

    [Fact]
    public void Resize_clamps_ratio()
    {
        var s = new SplitLayout();
        s.Resize(0.05); Assert.Equal(0.2, s.Ratio);
        s.Resize(0.95); Assert.Equal(0.8, s.Ratio);
        s.Resize(0.6); Assert.Equal(0.6, s.Ratio);
    }

    [Fact]
    public void Closing_the_secondary_tab_ends_the_split()
    {
        var s = new SplitLayout();
        s.Open("t2", "t1");
        s.OnTabClosed("t9"); Assert.True(s.IsSplit);
        s.OnTabClosed("t2"); Assert.False(s.IsSplit);
    }
}
