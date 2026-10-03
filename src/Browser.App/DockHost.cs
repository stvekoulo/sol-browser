using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Browser.App;

/// <summary>
/// Encart flottant en bas à droite de la fenêtre : téléchargements en cours. Sa taille suit son contenu.
/// Coins arrondis par CornerClip, comme les menus.
/// </summary>
public sealed class DockHost(Grid frame, WebView2 view, UiBridge bridge)
{
    public bool Visible => frame.Visibility == Visibility.Visible;

    /// <summary>Envoie le contenu ; l'encart apparaît quand il a annoncé sa taille.</summary>
    public void Post(string json) => bridge.Post(json);

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
