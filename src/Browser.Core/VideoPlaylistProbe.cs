using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Browser.Core;

public static class VideoPlaylistProbe
{
    public static async Task<VideoPlaylistPage> InspectAsync(string url, string referer, int startIndex, string toolsRoot,
        IReadOnlyList<VideoCookie> cookies, Action<string> phase, CancellationToken ct)
    {
        if (startIndex < 1 || startIndex > 100_000) throw new ArgumentOutOfRangeException(nameof(startIndex));
        var tools = await VideoEngine.PrepareAsync(toolsRoot, phase, ct, inspectOnly: true);
        var start = new ProcessStartInfo(tools.Downloader)
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        // A flat, bounded page lists entries without fetching every video's formats or media.
        foreach (var arg in new[] { "--ignore-config", "--no-plugin-dirs", "--no-color", "--encoding", "utf-8",
            "--yes-playlist", "--flat-playlist", "--lazy-playlist", "--skip-download", "--dump-json", "--ignore-errors",
            "--playlist-items", $"{startIndex}:{startIndex + VideoPlaylist.PageSize}", "--socket-timeout", "20", "--retries", "1", "--referer", referer })
            start.ArgumentList.Add(arg);
        if (tools.Runtime is not null) { start.ArgumentList.Add("--js-runtimes"); start.ArgumentList.Add("deno:" + tools.Runtime); }
        var jar = Path.Combine(toolsRoot, "cookies-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            if (cookies.Count > 0)
            {
                await File.WriteAllTextAsync(jar, VideoCookie.Jar(cookies), ct);
                start.ArgumentList.Add("--cookies"); start.ArgumentList.Add(jar);
            }
            start.ArgumentList.Add("--"); start.ArgumentList.Add(VideoPlaylist.CollectionUrl(url));
            phase("inspect");
            using var process = Process.Start(start) ?? throw new IOException("Playlist engine start");
            using var registration = ct.Register(() => Kill(process));
            try
            {
                var errors = DrainAsync(process.StandardError, ct);
                var items = new List<VideoPlaylistEntry>();
                var count = 0;
                var hasMore = false;
                while (await process.StandardOutput.ReadLineAsync(ct) is { } line)
                {
                    if (line.Length > 1_000_000) throw new InvalidDataException("Playlist entry too large");
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (++count > VideoPlaylist.PageSize) { hasMore = true; continue; }
                    try
                    {
                        if (VideoPlaylist.ParseEntry(line, startIndex + count - 1) is { } item && items.All(e => e.Key != item.Key)) items.Add(item);
                    }
                    catch (JsonException) { }
                }
                await process.WaitForExitAsync(ct);
                await errors;
                ct.ThrowIfCancellationRequested();
                if (process.ExitCode != 0 && items.Count == 0) throw new IOException("Playlist unavailable");
                return new(items.ToArray(), startIndex, hasMore);
            }
            finally { Kill(process); }
        }
        finally { if (File.Exists(jar)) File.Delete(jar); }
    }
    private static async Task DrainAsync(StreamReader reader, CancellationToken ct)
    {
        // Do not retain extractor stderr: it may contain signed URLs or account identifiers.
        while (await reader.ReadLineAsync(ct) is not null) { }
    }
    private static void Kill(Process process)
    {
        try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { }
    }
}
