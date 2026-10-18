using Browser.Core;
using Microsoft.Web.WebView2.Core;

namespace Browser.App;

public sealed partial class PageHost
{
    private (string Tab, string Marker, TaskCompletionSource<bool> Completion)? _extensionAction;

    public async Task<bool> InvokeExtensionActionAsync(string tabId, string marker)
    {
        if (CoreOf(tabId) is not { } core || !_ready.Contains(tabId)) return false;
        try
        {
            await core.ExecuteScriptAsync("globalThis.__solNativeContextMenu?.(true)");
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _extensionAction = (tabId, marker, completion);
                await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",
                    """{"type":"mousePressed","button":"right","buttons":2,"x":4,"y":4,"clickCount":1}""");
                await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",
                    """{"type":"mouseReleased","button":"right","x":4,"y":4,"clickCount":1}""");
                if (await completion.Task.WaitAsync(TimeSpan.FromSeconds(5))) return true;
                await Task.Delay(150 * (attempt + 1));
            }
            return false;
        }
        finally
        {
            _extensionAction = null;
            try { await core.ExecuteScriptAsync("globalThis.__solNativeContextMenu?.(false)"); }
            catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.COMException) { }
        }
    }

    private bool HandleExtensionAction(string tabId, CoreWebView2ContextMenuRequestedEventArgs args)
    {
        if (_extensionAction is not { } request || request.Tab != tabId) return false;
        _extensionAction = null;
        args.Handled = true;
        var item = FindExtensionAction(args.MenuItems, request.Marker);
        if (item is not null) args.SelectedCommandId = item.CommandId;
        request.Completion.TrySetResult(item is not null);
        return true;
    }

    private static CoreWebView2ContextMenuItem? FindExtensionAction(IEnumerable<CoreWebView2ContextMenuItem> items, string marker)
    {
        foreach (var item in items)
        {
            if (!item.IsEnabled) continue;
            if (item.Name == "extension" && item.Kind == CoreWebView2ContextMenuItemKind.Command && item.Label == marker) return item;
            if (item.Kind == CoreWebView2ContextMenuItemKind.Submenu && FindExtensionAction(item.Children, marker) is { } child) return child;
        }
        return null;
    }
}
