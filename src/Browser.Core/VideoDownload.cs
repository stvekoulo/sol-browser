using System.Diagnostics;
using System.Text.Json;

namespace Browser.Core;

public sealed partial class VideoDownload(string url, string referer, string title, string directory, string toolsRoot,
    IReadOnlyList<VideoCookie> cookies, int height = 0, bool direct = false, string? audioUrl = null)
{
    public event Action? Changed;
    public string State { get; private set; } = "InProgress";
    public string Phase { get; private set; } = "engine.downloader";
    public long BytesReceived { get; private set; }
    public long TotalBytesToReceive { get; private set; }
    public string ResultFilePath { get; private set; } = Path.Combine(directory, VideoSources.FileStem(title) + ".mp4");
    public bool Paused { get; private set; }
    public bool Canceled { get; private set; }
    public bool CanResume => State == "Interrupted" && !Canceled;
    private readonly string _key = Guid.NewGuid().ToString("N")[..10];
    private CancellationTokenSource? _cancel;
    private Task? _running;
    private bool _resuming;

    public void Start() => _running ??= Task.Run(RunAsync);
    public void Pause() { if (State != "InProgress") return; Paused = true; _cancel?.Cancel(); }
    public void Cancel()
    {
        if (State == "Completed") return;
        Canceled = true; Paused = false; _cancel?.Cancel(); State = "Interrupted";
        if (_running?.IsCompleted == true) RemovePartialFiles();
        Changed?.Invoke();
    }
    public async void Resume()
    {
        if (!CanResume || _resuming) return;
        _resuming = true;
        if (_running is not null) await _running;
        if (Canceled) { _resuming = false; return; }
        Paused = false; State = "InProgress"; _running = Task.Run(RunAsync); _resuming = false;
    }

    private async Task RunAsync()
    {
        using var cancellation = new CancellationTokenSource();
        _cancel = cancellation;
        var ct = cancellation.Token;
        if (Canceled || Paused) cancellation.Cancel();
        string? jar = null;
        try
        {
            if (direct)
            {
                try { await DownloadDirectAsync(ct); return; }
                catch (System.Net.Http.HttpRequestException) when (new Uri(url).Host.EndsWith(".googlevideo.com", StringComparison.OrdinalIgnoreCase))
                { RemovePartialFiles(); url = referer; direct = false; }
            }
            var tools = await VideoEngine.PrepareAsync(toolsRoot, p => { Phase = p; Changed?.Invoke(); }, ct);
            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(directory);
            var prefix = VideoSources.FileStem(title) + " [" + _key + "]";
            var start = new ProcessStartInfo(tools.Downloader)
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8, WorkingDirectory = directory };
            var args = start.ArgumentList;
            foreach (var a in new[] { "--ignore-config", "--no-plugin-dirs", "--no-playlist", "--no-color", "--encoding", "utf-8", "--newline", "--windows-filenames",
                "--no-overwrites", "--continue", "--socket-timeout", "30", "--retries", "3", "--fragment-retries", "3",
                "--progress", "--progress-template", "download:SOL_PROGRESS:%(progress)j", "--print", "after_move:SOL_FILE:%(filepath)j",
                "--no-simulate", "--output", prefix + ".%(ext)s", "--format", VideoQualitySelection.Selector(height, tools.Ffmpeg is not null),
                "--referer", referer }) args.Add(a);
            if (tools.Ffmpeg is not null) { args.Add("--ffmpeg-location"); args.Add(Path.GetDirectoryName(tools.Ffmpeg)!); }
            if (tools.Runtime is not null) { args.Add("--js-runtimes"); args.Add("deno:" + tools.Runtime); }
            if (cookies.Count > 0)
            {
                // Cookie jar temporaire, seulement les origines du site et de la source choisie.
                jar = Path.Combine(toolsRoot, "cookies-" + Guid.NewGuid().ToString("N") + ".txt");
                await File.WriteAllTextAsync(jar, VideoCookie.Jar(cookies), ct);
                args.Add("--cookies"); args.Add(jar);
            }
            args.Add("--"); args.Add(url);
            Phase = "download"; Changed?.Invoke();
            using var process = Process.Start(start) ?? throw new IOException("Engine start");
            using var registration = ct.Register(() => { try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } });
            var errors = DrainErrorsAsync(process.StandardError, ct);
            while (await process.StandardOutput.ReadLineAsync(ct) is { } line) ReadProgress(line);
            await process.WaitForExitAsync(ct);
            var failure = await errors;
            ct.ThrowIfCancellationRequested();
            if (process.ExitCode != 0) { Phase = failure; State = "Interrupted"; }
            else if (!File.Exists(ResultFilePath) || !Path.GetFileName(ResultFilePath).Contains(_key)) throw new IOException("Missing output");
            else { State = "Completed"; Phase = "done"; BytesReceived = new FileInfo(ResultFilePath).Length; TotalBytesToReceive = BytesReceived; }
        }
        catch (OperationCanceledException) { State = "Interrupted"; Phase = Canceled ? "canceled" : "paused"; }
        catch (Exception e) when (e is IOException or InvalidDataException or System.Net.Http.HttpRequestException or JsonException or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or ArgumentException)
        { State = "Interrupted"; Phase = "failed"; }
        finally
        {
            _cancel = null;
            if (jar is not null) { try { File.Delete(jar); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }
            if (Canceled) RemovePartialFiles();
            Changed?.Invoke();
        }
    }
    private void RemovePartialFiles()
    {
        try
        {
            foreach (var file in Directory.Exists(directory) ? Directory.GetFiles(directory, "*" + _key + "*") : []) File.Delete(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private void ReadProgress(string line)
    {
        if (line.StartsWith("SOL_FILE:"))
        {
            var path = JsonSerializer.Deserialize<string>(line[9..]);
            if (path is not null && Path.GetFullPath(path).StartsWith(Path.GetFullPath(directory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(path).Contains(_key) && VideoSources.Kind("https://output.invalid/" + Uri.EscapeDataString(Path.GetFileName(path))) is not null)
                ResultFilePath = path;
        }
        if (!line.StartsWith("SOL_PROGRESS:")) return;
        try
        {
            using var json = JsonDocument.Parse(line[13..]);
            var root = json.RootElement;
            BytesReceived = Number(root, "downloaded_bytes");
            TotalBytesToReceive = Number(root, "total_bytes");
            if (TotalBytesToReceive == 0) TotalBytesToReceive = Number(root, "total_bytes_estimate");
            Phase = root.TryGetProperty("status", out var status) && status.GetString() == "finished" ? "merge" : "download";
            Changed?.Invoke();
        }
        catch (JsonException) { }
    }
    private static long Number(JsonElement root, string key) => root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var n) ? Math.Max(0, n) : 0;
    private async Task<string> DrainErrorsAsync(StreamReader reader, CancellationToken ct)
    {
        var failure = "failed";
        // Ne conserver ni afficher les lignes : les URL signées peuvent contenir des identifiants.
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (line.StartsWith("SOL_PROGRESS:")) ReadProgress(line);
            else if (line.Contains("DRM", StringComparison.OrdinalIgnoreCase)) failure = "protected";
            else if (line.Contains("403") || line.Contains("401") || line.Contains("sign in", StringComparison.OrdinalIgnoreCase)) failure = "auth";
        }
        return failure;
    }
}

public sealed record VideoCookie(string Domain, string Path, bool Secure, long Expires, string Name, string Value)
{
    public static string Jar(IEnumerable<VideoCookie> cookies) => "# Netscape HTTP Cookie File\n" + string.Join("\n", cookies
        .Where(c => !(c.Domain + c.Path + c.Name + c.Value).Any(ch => ch is '\r' or '\n' or '\t'))
        .Select(c => $"{c.Domain}\t{(c.Domain.StartsWith('.') ? "TRUE" : "FALSE")}\t{c.Path}\t{(c.Secure ? "TRUE" : "FALSE")}\t{c.Expires}\t{c.Name}\t{c.Value}")) + "\n";
}
