using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI.ViewManagement;

namespace Browser.App;

public sealed partial class PageHost
{
    private const double PreviewActionSize = 32, PreviewActionSpacing = 6, PreviewRailGap = 10;
    private static Button PreviewRailButton(string label, string geometry, Action run, string kind,
        Brush ink, Brush background, Brush hover, Brush border)
    {
        // Lucide-style open paths need a stroke. PathIcon fills them and loses the close/arrow lines.
        var path = (Microsoft.UI.Xaml.Shapes.Path)XamlReader.Load(
            $"<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Data='{geometry}' StrokeThickness='1.7' StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round' />");
        path.Stroke = ink;
        var canvas = new Canvas { Width = 20, Height = 20 };
        canvas.Children.Add(path);
        var icon = new Viewbox { Width = 18, Height = 18, Child = canvas };
        var turn = new CompositeTransform { CenterX = 9, CenterY = 9 };
        icon.RenderTransform = turn;
        var press = new ScaleTransform { CenterX = PreviewActionSize / 2, CenterY = PreviewActionSize / 2 };
        var button = new Button
        {
            Content = icon, Width = PreviewActionSize, Height = PreviewActionSize,
            MinWidth = PreviewActionSize, MinHeight = PreviewActionSize, Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(10), Background = background,
            BorderBrush = border, BorderThickness = new Thickness(1), RenderTransform = press
        };
        button.Resources["ButtonBackgroundPointerOver"] = hover;
        button.Resources["ButtonBackgroundPressed"] = hover;
        button.Resources["ButtonBorderBrushPointerOver"] = border;
        button.Resources["ButtonBorderBrushPressed"] = border;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label);
        ToolTipService.SetToolTip(button, label);
        var preferences = new UISettings();
        var running = new Dictionary<string, Storyboard>();
        void Animate(DependencyObject target, string property, double value)
        {
            var key = (ReferenceEquals(target, turn) ? "icon." : "button.") + property;
            if (running.Remove(key, out var previous)) previous.Stop();
            var animation = new DoubleAnimation
            {
                To = value, Duration = TimeSpan.FromMilliseconds(preferences.AnimationsEnabled ? 180 : 0),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(animation, target); Storyboard.SetTargetProperty(animation, property);
            var story = new Storyboard(); story.Children.Add(animation); running[key] = story; story.Begin();
        }
        void Highlight(bool active)
        {
            var moving = active && preferences.AnimationsEnabled;
            Animate(turn, "Rotation", kind == "close" && moving ? 90 : 0);
            Animate(turn, "TranslateX", kind == "tab" && moving ? 1.5 : 0);
            Animate(turn, "TranslateY", kind == "tab" && moving ? -1.5 : 0);
            Animate(turn, "ScaleX", moving ? kind == "split" ? 1.14 : 1.08 : 1);
            Animate(turn, "ScaleY", moving ? 1.08 : 1);
        }
        void ResetPress() { Animate(press, "ScaleX", 1); Animate(press, "ScaleY", 1); }
        button.Click += (_, _) => run();
        button.PointerEntered += (_, _) => Highlight(true);
        button.PointerExited += (_, _) => { Highlight(false); ResetPress(); };
        button.GotFocus += (_, _) => Highlight(true);
        button.LostFocus += (_, _) => { Highlight(false); ResetPress(); };
        button.PointerPressed += (_, _) =>
        {
            Animate(press, "ScaleX", preferences.AnimationsEnabled ? .94 : 1);
            Animate(press, "ScaleY", preferences.AnimationsEnabled ? .94 : 1);
        };
        button.PointerReleased += (_, _) => ResetPress();
        button.PointerCaptureLost += (_, _) => ResetPress();
        button.Unloaded += (_, _) => { foreach (var story in running.Values) story.Stop(); running.Clear(); };
        return button;
    }
}
