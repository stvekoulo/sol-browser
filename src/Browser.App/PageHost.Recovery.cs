using Browser.Core;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace Browser.App;

public sealed partial class PageHost
{
    private readonly HashSet<CoreWebView2> _reloading = new();
    public event Action? EngineLost;              // le moteur entier s'est arrêté : aucune vue de cette fenêtre ne survit
    public event Action<string>? PageUnresponsive; // l'onglet affiché ne répond plus

    private async Task ReloadResponsiveAsync(string id, CoreWebView2 core)
    {
        if (!_reloading.Add(core)) return;
        var navigation = _navigation.GetValueOrDefault(id);
        try
        {
            // A reload queued in a hung renderer never runs. Test its script queue first.
            await core.ExecuteScriptAsync("0").AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            if (Core(id) == core && _navigation.GetValueOrDefault(id) == navigation) core.Reload();
        }
        catch (Exception error) when (error is TimeoutException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            if (Core(id) != core || _navigation.GetValueOrDefault(id) != navigation) return;
            DebugLog.Write($"renderer.recover {id} {error.GetType().Name}");
            if (await InterruptForReloadAsync(core))
            {
                if (Core(id) == core && _navigation.GetValueOrDefault(id) == navigation) core.Reload();
                return;
            }
            if (Core(id) != core || _navigation.GetValueOrDefault(id) != navigation) return;
            var tab = TabOf(id, core);
            Remove(id, preserveProfile: true);
            await AddGuardedAsync(tab);
            ApplyLayout(_layout.Primary, _layout.Secondary, _layout.Ratio);
            RuntimeChanged?.Invoke();
        }
        finally { _reloading.Remove(core); }
    }

    private Tab TabOf(string id, CoreWebView2 core)
    {
        var profile = core.Profile.ProfileName;
        return new Tab(id, _errorUrl.GetValueOrDefault(id, core.Source), core.DocumentTitle,
            SessionId: profile.StartsWith("sol-") ? profile[4..] : "", Private: IsPrivate(id));
    }

    /// <summary>Le processus de rendu d'un onglet est mort ou bloqué : masqué, il repartira proprement quand on y reviendra.</summary>
    private void OnProcessFailed(string id, WebView2 view, CoreWebView2 core, CoreWebView2ProcessFailedKind kind)
    {
        if (_views.GetValueOrDefault(id) != view) return;
        DebugLog.Write($"process.failed {id} {kind}");
        try
        {
            if (kind == CoreWebView2ProcessFailedKind.BrowserProcessExited) EngineLost?.Invoke();
            else if (kind is not (CoreWebView2ProcessFailedKind.RenderProcessExited or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)) return;
            else if (!_visible.Contains(id))
            {
                var tab = TabOf(id, core);
                Remove(id, preserveProfile: true);
                _asleep[id] = tab;
                Discarded?.Invoke(id);
            }
            else if (kind == CoreWebView2ProcessFailedKind.RenderProcessExited)
                ShowError(id, core, _errorUrl.GetValueOrDefault(id, core.Source), "crash");
            else PageUnresponsive?.Invoke(id);
        }
        catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.COMException)
        { DebugLog.Write($"process.recover.failed {id} {error.HResult}"); }
    }

    // Un onglet qui ne répond plus longtemps après être redevenu visible ne se réveillera pas seul : il est rechargé.
    private async Task CheckReturnedTabAsync(string id, CoreWebView2 core)
    {
        try { await core.ExecuteScriptAsync("0").AsTask().WaitAsync(TimeSpan.FromSeconds(8)); }
        catch (TimeoutException)
        {
            if (Core(id) != core || !_visible.Contains(id) || _loading.Contains(id)) return;
            DebugLog.Write($"tab.return.frozen {id}");
            Wake(id, core);
            await ReloadResponsiveAsync(id, core);
        }
        catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.COMException) { /* vue fermée entre-temps */ }
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
