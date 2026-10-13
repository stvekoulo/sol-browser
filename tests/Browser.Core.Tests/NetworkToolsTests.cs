using Browser.Core;

public class NetworkToolsTests
{
    [Theory]
    [InlineData("{\"access_token\":\"fixture secret with spaces\",\"unfinished\":")]
    [InlineData("{\"password\":\"fixture secret with spaces")]
    [InlineData("api-key='fixture secret with spaces'; body=ok")]
    public void Truncated_or_non_json_bodies_still_mask_quoted_secrets(string text)
    {
        var redacted = NetworkTools.Redact(text);
        Assert.DoesNotContain("fixture secret with spaces", redacted);
        Assert.Contains(NetworkTools.Mask, redacted);
    }

    [Fact]
    public void Redaction_preserves_valid_json_and_keeps_the_mask_detectable_by_the_composer()
    {
        var redacted = NetworkTools.Redact("""{"token":"fixture-secret","message":"Bearer fixture-secret","lines":["password='fixture secret'"]}""");
        using var parsed = System.Text.Json.JsonDocument.Parse(redacted);
        Assert.Equal(NetworkTools.Mask, parsed.RootElement.GetProperty("token").GetString());
        Assert.Contains(NetworkTools.Mask, redacted);
        Assert.DoesNotContain("fixture-secret", redacted);
        Assert.DoesNotContain("fixture secret", redacted);
        Assert.Null(NetworkTools.Parse(System.Text.Json.JsonSerializer.Serialize(new NetworkDraft("https://site.test", "POST", [], redacted))));
    }

    [Theory]
    [InlineData("network.pause")]
    [InlineData("network.preserve")]
    public void Capture_options_require_a_tab_and_integer_value(string command)
    {
        Assert.NotNull(CommandParser.Parse($$"""{"type":"{{command}}","id":"t1","index":1}"""));
        Assert.Null(CommandParser.Parse($$"""{"type":"{{command}}","id":"t1","index":0.5}"""));
        Assert.Null(CommandParser.Parse($$"""{"type":"{{command}}","index":1}"""));
    }

    [Fact]
    public void Native_devtools_has_a_registered_keyboard_shortcut()
    {
        Assert.Contains(BrowserShortcuts.All, shortcut => shortcut.Action == "devtools.open" && shortcut.Key == 0x7B && shortcut.Modifiers == 0);
        Assert.NotNull(CommandParser.Parse("""{"type":"devtools.open"}"""));
        Assert.Null(CommandParser.Parse("""{"type":"network.export"}"""));
    }

    [Fact]
    public void Masks_known_secrets_and_validates_explicit_requests()
    {
        Assert.DoesNotContain("secret123", NetworkTools.Redact("{\"nested\":{\"access_token\":\"secret123\"}}"));
        Assert.DoesNotContain("secret123", NetworkTools.SafeUrl("https://user:secret123@site.test/?token=secret123&q=ok"));
        Assert.NotNull(NetworkTools.Parse("""{"url":"https://site.test/api","method":"GET","headers":{},"body":""}"""));
        foreach (var bad in new[] { "file:///C:/secret", "https://user:password@site.test", "https://site.test/?token=[masqué]" })
            Assert.Null(NetworkTools.Parse(System.Text.Json.JsonSerializer.Serialize(new NetworkDraft(bad,"GET",[],""))));
        Assert.Null(NetworkTools.Parse("""{"url":"https://site.test","method":"GET","headers":{"Cookie":"session=secret"},"body":""}"""));
        Assert.Null(NetworkTools.Parse("""{"url":"https://site.test","method":"GET","headers":{"X-Test":"a\r\nInjected: b"},"body":""}"""));
    }
}
