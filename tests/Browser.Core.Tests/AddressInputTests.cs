using Browser.Core;

public class AddressInputTests
{
    [Fact]
    public void Notes_address_resolves_to_the_internal_page() => Assert.Equal(QuickNotes.PageUrl, AddressInput.Resolve(" SOL://notes/ ", "https://example.test?q={0}"));
    [Theory]
    [InlineData("/notes")]
    [InlineData(":notes")]
    [InlineData(" /NOTES ")]
    [InlineData(" :Notes ")]
    public void Notes_shortcuts_open_the_internal_page(string input) => Assert.Equal(QuickNotes.PageUrl, AddressInput.Resolve(input, Search));

    private const string Search = "https://www.google.com/search?q={0}";

    [Theory]
    [InlineData("https://a.test/x", "https://a.test/x")]
    [InlineData("http://a.test", "http://a.test")]
    [InlineData("about:blank", "about:blank")]
    [InlineData("github.com/WebKit", "https://github.com/WebKit")]
    [InlineData("  example.org  ", "https://example.org")]
    [InlineData("localhost:5173", "http://localhost:5173")]
    [InlineData("localhost", "http://localhost")]
    [InlineData("127.0.0.1:8080/api", "http://127.0.0.1:8080/api")]
    [InlineData("192.168.1.20", "http://192.168.1.20")]
    [InlineData("react hooks", "https://www.google.com/search?q=react%20hooks")]
    [InlineData("c# 12", "https://www.google.com/search?q=c%23%2012")]
    [InlineData("notes", "https://www.google.com/search?q=notes")]
    [InlineData("/notes exemple", "https://www.google.com/search?q=%2Fnotes%20exemple")]
    [InlineData("webkit", "https://www.google.com/search?q=webkit")]
    [InlineData("42", "https://www.google.com/search?q=42")]
    [InlineData("what is a.b", "https://www.google.com/search?q=what%20is%20a.b")]
    public void Resolves(string input, string expected) =>
        Assert.Equal(expected, AddressInput.Resolve(input, Search));
}
