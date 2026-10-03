using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Browser.App;

/// <summary>Encart flottant en bas à droite de la fenêtre : téléchargements en cours. Sa taille suit son contenu.</summary>
public sealed class DockHost
{
    private const float Radius = 20f;
    private readonly Grid _frame;
    private readonly WebView2 _view;
    private readonly UiBridge _bridge;

    public DockHost(Grid frame, WebView2 view, UiBridge bridge)
    {
        (_frame, _view, _bridge) = (frame, view, bridge);
        _frame.SizeChanged += (_, _) => RoundedClip.Apply(_frame, _view, Radius);
    }

    public bool Visible => _frame.Visibility == Visibility.Visible;

    /// <summary>Envoie le contenu ; l'encart apparaît quand il a annoncé sa taille.</summary>
    public void Post(string json) => _bridge.Post(json);

    public void SetSize(double width, double height)
    {
        _frame.Width = Math.Clamp(width, 44, 360);
        _frame.Height = Math.Clamp(height, 44, 420);
        _frame.Visibility = Visibility.Visible;
        _frame.UpdateLayout();
        RoundedClip.Apply(_frame, _view, Radius);
    }

    public void Hide() => _frame.Visibility = Visibility.Collapsed;
}
