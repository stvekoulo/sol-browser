using Browser.Core;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace Browser.App;

/// <summary>Vue d'interface : ne charge que les fichiers locaux, reçoit les commandes, envoie des messages.</summary>
public sealed class UiBridge(WebView2 view, string page = "index.html")
{
    private const string Origin = "https://ui.navigateur/";

    public async Task InitAsync(string uiFolder, Action<Command> onCommand)
    {
        await view.EnsureCoreWebView2Async();
        var core = view.CoreWebView2;
        core.SetVirtualHostNameToFolderMapping("ui.navigateur", uiFolder, CoreWebView2HostResourceAccessKind.Deny);
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false; // pas de F5, Ctrl+P, Ctrl+F… sur l'interface elle-même
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;

        // L'interface ne quitte jamais ses fichiers locaux.
        core.NavigationStarting += (_, e) => { if (!e.Uri.StartsWith(Origin, StringComparison.Ordinal)) e.Cancel = true; };
        core.NewWindowRequested += (_, e) => e.Handled = true;

        core.WebMessageReceived += (_, e) =>
        {
            string json;
            try { json = e.TryGetWebMessageAsString(); }
            catch (ArgumentException) { return; } // message non textuel
            if (e.Source.StartsWith(Origin, StringComparison.Ordinal) && CommandParser.Parse(json) is { } command)
                onCommand(command);
        };

        core.Navigate(Origin + page);
    }

    public void Post(string json) => view.CoreWebView2?.PostWebMessageAsJson(json);
}
