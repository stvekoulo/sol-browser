using Microsoft.Web.WebView2.Core;
using Browser.Core;

namespace Browser.App;

public sealed partial class PageHost
{
    public event Action? ExtensionsChanged;

    public async Task SyncExtensionsAsync(string? reloadId = null)
    {
        var profiles = _views.Values.Select(view => view.CoreWebView2?.Profile).OfType<CoreWebView2Profile>()
            .DistinctBy(profile => profile.ProfileName).ToArray();
        var success = true;
        foreach (var profile in profiles) success &= await SharedExtensions.SyncAsync(profile, reloadId);
        ExtensionsChanged?.Invoke();
        if (!success) throw new InvalidOperationException(Language.Current == "fr"
            ? "Une extension n’a pas pu être synchronisée dans toutes les sessions. Réessayez ou utilisez Microsoft Edge."
            : "An extension could not be synchronized across all sessions. Retry or use Microsoft Edge.");
    }
}
