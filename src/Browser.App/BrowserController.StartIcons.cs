using Browser.Core;
using Microsoft.UI.Xaml;

namespace Browser.App;

public sealed partial class BrowserController
{
    private readonly SemaphoreSlim _startIconSlots = new(4);
    private readonly DispatcherTimer _startIconPublish = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private bool _startIconTimerWired;

    private void PrimeStartIcons(IEnumerable<string> urls)
    {
        foreach (var domain in urls.Select(SiteShortcut.DomainOf).Distinct())
            if (!_startIcons.ContainsKey(domain) && _db.CachedSiteIcon(domain) is { Found: true } cached)
                _startIcons[domain] = cached.Icon;
    }

    private void StartReady(string id)
    {
        var urls = _db.Bookmarks(48).Select(b => b.Url);
        if (!_pages.IsPrivate(id)) urls = urls.Concat(_db.RecentHistory(12).Select(v => v.Url)).Concat(CategoryViews().SelectMany(c => c.Links.Take(8)).Select(l => l.Url));
        var sites = urls.Where(IsWeb).DistinctBy(SiteShortcut.DomainOf).ToArray();
        PrimeStartIcons(sites);
        _bridge.BroadcastTheme();
        _pages.PostStart(id, _startPosted[id] = StartJson(_pages.IsPrivate(id)));
        _ = PostStartWallpaperAsync(id);
        foreach (var url in sites) FetchStartIcon(url);
    }

    private async void FetchStartIcon(string url) => await FetchStartIconAsync(url);
    private async Task FetchStartIconAsync(string url)
    {
        if (_closingWindow) return;
        var domain = SiteShortcut.DomainOf(url);
        if (_startIcons.ContainsKey(domain)) return;
        if (_db.CachedSiteIcon(domain) is { Found: true } cached) { _startIcons[domain] = cached.Icon; return; }
        _startIcons[domain] = null;
        await _startIconSlots.WaitAsync();
        string? icon;
        try {
            if (_closingWindow) return;
            try { icon = await FetchSiteIconAsync(url); }
            catch (Exception error) when (error is HttpRequestException or OperationCanceledException or IOException) { icon = null; }
        }
        finally { _startIconSlots.Release(); }
        if (_closingWindow) return;
        _startIcons[domain] = icon;
        if (!AppPaths.Private)
            try { _db.CacheSiteIcon(domain, icon); }
            catch (Microsoft.Data.Sqlite.SqliteException) { DebugLog.Write("start.icon.cache-write-failed"); }
        if (icon is not null)
            foreach (var tab in _tabs.Tabs.Where(t => SiteShortcut.DomainOf(t.Url) == domain))
            { _favicons[tab.Id] = icon; PostFavicon(tab.Id); LearnShortcutIcon(tab.Id, icon); }
        if (!_startIconTimerWired)
        {
            _startIconTimerWired = true;
            _startIconPublish.Tick += (_, _) => {
                _startIconPublish.Stop();
                if (!_closingWindow) PostStartPages(_tabs.Tabs.Where(t => t.Url == Settings.DefaultHomePage && !_pages.IsAsleep(t.Id)).ToList());
            };
        }
        if (!_startIconPublish.IsEnabled) _startIconPublish.Start();
    }
}
