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
    private double _width;
    private bool _bottom;
    private bool _selecting;
    public void Rescale()
    {
        if (!IsOpen) return;
        frame.Width = Math.Clamp(_width * (UiBridge.Settings?.UiScale ?? 1), 128, 900);
    }

    /// <summary>Un menu est affiché ou en cours d'affichage.</summary>
    public bool IsOpen => _pending || frame.Visibility == Visibility.Visible;

    /// <summary>Place la vue et lui envoie son contenu. Elle devient visible quand elle a annoncé sa hauteur.</summary>
    public void Show(double x, double y, double width, string contentJson, bool focus)
    {
        _bottom = false;
        _width = width;
        frame.Width = Math.Clamp(width * (UiBridge.Settings?.UiScale ?? 1), 128, 900);
        (_x, _y) = (x, y);
        _pending = true;
        _focus = focus;
        bridge.Post("""{"fresh":true}"""); // nouvelle ouverture : le contenu peut jouer son apparition
        bridge.Post(contentJson);
    }

    /// <summary>Bord gauche de la zone des pages : un menu centré l'est sur cette zone, pas sur la carte latérale.</summary>
    public Func<double>? PageLeft { get; set; }

    /// <summary>Fenêtre de saisie, au milieu de la zone des pages.</summary>
    /// <param name="top">Distance au haut de la fenêtre ; par défaut, vers le milieu.</param>
    public void ShowCentered(double width, string contentJson, double? top = null)
    {
        var window = (FrameworkElement)frame.Parent;
        var left = PageLeft?.Invoke() ?? 0;
        Show(left + Math.Max(0, (window.ActualWidth - left - width * (UiBridge.Settings?.UiScale ?? 1)) / 2), top ?? Math.Max(90, window.ActualHeight / 2 - 150), width, contentJson, focus: true);
    }

    public void Post(string json) => bridge.Post(json);

    public void ShowBottom(double width, string json)
    {
        Show(0, 0, width, json, focus: true);
        _bottom = true;
    }

    public void SetVisible(bool visible) => frame.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    public void SetSelection(bool selecting)
    {
        _selecting = selecting;
        if (!selecting) { _bottom = true; SetHeight(100); return; }
        var window = (FrameworkElement)frame.Parent;
        _bottom = false;
        frame.Width = window.ActualWidth; frame.Height = window.ActualHeight;
        frame.Margin = new Thickness(0); frame.Visibility = Visibility.Visible;
        CornerClip.Set(view, 0);
        view.Focus(FocusState.Programmatic);
    }

    public void SetHeight(double height)
    {
        if (_selecting) { SetSelection(true); return; }
        if (!_pending && frame.Visibility == Visibility.Collapsed) return;


        // La vue reste entière dans la fenêtre : elle se décale si elle dépasserait à droite ou en bas.
        var window = (FrameworkElement)frame.Parent;
        if (_bottom) frame.Width = Math.Max(128, Math.Min(_width * (UiBridge.Settings?.UiScale ?? 1), window.ActualWidth - 2 * Edge));
        // Jamais plus haut que la fenêtre : au-delà, le contenu du panneau défile.
        frame.Height = Math.Clamp(height, 1, Math.Max(160, window.ActualHeight - 2 * Edge - 48));
        var x = Math.Max(Edge, Math.Min(_x, window.ActualWidth - frame.Width - Edge));
        var y = Math.Max(Edge, Math.Min(_y, window.ActualHeight - frame.Height - Edge));
        if (_bottom) { x = (window.ActualWidth - frame.Width) / 2; y = window.ActualHeight - frame.Height - 18; }
        frame.Margin = new Thickness(x, y, 0, 0);

        frame.Visibility = Visibility.Visible;
        frame.UpdateLayout();
        CornerClip.Set(view, (_bottom ? 32 : Radius) * (float)(UiBridge.Settings?.UiScale ?? 1));
        if (_pending && _focus) view.Focus(FocusState.Programmatic);
        _pending = false;
    }

    public void Hide()
    {
        _pending = false;
        frame.Visibility = Visibility.Collapsed;
    }
}
