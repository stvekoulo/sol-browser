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
    private bool _tracksBounds;
    private double _requestedWidth, _requestedHeight;

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
        (_requestedWidth, _requestedHeight) = (width, height);
        var parent = frame.Parent as FrameworkElement;
        if (!_tracksBounds && parent is not null)
        {
            _tracksBounds = true;
            parent.SizeChanged += (_, _) => { if (Visible) SetSize(_requestedWidth, _requestedHeight); };
        }
        var factor = UiBridge.Settings?.UiFactor ?? 0.92;
        var maxWidth = Math.Max(44, Math.Min(360, (parent?.ActualWidth ?? 400) - frame.Margin.Left - frame.Margin.Right));
        var maxHeight = Math.Max(44, Math.Min(420, (parent?.ActualHeight ?? 500) - frame.Margin.Top - frame.Margin.Bottom - 52 * factor));
        frame.Width = Math.Clamp(width, 44, maxWidth);
        frame.Height = Math.Clamp(height, 44, maxHeight);
        frame.Visibility = Visibility.Visible;
        frame.UpdateLayout();
        CornerClip.Set(view, 16 * (float)(UiBridge.Settings?.UiFactor ?? 0.92)); // beUI Card rounded-2xl.
    }

    public void Hide() => frame.Visibility = Visibility.Collapsed;
}
