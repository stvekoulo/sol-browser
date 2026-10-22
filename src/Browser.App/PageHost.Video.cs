using System.Runtime.InteropServices;
using System.Text.Json;
using Browser.Core;
using Microsoft.Web.WebView2.Core;

namespace Browser.App;

public sealed partial class PageHost
{
    private sealed class VideoPage(string url)
    {
        public string Url { get; } = url;
        public bool Protected { get; set; }
        public List<VideoSource> Sources { get; } = [];
        public Dictionary<string, VideoVariant> Variants { get; } = new();
        public Dictionary<string, PlayerTrack> Tracks { get; } = new();
        public Dictionary<string, string> Resolved { get; } = new();
        public HashSet<string> Manifests { get; } = new();
    }
    private readonly Dictionary<string, VideoPage> _videos = new();
    public IReadOnlyList<VideoSource> VideosOf(string id) => settings.VideoDownloads && _videos.TryGetValue(id, out var page)
        && Core(id)?.Source == page.Url ? page.Sources : [];
    public bool VideoProtected(string id) => _videos.TryGetValue(id, out var page) && page.Protected;

    private async Task WireVideoAsync(string id, CoreWebView2 core)
    {
        await core.AddScriptToExecuteOnDocumentCreatedAsync(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "inject", "video-detection.js")));
        core.NavigationStarting += (_, _) => { if (_videos.Remove(id)) VideoCatalogChanged(id); };
        core.SourceChanged += (_, _) => { if (_videos.TryGetValue(id, out var page) && page.Url != core.Source) { _videos.Remove(id); VideoCatalogChanged(id); } };
        core.WebResourceResponseReceived += (_, e) =>
        {
            if (!settings.VideoDownloads || Core(id) != core || e.Response.StatusCode is < 200 or >= 300) return;
            try
            {
                var mime = e.Response.Headers.Contains("Content-Type") ? e.Response.Headers.GetHeader("Content-Type") : "";
                if (VideoSources.Kind(e.Request.Uri, mime) is { } kind)
                {
                    AddVideo(id, core, e.Request.Uri, kind);
                    if (kind is "hls" or "dash") CaptureManifest(id, core, e.Request.Uri, kind, e.Response);
                }
                if (_videos.TryGetValue(id, out var page) && VideoVariants.Query(e.Request.Uri, "itag") is { } itag
                    && new Uri(e.Request.Uri).Host.EndsWith(".googlevideo.com", StringComparison.OrdinalIgnoreCase))
                { page.Resolved[itag] = VideoVariants.WholeFile(e.Request.Uri); VideoCatalogChanged(id); }
            }
            catch (Exception error) when (error is COMException or InvalidOperationException or ArgumentException) { }
        };
        core.WebMessageReceived += (_, e) => VideoHint(id, core, e);
        core.FrameCreated += (_, e) => e.Frame.WebMessageReceived += (_, message) => VideoHint(id, core, message);
    }

    private void VideoHint(string id, CoreWebView2 core, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!settings.VideoDownloads || Core(id) != core || e.WebMessageAsJson.Length > 400000
            || !e.WebMessageAsJson.StartsWith("{\"solVideo\":", StringComparison.Ordinal)) return;
        try
        {
            using var json = JsonDocument.Parse(e.WebMessageAsJson);
            var data = json.RootElement;
            if (!VideoSources.IsHttp(e.Source) || !VideoSources.IsHttp(core.Source)) return;
            if (data.TryGetProperty("page", out var documentUrl) && documentUrl.GetString() != e.Source) return;
            if (data.TryGetProperty("protected", out var drm) && drm.ValueKind == JsonValueKind.True)
            {
                if (!_videos.TryGetValue(id, out var page) || page.Url != core.Source) _videos[id] = page = new(core.Source);
                page.Protected = true; VideoCatalogChanged(id); return;
            }
            if (data.TryGetProperty("src", out var src) && src.ValueKind == JsonValueKind.String && src.GetString() is { } url
                && VideoSources.Kind(url) is { } kind)
            {
                AddVideo(id, core, url, kind);
                if (kind == "file" && _videos.TryGetValue(id, out var page))
                {
                    var height = data.TryGetProperty("height", out var h) && h.TryGetInt32(out var n) ? Math.Clamp(n, 0, 16384) : 0;
                    page.Variants["file:" + url] = new("file:" + url, url, height, "file", height > 0 ? $"{height}p" : "Vidéo");
                }
            }
            ReadPlayerTracks(id, core, data);
            VideoCatalogChanged(id);
            // Une URL blob: n'est pas un fichier. L'extracteur peut retrouver les pistes depuis la page.
            AddVideo(id, core, core.Source, "page");
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException or COMException) { }
    }

    private void AddVideo(string id, CoreWebView2 core, string url, string kind)
    {
        if (!VideoSources.IsHttp(core.Source) || new Uri(core.Source).Host is "start.sol" or "ui.navigateur" || !VideoSources.IsHttp(url)) return;
        if (!_videos.TryGetValue(id, out var page) || page.Url != core.Source) _videos[id] = page = new(core.Source);
        if (page.Sources.Count >= 30 || page.Sources.Any(s => s.Url == url)) return;
        var label = kind switch { "hls" => "HLS", "dash" => "DASH", "page" => "Automatique", _ => Path.GetExtension(new Uri(url).AbsolutePath).TrimStart('.').ToUpperInvariant() };
        page.Sources.Add(new(Guid.NewGuid().ToString("N"), url, kind, label.Length > 0 ? label : "Vidéo"));
        VideoCatalogChanged(id);
    }

    public async Task<IReadOnlyList<VideoCookie>> VideoCookiesAsync(string id, string url)
    {
        if (Core(id) is not { } core || !VideoSources.IsHttp(url)) return [];
        var cookies = new List<VideoCookie>();
        foreach (var source in new[] { url, core.Source }.Where(VideoSources.IsHttp).Distinct())
            foreach (var cookie in await core.CookieManager.GetCookiesAsync(source))
                cookies.Add(new(cookie.Domain, cookie.Path, cookie.IsSecure, cookie.IsSession ? 0 : (long)cookie.Expires, cookie.Name, cookie.Value));
        return cookies.DistinctBy(c => (c.Domain, c.Path, c.Name)).ToArray();
    }
}
