using Browser.Core;

namespace Browser.App;

public sealed partial class BrowserController
{
    private bool _restoredPinsPrepared;

    private async Task PrepareRestoredPinsAsync()
    {
        if (_restoredPinsPrepared) return;
        _restoredPinsPrepared = true;
        // Laisser l'interface et la page active apparaître avant de préparer les accès rapides.
        await Task.Delay(750);
        var ids = _tabs.Tabs.Where(t => t.Pinned && _restored.Contains(t.Id)).Select(t => t.Id).ToArray();
        foreach (var id in ids)
        {
            if (_closingWindow) return;
            var tab = _tabs.Tabs.FirstOrDefault(t => t.Id == id);
            if (tab is not { Pinned: true } || !IsWeb(tab.Url) || TabSessions.IsSignIn(tab.Url)) continue;
            await _pages.PrepareRestoredAsync(id);
            Refresh();
            await Task.Delay(100);
        }
    }
}
