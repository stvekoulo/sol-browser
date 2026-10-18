using Browser.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Browser.App;

public sealed partial class UiBridge
{
    public void ReplayTheme()
    {
        if (s_lastThemeMsg is not null) view.CoreWebView2?.PostWebMessageAsJson(s_lastThemeMsg);
    }

    private (byte R, byte G, byte B)? _publishedColor;
    private void PublishPageColor(byte r, byte g, byte b)
    {
        if (_publishedColor == (r, g, b)) return;
        _publishedColor = (r, g, b);
        PageColorChanged?.Invoke(r, g, b);
    }

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
