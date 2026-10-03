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

    private bool _animate, _anchorRight;

    /// <param name="animate">Apparition animée. Jamais pour les suggestions, qui changent à chaque touche.</param>
    /// <param name="anchorRight">La vue est accrochée par son coin haut droit (panneau d'un bouton de la barre).</param>
    public void Show(double x, double y, double width, string contentJson, bool focus, bool animate = false, bool anchorRight = false)
    {
        _frame.Width = Math.Clamp(width, 160, 900);
        (_x, _y) = (x, y);
        (_animate, _anchorRight) = (animate, anchorRight);
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

        // DIAGNOSTIC TEMPORAIRE : la vraie souris traverse le menu et atteint la page dessous, alors que le test
        // de position de XAML donne bien le menu. Chaque ouverture essaie une combinaison différente des trois
        // habillages du cadre, pour trouver lequel détourne les clics. Le journal note la combinaison active.
        if (_pending)
        {
            _variant = _opens++ % 8;
            var plain = (_variant & 1) != 0;
            _shadow ??= _frame.Shadow;
            _frame.Shadow = plain ? null : _shadow;
            _frame.Translation = plain ? Vector3.Zero : new Vector3(0, 0, 32);
            DebugLog.Write($"menu ouvert variante {_variant} : ombre={(plain ? "non" : "oui")} decoupe={((_variant & 2) != 0 ? "non" : "oui")} animation={((_variant & 4) != 0 ? "non" : "oui")}");
        }

        _frame.Visibility = Visibility.Visible;
        _frame.UpdateLayout();
        ApplyRoundedClip();

        if (_pending && _animate && (_variant & 4) == 0) FloatIn.Play(_frame, _anchorRight ? (float)_frame.Width : 0, 0);
        if (_pending && _focus) _view.Focus(FocusState.Programmatic);
        _pending = false;
    }

    public void Hide()
    {
        _pending = false;
        _frame.Visibility = Visibility.Collapsed;
    }

    private int _opens, _variant;
    private Microsoft.UI.Xaml.Media.Shadow? _shadow;

    // Applique la découpe arrondie après le calcul de mise en page XAML.
    private void ApplyRoundedClip()
    {
        if ((_variant & 2) == 0) { RoundedClip.Apply(_frame, _view, Radius); return; }
        ElementCompositionPreview.GetElementVisual(_frame).Clip = null;
        ElementCompositionPreview.GetElementVisual(_view).Clip = null;
        if (ElementCompositionPreview.GetElementChildVisual(_view) is { } child) child.Clip = null;
    }
}
