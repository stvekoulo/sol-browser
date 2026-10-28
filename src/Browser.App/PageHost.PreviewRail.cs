using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI.ViewManagement;
using StrokePath = Microsoft.UI.Xaml.Shapes.Path;

namespace Browser.App;

public sealed partial class PageHost
{
    private const double PreviewActionSize = 36, PreviewActionSpacing = 8, PreviewRailGap = 12, PreviewHeaderHeight = 48;
    private static readonly UISettings PreviewMotion = new();

    // Les icônes animées de l'interface (lucide-animated : grille 24, trait 2), rejouées en XAML :
    // la carte d'aperçu est native, au-dessus de la page, et n'héberge pas les composants React.
    private static (Viewbox Icon, Action<bool> Animate) PreviewIcon(string kind, Brush ink, double size)
    {
        var canvas = new Canvas { Width = 24, Height = 24 };
        var story = new Storyboard();
        StrokePath Stroke(string data, double dash = 0)
        {
            var path = (StrokePath)XamlReader.Load($"<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Data='{data}' StrokeThickness='2' StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round' />");
            path.Stroke = ink; path.RenderTransform = new CompositeTransform();
            // Tirets en unités d'épaisseur : un tiret puis un vide de la longueur du trait, pour le « dessiner ».
            if (dash > 0) path.StrokeDashArray = new DoubleCollection { dash, dash };
            canvas.Children.Add(path); return path;
        }
        static string Box(int x, int y, int w, int h, int r) =>
            $"M{x + r} {y} H{x + w - r} A{r} {r} 0 0 1 {x + w} {y + r} V{y + h - r} A{r} {r} 0 0 1 {x + w - r} {y + h} H{x + r} A{r} {r} 0 0 1 {x} {y + h - r} V{y + r} A{r} {r} 0 0 1 {x + r} {y} Z";
        void Keys(DependencyObject target, string property, params (double Ms, double Value)[] keys)
        {
            var animation = new DoubleAnimationUsingKeyFrames { EnableDependentAnimation = true };
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero), Value = keys[0].Value });
            foreach (var (ms, value) in keys.Skip(1))
                animation.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms)), Value = value, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            Storyboard.SetTarget(animation, target); Storyboard.SetTargetProperty(animation, property);
            story.Children.Add(animation);
        }
        void Draw(StrokePath path, double dash, double delay, double duration)
        {
            Keys(path, "StrokeDashOffset", (0, dash), (delay, dash), (delay + duration, 0));
            Keys(path, "Opacity", (0, 0), (delay, 0), (delay + duration, 1));
        }
        switch (kind)
        {
            case "close": // XIcon : chaque branche se dessine, la seconde après 0,2 s.
                Draw(Stroke("M18 6 L6 18", 8.6), 8.6, 0, 300);
                Draw(Stroke("M6 6 L18 18", 8.6), 8.6, 200, 300);
                break;
            case "tab": // LayoutPanelTopIcon : le bandeau descend, les deux blocs glissent depuis les côtés.
                var top = Stroke(Box(3, 3, 18, 7, 1)); var left = Stroke(Box(3, 14, 7, 7, 1)); var right = Stroke(Box(14, 14, 7, 7, 1));
                Keys(top.RenderTransform, "TranslateY", (0, -5), (500, 0)); Keys(top, "Opacity", (0, 0), (100, 0), (500, 1));
                Keys(left.RenderTransform, "TranslateX", (0, -10), (300, -10), (800, 0)); Keys(left, "Opacity", (0, 0), (350, 0), (700, 1));
                Keys(right.RenderTransform, "TranslateX", (0, 10), (400, 10), (900, 0)); Keys(right, "Opacity", (0, 0), (400, 0), (800, 1));
                break;
            case "split": // Même famille : le cadre reste, la séparation se dessine.
                Stroke(Box(3, 3, 18, 18, 2));
                Draw(Stroke("M12 3 V21", 9.1), 9.1, 0, 350);
                break;
            case "back" or "forward": // ArrowLeft/RightIcon : la pointe recule de 3 px et la tige se raccourcit (14 → 9).
                var back = kind == "back";
                var shaft = Stroke(back ? "M19 12 H5" : "M5 12 H19", 7.1);
                var head = Stroke(back ? "M12 19 L5 12 L12 5" : "M12 5 L19 12 L12 19");
                Keys(shaft, "StrokeDashOffset", (0, 0), (200, 2.6), (400, 0));
                Keys(head.RenderTransform, "TranslateX", (0, 0), (200, back ? 3 : -3), (400, 0));
                break;
            case "reload": // RefreshCWIcon : rotation de 50° tant que le pointeur reste dessus.
                foreach (var data in new[] { "M3 12 A9 9 0 0 1 12 3 A9.75 9.75 0 0 1 18.74 5.74 L21 8", "M21 3 V8 H16", "M21 12 A9 9 0 0 1 12 21 A9.75 9.75 0 0 1 5.26 18.26 L3 16", "M8 16 H3 V21" })
                    Stroke(data);
                var spin = new CompositeTransform { CenterX = 12, CenterY = 12 };
                canvas.RenderTransform = spin;
                Storyboard? turning = null;
                return (new Viewbox { Width = size, Height = size, Child = canvas }, play =>
                {
                    var animation = new DoubleAnimation
                    {
                        To = play ? 50 : 0, Duration = TimeSpan.FromMilliseconds(PreviewMotion.AnimationsEnabled ? 450 : 0),
                        EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = .3 }
                    };
                    Storyboard.SetTarget(animation, spin); Storyboard.SetTargetProperty(animation, "Rotation");
                    var next = new Storyboard(); next.Children.Add(animation); next.Begin();
                    turning?.Stop(); turning = next;
                });
        }
        return (new Viewbox { Width = size, Height = size, Child = canvas }, play =>
        {
            story.Stop();
            if (play && PreviewMotion.AnimationsEnabled) story.Begin();
        });
    }

    private static Button PreviewButton(string label, string kind, Action run, Brush ink, Brush background, Brush hover, Brush border,
        double size = PreviewActionSize, double iconSize = 18, double radius = 12)
    {
        var (icon, animate) = PreviewIcon(kind, ink, iconSize);
        var press = new ScaleTransform { CenterX = size / 2, CenterY = size / 2 };
        var button = new Button
        {
            Content = icon, Width = size, Height = size, MinWidth = size, MinHeight = size, Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(radius), Background = background,
            BorderBrush = border, BorderThickness = new Thickness(1), RenderTransform = press
        };
        button.Resources["ButtonBackgroundPointerOver"] = hover;
        button.Resources["ButtonBackgroundPressed"] = hover;
        button.Resources["ButtonBackgroundDisabled"] = background;
        button.Resources["ButtonBorderBrushPointerOver"] = border;
        button.Resources["ButtonBorderBrushPressed"] = border;
        button.Resources["ButtonBorderBrushDisabled"] = border;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label);
        ToolTipService.SetToolTip(button, label);
        void Press(double scale)
        {
            var moving = PreviewMotion.AnimationsEnabled;
            foreach (var property in new[] { "ScaleX", "ScaleY" })
            {
                var animation = new DoubleAnimation { To = moving ? scale : 1, Duration = TimeSpan.FromMilliseconds(moving ? 140 : 0), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                Storyboard.SetTarget(animation, press); Storyboard.SetTargetProperty(animation, property);
                var story = new Storyboard(); story.Children.Add(animation); story.Begin();
            }
        }
        button.Click += (_, _) => run();
        button.PointerEntered += (_, _) => animate(true);
        button.PointerExited += (_, _) => { animate(false); Press(1); };
        button.GotFocus += (_, _) => animate(true);
        button.LostFocus += (_, _) => { animate(false); Press(1); };
        button.PointerPressed += (_, _) => Press(.94);
        button.PointerReleased += (_, _) => Press(1);
        button.PointerCaptureLost += (_, _) => Press(1);
        button.IsEnabledChanged += (_, _) => button.Opacity = button.IsEnabled ? 1 : .35;
        return button;
    }
}
