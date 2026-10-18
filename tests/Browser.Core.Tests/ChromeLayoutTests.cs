using Browser.Core;

public class ChromeLayoutTests
{
    [Fact]
    public void Accepts_intermediate_frames_and_closed_sidebar()
    {
        Assert.Equal(new ChromeLayout(173.4, 47.84), ChromeLayout.From(new("ui.layout", X:173.4,Y:47.84,Width:826.6,Height:652.16),1000,700));
        Assert.Equal(new ChromeLayout(0,48), ChromeLayout.From(new("ui.layout",X:0,Y:48,Width:1000,Height:652),1000,700));
    }
    [Theory]
    [InlineData(-1,48,1001,652)]
    [InlineData(950,48,50,652)]
    [InlineData(220,800,780,1)]
    [InlineData(220,48,900,652)]
    [InlineData(double.NaN,48,780,652)]
    public void Rejects_invalid_or_stale_window_bounds(double x,double y,double width,double height)
        => Assert.Null(ChromeLayout.From(new("ui.layout",X:x,Y:y,Width:width,Height:height),1000,700));
}
