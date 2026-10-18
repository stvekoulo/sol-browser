using Browser.Core;
using Microsoft.Web.WebView2.Core;

namespace Browser.App;

public sealed partial class BrowserController
{
    private static IEnumerable<(string Label, Action? Run)> NativeExtensionEntries(
        IEnumerable<CoreWebView2ContextMenuItem> items, CoreWebView2ContextMenuRequestedEventArgs args, string prefix = "")
    {
        foreach (var item in items)
        {
            if (!item.IsEnabled || item.Label.StartsWith(ExtensionAction.Prefix, StringComparison.Ordinal)) continue;
            if (item.Name != "extension" && prefix.Length == 0) continue;
            var label = prefix + item.Label.Replace("&", "");
            if (item.Kind == CoreWebView2ContextMenuItemKind.Submenu)
            {
                foreach (var child in NativeExtensionEntries(item.Children, args, label + " › ")) yield return child;
            }
            else if (item.Kind is CoreWebView2ContextMenuItemKind.Command or CoreWebView2ContextMenuItemKind.CheckBox or CoreWebView2ContextMenuItemKind.Radio)
            {
                var id = item.CommandId;
                yield return (label, () => args.SelectedCommandId = id);
            }
        }
    }
}
