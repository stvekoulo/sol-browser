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

    private string? KnownSiteIcon(string url)
    {
        if (!IsWeb(url) || url is Settings.DefaultHomePage or QuickNotes.PageUrl) return null;
        var domain = SiteShortcut.DomainOf(url);
        return _startIcons.GetValueOrDefault(domain) ?? _db.CachedSiteIcon(domain).Icon ?? ShortcutFor(url)?.Icon;
    }

    private string? TabSiteIcon(string domain) => _tabs.Tabs
        .Where(t => !t.Private && SiteShortcut.DomainOf(t.Url) == domain)
        .Select(t => _favicons.GetValueOrDefault(t.Id)).FirstOrDefault(icon => icon is not null);

    private void SetTabFavicon(string id, string? icon)
    {
        if (_favicons.GetValueOrDefault(id) == icon) return;
        if (icon is null) _favicons.Remove(id);
        else _favicons[id] = icon;
        PostFavicon(id);
    }

    private void LearnPageFavicon(string id, string data)
    {
        if (_closingWindow || _tabs.Tabs.FirstOrDefault(t => t.Id == id) is not { Private: false } tab) return;
        if (!IsWeb(tab.Url) || tab.Url is Settings.DefaultHomePage or QuickNotes.PageUrl) return;
        var domain = SiteShortcut.DomainOf(tab.Url);
        var icon = _startIcons.GetValueOrDefault(domain) ?? data;
        SetTabFavicon(id, icon);
        LearnShortcutIcon(id, icon);
        if (_startIcons.TryGetValue(domain, out var siteIcon) && siteIcon is null)
        {
            UseCachedIcon(domain, icon);
            CacheStartIcon(domain, icon);
            ApplySiteIconToTabs(domain, icon);
        }
        PublishStartIcons();
    }

    private void ApplySiteIconToTabs(string domain, string icon)
    {
        foreach (var tab in _tabs.Tabs.Where(t => !t.Private && SiteShortcut.DomainOf(t.Url) == domain))
        {
            SetTabFavicon(tab.Id, icon);
            LearnShortcutIcon(tab.Id, icon);
        }
    }

    private void CacheStartIcon(string domain, string? icon)
    {
        if (AppPaths.Private) return;
        try { _db.CacheSiteIcon(domain, icon); }
        catch (Microsoft.Data.Sqlite.SqliteException) { DebugLog.Write("start.icon.cache-write-failed"); }
    }

    // Une icône mise en cache avant leur réduction sert telle quelle cette fois, puis est remplacée par sa version réduite.
    private void UseCachedIcon(string domain, string? icon)
    {
        _startIcons[domain] = icon;
        if (icon is { Length: > MaxStartIcon }) _ = ReplaceWithSmallIconAsync(domain, icon);
    }

    private async Task ReplaceWithSmallIconAsync(string domain, string icon)
    {
        var small = (await PrepareSiteIconAsync(icon))?.Data;
        if (_closingWindow || small == icon || _startIcons.GetValueOrDefault(domain) != icon) return;
        if (small is null) return;
        _startIcons[domain] = small;
        CacheStartIcon(domain, small);
        ApplySiteIconToTabs(domain, small);
        PublishStartIcons();
    }

    private const int MaxStartIcon = 6_000;
    private const int RenderedIconSize = 96;
    private sealed record SiteIcon(string Data, int Size);

    private static async Task<SiteIcon?> PrepareSiteIconAsync(string icon)
    {
        var data = icon.IndexOf(";base64,", StringComparison.Ordinal);
        if (data < 0 || !icon.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            var original = Convert.FromBase64String(icon[(data + 8)..]);
            if (icon.StartsWith("data:image/svg+xml;", StringComparison.OrdinalIgnoreCase))
            {
                using var xml = System.Xml.XmlReader.Create(new MemoryStream(original),
                    new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Ignore, XmlResolver = null, CloseInput = true });
                var document = new System.Xml.XmlDocument { XmlResolver = null };
                document.Load(xml);
                return document.DocumentElement is { LocalName: "svg", NamespaceURI: "http://www.w3.org/2000/svg" }
                    ? new SiteIcon(icon, int.MaxValue) : null;
            }
            using var input = new InMemoryRandomAccessStream();
            await input.WriteAsync(original.AsBuffer());
            input.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(input);
            // Un fichier .ico contient plusieurs tailles : la plus grande, seule, réduite ; les autres partent.
            var frame = await decoder.GetFrameAsync(0);
            for (uint i = 1; i < decoder.FrameCount; i++)
                if (await decoder.GetFrameAsync(i) is var next && next.PixelWidth > frame.PixelWidth) frame = next;
            var edge = Math.Max(frame.PixelWidth, frame.PixelHeight);
            var factor = Math.Min(1d, RenderedIconSize / (double)edge);
            var transform = new BitmapTransform
            {
                ScaledWidth = Math.Max(1, (uint)Math.Round(frame.PixelWidth * factor)),
                ScaledHeight = Math.Max(1, (uint)Math.Round(frame.PixelHeight * factor)),
                InterpolationMode = BitmapInterpolationMode.Fant,
            };
            using var bitmap = await frame.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, transform,
                ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.ColorManageToSRgb);
            if (factor == 1 && decoder.FrameCount == 1 && decoder.DecoderInformation.CodecId == BitmapDecoder.PngDecoderId
                && icon.StartsWith("data:image/png;", StringComparison.OrdinalIgnoreCase))
                return new SiteIcon(icon, (int)edge);
            using var output = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
            encoder.SetSoftwareBitmap(bitmap);
            await encoder.FlushAsync();
            var bytes = new byte[output.Size];
            output.Seek(0);
            await output.ReadAsync(bytes.AsBuffer(), (uint)bytes.Length, InputStreamOptions.None);
            var small = "data:image/png;base64," + Convert.ToBase64String(bytes);
            var size = (int)Math.Max(transform.ScaledWidth, transform.ScaledHeight);
            return new SiteIcon(small, size);
        }
        catch (Exception e) when (e is FormatException or ArgumentException or System.Xml.XmlException
            or IOException or System.Runtime.InteropServices.COMException) { return null; }
    }

    private void StartReady(string id)
    {
        var urls = _db.Bookmarks(48).Select(b => b.Url);
        if (!_pages.IsPrivate(id)) urls = urls.Concat(_db.RecentHistory(12).Select(v => v.Url)).Concat(CategoryViews().SelectMany(c => c.Links.Take(8)).Select(l => l.Url));
        var sites = urls.Where(IsWeb).DistinctBy(SiteShortcut.DomainOf).ToArray();
        foreach (var url in sites) FetchStartIcon(url);
        _bridge.BroadcastTheme();
        _pages.PostStart(id, _startPosted[id] = StartJson(_pages.IsPrivate(id)));
        _ = PostStartWallpaperAsync(id);
    }

    private async void FetchStartIcon(string url) => await FetchStartIconAsync(url);
    private async Task FetchStartIconAsync(string url)
    {
        if (_closingWindow) return;
        var domain = SiteShortcut.DomainOf(url);
        if (_startIcons.ContainsKey(domain)) return;
        var cached = _db.CachedSiteIcon(domain);
        var fallback = cached.Icon ?? TabSiteIcon(domain) ?? ShortcutFor(url)?.Icon;
        _startIcons[domain] = fallback;
        if (cached.Found && fallback is null) return;
        await _startIconSlots.WaitAsync();
        string? icon;
        try {
            if (_closingWindow) return;
            try
            {
                var previous = fallback is null ? null : await PrepareSiteIconAsync(fallback);
                if (previous is null && _startIcons[domain] == fallback) _startIcons[domain] = null;
                var found = cached.Found && previous is not null ? null : await FetchSiteIconAsync(url);
                // Une icône WebView2 peut arriver pendant la requête réseau.
                if (_startIcons[domain] is { } current && current != fallback)
                    previous = await PrepareSiteIconAsync(current) ?? previous;
                icon = found is not null && (previous is null || found.Size >= previous.Size) ? found.Data : previous?.Data;
            }
            catch (Exception error) when (error is HttpRequestException or OperationCanceledException or IOException) { icon = null; }
        }
        finally { _startIconSlots.Release(); }
        if (_closingWindow) return;
        icon ??= _startIcons[domain];
        _startIcons[domain] = icon;
        if (!cached.Found || icon != cached.Icon) CacheStartIcon(domain, icon);
        if (icon is not null) ApplySiteIconToTabs(domain, icon);
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
