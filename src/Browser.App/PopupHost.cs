using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Browser.App;

/// <summary>
/// Petite vue d'interface posée par-dessus la page, à la taille de son contenu : suggestions, bibliothèque.
/// Opaque par construction : la transparence d'une vue web ne fonctionne pas en WinUI 3 (voir les résultats du prototype 1).
/// </summary>
public sealed class PopupHost(Grid frame, WebView2 view, UiBridge bridge)
{
    private bool _pending;

    /// <summary>Place la vue et lui envoie son contenu. Elle devient visible quand elle a annoncé sa hauteur.</summary>
    public void Show(double x, double y, double width, string contentJson, bool focus)
    {
        frame.Margin = new Thickness(Math.Max(0, x), Math.Max(0, y), 0, 0);
        frame.Width = Math.Clamp(width, 120, 900);
        _pending = true;
        _focus = focus;
        bridge.Post(contentJson);
    }

    private bool _focus;

    public void SetHeight(double height)
    {
        if (!_pending && frame.Visibility == Visibility.Collapsed) return;
        frame.Height = Math.Clamp(height, 1, 520);
        frame.Visibility = Visibility.Visible;
        if (_pending && _focus) view.Focus(FocusState.Programmatic);
        _pending = false;
    }

    public void Hide()
    {
        _pending = false;
        frame.Visibility = Visibility.Collapsed;
    }
}
