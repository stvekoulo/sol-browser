using System.Runtime.InteropServices;
using System.Text.Json;
using Browser.Core;
using Microsoft.Web.WebView2.Core;

namespace Browser.App;

public sealed partial class PageHost
{
    private readonly Dictionary<string, (string Url, WebAppManifest App)> _webApps = new();
    private readonly Dictionary<string, int> _webAppGeneration = new();
    private readonly HashSet<string> _webAppPending = new();
    private readonly HashSet<string> _webAppAgain = new();
    public WebAppManifest? WebAppOf(string id) => _webApps.TryGetValue(id, out var value) && Core(id)?.Source == value.Url ? value.App : null;

    private async Task WireWebAppsAsync(string id, CoreWebView2 core)
    {
        // Le message est seulement un signal de rafraîchissement : aucune donnée de la page n'est exécutée.
        await core.AddScriptToExecuteOnDocumentCreatedAsync("""
            (() => {
              if (window !== top || !window.chrome?.webview) return;
              let timer;
              const notify = () => { clearTimeout(timer); timer=setTimeout(() => chrome.webview.postMessage('sol.pwa.changed'), 500); };
              const watch = () => {
                if (!document.head) return;
                new MutationObserver(records => {
                  if (records.some(r => r.target instanceof HTMLLinkElement || [...r.addedNodes,...r.removedNodes].some(n => n instanceof HTMLLinkElement))) notify();
                }).observe(document.head,{subtree:true,childList:true,attributes:true,attributeFilter:['rel','href']});
                notify();
              };
              document.addEventListener('DOMContentLoaded',watch,{once:true});
              window.addEventListener('beforeinstallprompt',notify);
            })()
            """);
        core.NavigationStarting += (_, _) =>
        {
            _webAppGeneration[id] = _webAppGeneration.GetValueOrDefault(id) + 1;
            if (_webApps.Remove(id)) RuntimeChanged?.Invoke();
        };
        core.SourceChanged += (_, _) => QueueWebAppCheck(id, core);
        core.DOMContentLoaded += (_, _) => QueueWebAppCheck(id, core);
        core.WebMessageReceived += (_, e) =>
        {
            if (e.Source == core.Source && e.WebMessageAsJson == "\"sol.pwa.changed\"") QueueWebAppCheck(id, core);
        };
    }

    private async void QueueWebAppCheck(string id, CoreWebView2 core)
    {
        if (!_webAppPending.Add(id)) { _webAppAgain.Add(id); return; }
        try
        {
            await Task.Delay(600);
            if (Core(id) != core || !Uri.TryCreate(core.Source, UriKind.Absolute, out var page)
                || !WebAppManifest.IsSecure(page) || page.Host is "start.sol" or "ui.navigateur" || IsPdf(id)) return;
            var url = core.Source;
            var generation = _webAppGeneration.GetValueOrDefault(id);
            using var result = JsonDocument.Parse(await core.CallDevToolsProtocolMethodAsync("Page.getAppManifest", "{}"));
            if (Core(id) != core || core.Source != url || _webAppGeneration.GetValueOrDefault(id) != generation) return;
            var root = result.RootElement;
            WebAppManifest? app = null;
            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.String
                && root.TryGetProperty("url", out var source) && source.ValueKind == JsonValueKind.String)
                app = WebAppManifest.Parse(url, source.GetString()!, data.GetString()!);
            if (app is null) _webApps.Remove(id); else _webApps[id] = (url, app);
            RuntimeChanged?.Invoke();
        }
        catch (Exception error) when (error is COMException or InvalidOperationException or JsonException or ArgumentException or OperationCanceledException) { }
        finally
        {
            _webAppPending.Remove(id);
            if (_webAppAgain.Remove(id) && Core(id) == core) QueueWebAppCheck(id, core);
        }
    }
}
