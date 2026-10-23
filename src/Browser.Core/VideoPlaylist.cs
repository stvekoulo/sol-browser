using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Browser.Core;

public sealed record VideoPlaylistEntry(string Key, string Url, string Title, int Position, string? Referer = null, bool Direct = false);
public sealed record VideoPlaylistPage(VideoPlaylistEntry[] Items, int Start, bool HasMore);

public static class VideoPlaylist
{
    public const int PageSize = 100;
    public static string CollectionUrl(string url)
    {
        if (!VideoSources.IsHttp(url)) throw new ArgumentException("Invalid playlist URL");
        var uri = new Uri(url);
        var list = VideoVariants.Query(url, "list");
        return (uri.Host == "youtu.be" || uri.Host == "youtube.com" || uri.Host.EndsWith(".youtube.com"))
            && list is not null && Regex.IsMatch(list, "^[A-Za-z0-9_-]{1,200}$")
            ? "https://www.youtube.com/playlist?list=" + list : url;
    }
    public static string Key(string url) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..20];
    public static VideoPlaylistEntry? ParseEntry(string json, int position)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;
        string Text(string key) => root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
        bool Flag(string key) => root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;
        if (Flag("has_drm") || Flag("is_live") || Text("live_status") is "is_live" or "is_upcoming"
            || Text("availability") is "private" or "premium_only" or "subscriber_only"
            || Text("_type") is "playlist" or "multi_video") return null;
        var title = Text("title");
        if (title is "[Private video]" or "[Deleted video]") return null;
        // Flat entries point to an individual video in url. Generic multi-video entries
        // can share their parent's webpage_url; choosing it would collapse the whole list.
        var url = Text("url");
        if (!VideoSources.IsHttp(url)) url = Text("webpage_url");
        if (!VideoSources.IsHttp(url) && Text("ie_key") == "Youtube" && Regex.IsMatch(Text("id"), "^[A-Za-z0-9_-]{11}$"))
            url = "https://www.youtube.com/watch?v=" + Text("id");
        if (!VideoSources.IsHttp(url)) return null;
        if (root.TryGetProperty("playlist_index", out var index) && index.ValueKind == JsonValueKind.Number && index.TryGetInt32(out var n) && n > 0) position = n;
        return new(Key(url), url, title.Length > 0 ? title[..Math.Min(300, title.Length)] : $"Video {position}", position);
    }
}
