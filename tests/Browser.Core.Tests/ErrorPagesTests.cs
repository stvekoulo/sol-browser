using Browser.Core;

public class ErrorPagesTests
{
    [Theory]
    [InlineData("reset", "Connexion interrompue")]
    [InlineData("response", "Réponse du serveur invalide")]
    [InlineData("redirect", "Redirection impossible")]
    [InlineData("connect", "Site inaccessible")]
    public void Distinguishes_server_failures_from_being_offline(string kind, string title)
    {
        var description = ErrorPages.Describe(kind);
        Assert.Equal(title, description.Title);
        Assert.DoesNotContain("pas connecté", description.Text);
    }

    [Fact]
    public void Shows_the_engine_status_without_allowing_markup()
    {
        var html = ErrorPages.Html("https://example.test", "other", status: "<script>Unknown</script>");
        Assert.Contains("&lt;script&gt;Unknown&lt;/script&gt;", html);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("Cette page ne peut pas", html);
    }

    [Fact]
    public void Shows_the_address_and_a_retry_link()
    {
        var html = ErrorPages.Html("https://exemple.test/page", "dns");
        Assert.Contains("<title>Site introuvable</title>", html);
        Assert.Contains("<code>https://exemple.test/page</code>", html);
        Assert.Contains("""<a class="retry" href="https://exemple.test/page">""", html);
    }

    [Fact]
    public void Escapes_an_address_that_tries_to_inject_markup()
    {
        var html = ErrorPages.Html("""https://x.test/"><script>alert(1)</script>""", "offline");
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("""x.test/"><""", html);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("pas une adresse")]
    public void Offers_no_link_for_anything_but_a_web_address(string url) =>
        Assert.DoesNotContain("href=", ErrorPages.Html(url, "other"));

    [Fact]
    public void Offers_no_way_past_a_certificate_error()
    {
        var html = ErrorPages.Html("https://banque.test/", "certificate");
        // Les lettres accentuées sortent sous forme d'entités HTML : on compare le texte une fois décodé.
        Assert.Contains("Connexion non sécurisée", System.Net.WebUtility.HtmlDecode(html));
        Assert.DoesNotContain("href=", html);
    }

    [Fact]
    public void Has_no_script_and_no_external_resource()
    {
        var html = ErrorPages.Html("https://exemple.test/", "crash");
        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("src=", html);
        Assert.DoesNotContain("@import", html);
    }
}
