using Browser.Core;

public class RemainingShellTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("nav-reste").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Rename_changes_the_shown_name_and_keeps_the_profile_id()
    {
        var id = Profiles.Create(_dir, "Travail");
        Assert.True(Profiles.Rename(_dir, id, "  Bureau  "));
        Assert.Equal("Bureau", Profiles.NameOf(_dir, id));
        Assert.Contains(id, Profiles.List(_dir));
    }

    [Fact]
    public void Rename_refuses_the_default_profile_an_empty_name_and_an_unknown_or_unsafe_id()
    {
        var id = Profiles.Create(_dir, "Travail");
        Assert.False(Profiles.Rename(_dir, Profiles.Default, "x"));
        Assert.False(Profiles.Rename(_dir, id, "   "));
        Assert.False(Profiles.Rename(_dir, "inconnu", "x"));
        Assert.False(Profiles.Rename(_dir, "../ailleurs", "x"));
        Assert.Equal("Travail", Profiles.NameOf(_dir, id));
    }

    [Fact]
    public void Forget_removes_one_site_and_leaves_the_others()
    {
        var file = Path.Combine(_dir, "permissions.json");
        var store = new PermissionStore(file);
        store.Set("https://a.test", "Microphone", true);
        store.Set("https://a.test", "Camera", false);
        store.Set("https://a.test.evil.example", "Camera", true); // même début, autre site
        store.Forget("https://a.test");

        var reopened = new PermissionStore(file);
        Assert.Null(reopened.Get("https://a.test", "Microphone"));
        Assert.Null(reopened.Get("https://a.test", "Camera"));
        Assert.Equal([("https://a.test.evil.example", "Camera", true)], reopened.All);
    }
}
