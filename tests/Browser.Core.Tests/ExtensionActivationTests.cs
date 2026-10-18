using Browser.Core;

public sealed class ExtensionActivationTests
{
    [Fact]
    public void Native_action_bridge_keeps_identity_and_does_not_expand_site_access()
    {
        var folder = Directory.CreateTempSubdirectory("sol-native-action-").FullName;
        try
        {
            var path = Path.Combine(folder, "manifest.json");
            File.WriteAllText(path, """{"manifest_version":3,"name":"T","version":"1","key":"original-key","action":{},"permissions":["activeTab","scripting"],"background":{"service_worker":"bg.js"}}""");
            File.WriteAllText(Path.Combine(folder, "bg.js"), "original worker");
            Assert.True(ExtensionShim.Apply(folder));
            var marker = ExtensionAction.Marker(folder);
            Assert.NotNull(marker);
            using var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            Assert.Equal("original-key", manifest.RootElement.GetProperty("key").GetString());
            Assert.False(manifest.RootElement.TryGetProperty("host_permissions", out _));
            Assert.Equal(new[] { "activeTab", "scripting", "contextMenus" },
                manifest.RootElement.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).ToArray());
            Assert.Equal("original worker", File.ReadAllText(Path.Combine(folder, "bg.js")));
            Assert.False(ExtensionShim.Apply(folder));
            Assert.Equal(marker, ExtensionAction.Marker(folder));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void Popup_without_background_gets_an_idempotent_native_tab_bridge()
    {
        var folder = Directory.CreateTempSubdirectory("sol-native-popup-").FullName;
        try
        {
            var path = Path.Combine(folder, "manifest.json");
            File.WriteAllText(path, """{"manifest_version":3,"name":"T","version":"1","action":{"default_popup":"popup.html"}}""");
            Assert.True(ExtensionShim.Apply(folder));
            Assert.NotNull(ExtensionAction.Marker(folder));
            Assert.Contains(ExtensionShim.TabsFile, File.ReadAllText(Path.Combine(folder, ExtensionShim.WorkerFile)));
            using var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            Assert.False(manifest.RootElement.TryGetProperty("host_permissions", out _));
            Assert.Equal("popup.html", manifest.RootElement.GetProperty("action").GetProperty("default_popup").GetString());
            Assert.False(ExtensionShim.Apply(folder));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void External_browser_handoff_is_not_a_browser_command()
    {
        Assert.Null(CommandParser.Parse("""{"type":"extension.edge"}"""));
        Assert.Null(CommandParser.Parse("""{"type":"extension.edge","id":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}"""));
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
