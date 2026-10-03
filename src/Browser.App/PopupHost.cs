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
    private readonly UIElement _pages;
    private bool _pending;
    private bool _focus;
    private bool _animate, _anchorRight;
    private double _x, _y;

    // DIAGNOSTIC TEMPORAIRE : la vraie souris traversait le menu et atteignait la page dessous. Les ouvertures
    // successives essaient les remèdes dans l'ordre, et le journal note lequel est actif :
    //   0-1 : cadre jamais touché par la couche de composition, pages actives ;
    //   2-3 : idem, pages rendues insensibles à la souris pendant que le menu est ouvert ;
    //   4+  : pages insensibles, et le cadre (seul, pas la vue web) reçoit sa découpe arrondie et son animation.
    private int _opens;
    private bool _pagesOff, _dressed;

    public PopupHost(Grid frame, WebView2 view, UiBridge bridge, UIElement pages)
    {
        (_frame, _view, _bridge, _pages) = (frame, view, bridge, pages);
        _frame.SizeChanged += (_, _) => ApplyRoundedClip();
    }

    /// <summary>Un menu est affiché ou en cours d'affichage.</summary>
    public bool IsOpen => _pending || _frame.Visibility == Visibility.Visible;

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

        if (_pending && _focus) // les suggestions (sans curseur) ne comptent pas dans l'essai
        {
            var n = _opens++;
            (_pagesOff, _dressed) = (n >= 2, _dressed || n >= 4);
            DebugLog.Write($"menu ouvert essai {n} : pages {(_pagesOff ? "insensibles" : "actives")}, cadre {(_dressed ? "habillé" : "nu")}");
        }
        // Tant qu'un menu à curseur est ouvert, les pages ne reçoivent plus la souris : elle ne peut plus le traverser.
        _pages.IsHitTestVisible = !(_pagesOff && _focus);

        _frame.Visibility = Visibility.Visible;
        _frame.UpdateLayout();
        ApplyRoundedClip();

        if (_pending && _animate && _dressed) FloatIn.Play(_frame, _anchorRight ? (float)_frame.Width : 0, 0);
        if (_pending && _focus) _view.Focus(FocusState.Programmatic);
        _pending = false;
    }

    public void Hide()
    {
        _pending = false;
        _pages.IsHitTestVisible = true;
        _frame.Visibility = Visibility.Collapsed;
    }

    // Découpe arrondie, sur le cadre uniquement : la vue web elle-même n'est jamais confiée à la couche de composition.
    private void ApplyRoundedClip()
    {
        if (_dressed) RoundedClip.Apply(_frame, null, Radius);
    }
}
