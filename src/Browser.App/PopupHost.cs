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
    private double _height = 1;
    private bool _bottom;
    private bool _above; // posée au-dessus de son point d'ancrage (bouton en bas de la fenêtre), et non dessous
    private bool _center;
    private bool _selecting;
    public void Rescale()
    {
        if (!IsOpen) return;
        SetHeight(_height);
    }

    /// <summary>Un menu est affiché ou en cours d'affichage.</summary>
    public bool IsOpen => _pending || frame.Visibility == Visibility.Visible;

    /// <summary>Place la vue et lui envoie son contenu. Elle devient visible quand elle a annoncé sa hauteur.</summary>
    /// <param name="above">Le point donné est le bas de la vue : elle s'ouvre vers le haut, sans recouvrir le bouton d'où elle vient.</param>
    public void Show(double x, double y, double width, string contentJson, bool focus, bool above = false)
    {
        _bottom = false;
        _above = above;
        _center = false;
        _width = width;
        frame.Width = Math.Min(Math.Clamp(width * (UiBridge.Settings?.UiFactor ?? 0.92), 128, 900), Math.Max(1, ((FrameworkElement)frame.Parent).ActualWidth - 2 * Edge));
        (_x, _y) = (x, y);
        _pending = true;
        _focus = focus;
        bridge.Post("""{"fresh":true}"""); // nouvelle ouverture : le contenu peut jouer son apparition
        bridge.Post(contentJson);
    }

    /// <summary>Bord gauche de la zone des pages : un menu centré l'est sur cette zone, pas sur la carte latérale.</summary>
    public Func<double>? PageLeft { get; set; }
    public Func<(double Top, double Height)>? PageVertical { get; set; }

    /// <summary>Fenêtre de saisie, au milieu de la zone des pages.</summary>
    /// <param name="top">Distance au haut de la fenêtre ; par défaut, vers le milieu.</param>
    public void ShowCentered(double width, string contentJson, double? top = null)
    {
        var window = (FrameworkElement)frame.Parent;
        var left = PageLeft?.Invoke() ?? 0;
        Show(left + Math.Max(0, (window.ActualWidth - left - width * (UiBridge.Settings?.UiFactor ?? 0.92)) / 2), top ?? Math.Max(90, window.ActualHeight / 2 - 150), width, contentJson, focus: true);
        _center = top is null;
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
        _height = height;
        if (_selecting) { SetSelection(true); return; }
        if (!_pending && frame.Visibility == Visibility.Collapsed) return;


        // La vue reste entière dans la fenêtre : elle se décale si elle dépasserait à droite ou en bas.
        var window = (FrameworkElement)frame.Parent;
        frame.Width = Math.Min(Math.Clamp(_width * (UiBridge.Settings?.UiFactor ?? 0.92), 128, 900), Math.Max(1, window.ActualWidth - 2 * Edge));
        // Jamais plus haut que la fenêtre : au-delà, le contenu du panneau défile.
        frame.Height = Math.Clamp(height, 1, Math.Max(1, window.ActualHeight - 2 * Edge - 48));
        var x = Math.Max(Edge, Math.Min(_x, window.ActualWidth - frame.Width - Edge));
        var y = Math.Max(Edge, Math.Min(_above ? _y - frame.Height : _y, window.ActualHeight - frame.Height - Edge));
        if (_center)
        {
            var left = PageLeft?.Invoke() ?? 0;
            var vertical = PageVertical?.Invoke() ?? (0d, window.ActualHeight);
            frame.Height = Math.Min(frame.Height, Math.Max(1, vertical.Item2 - 2 * Edge));
            x = Math.Max(Edge, left + (window.ActualWidth - left - frame.Width) / 2);
            y = Math.Clamp(vertical.Item1 + (vertical.Item2 - frame.Height) / 2, Edge, Math.Max(Edge, window.ActualHeight - frame.Height - Edge));
        }
        if (_bottom) { x = (window.ActualWidth - frame.Width) / 2; y = window.ActualHeight - frame.Height - 18; }
        frame.Margin = new Thickness(x, y, 0, 0);

        frame.Visibility = Visibility.Visible;
        frame.UpdateLayout();
        CornerClip.Set(view, (_bottom ? 32 : Radius) * (float)(UiBridge.Settings?.UiFactor ?? 0.92));
        if (_pending && _focus) view.Focus(FocusState.Programmatic);
        _pending = false;
    }

    public void Hide()
    {
        _pending = false;
        frame.Visibility = Visibility.Collapsed;
    }
}
