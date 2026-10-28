using System.Runtime.InteropServices.WindowsRuntime;
using Browser.Core;
using Microsoft.UI.Xaml;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

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
                UseCachedIcon(domain, cached.Icon);
    }

    // Une icône mise en cache avant leur réduction sert telle quelle cette fois, puis est remplacée par sa version réduite.
    private void UseCachedIcon(string domain, string? icon)
    {
        _startIcons[domain] = icon;
        if (icon is { Length: > MaxStartIcon }) _ = ReplaceWithSmallIconAsync(domain, icon);
    }

    private async Task ReplaceWithSmallIconAsync(string domain, string icon)
    {
        var small = await ShrinkIconAsync(icon);
        if (_closingWindow || small == icon || _startIcons.GetValueOrDefault(domain) != icon) return;
        _startIcons[domain] = small;
        if (!AppPaths.Private)
            try { _db.CacheSiteIcon(domain, small); }
            catch (Microsoft.Data.Sqlite.SqliteException) { DebugLog.Write("start.icon.cache-write-failed"); }
        PublishStartIcons();
    }

    // Les sites fournissent souvent leur plus grande image (jusqu'à plus de 100 Ko) pour une tuile de 38 px : elle est
    // réduite à 76 px (net jusqu'à 200 %), ce qui allège d'autant le message de la page d'accueil et le décodage de chaque tuile.
    private const int MaxStartIcon = 6_000;
    private static async Task<string> ShrinkIconAsync(string icon)
    {
        var data = icon.IndexOf(";base64,", StringComparison.Ordinal);
        if (icon.Length <= MaxStartIcon || data < 0 || icon.StartsWith("data:image/svg", StringComparison.OrdinalIgnoreCase)) return icon;
        try
        {
            using var input = new InMemoryRandomAccessStream();
            await input.WriteAsync(Convert.FromBase64String(icon[(data + 8)..]).AsBuffer());
            input.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(input);
            // Un fichier .ico contient plusieurs tailles : la plus grande, seule, réduite ; les autres partent.
            var frame = await decoder.GetFrameAsync(0);
            for (uint i = 1; i < decoder.FrameCount; i++)
                if (await decoder.GetFrameAsync(i) is var next && next.PixelWidth > frame.PixelWidth) frame = next;
            var factor = Math.Min(1d, 76d / Math.Max(frame.PixelWidth, frame.PixelHeight));
            var transform = new BitmapTransform
            {
                ScaledWidth = Math.Max(1, (uint)Math.Round(frame.PixelWidth * factor)),
                ScaledHeight = Math.Max(1, (uint)Math.Round(frame.PixelHeight * factor)),
                InterpolationMode = BitmapInterpolationMode.Fant,
            };
            using var bitmap = await frame.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, transform,
                ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.ColorManageToSRgb);
            using var output = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
            encoder.SetSoftwareBitmap(bitmap);
            await encoder.FlushAsync();
            var bytes = new byte[output.Size];
            output.Seek(0);
            await output.ReadAsync(bytes.AsBuffer(), (uint)bytes.Length, InputStreamOptions.None);
            var small = "data:image/png;base64," + Convert.ToBase64String(bytes);
            return small.Length < icon.Length ? small : icon;
        }
        catch (Exception e) when (e is FormatException or ArgumentException or System.Runtime.InteropServices.COMException) { return icon; } // format illisible : l'icône reste telle quelle
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
        if (_db.CachedSiteIcon(domain) is { Found: true } cached) { UseCachedIcon(domain, cached.Icon); return; }
        _startIcons[domain] = null;
        await _startIconSlots.WaitAsync();
        string? icon;
        try {
            if (_closingWindow) return;
            try { icon = await FetchSiteIconAsync(url) is { } found ? await ShrinkIconAsync(found) : null; }
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
        PublishStartIcons();
    }

    // Les icônes reçues coup sur coup partent ensemble vers les pages d'accueil.
    private void PublishStartIcons()
    {
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
