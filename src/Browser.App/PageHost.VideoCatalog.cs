using System.Runtime.InteropServices;
using System.Text.Json;
using Browser.Core;
using Microsoft.Web.WebView2.Core;

namespace Browser.App;

public sealed partial class PageHost
{
    public event Action<string>? VideoChanged;
    private sealed record PlayerTrack(string Id, string Url, int Height, string Mime, bool Audio, bool Resolve);
    public IReadOnlyList<VideoVariant> VideoVariantsOf(string id)
    {
        if (!settings.VideoDownloads || !_videos.TryGetValue(id, out var page) || Core(id)?.Source != page.Url) return [];
        var result = page.Variants.Values.ToList();
        foreach (var track in page.Tracks.Values.Where(t => !t.Audio && t.Height > 0))
        {
            var resolved = page.Resolved.GetValueOrDefault(track.Id);
            var url = resolved ?? track.Url;
            if (!VideoSources.IsHttp(url)) url = page.Url;
            var audio = page.Tracks.Values.Where(t => t.Audio && track.Mime.Contains("video-only")).OrderByDescending(t => t.Mime.Contains("mp4") == track.Mime.Contains("mp4"))
                .FirstOrDefault(t => page.Resolved.ContainsKey(t.Id) || !t.Resolve);
            var audioUrl = audio is null ? null : page.Resolved.GetValueOrDefault(audio.Id, audio.Url);
            result.Add(new("player:" + track.Id, url, track.Height, "file", $"{track.Height}p", audioUrl,
                resolved is null && track.Resolve || track.Mime.Contains("video-only") && audioUrl is null, track.Id));
        }
        return result.OrderByDescending(v => v.Height).ThenBy(v => v.NeedsResolver).DistinctBy(v => (v.Height, v.Kind)).Take(80).ToArray();
    }

    private void VideoCatalogChanged(string id)
    {
        RuntimeChanged?.Invoke();
        VideoChanged?.Invoke(id);
    }

    public void CacheVideoQualities(string id, string document, VideoSource source, IEnumerable<int> heights)
    {
        if (!settings.VideoDownloads || Core(id)?.Source != document || !_videos.TryGetValue(id, out var page) || page.Url != document || page.Protected) return;
        foreach (var height in heights.Where(h => h > 0 && h <= 16384).Distinct().Take(80))
            page.Variants[$"probe:{source.Id}:{height}"] = new($"probe:{source.Id}:{height}", source.Url, height, source.Kind, $"{height}p", NeedsResolver: source.Kind == "page");
        VideoCatalogChanged(id);
    }

    private void ReadPlayerTracks(string id, CoreWebView2 core, JsonElement data)
    {
        if (!data.TryGetProperty("formats", out var formats) || formats.ValueKind != JsonValueKind.Array) return;
        if (!_videos.TryGetValue(id, out var page) || page.Url != core.Source) _videos[id] = page = new(core.Source);
        foreach (var f in formats.EnumerateArray().Take(100))
        {
            var trackId = f.TryGetProperty("id", out var key) && key.ValueKind == JsonValueKind.String ? key.GetString() : null;
            if (string.IsNullOrEmpty(trackId) || trackId.Length > 100) continue;
            var url = f.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() ?? "" : "";
            if (url.Length > 16000 || url.Length > 0 && !VideoSources.IsHttp(url)) continue;
            var height = f.TryGetProperty("height", out var h) && h.TryGetInt32(out var n) ? Math.Clamp(n, 0, 16384) : 0;
            var mime = f.TryGetProperty("mime", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() ?? "" : "";
            var audio = f.TryGetProperty("audio", out var a) && a.ValueKind == JsonValueKind.True;
            var resolve = f.TryGetProperty("resolve", out var r) && r.ValueKind == JsonValueKind.True;
            page.Tracks[trackId] = new(trackId, url, height, mime[..Math.Min(mime.Length, 200)], audio, resolve);
        }
        VideoCatalogChanged(id);
    }

    private async void CaptureManifest(string id, CoreWebView2 core, string url, string kind, CoreWebView2WebResourceResponseView response)
    {
        if (!_videos.TryGetValue(id, out var page) || !page.Manifests.Add(url)) return;
        try
        {
            // Réutiliser la réponse déjà chargée : pas d'extraction ni de requête supplémentaire au site.
            if (response.Headers.Contains("Content-Length") && long.TryParse(response.Headers.GetHeader("Content-Length"), out var size) && size > 1_000_000) return;
            using var stream = await response.GetContentAsync();
            if (stream is null || Core(id) != core || !_videos.TryGetValue(id, out var current) || current != page) return;
            using var reader = new StreamReader(stream.AsStreamForRead());
            var chars = new char[1_000_001];
            var count = await reader.ReadBlockAsync(chars.AsMemory());
            if (count > 1_000_000) return;
            var text = new string(chars, 0, count);
            var variants = kind == "hls" ? VideoVariants.Hls(url, text) : VideoVariants.Dash(url, text);
            if (Core(id) != core || !_videos.TryGetValue(id, out current) || current != page) return;
            foreach (var variant in variants) page.Variants[variant.Key] = variant;
            DebugLog.Write($"video.manifest {kind} qualities={variants.Count}");
            VideoCatalogChanged(id);
        }
        catch (Exception e) when (e is IOException or COMException or InvalidOperationException or ArgumentException or OperationCanceledException)
        { DebugLog.Write($"video.manifest.failed {e.GetType().Name}"); }
    }
}
