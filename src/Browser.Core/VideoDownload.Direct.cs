using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace Browser.Core;

public sealed partial class VideoDownload
{
    private async Task DownloadDirectAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(directory);
        var stem = Path.Combine(directory, VideoSources.FileStem(title) + " [" + _key + "]");
        var video = stem + ".video.part";
        var audio = stem + ".audio.part";
        Phase = "download"; BytesReceived = 0; TotalBytesToReceive = 0; Changed?.Invoke();
        var counts = new long[2]; var totals = new long[2];
        void Progress(int index, long bytes, long total)
        {
            Interlocked.Exchange(ref counts[index], bytes); Interlocked.Exchange(ref totals[index], total);
            BytesReceived = Interlocked.Read(ref counts[0]) + Interlocked.Read(ref counts[1]);
            TotalBytesToReceive = Interlocked.Read(ref totals[0]) + Interlocked.Read(ref totals[1]);
            Changed?.Invoke();
        }
        // Les premiers octets n'attendent jamais l'installation de l'assembleur.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var mux = audioUrl is null ? null : VideoEngine.PrepareAsync(toolsRoot, _ => { }, linked.Token);
        async Task<string> Transfer(string source, string path, int index)
        {
            try { return await TransferAsync(source, path, (bytes, total) => Progress(index, bytes, total), linked.Token); }
            catch { linked.Cancel(); throw; }
        }
        var videoTask = Transfer(url, video, 0);
        var audioTask = audioUrl is null ? null : Transfer(audioUrl, audio, 1);
        try
        {
            if (audioTask is not null) await Task.WhenAll(videoTask, audioTask);
            var extension = await videoTask;
            if (audioTask is null)
            {
                ResultFilePath = stem + extension;
                File.Move(video, ResultFilePath, false);
            }
            else
            {
                var tools = await mux!;
                if (tools.Ffmpeg is null) throw new IOException("Missing muxer");
                Phase = "merge"; Changed?.Invoke();
                var audioExtension = await audioTask;
                ResultFilePath = stem + (extension == audioExtension ? extension : ".mkv");
                // Une pause pendant l'assemblage peut laisser une sortie incomplète portant notre clé.
                File.Delete(ResultFilePath);
                var start = new ProcessStartInfo(tools.Ffmpeg) { UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardError = true, RedirectStandardOutput = true };
                foreach (var arg in new[] { "-nostdin", "-v", "error", "-n", "-i", video, "-i", audio, "-map", "0:v:0", "-map", "1:a:0", "-c", "copy", ResultFilePath })
                    start.ArgumentList.Add(arg);
                using var process = Process.Start(start) ?? throw new IOException("Muxer start");
                using var kill = ct.Register(() => { try { process.Kill(true); } catch (InvalidOperationException) { } });
                var errors = DrainErrorsAsync(process.StandardError, ct);
                await process.WaitForExitAsync(ct); await errors;
                if (process.ExitCode != 0) throw new IOException("Mux failed");
                File.Delete(video); File.Delete(audio);
            }
            foreach (var path in new[] { video, audio }) File.Delete(path + ".etag");
            BytesReceived = TotalBytesToReceive = new FileInfo(ResultFilePath).Length;
            State = "Completed"; Phase = "done";
        }
        finally { linked.Cancel(); if (mux is not null) { try { await mux; } catch { /* L'erreur du transfert reste prioritaire. */ } } }
    }

    private async Task<string> TransferAsync(string source, string path, Action<long, long> progress, CancellationToken ct)
    {
        var jar = new CookieContainer();
        foreach (var cookie in cookies)
        {
            try { jar.Add(new Cookie(cookie.Name, cookie.Value, cookie.Path, cookie.Domain) { Secure = cookie.Secure }); }
            catch (CookieException) { }
        }
        using var handler = new HttpClientHandler { CookieContainer = jar };
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var request = new HttpRequestMessage(HttpMethod.Get, source);
        request.Headers.Referrer = new Uri(referer);
        var offset = File.Exists(path) && File.Exists(path + ".etag") ? new FileInfo(path).Length : 0;
        if (offset > 0)
        {
            request.Headers.Range = new RangeHeaderValue(offset, null);
            request.Headers.TryAddWithoutValidation("If-Range", await File.ReadAllTextAsync(path + ".etag", ct));
        }
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        if (response.StatusCode == HttpStatusCode.PartialContent)
        {
            if (response.Content.Headers.ContentRange?.From != offset) throw new IOException("Invalid range");
        }
        else offset = 0;
        var mime = response.Content.Headers.ContentType?.MediaType ?? "";
        if (mime.Contains("html") || mime.Contains("json") || mime.Contains("xml")) throw new IOException("Not media");
        if (response.Headers.ETag is { IsWeak: false } etag) await File.WriteAllTextAsync(path + ".etag", etag.ToString(), ct);
        else File.Delete(path + ".etag");
        var total = response.Content.Headers.ContentRange?.Length ?? (response.Content.Headers.ContentLength is { } length ? offset + length : 0);
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        await using var output = new FileStream(path, offset > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read, 65536, true);
        var buffer = new byte[65536]; var received = offset; var last = Stopwatch.StartNew();
        progress(received, total);
        while (await input.ReadAsync(buffer, ct) is var read && read > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), ct); received += read;
            if (last.ElapsedMilliseconds >= 100 || received == total) { progress(received, total); last.Restart(); }
        }
        if (total > 0 && received != total) throw new IOException("Truncated media");
        if (received == 0) throw new IOException("Empty media");
        progress(received, total);
        return mime.Contains("webm") ? ".webm" : mime.Contains("quicktime") ? ".mov" : mime.Contains("matroska") ? ".mkv"
            : mime.Contains("ogg") ? ".ogv" : ".mp4";
    }
}
