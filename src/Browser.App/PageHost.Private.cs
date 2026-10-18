using Browser.Core;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace Browser.App;

public sealed partial class PageHost
{
    private readonly HashSet<string> _privateTabs = new();
    private string? _privateProfileName;
    private Task? _privateCleanup;
    public bool IsPrivate(string id) => AppPaths.Private || _privateTabs.Contains(id);

    private string PrivateProfileName => _privateProfileName ??= PrivateBrowsing.ProfilePrefix + Guid.NewGuid().ToString("N");

    private async Task DeleteAbandonedPrivateProfilesAsync(CoreWebView2Profile current)
    {
        var parent = Path.GetDirectoryName(current.ProfilePath);
        if (parent is null || !Directory.Exists(parent)) return;
        var environment = await AdBlock.EnvironmentAsync();
        foreach (var folder in Directory.GetDirectories(parent))
        {
            var directory = Path.GetFileName(folder);
            if (!directory.StartsWith("WV2Profile_", StringComparison.Ordinal)) continue;
            var name = directory[11..];
            if (!PrivateBrowsing.IsProfile(name) || name == _privateProfileName) continue;
            var view = new WebView2 { Opacity = 0, IsHitTestVisible = false, Width = 1, Height = 1 };
            container.Children.Add(view);
            try
            {
                var options = environment.CreateCoreWebView2ControllerOptions();
                options.ProfileName = name;
                await view.EnsureCoreWebView2Async(environment, options);
                await view.CoreWebView2.Profile.ClearBrowsingDataAsync();
                view.CoreWebView2.Profile.Delete();
            }
            catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.COMException)
            { DebugLog.Write($"private.cleanup.failed {error.GetType().Name}"); }
            finally { container.Children.Remove(view); view.Close(); }
        }
    }

    private async Task DeletePrivateProfileAsync(WebView2 view, CoreWebView2Profile profile)
    {
        _profileReady.Remove(profile.ProfileName);
        _sessionCookies.Remove(profile.ProfileName);
        if (_privateProfileName == profile.ProfileName) _privateProfileName = null;
        try
        {
            // Delete alone defers removal until the browser process exits.
            await profile.ClearBrowsingDataAsync();
            profile.Delete();
        }
        catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.COMException)
        { DebugLog.Write($"private.delete.failed {error.GetType().Name}"); }
        finally { view.Close(); }
    }

    public async Task ClosePrivateTabsAsync()
    {
        foreach (var id in _privateTabs.ToArray()) Remove(id);
        await Task.WhenAll(_closingProfiles.Where(p => PrivateBrowsing.IsProfile(p.Key)).Select(p => p.Value).ToArray());
    }
}
