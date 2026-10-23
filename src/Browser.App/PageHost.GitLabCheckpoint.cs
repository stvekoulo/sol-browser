using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Browser.Core;
using Microsoft.Web.WebView2.Core;

namespace Browser.App;

public sealed partial class PageHost
{
    private readonly ConditionalWeakTable<CoreWebView2, object> _gitLabWatchers = new();
    private readonly Dictionary<string, Task> _gitLabCheckpoints = new();
    private readonly HashSet<string> _gitLabCheckpointPending = new();

    private void WatchGitLabSession(CoreWebView2 core)
    {
        var profile = core.Profile;
        if (AppPaths.Private || PrivateBrowsing.IsProfile(profile.ProfileName) || _gitLabWatchers.TryGetValue(core, out _)) return;
        _gitLabWatchers.Add(core, new object());
        core.WebResourceResponseReceived += (_, args) =>
        {
            try
            {
                if (!args.Response.Headers.Contains("Set-Cookie")) return;
                var header = args.Response.Headers.GetHeader("Set-Cookie");
                if (header.Contains("_gitlab_session=", StringComparison.Ordinal)
                    || header.Contains("gitlab_user=", StringComparison.Ordinal)) QueueGitLabCheckpoint(profile);
            }
            catch (Exception error) when (error is InvalidOperationException or COMException)
            { DebugLog.Write($"gitlab.session.observe.failed {error.GetType().Name}"); }
        };
    }

    private void QueueGitLabCheckpoint(CoreWebView2Profile profile)
    {
        var name = profile.ProfileName;
        _gitLabCheckpointPending.Add(name);
        if (!_gitLabCheckpoints.ContainsKey(name)) _gitLabCheckpoints[name] = DrainGitLabCheckpointsAsync(profile, name);
    }

    private async Task DrainGitLabCheckpointsAsync(CoreWebView2Profile profile, string name)
    {
        try
        {
            // ResponseReceived precedes completion of the cookie-store update. Coalesce
            // parallel responses and read again if another rotation arrives during a read.
            while (_gitLabCheckpointPending.Remove(name))
            {
                await Task.Delay(100);
                await CheckpointGitLabNowAsync(profile);
            }
        }
        finally { _gitLabCheckpoints.Remove(name); }
    }

    private async Task CheckpointGitLabNowAsync(CoreWebView2Profile profile)
    {
        if (AppPaths.Private || PrivateBrowsing.IsProfile(profile.ProfileName)) return;
        try
        {
            var cookies = await profile.CookieManager.GetCookiesAsync("");
            SaveGitLabSession(profile.ProfileName, cookies);
        }
        catch (Exception error) when (error is InvalidOperationException or COMException)
        { DebugLog.Write($"gitlab.session.checkpoint.failed {error.GetType().Name}"); }
    }
}
