using Browser.Core;

public class CommandParserTests
{
    [Fact]
    public void Parses_a_valid_command()
    {
        var c = CommandParser.Parse("""{"type":"tab.move","id":"t3","index":1}""");
        Assert.Equal(new Command("tab.move", Id: "t3", Index: 1), c);
    }

    [Fact]
    public void Optional_field_may_be_absent()
    {
        Assert.Equal(new Command("tab.open"), CommandParser.Parse("""{"type":"tab.open"}"""));
        Assert.Equal("https://a.test", CommandParser.Parse("""{"type":"tab.open","url":"https://a.test"}""")!.Url);
    }

    [Fact]
    public void Accepts_host_lifecycle_commands()
    {
        Assert.NotNull(CommandParser.Parse("""{"type":"ui.ready"}"""));
        Assert.NotNull(CommandParser.Parse("""{"type":"window.drag"}"""));
    }

    [Fact]
    public void Palette_choices_require_an_identifier()
    {
        Assert.Equal(new Command("palette.open"), CommandParser.Parse("""{"type":"palette.open"}"""));
        Assert.Equal(new Command("palette.pick", Id: "1:2"), CommandParser.Parse("""{"type":"palette.pick","id":"1:2"}"""));
        Assert.Null(CommandParser.Parse("""{"type":"palette.pick"}"""));
        Assert.Null(CommandParser.Parse("""{"type":"palette.pick","id":2}"""));
    }

    [Fact]
    public void Parses_placement_numbers()
    {
        var c = CommandParser.Parse("""{"type":"suggest","id":"t1","query":"web","x":120,"y":84.5,"width":600}""");
        Assert.Equal(120, c!.X);
        Assert.Equal(84.5, c.Y);
        Assert.Equal(600, c.Width);
        Assert.Null(CommandParser.Parse("""{"type":"suggest","id":"t1","query":"web","x":120,"y":84.5}"""));
        Assert.Null(CommandParser.Parse("""{"type":"popup.size","height":"12"}"""));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"id":"t1"}""")]
    [InlineData("""{"type":"shell.exec","id":"t1"}""")]
    [InlineData("""{"type":"tab.close"}""")]
    [InlineData("""{"type":"tab.close","id":42}""")]
    [InlineData("""{"type":"tab.move","id":"t1","index":"1"}""")]
    [InlineData("""{"type":"tab.move","id":"t1","index":1.5}""")]
    [InlineData("""{"type":"split.resize","ratio":"half"}""")]
    [InlineData("""{"type":"bookmark.add","url":"https://a.test"}""")]
    public void Rejects_invalid_messages(string json) => Assert.Null(CommandParser.Parse(json));
}
