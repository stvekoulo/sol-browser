using Browser.Core;

public class SplitLayoutTests
{
    [Fact]
    public void Narrow_pages_keep_a_usable_width_and_invalid_ratios_are_ignored()
    {
        Assert.Equal(0.5, SplitLayout.FitRatio(0.2, 400));
        Assert.Equal(220.0 / 600, SplitLayout.FitRatio(0.2, 600));
        Assert.Equal(0.2, SplitLayout.FitRatio(0.2, 1500));
        var split = new SplitLayout(); split.Resize(0.6); split.Resize(double.NaN);
        Assert.Equal(0.6, split.Ratio);
    }

    [Fact]
    public void Open_pairs_two_distinct_tabs()
    {
        var s = new SplitLayout();
        Assert.False(s.Open("t1", "t1"));
        Assert.False(s.IsSplit);
        Assert.True(s.Open("t1", "t2"));
        Assert.Equal(("t1", "t2"), (s.PrimaryId, s.SecondaryId));
    }

    [Fact]
    public void Contains_is_true_only_for_the_pair()
    {
        var s = new SplitLayout();
        Assert.False(s.Contains("t1"));
        s.Open("t1", "t2");
        Assert.True(s.Contains("t1"));
        Assert.True(s.Contains("t2"));
        Assert.False(s.Contains("t3"));
        Assert.False(s.Contains(null));
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
    public void Closing_either_tab_of_the_pair_ends_the_split()
    {
        var s = new SplitLayout();
        s.Open("t1", "t2");
        s.OnTabClosed("t9"); Assert.True(s.IsSplit);
        s.OnTabClosed("t1"); Assert.False(s.IsSplit);
        s.Open("t1", "t2");
        s.OnTabClosed("t2"); Assert.False(s.IsSplit);
    }
}
