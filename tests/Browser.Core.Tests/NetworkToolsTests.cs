using Browser.Core;

public class NetworkToolsTests
{
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
