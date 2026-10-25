using Browser.Core;
using Microsoft.Web.WebView2.Core;

namespace Browser.App;

internal static class NativePermissions
{
    public static async Task SyncAsync(CoreWebView2Profile profile)
    {
        if (PrivateBrowsing.IsProfile(profile.ProfileName)) return;
        var store = new PermissionStore(PermissionStore.FileForProfile(AppPaths.DataDir, profile.ProfileName));
        // Sol's saved consent is authoritative for websites. Extensions also appear
        // here, but their manifest grants cannot be reset through the website API.
        var currentPermissions = await profile.GetNonDefaultPermissionSettingsAsync();
        foreach (var current in currentPermissions)
        {
            if (PermissionStore.OriginOf(current.PermissionOrigin) is not { } origin) continue;
            var answer = store.Get(origin, current.PermissionKind.ToString());
            var expected = answer is null ? CoreWebView2PermissionState.Default
                : answer.Value ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny;
            if (current.PermissionState != expected)
                await profile.SetPermissionStateAsync(current.PermissionKind, origin, expected);
        }
        foreach (var answer in store.All)
            if (Enum.TryParse<CoreWebView2PermissionKind>(answer.Kind, out var kind)
                && Enum.IsDefined(kind) && kind != CoreWebView2PermissionKind.UnknownPermission
                && PermissionStore.OriginOf(answer.Origin) == answer.Origin)
                await profile.SetPermissionStateAsync(kind, answer.Origin,
                    answer.Allowed ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny);
    }

    public static async Task ResetAsync(CoreWebView2Profile profile, string? origin = null)
    {
        foreach (var current in await profile.GetNonDefaultPermissionSettingsAsync())
        {
            if (PermissionStore.OriginOf(current.PermissionOrigin) is not { } website) continue;
            if (origin is null || website == origin)
                await profile.SetPermissionStateAsync(current.PermissionKind, website, CoreWebView2PermissionState.Default);
        }
    }
}
