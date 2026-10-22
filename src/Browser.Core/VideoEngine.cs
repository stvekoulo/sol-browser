using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace Browser.Core;

// Outils indépendants, acquis seulement au premier téléchargement, jamais depuis une page web.
public static class VideoEngine
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(12) };
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> Gates = new();
    public sealed record Tools(string Downloader, string? Ffmpeg, string? Runtime = null);
    private sealed record Receipt(string File, string Hash);

    public static async Task<Tools> PrepareAsync(string root, Action<string> phase, CancellationToken ct, bool inspectOnly = false)
    {
        Directory.CreateDirectory(root);
        var arch = RuntimeInformation.OSArchitecture;
        var exe = arch == Architecture.Arm64 ? "yt-dlp_arm64.exe" : arch == Architecture.X86 ? "yt-dlp_x86.exe" : "yt-dlp.exe";
        var downloader = await AcquireAsync(root, "yt-dlp/yt-dlp", exe, false, "yt-dlp", "yt-dlp.exe", phase, ct);
        var runtime = arch == Architecture.X86 ? null : await AcquireAsync(root, "denoland/deno",
            "deno-x86_64-pc-windows-msvc.zip", true, "deno", "deno.exe", phase, ct);
        // FFmpeg win64 fonctionne aussi sous Windows ARM64 par émulation ; pas sous Windows 32 bits.
        var ffmpeg = arch == Architecture.X86 || inspectOnly ? null : await AcquireAsync(root, "BtbN/FFmpeg-Builds",
            "ffmpeg-master-latest-win64-lgpl-shared.zip", true, "ffmpeg", "ffmpeg.exe", phase, ct);
        return new(downloader, ffmpeg, runtime);
    }

    private static async Task<string> AcquireAsync(string root, string repo, string assetName, bool zip, string tool, string executableName, Action<string> phase, CancellationToken ct)
    {
        var gate = Gates.GetOrAdd(Path.GetFullPath(root) + ":" + tool, _ => new(1));
        await gate.WaitAsync(ct);
        try
        {
            await using var installLock = await LockAsync(Path.Combine(root, tool + ".install.lock"), ct);
            return await AcquireVerifiedAsync(root, repo, assetName, zip, tool, executableName, phase, ct);
        }
        finally { gate.Release(); }
    }

    private static async Task<string> AcquireVerifiedAsync(string root, string repo, string assetName, bool zip, string tool, string executableName, Action<string> phase, CancellationToken ct)
    {
        var folder = Path.Combine(root, tool);
        var receiptPath = folder + ".json";
        if (File.Exists(receiptPath))
        {
            try
            {
                var entries = JsonSerializer.Deserialize<Receipt[]>(await File.ReadAllTextAsync(receiptPath, ct))!;
                if (entries is { Length: > 0 } && entries.All(e => e is not null && !string.IsNullOrEmpty(e.File)
                    && SafeChild(folder, e.File) is { } path && File.Exists(path)
                    && Hash(path) == e.Hash))
                {
                    var executable = entries.First(e => Path.GetFileName(e.File) == executableName);
                    return SafeChild(folder, executable.File)!;
                }
            }
            catch (Exception e) when (e is IOException or JsonException or InvalidOperationException or ArgumentException) { }
        }
        phase(tool == "ffmpeg" ? "engine.ffmpeg" : "engine.downloader");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repo}/releases/latest");
        request.Headers.UserAgent.ParseAdd("Sol-Video/1.0");
        using var response = await Http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        using var metadata = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var asset = metadata.RootElement.GetProperty("assets").EnumerateArray().First(e => e.GetProperty("name").GetString() == assetName);
        var digest = asset.GetProperty("digest").GetString();
        if (digest is null || !digest.StartsWith("sha256:") || digest.Length != 71) throw new InvalidDataException("Missing SHA256");
        var url = asset.GetProperty("browser_download_url").GetString()!;
        if (!url.StartsWith($"https://github.com/{repo}/releases/download/", StringComparison.Ordinal)) throw new InvalidDataException("Asset origin");
        var temp = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var archive = Path.Combine(temp, "asset");
            using (var download = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                download.EnsureSuccessStatusCode();
                await using var target = File.Create(archive);
                await download.Content.CopyToAsync(target, ct);
            }
            if (!Hash(archive).Equals(digest[7..], StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("SHA256 mismatch");
            var unpacked = Path.Combine(temp, "unpacked");
            Directory.CreateDirectory(unpacked);
            if (zip) ZipFile.ExtractToDirectory(archive, unpacked);
            else File.Move(archive, Path.Combine(unpacked, executableName));
            ct.ThrowIfCancellationRequested();
            // On garde les licences et notices présentes dans la distribution FFmpeg.
            var entries = Directory.GetFiles(unpacked, "*", SearchOption.AllDirectories)
                .Select(p => new Receipt(Path.GetRelativePath(unpacked, p), Hash(p))).ToArray();
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            Directory.Move(unpacked, folder);
            await File.WriteAllTextAsync(receiptPath, JsonSerializer.Serialize(entries), ct);
            var executable = entries.Single(e => Path.GetFileName(e.File) == executableName);
            return SafeChild(folder, executable.File)!;
        }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
    private static async Task<FileStream> LockAsync(string path, CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { await Task.Delay(200, ct); }
        }
    }
    private static string? SafeChild(string root, string relative)
    {
        var path = Path.GetFullPath(Path.Combine(root, relative));
        return path.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? path : null;
    }
}
