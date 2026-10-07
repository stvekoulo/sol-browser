using Browser.Core;

public class PageTranslationTests
{
    [Theory]
    [InlineData("fr-FR", "fr", "en")]
    [InlineData("EN_us", "en", "fr")]
    [InlineData("es", "fr", "fr")]
    [InlineData("ja", "en", "en")]
    [InlineData("", "fr", "fr")]
    public void ProposesOneLanguage(string page, string browser, string expected) =>
        Assert.Equal(expected, PageTranslation.TargetLanguage(page, browser));
    [Fact]
    public void TranslationEncodesTheWholePublicAddressAndRejectsInvalidTargets()
    {
        const string source = "https://example.com/page?q=a&lang=en#section";
        Assert.Equal("https://translate.google.com/translate?sl=auto&tl=fr&u=" + Uri.EscapeDataString(source), PageTranslation.GoogleUrl(source, "fr"));
        foreach (var address in new[] { "file:///secret", "http://localhost/page", "http://127.0.0.1/", "https://user:secret@example.com", "javascript:alert(1)" })
            Assert.Null(PageTranslation.GoogleUrl(address, "en"));
        Assert.Null(PageTranslation.GoogleUrl(source, "invalid"));
        Assert.True(PageTranslation.IsGooglePage("https://example-com.translate.goog/page?_x_tr_tl=fr"));
        Assert.False(PageTranslation.IsGooglePage("https://translate.google.com.example.org/"));
    }
}
