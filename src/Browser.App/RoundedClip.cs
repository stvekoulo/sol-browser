using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;

namespace Browser.App;

/// <summary>Découpe arrondie d'une vue flottante (menu, encart) : le cadre XAML et la vue web qu'il contient.</summary>
public static class RoundedClip
{
    /// <param name="view">Vue web à découper aussi, ou null pour ne découper que le cadre.</param>
    public static void Apply(Grid frame, WebView2? view, float radius)
    {
        if (frame.Visibility != Visibility.Visible) return;
        var w = (float)(frame.ActualWidth > 1 ? frame.ActualWidth : frame.Width);
        var h = (float)(frame.ActualHeight > 1 ? frame.ActualHeight : frame.Height);
        if (w <= 1 || h <= 1 || float.IsNaN(w) || float.IsNaN(h)) return;

        var visual = ElementCompositionPreview.GetElementVisual(frame);
        var geometry = visual.Compositor.CreateRoundedRectangleGeometry();
        geometry.Size = new Vector2(w, h);
        geometry.CornerRadius = new Vector2(radius, radius);
        var clip = visual.Compositor.CreateGeometricClip(geometry);
        visual.Clip = clip;
        if (view is null) return;
        ElementCompositionPreview.GetElementVisual(view).Clip = clip;
        if (ElementCompositionPreview.GetElementChildVisual(view) is { } child) child.Clip = clip;
    }
}
