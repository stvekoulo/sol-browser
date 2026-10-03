using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Browser.App;

/// <summary>
/// Petite vue flottante posée par-dessus la page (suggestions, bibliothèque, téléchargements, menus).
/// Ses coins arrondis viennent de CornerClip (voir ce fichier pour ce qu'il ne faut surtout pas faire) ;
/// son mouvement d'apparition est joué par son contenu web.
/// </summary>
public sealed class PopupHost(Grid frame, WebView2 view, UiBridge bridge)
{
    private const double Edge = 10;
    private const float Radius = 20;
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

    /// <summary>Bord gauche de la zone des pages : un menu centré l'est sur cette zone, pas sur la carte latérale.</summary>
    public Func<double>? PageLeft { get; set; }

    /// <summary>Fenêtre de saisie, au milieu de la zone des pages.</summary>
    public void ShowCentered(double width, string contentJson)
    {
        var window = (FrameworkElement)frame.Parent;
        var left = PageLeft?.Invoke() ?? 0;
        Show(left + Math.Max(0, (window.ActualWidth - left - width) / 2), Math.Max(90, window.ActualHeight / 2 - 150), width, contentJson, focus: true);
    }

    public void Post(string json) => bridge.Post(json);

    public void SetHeight(double height)
    {
        if (!_pending && frame.Visibility == Visibility.Collapsed) return;


        // La vue reste entière dans la fenêtre : elle se décale si elle dépasserait à droite ou en bas.
        var window = (FrameworkElement)frame.Parent;
        // Jamais plus haut que la fenêtre : au-delà, le contenu du panneau défile.
        frame.Height = Math.Clamp(height, 1, Math.Max(160, window.ActualHeight - 2 * Edge - 48));
        var x = Math.Max(Edge, Math.Min(_x, window.ActualWidth - frame.Width - Edge));
        var y = Math.Max(Edge, Math.Min(_y, window.ActualHeight - frame.Height - Edge));
        frame.Margin = new Thickness(x, y, 0, 0);

        frame.Visibility = Visibility.Visible;
        frame.UpdateLayout();
        CornerClip.Set(view, Radius);
        if (_pending && _focus) view.Focus(FocusState.Programmatic);
        _pending = false;
    }

    public void Hide()
    {
        _pending = false;
        frame.Visibility = Visibility.Collapsed;
    }
}
