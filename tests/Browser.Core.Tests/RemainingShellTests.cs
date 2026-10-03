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

    [Theory]
    [InlineData(":3000", "http://localhost:3000")]
    [InlineData(" :5173 ", "http://localhost:5173")]
    [InlineData("localhost:8080/api", "http://localhost:8080/api")]
    [InlineData(":", "https://s.test/?q=%3A")]
    [InlineData(":abc", "https://s.test/?q=%3Aabc")]
    [InlineData(":1234567", "https://s.test/?q=%3A1234567")]
    public void A_bare_port_opens_the_local_development_server(string input, string expected) =>
        Assert.Equal(expected, AddressInput.Resolve(input, "https://s.test/?q={0}"));

    [Fact]
    public void A_local_server_error_page_retries_by_itself_without_script()
    {
        var html = ErrorPages.Html("http://localhost:3000/", "offline", "fr");
        Assert.Contains("""<meta http-equiv="refresh" content="3;url=http://localhost:3000/">""", html);
        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("http-equiv=\"refresh\"", ErrorPages.Html("https://exemple.test/", "offline", "fr"));
        Assert.DoesNotContain("http-equiv=\"refresh\"", ErrorPages.Html("https://localhost/", "certificate", "fr"));
    }

    [Fact]
    public void A_temporary_profile_is_deleted_once_its_window_is_closed_and_ordinary_profiles_are_kept()
    {
        var kept = Profiles.Create(_dir, "Travail");
        var open = Profiles.CreateTemporary(_dir, "Session isolée");
        var closed = Profiles.CreateTemporary(_dir, "Session isolée");
        Assert.True(Profiles.IsTemporary(_dir, open));
        Assert.False(Profiles.IsTemporary(_dir, kept));
        Assert.False(Profiles.IsTemporary(_dir, Profiles.Default));
        Assert.Equal("Session isolée", Profiles.NameOf(_dir, open));
        Assert.NotEqual(open, closed);

        Profiles.DeleteClosedTemporary(_dir, id => id == open);
        Assert.Equal([Profiles.Default, open, kept], Profiles.List(_dir).OrderBy(id => id == Profiles.Default ? "" : id.StartsWith("temp-") ? "a" : "b"));
        Assert.False(Directory.Exists(Profiles.DataDir(_dir, closed)));
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
