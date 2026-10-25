using Browser.Core;
using System.Text.Json.Nodes;

namespace Browser.Core.Tests;

public sealed class WebAppManifestTests
{
    private const string Manifest = """
        {"name":"Mon application","start_url":"./start","display":"standalone",
        "icons":[{"src":"192.png","sizes":"192x192"},{"src":"512.png","sizes":"512x512"}]}
        """;

    [Fact]
    public void Resolves_launch_address_from_the_manifest_and_accepts_local_development()
    {
        var app = WebAppManifest.Parse("https://example.org/page", "https://example.org/app/manifest.json", Manifest);
        Assert.NotNull(app);
        Assert.Equal("Mon application", app.Name);
        Assert.Equal("https://example.org/app/start", app.StartUrl);
        Assert.NotNull(WebAppManifest.Parse("http://127.0.0.1:8000/page", "http://127.0.0.1:8000/manifest.json", Manifest));
    }

    [Theory]
    [InlineData("http://example.org/page", "http://example.org/manifest.json")]
    [InlineData("https://other.test/page", "https://example.org/manifest.json")]
    [InlineData("file:///page.html", "file:///manifest.json")]
    public void Rejects_insecure_pages_and_launches_on_a_different_origin(string page, string manifest)
        => Assert.Null(WebAppManifest.Parse(page, manifest, Manifest));

    [Theory]
    [InlineData("display", "browser")]
    [InlineData("start_url", "javascript:alert(1)")]
    [InlineData("start_url", "https://other.test/")]
    [InlineData("name", "")]
    public void Does_not_advertise_ineligible_manifests(string key, string value)
    {
        var json = JsonNode.Parse(Manifest)!;
        json[key] = value;
        Assert.Null(WebAppManifest.Parse("https://example.org/", "https://example.org/manifest.json", json.ToJsonString()));
    }

    [Fact]
    public void Requires_icons_and_does_not_offer_a_preferred_native_app_as_a_pwa()
    {
        var json = JsonNode.Parse(Manifest)!;
        json["prefer_related_applications"] = true;
        Assert.Null(Parse(json));
        json["prefer_related_applications"] = false;
        json["icons"]!.AsArray().RemoveAt(1);
        Assert.Null(Parse(json));
        Assert.Null(WebAppManifest.Parse("https://example.org/", "https://example.org/manifest.json", "not json"));
    }

    [Fact]
    public void Supports_short_names_and_display_override_without_changing_the_visited_site()
    {
        var json = JsonNode.Parse(Manifest)!;
        json.AsObject().Remove("name");
        json["short_name"] = "Sol Notes";
        json["display"] = "browser";
        json["display_override"] = new JsonArray("future-mode", "standalone");
        Assert.Equal("Sol Notes", Parse(json)!.Name);
    }

    private static WebAppManifest? Parse(JsonNode json)
        => WebAppManifest.Parse("https://example.org/", "https://example.org/manifest.json", json.ToJsonString());
}
