using Browser.Core;
using Microsoft.Web.WebView2.Core;

namespace Browser.App;

public sealed partial class PageHost
{
    private static void RecordStartupFailure(string stage, Exception error, bool isPrivate)
    {
        if (!isPrivate) SessionStartupDiagnostics.Record(stage, error);
        DebugLog.Write($"startup.{stage}.failed {error.GetType().Name} {error.HResult}");
    }

    private async Task PrepareProfileAsync(CoreWebView2Profile profile)
    {
        // Required: a revoked website grant must never survive because preparation was skipped.
        await NativePermissions.SyncAsync(profile);
        var isPrivate = PrivateBrowsing.IsProfile(profile.ProfileName);
        Task Optional(string stage, Func<Task> prepare) =>
            StartupPreparation.OptionalAsync(stage, prepare, (step, error) => RecordStartupFailure(step, error, isPrivate));
        await Optional("legacy-blocker", () => AdBlock.RemoveBundledAsync(profile));
        await Optional("page-zoom", () => PageZoom.InstallAsync(profile, settings.PageZoom));
        await Optional("extensions", async () => { await SharedExtensions.SyncAsync(profile); });
        ExtensionsChanged?.Invoke();
    }
}
