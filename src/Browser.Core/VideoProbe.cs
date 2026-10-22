using System.Diagnostics;

namespace Browser.Core;

public static class VideoProbe
{
    public static async Task<int[]> InspectAsync(string url, string referer, string toolsRoot,
        IReadOnlyList<VideoCookie> cookies, Action<string> phase, CancellationToken ct)
    {
        var tools = await VideoEngine.PrepareAsync(toolsRoot, phase, ct);
        phase("inspect");
        var jar = Path.Combine(toolsRoot, "cookies-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            var start = new ProcessStartInfo(tools.Downloader)
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8 };
            foreach (var arg in new[] { "--ignore-config", "--no-plugin-dirs", "--no-playlist", "--no-color", "--encoding", "utf-8", "--skip-download",
                "--dump-single-json", "--socket-timeout", "25", "--retries", "1", "--referer", referer }) start.ArgumentList.Add(arg);
            if (tools.Ffmpeg is not null) { start.ArgumentList.Add("--ffmpeg-location"); start.ArgumentList.Add(Path.GetDirectoryName(tools.Ffmpeg)!); }
            if (cookies.Count > 0)
            {
                await File.WriteAllTextAsync(jar, VideoCookie.Jar(cookies), ct);
                start.ArgumentList.Add("--cookies"); start.ArgumentList.Add(jar);
            }
            start.ArgumentList.Add("--"); start.ArgumentList.Add(url);
            using var process = Process.Start(start) ?? throw new IOException("Engine start");
            using var kill = ct.Register(() => { try { process.Kill(true); } catch (InvalidOperationException) { } });
            var errors = DrainAsync(process.StandardError, ct);
            var data = new System.Text.StringBuilder();
            var buffer = new char[8192];
            while (await process.StandardOutput.ReadAsync(buffer.AsMemory(), ct) is var read && read > 0)
            {
                if (data.Length + read > 8_000_000) { process.Kill(true); throw new InvalidDataException("Metadata too large"); }
                data.Append(buffer, 0, read);
            }
            await process.WaitForExitAsync(ct); await errors;
            if (process.ExitCode != 0) throw new InvalidDataException("Unsupported source");
            return VideoQualitySelection.Parse(data.ToString());
        }
        finally { if (File.Exists(jar)) File.Delete(jar); }
    }
    private static async Task DrainAsync(StreamReader reader, CancellationToken ct)
    { while (await reader.ReadLineAsync(ct) is not null) { } }
}
