using System.Runtime.InteropServices;

namespace Browser.App;

public sealed partial class BrowserController
{
    private void OpenDevTools()
    {
        if (_tabs.ActiveId is not { } id || _pages.CoreOf(id) is not { } core) return;
        HidePopup();
        try { core.Settings.AreDevToolsEnabled = true; core.OpenDevToolsWindow(); }
        catch (Exception e) when (e is InvalidOperationException or COMException)
        { Notice(T("DevTools indisponibles", "DevTools unavailable"), T("Rechargez la page puis réessayez.", "Reload the page and try again.")); }
    }

    private async Task ExportNetworkAsync(string tabId)
    {
        var text = _network.Export(tabId);
        try
        {
            var picker = new Windows.Storage.Pickers.FileSavePicker
            { SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary, SuggestedFileName = "Sol network" };
            picker.FileTypeChoices.Add(T("Journal réseau JSON", "JSON network log"), [".json"]);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(_window));
            if (await picker.PickSaveFileAsync() is { } file) await Windows.Storage.FileIO.WriteTextAsync(file, text);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or COMException)
        { Notice(T("Export impossible", "Export failed"), T("Le journal est conservé. Choisissez un autre emplacement.", "The log is kept. Choose another location.")); }
    }
}
