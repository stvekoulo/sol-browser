using Browser.Core;

namespace Browser.App;

public sealed partial class BrowserController
{
    // Une nouvelle fenêtre de site suit son onglet source, même si un autre groupe est actif.
    private Tab? OpenLinkedTab(string sourceId, string url)
    {
        var opener = _tabs.Tabs.FirstOrDefault(t => t.Id == sourceId);
        if (opener is null) return null;
        var child = _tabs.Open(url, sessionId: opener.SessionId, isPrivate: opener.Private);
        child = KeepInSourceGroup(child, opener);
        Refresh();
        return child;
    }

    private Tab KeepInSourceGroup(Tab child, Tab? opener)
    {
        if (opener?.GroupId is { } group) _tabs.AddToGroup(child.Id, group);
        return _tabs.Tabs.First(t => t.Id == child.Id);
    }
}
