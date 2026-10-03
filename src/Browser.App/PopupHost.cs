using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Browser.App;

/// <summary>
/// Petite vue flottante posée par-dessus la page (suggestions, bibliothèque, téléchargements, menus).
///
/// NE JAMAIS appeler ElementCompositionPreview (découpe, animation…) sur ce cadre ni sur sa vue web : une fois
/// le cadre confié à la couche de composition, la vraie souris le traverse et atteint la page dessous, alors
/// que les tests de position de XAML répondent toujours « le menu ». Constaté au journal le 2026-10-03.
/// L'arrondi vient du CornerRadius du cadre, le mouvement du contenu web lui-même.
/// </summary>
public sealed class PopupHost(Grid frame, WebView2 view, UiBridge bridge)
{
    private const double Edge = 10;
    private bool _pending;
    private bool _focus;
    private double _x, _y;

    /// <summary>Un menu est affiché ou en cours d'affichage.</summary>
    public bool IsOpen => _pending || frame.Visibility == Visibility.Visible;

    /// <summary>Place la vue et lui envoie son contenu. Elle devient visible quand elle a annoncé sa hauteur.</summary>
    public void Show(double x, double y, double width, string contentJson, bool focus)
    {
        frame.Width = Math.Clamp(width, 160, 900);
        (_x, _y) = (x, y);
        _pending = true;
        _focus = focus;
        bridge.Post("""{"fresh":true}"""); // nouvelle ouverture : le contenu peut jouer son apparition
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
