using Browser.Core;
using Microsoft.Web.WebView2.Core;

namespace Browser.App;

public sealed partial class PageHost
{
    private readonly HashSet<CoreWebView2> _reloading = new();

    private async Task ReloadResponsiveAsync(string id, CoreWebView2 core)
    {
        if (!_reloading.Add(core)) return;
        var navigation = _navigation.GetValueOrDefault(id);
        try
        {
            // A reload queued in a hung renderer never runs. Test its script queue first.
            await core.ExecuteScriptAsync("0").AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            if (Core(id) == core) core.Reload();
        }
        catch (Exception error) when (error is TimeoutException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            if (Core(id) != core || _navigation.GetValueOrDefault(id) != navigation) return;
            DebugLog.Write($"renderer.recover {id} {error.GetType().Name}");
            if (await InterruptForReloadAsync(core))
            {
                if (Core(id) == core) core.Reload();
                return;
            }
            var profile = core.Profile.ProfileName;
            var tab = new Tab(id, _errorUrl.GetValueOrDefault(id, core.Source), core.DocumentTitle,
                SessionId: profile.StartsWith("sol-") ? profile[4..] : "", Private: IsPrivate(id));
            Remove(id, preserveProfile: true);
            await AddAsync(tab);
            ApplyLayout(_layout.Primary, _layout.Secondary, _layout.Ratio);
            RuntimeChanged?.Invoke();
        }
        finally { _reloading.Remove(core); }
    }

    private static async Task<bool> InterruptForReloadAsync(CoreWebView2 core)
    {
        try
        {
            // Only an explicit reload may interrupt page scripts; returning to a tab keeps its draft intact.
            await core.CallDevToolsProtocolMethodAsync("Runtime.terminateExecution", "{}").AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            await core.ExecuteScriptAsync("0").AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            return true;
        }
        catch (Exception error) when (error is TimeoutException or InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException)
        { return false; }
    }
}
