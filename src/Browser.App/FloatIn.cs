using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace Browser.App;

/// <summary>
/// Apparition d'une vue flottante (menu, panneau, encart) : fondu et légère mise à l'échelle depuis son point
/// d'ancrage. Courbe et durée : celles de theme.css (--ease-out, --fast).
/// </summary>
public static class FloatIn
{
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(160);
    private static readonly Windows.UI.ViewManagement.UISettings System = new();

    /// <param name="originX">Point d'ancrage dans le cadre : la vue grandit depuis ce point.</param>
    public static void Play(FrameworkElement frame, float originX, float originY)
    {
        var visual = ElementCompositionPreview.GetElementVisual(frame);
        var compositor = visual.Compositor;
        var ease = compositor.CreateCubicBezierEasingFunction(new Vector2(0.22f, 1f), new Vector2(0.36f, 1f));

        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 0);
        fade.InsertKeyFrame(1, 1, ease);
        fade.Duration = Duration;
        visual.StartAnimation("Opacity", fade);

        // Animations réduites dans Windows : on garde le fondu, on retire le mouvement.
        if (!System.AnimationsEnabled) return;
        visual.CenterPoint = new Vector3(originX, originY, 0);
        var grow = compositor.CreateVector3KeyFrameAnimation();
        grow.InsertKeyFrame(0, new Vector3(0.96f, 0.96f, 1));
        grow.InsertKeyFrame(1, Vector3.One, ease);
        grow.Duration = Duration;
        visual.StartAnimation("Scale", grow);
    }
}
