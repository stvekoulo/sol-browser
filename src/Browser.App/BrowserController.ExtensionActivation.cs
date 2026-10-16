using System.Runtime.InteropServices;
using Browser.Core;

namespace Browser.App;

public sealed partial class BrowserController
{
    private async void ShowExtension(ExtensionInfo extension, double right, double top)
    {
        await _extensionChanges.WaitAsync();
        try
        {
            if (AppPaths.Private || EngineProfile is not { } profile) return;
            var state = await SharedExtensions.SnapshotAsync();
            if (!state.TryGetValue(extension.Id, out var enabled) || enabled != true) return;
            if (!(await profile.GetBrowserExtensionsAsync()).Any(e => e.Id == extension.Id && e.IsEnabled))
            { ShowExtensionEdgeFallback(extension, right, top); return; }
            // Les réglages ne remplacent pas l'action du bouton d'une extension.
            if ((extension.Popup ?? extension.SidePanel) is { Length: > 0 } page && ExtensionPopup is { } host)
                await host.ShowAsync($"chrome-extension://{extension.Id}/{page.TrimStart('/')}", right, top,
                    profile.ProfileName, panel: extension.Popup is null);
            else ShowExtensionEdgeFallback(extension, right, top);
        }
        catch (Exception error) when (error is InvalidOperationException or COMException or ArgumentException)
        {
            DebugLog.Write($"extension {extension.Id} : ouverture impossible : {error.Message}");
            ShowExtensionEdgeFallback(extension, right, top);
        }
        finally { _extensionChanges.Release(); }
    }

    private void ShowExtensionEdgeFallback(ExtensionInfo extension, double right, double top)
    {
        var entries = new List<(string Label, Action? Run)>();
        if (_tabs.ActiveId is { } id && _pages.CoreOf(id)?.Source is { } source
            && Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
            entries.Add((T("Ouvrir cette page dans Microsoft Edge", "Open this page in Microsoft Edge"),
                () => _afterMenu = () => _ = OpenExtensionEdgeAsync(source)));
        if (extension.Store)
            entries.Add((T("Installer l’extension dans Edge…", "Install the extension in Edge…"),
                () => _afterMenu = () => _ = OpenExtensionEdgeAsync($"https://chromewebstore.google.com/detail/{extension.Id}")));
        else entries.Add((T("Trouver l’extension dans Edge…", "Find the extension in Edge…"),
            () => _afterMenu = () => _ = OpenExtensionEdgeAsync("https://microsoftedge.microsoft.com/addons/")));
        if (extension.Options is { Length: > 0 } options)
            entries.Add((T("Réglages de l’extension dans Sol", "Extension settings in Sol"),
                () => _afterMenu = () => { Open($"chrome-extension://{extension.Id}/{options.TrimStart('/')}"); Refresh(); }));
        ShowMenu(Math.Max(8, right - Scaled(260)), top, entries);
    }

    private async Task OpenExtensionEdgeAsync(string url)
    {
        try
        {
            if (await Windows.System.Launcher.LaunchUriAsync(new Uri("microsoft-edge:" + url))) return;
        }
        catch (Exception error) when (error is COMException or ArgumentException)
        { DebugLog.Write("extension : lancement Edge : " + error.Message); }
        Notice(T("Microsoft Edge indisponible", "Microsoft Edge unavailable"),
            T("Installez Microsoft Edge pour utiliser cette extension dans son navigateur natif.",
                "Install Microsoft Edge to use this extension in its native browser."));
    }
}
