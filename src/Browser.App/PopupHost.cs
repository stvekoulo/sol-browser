using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;

namespace Browser.App;

// Petite vue flottante posée par-dessus la page (suggestions, bibliothèque, téléchargements, menus).
public sealed class PopupHost
{
    private const double Edge = 10;
    private const float Radius = 20f;
    private readonly Grid _frame;
    private readonly WebView2 _view;
    private readonly UiBridge _bridge;
    private bool _pending;
    private bool _focus;
    private double _x, _y;

    public PopupHost(Grid frame, WebView2 view, UiBridge bridge)
    {
        (_frame, _view, _bridge) = (frame, view, bridge);
        _frame.SizeChanged += (_, _) => ApplyRoundedClip();
        _frame.LayoutUpdated += (_, _) => ApplyRoundedClip();
    }

    public void Show(double x, double y, double width, string contentJson, bool focus)
    {
        _frame.Width = Math.Clamp(width, 160, 900);
        (_x, _y) = (x, y);
        _pending = true;
        _focus = focus;
        _bridge.Post(contentJson);
    }

    public void Post(string json) => _bridge.Post(json);

    public void SetHeight(double height)
    {
        if (!_pending && _frame.Visibility == Visibility.Collapsed) return;
        _frame.Height = Math.Clamp(height, 1, 520);

        var window = (FrameworkElement)_frame.Parent;
        var x = Math.Max(Edge, Math.Min(_x, window.ActualWidth - _frame.Width - Edge));
        var y = Math.Max(Edge, Math.Min(_y, window.ActualHeight - _frame.Height - Edge));
        _frame.Margin = new Thickness(x, y, 0, 0);

        _frame.Visibility = Visibility.Visible;
        _frame.UpdateLayout();
        ApplyRoundedClip();

        if (_pending && _focus) _view.Focus(FocusState.Programmatic);
        _pending = false;
    }

    public void Hide()
    {
        _pending = false;
        _frame.Visibility = Visibility.Collapsed;
    }

    // Applique la découpe arrondie après le calcul de mise en page XAML.
    private void ApplyRoundedClip() => RoundedClip.Apply(_frame, _view, Radius);
}
