using Browser.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Browser.App;

public sealed partial class UiBridge
{
    public event Action? Painted;
    public event Action<ChromeLayout>? LayoutRequested;
    private bool PresentationCommand(WebView2 target, Command command)
    {
        if(command.Type!="ui.layout")return false;
        if(target==view && page=="index.html" && target.Parent is FrameworkElement root &&
            ChromeLayout.From(command,root.ActualWidth,root.ActualHeight) is { } layout)
            LayoutRequested?.Invoke(layout);
        return true;
    }
}
