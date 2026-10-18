using Browser.Core;
using Xunit;

namespace Browser.Core.Tests;

public sealed class PrivateBrowsingTests
{
    [Fact]
    public void PrivateTabsCannotBecomePersistentOrReopen()
    {
        var tabs = new TabManager();
        var normal = tabs.Open("https://normal.test");
        tabs.Close(normal.Id);
        var secret = tabs.Open("https://private.test", sessionId: new string('a', 32), isPrivate: true);
        Assert.True(secret.Private);
        Assert.Empty(secret.SessionId);
        Assert.Null(tabs.SetSession(secret.Id, new string('b', 32)));
        tabs.Close(secret.Id);
        Assert.Equal(normal.Url, tabs.ReopenClosed()?.Url);
        Assert.Null(tabs.ReopenClosed());
    }

    [Fact]
    public void PrivatePermissionsAreKeptOnlyInMemory()
    {
        var store = new PermissionStore(null);
        Assert.True(store.Set("https://private.test", "Camera", true));
        Assert.True(store.Get("https://private.test", "Camera"));
        Assert.Null(new PermissionStore(null).Get("https://private.test", "Camera"));
        Assert.True(store.Clear());
        Assert.Equal(0, store.Count);
    }

    [Theory]
    [InlineData("sol-private-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", true)]
    [InlineData("sol-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", false)]
    [InlineData("Default", false)]
    [InlineData("sol-private-../Default", false)]
    [InlineData("sol-private-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa-extra", false)]
    public void OnlyOwnedPrivateProfilesCanBeDeleted(string name, bool expected) =>
        Assert.Equal(expected, PrivateBrowsing.IsProfile(name));
}
