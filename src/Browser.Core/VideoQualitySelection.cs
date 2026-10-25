using System.Text.Json;

namespace Browser.Core;

public static class VideoQualitySelection
{
    public static readonly int[] Preferences = [0, 480, 720, 1080, 1440, 2160, 4320];
    public static int Pick(IEnumerable<int> available, int preference)
    {
        var heights = available.Where(n => n > 0).Distinct().Order().ToArray();
        if (heights.Length == 0) return 0;
        if (preference == 0) return heights[^1];
        return heights.LastOrDefault(h => h <= preference, heights[0]);
    }
    public static int[] Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.TryGetProperty("is_live", out var live) && live.ValueKind == JsonValueKind.True)
            throw new InvalidDataException("Live stream");
        if (root.TryGetProperty("has_drm", out var drm) && drm.ValueKind == JsonValueKind.True)
            throw new InvalidDataException("DRM");
        if (!root.TryGetProperty("formats", out var formats)) return [];
        return formats.EnumerateArray().Where(f => (!f.TryGetProperty("has_drm", out var protectedFormat) || protectedFormat.ValueKind != JsonValueKind.True)
                && (!f.TryGetProperty("vcodec", out var codec) || codec.ValueKind != JsonValueKind.String || codec.GetString() != "none"))
            .Select(f => f.TryGetProperty("height", out var height) && height.ValueKind == JsonValueKind.Number && height.TryGetInt32(out var n) ? n : 0)
            .Where(n => n > 0 && n <= 16384).Distinct().OrderDescending().ToArray();
    }
    public static string Selector(int height, bool merge) => height <= 0 ? (merge ? "bv*+ba/b" : "b")
        : merge ? $"bv*[height={height}]+ba/b[height={height}]" : $"b[height={height}]";
}
