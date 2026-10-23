using Browser.Core;

public class PermissionStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("nav-acces").FullName;
    private string File_ => Path.Combine(_dir, "permissions.json");
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Corruption_never_restores_a_revoked_permission_from_backup()
    {
        var store = new PermissionStore(File_);
        store.Set("https://a.test", "Camera", true);
        store.Set("https://a.test", "Camera", false);
        File.WriteAllText(File_, "{ broken");
        Assert.Null(new PermissionStore(File_).Get("https://a.test", "Camera"));
    }

    [Fact]
    public void An_unanswered_request_has_no_answer()
    {
        var store = new PermissionStore(File_);
        Assert.Null(store.Get("https://a.test", "Microphone"));
    }

    [Fact]
    public void A_failed_save_does_not_claim_that_consent_was_recorded()
    {
        Directory.CreateDirectory(File_);
        var store = new PermissionStore(File_);
        Assert.False(store.Set("https://a.test", "Camera", true));
        Assert.Null(store.Get("https://a.test", "Camera"));
    }

    [Fact]
    public void Native_profiles_resolve_to_the_same_isolated_permission_files()
    {
        Assert.Equal(File_, PermissionStore.FileForProfile(_dir, "Default"));
        Assert.Equal(File_, PermissionStore.FileForProfile(_dir, ""));
        var session = new string('a', 32);
        Assert.Equal(Path.Combine(_dir, "session-permissions", session + ".json"), PermissionStore.FileForProfile(_dir, "sol-" + session));
        Assert.Throws<ArgumentException>(() => PermissionStore.FileForProfile(_dir, "sol-../other"));
    }

    [Fact]
    public void Answers_are_kept_per_site_and_per_kind_across_restarts()
    {
        var store = new PermissionStore(File_);
        store.Set("https://a.test", "Microphone", true);
        store.Set("https://a.test", "Camera", false);

        var reopened = new PermissionStore(File_);
        Assert.True(reopened.Get("https://a.test", "Microphone"));
        Assert.False(reopened.Get("https://a.test", "Camera"));
        Assert.Null(reopened.Get("https://b.test", "Microphone"));
        Assert.Null(reopened.Get("http://a.test", "Microphone")); // autre schéma = autre site
    }

    [Fact]
    public void Clear_forgets_everything()
    {
        var store = new PermissionStore(File_);
        store.Set("https://a.test", "Notifications", true);
        store.Clear();
        Assert.Equal(0, new PermissionStore(File_).Count);
    }

    [Theory]
    [InlineData("https://www.exemple.fr/page?x=1", "https://www.exemple.fr")]
    [InlineData("http://localhost:5173/app", "http://localhost:5173")]
    [InlineData("chrome-extension://aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/", null)]
    [InlineData("edge://settings/", null)]
    [InlineData("file:///C:/document.html", null)]
    [InlineData("about:blank", null)]
    [InlineData("data:text/html,x", null)]
    [InlineData("pas une adresse", null)]
    public void OriginOf_keeps_scheme_host_and_port(string url, string? origin) =>
        Assert.Equal(origin, PermissionStore.OriginOf(url));
}
