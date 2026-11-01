using Browser.App;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

static void Check(bool passed, string message)
{
    if (!passed) throw new Exception(message);
}

var parent = new FrameworkElement();
var frame = new Grid { Parent = parent };
var bridge = new UiBridge();
var initialized = new TaskCompletionSource();
var host = new DockHost(frame, new(), bridge, () => initialized.Task);
host.Post("first");
host.Hide();
initialized.SetResult();
host.Ready();
Check(bridge.Messages.Count == 0, "Ready must not replay hidden content after slow initialization");
host.SetSize(300, 160);
Check(!host.Visible, "A late size must not resurrect a hidden dock");
host.Post("second");
host.SetSize(300, 160);
Check(host.Visible, "A subsequent download must show the dock");
host.Hide();
parent.Resize();
Check(!host.Visible, "A parent resize must not show hidden content");
host.Post("third");
Check(host.Visible, "A same-size subsequent download must reopen without ResizeObserver");
host.SetSize(double.NaN, 100);
Check(double.IsFinite(frame.Width), "Malformed size must not reach native layout");

var attempts = 0;
var retryBridge = new UiBridge();
var retry = new DockHost(new(), new(), retryBridge, () =>
{
    attempts++;
    return attempts == 1 ? Task.FromException(new InvalidOperationException("fixture failure")) : Task.CompletedTask;
});
retry.Post("failed");
retry.Post("retry");
Check(attempts == 2, "An initialization failure must allow the next post to retry");
retry.Ready();
Check(retryBridge.Messages.Last() == "retry", "Retry must replay the current download");
Console.WriteLine("PASS: DockHost late Ready/size, same-size reopening, parent resize, malformed size and initialization retry");
