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
    public static void Set(WebView2 view, float radius)
    {
        if (ElementCompositionPreview.GetElementChildVisual(view) is not { } visual) return;
        if (radius <= 0 || view.ActualWidth < 2 || view.ActualHeight < 2)
        {
            visual.Clip = null;
            return;
        }
        var shape = visual.Compositor.CreateRoundedRectangleGeometry();
        shape.Size = new Vector2((float)view.ActualWidth, (float)view.ActualHeight);
        shape.CornerRadius = new Vector2(radius, radius);
        visual.Clip = visual.Compositor.CreateGeometricClip(shape);
    }
}
