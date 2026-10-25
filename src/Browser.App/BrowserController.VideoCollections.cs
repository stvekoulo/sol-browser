using System.Runtime.InteropServices;
using System.Text.Json;
using Browser.Core;

namespace Browser.App;

public sealed partial class BrowserController
{
    private sealed record VideoCollection(string Token, string Id, string Document, string Mode, int Start, bool More, VideoPlaylistEntry[] Items);
    private VideoCollection? _videoCollection;

    private async Task InspectVideoCollectionAsync(string id, string mode, int start)
    {
        if (mode is not ("playlist" or "page") || _videoPicker is not { } picker || picker.Id != id
            || !_settings.VideoDownloads || _pages.CoreOf(id)?.Source != picker.Url || _pages.VideoProtected(id)) return;
        _videoProbeCancel?.Cancel();
        using var cancel = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        _videoProbeCancel = cancel; _videoQualities = null;
        var request = new VideoCollection(Guid.NewGuid().ToString("N"), id, picker.Url, mode, Math.Clamp(start, 1, 100_000), false, []);
        _videoCollection = request;
        void Publish(string phase, VideoCollection collection)
        {
            if (_closingWindow || cancel.IsCancellationRequested || _videoProbeCancel != cancel || _videoPicker != picker
                || _pages.CoreOf(id)?.Source != picker.Url || !_popup.IsOpen) return;
            _popup.Post(JsonSerializer.Serialize(new
            {
                popup = "videos", id, title = _tabs.Tabs.FirstOrDefault(t => t.Id == id)?.Title ?? "Vidéos",
                protectedMedia = _pages.VideoProtected(id), phase, sourceId = "collection", qualities = Array.Empty<int>(),
                preferred = _settings.VideoQuality, selected = 0, items = Array.Empty<object>(),
                collection = new { token = collection.Token, mode, start = collection.Start, more = collection.More,
                    entries = collection.Items.Select(e => new { key = e.Key, title = e.Title, position = e.Position, host = new Uri(e.Url).Host }) }
            }));
        }
        try
        {
            Publish("inspect", request);
            VideoPlaylistPage result;
            if (mode == "page")
            {
                var title = _tabs.Tabs.FirstOrDefault(t => t.Id == id)?.Title ?? "Vidéo";
                var sources = _pages.VideosOf(id).Where(s => s.Kind != "page").DistinctBy(s => s.Url).ToArray();
                result = new(sources.Select((s, index) => new VideoPlaylistEntry(VideoPlaylist.Key(s.Url), s.Url,
                    $"{title} · {s.Label} {index + 1}", index + 1, s.Referer ?? picker.Url, s.Kind == "file")).ToArray(), 1, false);
            }
            else
            {
                var cookies = await _pages.VideoCookiesAsync(id, picker.Url);
                result = await Task.Run(() => VideoPlaylistProbe.InspectAsync(picker.Url, picker.Url, request.Start,
                    Path.Combine(AppPaths.BaseDir, "tools", "video"), cookies, _ => { }, cancel.Token));
            }
            if (cancel.IsCancellationRequested || _videoPicker != picker || _pages.CoreOf(id)?.Source != picker.Url) return;
            _videoCollection = request with { Items = result.Items, More = result.HasMore };
            Publish("ready", _videoCollection);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is IOException or InvalidDataException or JsonException or InvalidOperationException
            or System.Net.Http.HttpRequestException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or COMException or ArgumentException)
        { Publish("failed", request); }
        finally { if (_videoProbeCancel == cancel) _videoProbeCancel = null; }
    }

    private async Task DownloadVideoCollectionAsync(string id, string token, string input, int height)
    {
        if (_startingVideo || !_settings.VideoDownloads || _videoCollection is not { } collection || collection.Id != id
            || collection.Token != token || _videoPicker is not { } picker || picker.Id != id || picker.Url != collection.Document
            || _pages.CoreOf(id) is not { } core || core.Source != picker.Url || _pages.VideoProtected(id)
            || !VideoQualitySelection.Preferences.Contains(height) || input.Length > 5000) return;
        string[]? keys;
        try { keys = JsonSerializer.Deserialize<string[]>(input); } catch (JsonException) { return; }
        if (keys is null || keys.Length == 0 || keys.Length > VideoPlaylist.PageSize) return;
        var selected = keys.Distinct().Select(key => collection.Items.FirstOrDefault(e => e.Key == key)).ToArray();
        if (selected.Any(e => e is null)) return;
        _startingVideo = true;
        try
        {
            var folder = Environment.GetEnvironmentVariable("NAVIGATEUR_TEST_DOWNLOAD_DIR");
            if (string.IsNullOrEmpty(folder)) folder = core.Profile.DefaultDownloadFolderPath;
            var jobs = new List<VideoDownload>();
            foreach (var item in selected.OfType<VideoPlaylistEntry>())
            {
                var referer = item.Referer ?? picker.Url;
                var cookies = await _pages.VideoCookiesAsync(id, item.Url, referer);
                if (_closingWindow || _videoPicker != picker || core.Source != picker.Url || _pages.VideoProtected(id)) return;
                jobs.Add(new(item.Url, referer, $"{item.Position:D3} - {item.Title}", folder, Path.Combine(AppPaths.BaseDir, "tools", "video"),
                    cookies, height, item.Direct, adaptQuality: true));
            }
            HidePopup();
            // Each selected video has its own pause/resume/cancel and failure state. Two run at a time.
            foreach (var job in jobs) AddVideoDownload(id, job);
            DownloadsChanged(throttle: false);
        }
        catch (Exception error) when (error is COMException or IOException or UnauthorizedAccessException or InvalidOperationException)
        { Notice(T("Téléchargements vidéo", "Video downloads"), T("Impossible de préparer la sélection. Réessayez.", "Unable to prepare this selection. Try again.")); }
        finally { _startingVideo = false; }
    }

    private void AddVideoDownload(string id, VideoDownload job)
    {
        _videoJobs.Add(job);
        var downloadId = "v" + Guid.NewGuid().ToString("N");
        var operation = new DownloadOperation(job);
        _downloads.Insert(0, (downloadId, operation, id));
        job.Changed += () => _window.DispatcherQueue.TryEnqueue(() =>
        {
            if (_closingWindow) return;
            if (operation.State == Microsoft.Web.WebView2.Core.CoreWebView2DownloadState.InProgress) _finishedAt.Remove(downloadId);
            else if (!DownloadPending(downloadId, operation) && _finishedAt.TryAdd(downloadId, DateTime.UtcNow))
                _ = Task.Delay(DockLinger).ContinueWith(_ => _window.DispatcherQueue.TryEnqueue(() => { if (!_closingWindow) UpdateDock(); }));
            DownloadsChanged(throttle: operation.State == Microsoft.Web.WebView2.Core.CoreWebView2DownloadState.InProgress);
        });
        job.Start();
    }
}
