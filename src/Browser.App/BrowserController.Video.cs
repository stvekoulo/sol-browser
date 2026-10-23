using System.Runtime.InteropServices;
using System.Text.Json;
using Browser.Core;

namespace Browser.App;

public sealed partial class BrowserController
{
    private (string Id, string Url)? _videoPicker;
    private readonly List<VideoDownload> _videoJobs = [];
    private bool _startingVideo;
    private CancellationTokenSource? _videoProbeCancel;
    private (string SourceId, int[] Heights)? _videoQualities;

    private void OpenVideos(string id)
    {
        if (!_settings.VideoDownloads || _pages.CoreOf(id) is not { } core || _pages.VideosOf(id).Count == 0) return;
        HidePopup();
        _videoProbeCancel?.Cancel(); _videoQualities = null;
        _videoPicker = (id, core.Source);
        if (PublishVideoCatalog(id, true)) return;
        _popup.ShowCentered(520, JsonSerializer.Serialize(new
        {
            popup = "videos", id, title = _tabs.Tabs.FirstOrDefault(t => t.Id == id)?.Title ?? "Vidéo",
            protectedMedia = _pages.VideoProtected(id), phase = "", sourceId = "", qualities = Array.Empty<int>(), preferred = _settings.VideoQuality, selected = 0,
            items = _pages.VideosOf(id).OrderBy(s => s.Kind == "page" ? 0 : s.Kind == "hls" || s.Kind == "dash" ? 1 : 2)
                .Select(s => new { id = s.Id, label = s.Label, kind = s.Kind, host = new Uri(s.Url).Host }),
        }));
    }

    private async Task InspectVideoAsync(string id, string sourceId)
    {
        if (_videoPicker is not { } picker || picker.Id != id || _pages.CoreOf(id)?.Source != picker.Url || _pages.VideoProtected(id)
            || _pages.VideosOf(id).FirstOrDefault(s => s.Id == sourceId) is not { } source) return;
        _videoProbeCancel?.Cancel();
        using var cancel = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        _videoProbeCancel = cancel; _videoQualities = null;
        var title = _tabs.Tabs.FirstOrDefault(t => t.Id == id)?.Title ?? "Vidéo";
        void Publish(string phase, int[]? heights = null)
        {
            if (_closingWindow || _videoProbeCancel != cancel || _videoPicker != picker || !_popup.IsOpen || _pages.CoreOf(id)?.Source != picker.Url) return;
            _popup.Post(JsonSerializer.Serialize(new { popup = "videos", id, title, phase, sourceId, protectedMedia = _pages.VideoProtected(id),
                items = Array.Empty<object>(), qualities = heights ?? [], preferred = _settings.VideoQuality,
                selected = VideoQualitySelection.Pick(heights ?? [], _settings.VideoQuality) }));
        }
        try
        {
            Publish("inspect");
            var cookies = await _pages.VideoCookiesAsync(id, source.Url, source.Referer);
            var heights = await Task.Run(() => VideoProbe.InspectAsync(source.Url, source.Referer ?? picker.Url, Path.Combine(AppPaths.BaseDir, "tools", "video"), cookies,
                phase => _window.DispatcherQueue.TryEnqueue(() => Publish(phase)), cancel.Token));
            if (_videoProbeCancel != cancel || _pages.CoreOf(id)?.Source != picker.Url) return;
            _videoQualities = (sourceId, heights.Length == 0 ? [0] : heights);
            Publish("ready", _videoQualities.Value.Heights);
        }
        catch (OperationCanceledException) { Publish("failed"); }
        catch (Exception e) when (e is IOException or InvalidDataException or System.Net.Http.HttpRequestException or JsonException or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { Publish("failed"); }
        finally { if (_videoProbeCancel == cancel) _videoProbeCancel = null; }
    }

    private async Task DownloadVideoAsync(string id, string sourceId, int height)
    {
        if (_startingVideo || !_settings.VideoDownloads || _videoPicker is not { } picker || picker.Id != id
            || _pages.CoreOf(id) is not { } core || core.Source != picker.Url || _pages.VideoProtected(id)
            || _videoQualities is not { } qualities || qualities.SourceId != sourceId || !qualities.Heights.Contains(height)) return;
        var variant = sourceId == "catalog" ? _pages.VideoVariantsOf(id).FirstOrDefault(v => v.Height == height) : null;
        var source = _pages.VideosOf(id).FirstOrDefault(s => s.Id == sourceId);
        if (variant is null && source is null) return;
        var url = variant is { NeedsResolver: false } ? variant.Url : variant is not null ? picker.Url : source!.Url;
        _startingVideo = true;
        try
        {
            var cookies = (await _pages.VideoCookiesAsync(id, url)).ToList();
            if (variant?.AudioUrl is { } audio) cookies.AddRange(await _pages.VideoCookiesAsync(id, audio));
            if (_closingWindow || _videoPicker != picker || core.Source != picker.Url || _pages.VideoProtected(id)) return;
            var folder = Environment.GetEnvironmentVariable("NAVIGATEUR_TEST_DOWNLOAD_DIR");
            if (string.IsNullOrEmpty(folder)) folder = core.Profile.DefaultDownloadFolderPath;
            var title = _tabs.Tabs.FirstOrDefault(t => t.Id == id)?.Title ?? "Video";
            var direct = variant is { Kind: "file", NeedsResolver: false };
            var referer = source?.Referer ?? _pages.VideosOf(id).FirstOrDefault(s => s.Url == url)?.Referer ?? picker.Url;
            cookies.AddRange(await _pages.VideoCookiesAsync(id, url, referer));
            if (_closingWindow || _videoPicker != picker || core.Source != picker.Url || _pages.VideoProtected(id)) return;
            var job = new VideoDownload(url, referer, title, folder, Path.Combine(AppPaths.BaseDir, "tools", "video"), cookies.DistinctBy(c => (c.Domain,c.Path,c.Name)).ToArray(), height,
                direct, direct ? variant!.AudioUrl : null);
            HidePopup(); _videoPicker = null;
            AddVideoDownload(id, job); DownloadsChanged(throttle: false);
        }
        catch (Exception error) when (error is COMException or InvalidOperationException or IOException or UnauthorizedAccessException)
        { Notice(T("Téléchargement vidéo", "Video download"), T("Impossible de démarrer le téléchargement. Réessayez sur cette page.", "Unable to start the download. Try again on this page.")); }
        finally { _startingVideo = false; }
    }
}
