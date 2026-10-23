using System.Text.Json;
using Browser.Core;

namespace Browser.Core.Tests;

public sealed class SolAiBrowserToolsTests
{
    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/secret.txt")]
    [InlineData("data:text/html,test")]
    [InlineData("https://user:pass@example.org/")]
    [InlineData("https://start.sol/index.html")]
    [InlineData("https://ui.navigateur/index.html#chat")]
    [InlineData("https://example.org/\nsecret")]
    public void RejectsNonWebCredentialsInternalPagesAndControlCharacters(string url) =>
        Assert.Throws<ArgumentException>(() => SolAiBrowserTools.Urls(JsonSerializer.Serialize(new { urls = new[] { "https://example.org/", url } })));

    [Fact]
    public void AcceptsWebAddressesAndPreservesQueryAndFragment()
    {
        var urls = SolAiBrowserTools.Urls("""{"urls":["https://example.org/?q=bonjour%20Sol#article","http://localhost:4000/form"]}""");
        Assert.Equal(["https://example.org/?q=bonjour%20Sol#article", "http://localhost:4000/form"], urls);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"urls\":[]}")]
    [InlineData("{\"urls\":[null]}")]
    [InlineData("{\"urls\":[42]}")]
    [InlineData("{\"urls\":\"https://example.org\"}")]
    public void RejectsInvalidBatchesBeforeOpeningAnything(string arguments) =>
        Assert.Throws<ArgumentException>(() => SolAiBrowserTools.Urls(arguments));

    [Fact]
    public void BoundsTheNumberOfResources()
    {
        Assert.Throws<ArgumentException>(() => SolAiBrowserTools.Urls(JsonSerializer.Serialize(new { urls = Enumerable.Repeat("https://example.org", 9) })));
        Assert.Throws<ArgumentException>(() => SolAiBrowserTools.ArrayArgument("""{"ids":["t1","t2","t3"]}""", "ids", 2, 2));
    }

    [Fact]
    public void ExposesStrictCompleteToolSchemasAndApprovalForFormChanges()
    {
        var names = new HashSet<string>();
        foreach (var tool in SolAiAgent.PageTools.Concat(SolAiAgent.NoteTools).Concat(SolAiBrowserTools.Tools))
        {
            var json = JsonSerializer.SerializeToElement(tool);
            Assert.True(names.Add(json.GetProperty("name").GetString()!));
            Assert.True(json.GetProperty("strict").GetBoolean());
            var parameters = json.GetProperty("parameters");
            Assert.False(parameters.GetProperty("additionalProperties").GetBoolean());
            Assert.Equal(parameters.GetProperty("properties").EnumerateObject().Select(p => p.Name).Order(),
                parameters.GetProperty("required").EnumerateArray().Select(p => p.GetString()).Order());
        }
        Assert.All(new[] { "fill_form", "press_key", "select_option", "set_checked", "close_tabs", "browser_history" },
            name => Assert.True(SolAiAgent.NeedsApproval(name)));
        Assert.All(new[] { "list_tabs", "open_tabs", "prepare_workspace", "read_tab" }, name =>
        { Assert.True(SolAiBrowserTools.Handles(name)); Assert.False(SolAiAgent.NeedsApproval(name)); });
    }
}
