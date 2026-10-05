using Browser.Core;

public class JsonFileStoreTests : IDisposable
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(0.9, 0.9)]
    [InlineData(0.8, 0.8)]
    [InlineData(0.7, 1)]
    [InlineData(double.NaN, 1)]
    public void Interface_scale_only_accepts_supported_sizes_and_does_not_change_page_zoom(double scale, double expected)
    {
        var settings = new Settings { PageZoom = 1.5, UiScale = scale };
        var path = File_("scale.json");
        JsonFileStore.Save(path, settings);
        var saved = JsonFileStore.Load<Settings>(path);
        Assert.Equal(expected, saved.UiScale);
        Assert.Equal(1.5, saved.PageZoom);
    }
    [Theory]
    [InlineData("https://www.google.com")]
    [InlineData("https://www.google.com/")]
    [InlineData("")]
    public void Old_default_home_page_becomes_local_start_page(string url)
    {
        Assert.Equal(Settings.DefaultHomePage, new Settings { HomePage = url }.HomePage);
        Assert.Equal("https://custom.test/", new Settings { HomePage = "https://custom.test/" }.HomePage);
    }
    [Fact]
    public void Preserves_the_page_zoom_in_profile_settings()
    {
        var path = File_("settings.json");
        File.WriteAllText(path, "{\"PageZoom\":0.8}");
        JsonFileStore.Save(path, JsonFileStore.Load<Settings>(path));
        Assert.Contains("\"PageZoom\":0.8", File.ReadAllText(path));
    }

    [Theory]
    [InlineData(-1, 0.5)]
    [InlineData(3, 2)]
    [InlineData(double.NaN, 1)]
    [InlineData(double.PositiveInfinity, 1)]
    public void Rejects_invalid_zoom_values(double value, double expected) =>
        Assert.Equal(expected, new Settings { PageZoom = value }.PageZoom);

    private readonly string _dir = Directory.CreateTempSubdirectory("nav-tests").FullName;
    private string File_(string name) => Path.Combine(_dir, name);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Missing_file_gives_defaults()
    {
        var s = JsonFileStore.Load<Settings>(File_("settings.json"));
        Assert.True(s.BlurEnabled);
        Assert.Equal("https://www.google.com/search?q={0}", s.SearchUrlTemplate);
    }

    [Fact]
    public void Round_trips_a_session()
    {
        var path = File_("sub/session.json");
        JsonFileStore.Save(path, new Session { Urls = ["https://a.test", "https://b.test"], ActiveIndex = 1, SecondaryIndex = 0, Ratio = 0.6 });
        var s = JsonFileStore.Load<Session>(path);
        Assert.Equal(new[] { "https://a.test", "https://b.test" }, s.Urls);
        Assert.Equal(1, s.ActiveIndex);
        Assert.Equal(0, s.SecondaryIndex);
        Assert.Equal(0.6, s.Ratio);
    }

    [Fact]
    public void Recovers_the_previous_session_after_corruption()
    {
        var path = File_("session.json");
        Assert.True(JsonFileStore.Save(path, new Session { Urls = ["https://saved.test"] }));
        Assert.True(JsonFileStore.Save(path, new Session { Urls = ["https://latest.test"] }));
        File.WriteAllText(path, "{ broken");
        Assert.Equal("https://saved.test", Assert.Single(JsonFileStore.Load<Session>(path).Urls));
        Assert.True(File.Exists(path + ".corrupt"));
    }

    [Fact]
    public void Locked_file_does_not_crash_or_replace_existing_data()
    {
        var path = File_("settings.json");
        Assert.True(JsonFileStore.Save(path, new Settings { PageZoom = 0.8 }));
        using (var locked = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.False(JsonFileStore.Save(path, new Settings { PageZoom = 1.5 }));
        Assert.Equal(0.8, JsonFileStore.Load<Settings>(path).PageZoom);
    }

    [Fact]
    public void Null_session_collections_are_treated_as_corruption()
    {
        var path = File_("session.json");
        File.WriteAllText(path, "{\"Urls\":null}");
        Assert.Empty(JsonFileStore.Load<Session>(path).Urls);
        Assert.True(File.Exists(path + ".corrupt"));
    }

    [Fact]
    public void Corrupt_file_gives_defaults_and_is_kept_aside()
    {
        var path = File_("session.json");
        File.WriteAllText(path, "{ not json");
        var s = JsonFileStore.Load<Session>(path);
        Assert.Empty(s.Urls);
        Assert.False(File.Exists(path));
        Assert.Equal("{ not json", File.ReadAllText(path + ".corrupt"));
    }
}
