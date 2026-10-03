using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Browser.App;

/// <summary>
/// Petite vue d'interface posée par-dessus la page, à la taille de son contenu : suggestions, bibliothèque, menu contextuel.
/// Opaque par construction : la transparence d'une vue web ne fonctionne pas en WinUI 3 (voir les résultats du prototype 1).
/// </summary>
public sealed class PopupHost(Grid frame, WebView2 view, UiBridge bridge)
{
    private const double Edge = 8; // marge gardée avec les bords de la fenêtre
    private bool _pending;
    private bool _focus;
    private double _x, _y;

    /// <summary>Place la vue et lui envoie son contenu. Elle devient visible quand elle a annoncé sa hauteur.</summary>
    public void Show(double x, double y, double width, string contentJson, bool focus)
    {
        frame.Width = Math.Clamp(width, 120, 900);
        (_x, _y) = (x, y);
        _pending = true;
        _focus = focus;
        bridge.Post(contentJson);
    }

    public void Post(string json) => bridge.Post(json);

    public void SetHeight(double height)
    {
        if (!_pending && frame.Visibility == Visibility.Collapsed) return;
        frame.Height = Math.Clamp(height, 1, 520);

        // La vue reste entière dans la fenêtre : elle se décale si elle dépasserait à droite ou en bas.
        var window = (FrameworkElement)frame.Parent;
        var x = Math.Max(Edge, Math.Min(_x, window.ActualWidth - frame.Width - Edge));
        var y = Math.Max(Edge, Math.Min(_y, window.ActualHeight - frame.Height - Edge));
        frame.Margin = new Thickness(x, y, 0, 0);

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
