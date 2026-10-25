using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Browser.Core;

public sealed record VideoVariant(string Key, string Url, int Height, string Kind, string Label,
    string? AudioUrl = null, bool NeedsResolver = false, string? FormatId = null);

public static class VideoVariants
{
    public static IReadOnlyList<VideoVariant> Hls(string url, string manifest)
    {
        if (!VideoSources.IsHttp(url) || manifest.Length > 1_000_000 || !manifest.TrimStart().StartsWith("#EXTM3U")) return [];
        var lines = manifest.Split('\n').Select(s => s.Trim()).ToArray();
        var variants = new List<VideoVariant>();
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].StartsWith("#EXT-X-STREAM-INF:")) continue;
            var resolution = Regex.Match(lines[i], @"RESOLUTION=\d+x(\d+)", RegexOptions.IgnoreCase);
            var next = lines.Skip(i + 1).FirstOrDefault(s => s.Length > 0 && !s.StartsWith('#'));
            if (next is null || !Uri.TryCreate(new Uri(url), next, out var target) || !VideoSources.IsHttp(target.AbsoluteUri)) continue;
            var height = resolution.Success && int.TryParse(resolution.Groups[1].Value, out var n) ? n : 0;
            // Le master conserve la relation entre qualité et pistes audio séparées.
            variants.Add(new("hls:" + target.AbsoluteUri, url, height, "hls", height > 0 ? $"{height}p" : "HLS"));
        }
        if (variants.Count == 0 && lines.Any(s => s.StartsWith("#EXTINF:"))) variants.Add(new("hls:" + url, url, 0, "hls", "HLS"));
        return variants.Take(80).ToArray();
    }

    public static IReadOnlyList<VideoVariant> Dash(string url, string manifest)
    {
        if (!VideoSources.IsHttp(url) || manifest.Length > 1_000_000) return [];
        try
        {
            using var text = new StringReader(manifest);
            using var reader = System.Xml.XmlReader.Create(text, new() { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null });
            var root = XDocument.Load(reader).Root;
            if (root is null || root.Descendants().Any(e => e.Name.LocalName == "ContentProtection")) return [];
            return root.Descendants().Where(e => e.Name.LocalName == "Representation")
                .Select(e => int.TryParse((string?)e.Attribute("height"), out var h) ? h : 0).Where(h => h > 0 && h <= 16384).Distinct()
                .Take(80).Select(h => new VideoVariant("dash:" + url + ":" + h, url, h, "dash", $"{h}p")).ToArray();
        }
        catch (Exception e) when (e is System.Xml.XmlException or InvalidOperationException) { return []; }
    }

    public static string? Query(string url, string key)
    {
        if (!VideoSources.IsHttp(url)) return null;
        foreach (var part in new Uri(url).Query.TrimStart('?').Split('&'))
        {
            var pair = part.Split('=', 2);
            if (pair.Length == 2 && Uri.UnescapeDataString(pair[0]) == key) return Uri.UnescapeDataString(pair[1]);
        }
        return null;
    }
    public static string WholeFile(string url)
    {
        if (!VideoSources.IsHttp(url)) return url;
        var uri = new Uri(url);
        if (!uri.Host.EndsWith(".googlevideo.com", StringComparison.OrdinalIgnoreCase)) return url;
        var signed = (Query(url, "sparams") ?? "").Split(',');
        var parts = uri.Query.TrimStart('?').Split('&').Where(p =>
        {
            var key = Uri.UnescapeDataString(p.Split('=', 2)[0]);
            return key is not ("range" or "rn" or "rbuf") || signed.Contains(key);
        });
        return new UriBuilder(uri) { Query = string.Join('&', parts) }.Uri.AbsoluteUri;
    }
}
