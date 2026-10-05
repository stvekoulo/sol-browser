using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Browser.App;

/// <summary>
/// Carte latérale : épinglée dans sa colonne, fermée, ou apparue par-dessus la page au survol du bord gauche.
/// Entrée et sortie sont animées par le contenu web de la carte ; pendant ce temps elle flotte au-dessus de la
/// page, qui n'est redimensionnée qu'une seule fois. Aucun appel à la couche de composition (voir PopupHost).
/// </summary>
public sealed class SidebarHost(WebView2 sidebar, ColumnDefinition column, Action<string> post)
{
    public const double MinWidth = 220, MaxWidth = 520, DefaultWidth = 296;

    /// <summary>Largeur de la carte ouverte, réglable par l'utilisateur.</summary>
    public double Width { get; private set; } = DefaultWidth;

    /// <summary>Change la largeur de la carte ; la colonne suit tout de suite si la carte y est épinglée.</summary>
    public void Resize(double width)
    {
        var scale = UiBridge.Settings?.UiFactor ?? 0.92;
        Width = Math.Clamp(Math.Round(width), MinWidth * scale, MaxWidth * scale);
        if (_pinned && column.Width.Value > 0) column.Width = new GridLength(Width);
    }
    private const float CardRadius = 22; // rayon de la carte (--radius-card)
    private const double CardMargin = 10; // marge de la carte dans sa colonne (sidebar.css)
    private const double EdgeZone = 16; // largeur de la bande, au bord gauche, qui fait apparaître la carte fermée
    private static readonly TimeSpan In = TimeSpan.FromMilliseconds(220), Out = TimeSpan.FromMilliseconds(180); // durées de motion.css

    private bool _pinned = true; // au démarrage, la carte est ouverte dans sa colonne
    private bool _peeking;
    private int _step; // invalide une fin d'animation dépassée par une nouvelle action

    /// <summary>Bouton de la barre : épingle ou ferme. Renvoie le nouvel état.</summary>
    public bool Toggle()
    {
        _pinned = !_pinned;
        if (!_pinned) Leave();
        else if (_peeking) { _step++; Dock(); column.Width = new GridLength(Width); } // déjà visible : on l'épingle sur place
        else Enter(dockAfter: true);
        _peeking = false;
        return _pinned;
    }

    /// <summary>Position du curseur dans la fenêtre : approcher le bord gauche fait apparaître la carte fermée.</summary>
    public void PointerAt(double x, double y, bool menuOpen)
    {
        if (_pinned) return;
        if (!_peeking && x <= EdgeZone && y > 52 * (UiBridge.Settings?.UiFactor ?? 0.92)) { _peeking = true; Enter(dockAfter: false); }
        else if (_peeking && (x > Width + 40 || y < 40) && !menuOpen) { _peeking = false; Leave(); }
    }

    private async void Enter(bool dockAfter)
    {
        var step = ++_step;
        Float();
        sidebar.Visibility = Visibility.Visible;
        post("""{"event":"sidebar-enter"}""");
        if (!dockAfter) return;
        await Task.Delay(In);
        if (step != _step) return;
        Dock();
        column.Width = new GridLength(Width); // la page se réduit une fois, la carte déjà en place
    }

    private async void Leave()
    {
        var step = ++_step;
        Float();
        column.Width = new GridLength(0); // la page s'étend tout de suite sous la carte qui s'en va
        post("""{"event":"sidebar-leave"}""");
        await Task.Delay(Out);
        if (step != _step) return;
        sidebar.Visibility = Visibility.Collapsed;
        Dock();
    }

    private void Float()
    {
        Grid.SetColumnSpan(sidebar, 2);
        Canvas.SetZIndex(sidebar, 1);
        sidebar.HorizontalAlignment = HorizontalAlignment.Left;
        // La vue se réduit à la carte elle-même : autour d'elle, c'est la vraie page qu'on voit.
        var scale = UiBridge.Settings?.UiFactor ?? 0.92;
        sidebar.Margin = new Thickness(CardMargin * scale, 2 * scale, 0, CardMargin * scale);
        sidebar.Width = Width - 2 * CardMargin * scale;
        sidebar.UpdateLayout();
        CornerClip.Set(sidebar, CardRadius * (float)scale); // flottante : ses coins laissent voir la page
    }

    private void Dock()
    {
        Grid.SetColumnSpan(sidebar, 1);
        Canvas.SetZIndex(sidebar, 0);
        sidebar.HorizontalAlignment = HorizontalAlignment.Stretch;
        sidebar.Margin = new Thickness(0);
        sidebar.Width = double.NaN;
        CornerClip.Set(sidebar, 0); // dans sa colonne : vue entière
        post("""{"event":"sidebar-docked"}""");
    }
}
