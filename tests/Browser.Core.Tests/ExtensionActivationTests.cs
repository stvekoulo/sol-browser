using Browser.Core;

public sealed class ExtensionActivationTests
{
    [Fact]
    public void Edge_handoff_command_requires_an_extension_id()
    {
        Assert.Null(CommandParser.Parse("""{"type":"extension.edge"}"""));
        Assert.Equal("extension.edge", CommandParser.Parse("""{"type":"extension.edge","id":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}""")?.Type);
    }

    [Theory]
    [InlineData("""{"action":{"default_popup":"popup.html"}}""", "popup.html", null)]
    [InlineData("""{"side_panel":{"default_path":"panel/index.html"}}""", null, "panel/index.html")]
    [InlineData("""{"action":{"default_popup":""},"side_panel":{"default_path":"panel.html"}}""", null, "panel.html")]
    [InlineData("""{"action":{"default_title":"PromptCard"},"options_page":"settings.html"}""", null, null)]
    [InlineData("""{"action":{"default_popup":"popup.html"},"side_panel":{"default_path":"panel.html"}}""", "popup.html", "panel.html")]
    public void Reads_popup_and_panel_separately_from_options(string properties, string? popup, string? panel)
    {
        var folder = Path.Combine(Path.GetTempPath(), "sol-extension-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "manifest.json"),
                "{\"name\":\"Fixture\",\"version\":\"1\"," + properties[1..]);
            var info = Assert.IsType<ExtensionInfo>(ExtensionCatalog.Read(folder, new string('a', 32)));
            Assert.Equal(popup, info.Popup);
            Assert.Equal(panel, info.SidePanel);
            if (properties.Contains("settings.html")) Assert.Equal("settings.html", info.Options);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }
}
