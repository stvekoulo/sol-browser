using System.Text.Json;

namespace Browser.Core;

public sealed record WebAppManifest(string Name, string StartUrl, string ManifestUrl)
{
    public static WebAppManifest? Parse(string documentUrl, string manifestUrl, string json)
    {
        if (!Uri.TryCreate(documentUrl, UriKind.Absolute, out var page) || !IsSecure(page)
            || !Uri.TryCreate(manifestUrl, UriKind.Absolute, out var manifest) || json.Length is 0 or > 1_000_000) return null;
        try
        {
            using var parsed = JsonDocument.Parse(json);
            var root = parsed.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.TryGetProperty("prefer_related_applications", out var related) && related.ValueKind == JsonValueKind.True) return null;
            var name = Text(root, "name") ?? Text(root, "short_name");
            var start = Text(root, "start_url");
            if (name is null || start is null || !Uri.TryCreate(manifest, start, out var launch)
                || !SameOrigin(page, launch) || launch.UserInfo.Length > 0) return null;
            var display = Text(root, "display");
            if (root.TryGetProperty("display_override", out var overrides) && overrides.ValueKind == JsonValueKind.Array)
                display = overrides.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String)
                    .Select(v => v.GetString()).FirstOrDefault(SupportedDisplay) ?? display;
            if (!SupportedDisplay(display) || !root.TryGetProperty("icons", out var icons) || icons.ValueKind != JsonValueKind.Array) return null;
            var sizes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var icon in icons.EnumerateArray())
            {
                if (icon.ValueKind != JsonValueKind.Object || Text(icon, "src") is not { } source
                    || !Uri.TryCreate(manifest, source, out var iconUri) || iconUri.Scheme is not ("https" or "http")) continue;
                foreach (var size in (Text(icon, "sizes") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries)) sizes.Add(size);
            }
            if (!sizes.Contains("any") && !(sizes.Contains("192x192") && sizes.Contains("512x512"))) return null;
            return new(name[..Math.Min(name.Length, 160)], launch.AbsoluteUri, manifest.AbsoluteUri);
        }
        catch (JsonException) { return null; }
    }

    public static bool IsSecure(Uri uri) => uri.UserInfo.Length == 0 &&
        (uri.Scheme == "https" || uri.Scheme == "http" && (uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)));

    private static bool SameOrigin(Uri a, Uri b) => a.Scheme == b.Scheme && a.Host == b.Host && a.Port == b.Port;
    private static bool SupportedDisplay(string? mode) => mode is "standalone" or "minimal-ui" or "fullscreen" or "window-controls-overlay" or "tabbed";
    private static string? Text(JsonElement root, string key) => root.TryGetProperty(key, out var value)
        && value.ValueKind == JsonValueKind.String && value.GetString()?.Trim() is { Length: > 0 } text ? text : null;
}
