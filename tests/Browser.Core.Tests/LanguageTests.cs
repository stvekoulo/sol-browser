using Browser.Core;

public class LanguageTests
{
    [Theory]
    [InlineData("system", "fr-FR", "fr")]
    [InlineData("system", "fr-BJ", "fr")]
    [InlineData("system", "en-US", "en")]
    [InlineData("system", "FR", "fr")]
    [InlineData("system", "de-DE", "en")] // langue non prise en charge : anglais
    [InlineData("system", "", "en")]
    [InlineData("system", null, "en")]
    [InlineData("en", "fr-FR", "en")] // le choix de l'utilisateur passe avant Windows
    [InlineData("fr", "en-US", "fr")]
    [InlineData("klingon", "fr-FR", "fr")] // réglage inconnu : comme « system »
    [InlineData(null, "ja-JP", "en")]
    public void Resolve_prefers_the_setting_then_the_system_then_english(string? setting, string? system, string expected) =>
        Assert.Equal(expected, Language.Resolve(setting, system));

    [Theory]
    [InlineData("https://www.YouTube.com/watch?v=1", "youtube.com")]
    [InlineData("https://github.com", "github.com")]
    [InlineData("https://gist.github.com/x", "gist.github.com")]
    [InlineData("about:blank", "")]
    [InlineData("pas une adresse", "")]
    [InlineData(null, "")]
    public void Shortcut_domain_ignores_www_and_case(string? url, string expected) =>
        Assert.Equal(expected, SiteShortcut.DomainOf(url));

    [Theory]
    [InlineData("https://fr.wikipedia.org/wiki/Blink", "Wikipedia")]
    [InlineData("https://devbenin.bj", "Devbenin")]
    [InlineData("https://www.bbc.co.uk/news", "Bbc")]
    [InlineData("http://localhost:3000", "Localhost")]
    [InlineData("about:blank", "")]
    public void Shortcut_name_comes_from_the_domain(string url, string expected) =>
        Assert.Equal(expected, SiteShortcut.NameOf(url));

    [Fact]
    public void Error_pages_exist_in_both_languages()
    {
        Assert.Contains("<title>Site not found</title>", ErrorPages.Html("https://exemple.test/", "dns", "en"));
        Assert.Contains("""<html lang="en" """, ErrorPages.Html("https://exemple.test/", "dns", "en"));
        Assert.Contains(">Try again</a>", ErrorPages.Html("https://exemple.test/", "dns", "en"));
        Assert.Contains("<title>Site introuvable</title>", ErrorPages.Html("https://exemple.test/", "dns", "fr"));
    }
}
