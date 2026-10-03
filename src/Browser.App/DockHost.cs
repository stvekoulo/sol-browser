using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Browser.App;

/// <summary>
/// Encart flottant en bas à droite de la fenêtre : téléchargements en cours. Sa taille suit son contenu.
/// Coins arrondis par CornerClip, comme les menus.
/// </summary>
public sealed class DockHost(Grid frame, WebView2 view, UiBridge bridge, Func<Task> init)
{
    // La vue de l'encart n'est créée qu'au premier téléchargement : sans téléchargement, elle ne coûte rien.
    private bool _started;
    private string? _last;

    public bool Visible => frame.Visibility == Visibility.Visible;

    /// <summary>Envoie le contenu ; l'encart apparaît quand il a annoncé sa taille.</summary>
    public void Post(string json)
    {
        _last = json;
        if (_started) { bridge.Post(json); return; }
        _started = true;
        _ = init(); // l'encart demandera son contenu une fois prêt (voir Ready)
    }

    /// <summary>L'encart vient de se charger : il reçoit le dernier contenu envoyé.</summary>
    public void Ready()
    {
        if (_last is not null) bridge.Post(_last);
    }

    public void SetSize(double width, double height)
    {
        frame.Width = Math.Clamp(width, 44, 360);
        frame.Height = Math.Clamp(height, 44, 420);
        frame.Visibility = Visibility.Visible;
        frame.UpdateLayout();
        CornerClip.Set(view, 20);
    }

    public void Hide() => frame.Visibility = Visibility.Collapsed;
}
