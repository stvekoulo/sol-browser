using Microsoft.Web.WebView2.Core;

namespace Browser.App;

internal static class NativeDownloadUi
{
    public static void Suppress(CoreWebView2 core, Action? openSol = null, bool disableAccelerators = true)
    {
        core.Settings.AreBrowserAcceleratorKeysEnabled = !disableAccelerators;
        core.IsDefaultDownloadDialogOpenChanged += (_, _) =>
        {
            if (!core.IsDefaultDownloadDialogOpen) return;
            core.CloseDefaultDownloadDialog();
            openSol?.Invoke();
        };
    }
}
