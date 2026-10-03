using System.Numerics;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;

namespace Browser.App;

/// <summary>
/// Vrais coins arrondis pour une vue web flottante (menu, encart, carte latérale au survol).
///
/// La découpe est posée sur l'image interne de la vue web (son « child visual »), et sur rien d'autre.
/// NE PAS utiliser ElementCompositionPreview.GetElementVisual sur la vue ou sur son cadre : cela les confie à
/// la couche de composition, et la vraie souris traverse alors la vue (constaté au journal le 2026-10-03).
/// </summary>
public static class CornerClip
{
    /// <param name="radius">Rayon des coins ; 0 retire la découpe.</param>
    /// <param name="squareTop">Coins du haut laissés droits : seuls ceux du bas sont arrondis.</param>
    public static void Set(WebView2 view, float radius, bool squareTop = false)
    {
        if (ElementCompositionPreview.GetElementChildVisual(view) is not { } visual) return;
        if (radius <= 0 || view.ActualWidth < 2 || view.ActualHeight < 2)
        {
            visual.Clip = null;
            return;
        }
        var shape = visual.Compositor.CreateRoundedRectangleGeometry();
        // Coins du haut droits : la forme déborde vers le haut, ses coins arrondis du haut tombent hors de la vue.
        shape.Offset = new Vector2(0, squareTop ? -radius : 0);
        shape.Size = new Vector2((float)view.ActualWidth, (float)view.ActualHeight + (squareTop ? radius : 0));
        shape.CornerRadius = new Vector2(radius, radius);
        visual.Clip = visual.Compositor.CreateGeometricClip(shape);
    }
}
