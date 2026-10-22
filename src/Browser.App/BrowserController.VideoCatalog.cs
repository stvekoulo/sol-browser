using System.Text.Json;
using Browser.Core;

namespace Browser.App;

public sealed partial class BrowserController
{
    private Task? _videoWarmup;
    private readonly CancellationTokenSource _videoWarmupCancel = new();
    private readonly Dictionary<string, (string Url, CancellationTokenSource Cancel)> _videoDiscovery = new();
    private readonly Dictionary<string, string> _videoDiscoveryDone = new();
    private void VideoAvailable(string id)
    {
        if (_videoDiscovery.TryGetValue(id, out var pending) && (_pages.VideosOf(id).Count == 0 || _pages.CoreOf(id)?.Source != pending.Url))
        { pending.Cancel.Cancel(); _videoDiscovery.Remove(id); }
        if (_pages.VideosOf(id).Count == 0) _videoDiscoveryDone.Remove(id);
        if (_closingWindow || !_settings.VideoDownloads || _pages.VideoProtected(id)) return;
        // L'installation éventuelle ne participe jamais au rendu du sélecteur.
        if (_pages.VideosOf(id).Count > 0 && _videoWarmup is null)
            _videoWarmup = Task.Run(async () =>
            {
                try { await VideoEngine.PrepareAsync(Path.Combine(AppPaths.BaseDir, "tools", "video"), _ => { }, _videoWarmupCancel.Token); }
                catch (Exception e) when (e is IOException or InvalidDataException or System.Net.Http.HttpRequestException or JsonException or InvalidOperationException
                    or UnauthorizedAccessException or OperationCanceledException or ArgumentException) { }
            });
        if (_tabs.ActiveId == id && _pages.VideoVariantsOf(id).Count == 0 && _pages.VideosOf(id).Count > 0 && !_videoDiscovery.ContainsKey(id)
            && _videoDiscoveryDone.GetValueOrDefault(id) != _pages.CoreOf(id)?.Source)
            _ = DiscoverVideoAsync(id);
        if (_videoPicker is { } picker && picker.Id == id && _pages.CoreOf(id)?.Source == picker.Url && _popup.IsOpen)
            PublishVideoCatalog(id, false);
    }

    private async Task DiscoverVideoAsync(string id)
    {
        var document = _pages.CoreOf(id)?.Source;
        if (document is null) return;
        using var cancel = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        _videoDiscovery[id] = (document, cancel);
        try
        {
            // Laisser les métadonnées et les réponses déjà en vol arriver avant tout recours à l'extracteur.
            await Task.Delay(750, cancel.Token);
            if (_closingWindow || _pages.CoreOf(id)?.Source != document || _pages.VideoProtected(id) || _pages.VideoVariantsOf(id).Count > 0) return;
            var source = _pages.VideosOf(id).OrderBy(s => s.Kind == "page" ? 1 : 0).FirstOrDefault();
            if (source is null) return;
            var cookies = await _pages.VideoCookiesAsync(id, source.Url);
            var heights = await Task.Run(() => VideoProbe.InspectAsync(source.Url, document, Path.Combine(AppPaths.BaseDir, "tools", "video"), cookies, _ => { }, cancel.Token));
            if (!_closingWindow && !cancel.IsCancellationRequested) _pages.CacheVideoQualities(id, document, source, heights);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or System.Net.Http.HttpRequestException or JsonException
            or InvalidOperationException or UnauthorizedAccessException or OperationCanceledException or System.Runtime.InteropServices.COMException
            or System.ComponentModel.Win32Exception or ArgumentException) { }
        finally
        {
            // Marquer ce document traité, sans conserver une source de cancellation détruite.
            if (_videoDiscovery.TryGetValue(id, out var entry) && entry.Cancel == cancel)
            { _videoDiscovery.Remove(id); _videoDiscoveryDone[id] = document; }
        }
    }

    private bool PublishVideoCatalog(string id, bool show)
    {
        var variants = _pages.VideoVariantsOf(id);
        if (variants.Count == 0) return false;
        var heights = variants.Select(v => v.Height).Distinct().OrderDescending().ToArray();
        _videoQualities = ("catalog", heights);
        var json = JsonSerializer.Serialize(new
        {
            popup = "videos", id, title = _tabs.Tabs.FirstOrDefault(t => t.Id == id)?.Title ?? "Vidéo",
            protectedMedia = _pages.VideoProtected(id), phase = "ready", sourceId = "catalog", qualities = heights,
            preferred = _settings.VideoQuality, selected = VideoQualitySelection.Pick(heights, _settings.VideoQuality), items = Array.Empty<object>(),
        });
        if (show) _popup.ShowCentered(420, json); else _popup.Post(json);
        return true;
    }
}
