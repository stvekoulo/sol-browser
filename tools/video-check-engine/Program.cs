using Browser.Core;

var root = Path.GetFullPath(args.Length > 0 ? args[0] : "bin/video-engine-cache");
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
await VideoEngine.PrepareAsync(root, phase => Console.WriteLine(phase), timeout.Token);
