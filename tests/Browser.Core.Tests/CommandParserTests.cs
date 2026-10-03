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
