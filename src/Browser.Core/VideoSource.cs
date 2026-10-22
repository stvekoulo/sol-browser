namespace Browser.Core;

public sealed record VideoSource(string Id, string Url, string Kind, string Label);

public static class VideoSources
{
    public static bool IsHttp(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo) && url.Length <= 16000;

    // Ne pas confondre un segment isolé avec une vidéo complète.
    public static string? Kind(string url, string? contentType = null)
    {
        if (!IsHttp(url)) return null;
        var path = new Uri(url).AbsolutePath.ToLowerInvariant();
        var mime = (contentType ?? "").Split(';')[0].Trim().ToLowerInvariant();
        if (path.EndsWith(".m3u8") || mime is "application/vnd.apple.mpegurl" or "application/x-mpegurl") return "hls";
        if (path.EndsWith(".mpd") || mime == "application/dash+xml") return "dash";
        if (path.EndsWith(".m4s") || path.EndsWith(".ts") || mime == "video/mp2t") return null;
        if (new[] { ".mp4", ".webm", ".mov", ".mkv", ".m4v", ".ogv" }.Any(path.EndsWith)
            || mime is "video/mp4" or "video/webm" or "video/quicktime" or "video/x-matroska" or "video/ogg") return "file";
        return null;
    }

    public static string FileStem(string title)
    {
        var invalid = "<>:\"/\\|?*";
        var safe = new string(title.Select(c => c < 32 || invalid.Contains(c) || c == '%' ? '_' : c).ToArray()).Trim(' ', '.');
        return safe.Length == 0 ? "Video" : safe[..Math.Min(safe.Length, 90)];
    }
}
