using System.Net;
using System.Net.Sockets;
using System.Text;
using Browser.Core;

namespace Browser.Core.Tests;

public class VideoDirectTests
{
    [Fact]
    public async Task DirectTransferStartsWithoutAnyInstalledEngineAndUsesScopedCookies()
    {
        using var server = new TcpListener(IPAddress.Loopback, 0); server.Start();
        var port = ((IPEndPoint)server.LocalEndpoint).Port;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var ct = deadline.Token;
        var received = new TaskCompletionSource<string>();
        var body = Encoding.UTF8.GetBytes("local video fixture bytes");
        var serve = Task.Run(async () =>
        {
            using var client = await server.AcceptTcpClientAsync(ct);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, leaveOpen: true);
            var headers = new StringBuilder();
            while (await reader.ReadLineAsync(ct) is { Length: > 0 } line) headers.AppendLine(line);
            received.SetResult(headers.ToString());
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: video/mp4\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n"), ct);
            await stream.WriteAsync(body, ct);
        }, ct);
        var root = Path.Combine(Path.GetTempPath(), "sol-video-direct-" + Guid.NewGuid().ToString("N"));
        try
        {
            var job = new VideoDownload($"http://127.0.0.1:{port}/video.mp4", $"http://127.0.0.1:{port}/page", "Fixture", root,
                Path.Combine(root, "absent-engines"), [new("127.0.0.1", "/", false, 0, "session", "ok")], 720, direct: true);
            job.Start();
            var request = await received.Task.WaitAsync(ct);
            Assert.Contains("session=ok", request);
            while (job.State == "InProgress") await Task.Delay(10, ct);
            Assert.Equal("Completed", job.State);
            Assert.Equal(body, await File.ReadAllBytesAsync(job.ResultFilePath, ct));
            Assert.False(Directory.Exists(Path.Combine(root, "absent-engines")));
            await serve;
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
