using Browser.Core;

var root = Path.GetFullPath(args.Length > 0 ? args[0] : "bin/video-engine-cache");
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
await VideoEngine.PrepareAsync(root, phase => Console.WriteLine(phase), timeout.Token);
if (args.Length > 1)
{
    using var probeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
    try
    {
        if (args.Length > 2 && args[2] == "--playlist")
        {
            var page = await VideoPlaylistProbe.InspectAsync(args[1], args[1], 1, root, [], _ => { }, probeTimeout.Token);
            Console.WriteLine($"Playlist entries: {page.Items.Length}; more: {page.HasMore}; unique: {page.Items.DistinctBy(e => e.Key).Count()}");
        }
        else
        {
            var heights = await VideoProbe.InspectAsync(args[1], args[1], root, [], _ => { }, probeTimeout.Token);
            Console.WriteLine("Qualities: " + string.Join(",", heights));
        }
    }
    catch (Exception e) when (e is IOException or InvalidDataException or System.Net.Http.HttpRequestException or OperationCanceledException)
    { Console.WriteLine(e is IOException && e.Message.StartsWith("Video probe: ") ? e.Message : "Probe failed: " + e.GetType().Name); Environment.ExitCode = 1; }
}
