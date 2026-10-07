using Browser.Core;

namespace Browser.Core.Tests;

public sealed class TabSessionsTests
{
    [Fact]
    public void Site_choices_are_explicit_independent_persistent_and_removed_with_the_session()
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "sessions.json");
            var sessions = new TabSessions(path); sessions.Seed();
            var admin = sessions.Items[0].Id; var user = sessions.Items[1].Id;
            Assert.Equal("", sessions.ForSite("https://video.example/watch"));
            sessions.SetSite("https://www.video.example/watch", admin);
            Assert.Equal(admin, sessions.ForSite("http://video.example/other"));
            Assert.Equal("", sessions.ForSite("https://mail.example/"));
            sessions.SetSite("https://mail.example/", user);
            var restored = new TabSessions(path);
            Assert.Equal(admin, restored.ForSite("https://video.example/"));
            Assert.Equal(user, restored.ForSite("https://mail.example/"));
            Assert.Equal("", restored.ForSite("https://other.video.example/"));
            restored.SetSite("https://video.example/", "../../unknown");
            Assert.Equal(admin, restored.ForSite("https://video.example/"));
            restored.SetSite("https://mail.example/", "");
            Assert.Equal("", new TabSessions(path).ForSite("https://mail.example/"));
            restored.SetSite(Settings.DefaultHomePage, admin);
            Assert.Equal("", restored.ForSite(Settings.DefaultHomePage));
            Assert.Null(TabSessions.SiteOf("chrome-extension://anything/index.html"));
            Assert.False(TabSessions.SameSite("https://video.example:8443/", "https://video.example/"));
            restored.Delete(admin);
            Assert.Equal("", new TabSessions(path).ForSite("https://video.example/"));
        }
        finally { Directory.Delete(folder, true); }
    }
    [Fact]
    public void Sessions_keep_stable_ids_and_closed_tabs_keep_their_session()
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "sessions.json");
            var sessions = new TabSessions(path); sessions.Seed();
            Assert.Equal(3, sessions.Items.Count);
            var admin = sessions.Items[0].Id;
            Assert.Equal("", sessions.Resolve("../../outside"));
            Assert.True(sessions.Rename(admin, "Compte\nA"));
            Assert.Equal("CompteA", new TabSessions(path).Name(admin));
            var tabs = new TabManager(); var tab = tabs.Open("https://example.test", sessionId: admin);
            tabs.Close(tab.Id); var reopened = tabs.ReopenClosed()!;
            Assert.Equal(admin, reopened.SessionId);
            tabs.Close(reopened.Id); tabs.ForgetSession(admin); sessions.Delete(admin);
            Assert.Null(tabs.ReopenClosed());
            var restored = new TabSessions(path); restored.Seed();
            Assert.Equal(2, restored.Items.Count);
            Assert.Equal("", restored.Resolve(admin));
        }
        finally { Directory.Delete(folder, true); }
    }
}
