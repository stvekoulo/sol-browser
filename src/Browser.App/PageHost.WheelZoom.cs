using Microsoft.Web.WebView2.Core;

namespace Browser.App;

public sealed partial class PageHost
{
    public event Action<string, bool>? WheelZoomRequested;

    private async Task WireWheelZoomAsync(CoreWebView2 core)
    {
        var prefix = "sol.wheel:" + FanKey + ":";
        var script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "inject", "page-wheel.js"));
        await core.AddScriptToExecuteOnDocumentCreatedAsync(script.Replace("__SOL_WHEEL_KEY__", FanKey));
        void Handle(CoreWebView2WebMessageReceivedEventArgs e)
        {
            if (!e.WebMessageAsJson.StartsWith('"')) return;
            var signal = e.TryGetWebMessageAsString();
            if (signal != prefix + "in" && signal != prefix + "out") return;
            var id = _views.FirstOrDefault(pair => pair.Value.CoreWebView2 == core).Key;
            if (id is null || !_visible.Contains(id) || IsPdf(id)) return;
            WheelZoomRequested?.Invoke(id, signal == prefix + "in");
        }
        core.WebMessageReceived += (_, e) => { if (e.Source == core.Source) Handle(e); };
        core.FrameCreated += (_, e) => e.Frame.WebMessageReceived += (_, message) => Handle(message);
    }
}
