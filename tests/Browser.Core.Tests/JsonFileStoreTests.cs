using Browser.Core;

public class JsonFileStoreTests : IDisposable
{
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
