using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Browser.App;

public static partial class DeviceFrame
{
    // Barre d'état : l'heure à gauche, le réseau et la batterie à droite, dans la couleur lisible sur le fond de la page.
    private static Grid StatusBar(Device d, double s, double inset, SolidColorBrush ink)
    {
        var big = d.Kind is "island" or "fold-closed";
        // À côté de l'îlot ou du poinçon sur un téléphone, au bord sur les autres.
        var edge = (d.Kind switch { "island" => (d.Width - d.CutWidth) / 4 - 18, "fold-closed" => 52, "home" => 8, _ => 22 }) * s;
        var bar = new Grid { Height = inset * s, VerticalAlignment = VerticalAlignment.Top };
        bar.Children.Add(new TextBlock
        {
            Text = "9:41", FontSize = Math.Max(6, (big ? 16 : 12.5) * s), FontWeight = FontWeights.SemiBold, Foreground = ink,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(Math.Max(4, edge), 0, 0, 0),
        });
        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 5 * s, HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, Math.Max(4, edge), 0),
        };
        var unit = (big ? 1 : 0.8) * s;
        var bars = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1.5 * unit, VerticalAlignment = VerticalAlignment.Center };
        foreach (var height in new[] { 4, 6, 8.5, 11 })
            bars.Children.Add(new Border { Width = 3 * unit, Height = height * unit, CornerRadius = new CornerRadius(unit), Background = ink, VerticalAlignment = VerticalAlignment.Bottom });
        right.Children.Add(bars);
        right.Children.Add(new Border { Width = 25 * unit, Height = 12 * unit, CornerRadius = new CornerRadius(3.5 * unit), Background = ink, Opacity = 0.9, VerticalAlignment = VerticalAlignment.Center });
        bar.Children.Add(right);
        return bar;
    }

    // Aluminium (noir, sur tous les Mac) : un dégradé de gris du haut vers le bas.
    private static LinearGradientBrush Aluminium(params (double Offset, byte Grey)[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(0, 1) };
        foreach (var (offset, grey) in stops)
            brush.GradientStops.Add(new GradientStop { Offset = offset, Color = Windows.UI.Color.FromArgb(255, grey, grey, (byte)Math.Min(255, grey + 4)) });
        return brush;
    }

    // Base d'un portable Mac, vue de face : une lame d'aluminium plus large que le capot, creusée au milieu pour
    // l'ouvrir, posée sur deux patins.
    private static Grid Base(Device d, double s)
    {
        const double feet = 8;
        double lid = d.ShellWidth - 2 * d.Side, height = d.Stand - feet;
        var grid = new Grid { Height = d.Stand * s, VerticalAlignment = VerticalAlignment.Bottom };
        foreach (var edge in new[] { HorizontalAlignment.Left, HorizontalAlignment.Right })
            grid.Children.Add(new Border
            {
                Width = lid * 0.06 * s, Height = (feet + 4) * s, CornerRadius = new CornerRadius(0, 0, 4 * s, 4 * s), Background = Brush(20, 20, 22),
                HorizontalAlignment = edge, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(lid * 0.055 * s, 0, lid * 0.055 * s, 0),
            });
        grid.Children.Add(new Border
        {
            Height = height * s, VerticalAlignment = VerticalAlignment.Top, CornerRadius = new CornerRadius(2 * s, 2 * s, height * 0.6 * s, height * 0.6 * s),
            Background = Aluminium((0, 92), (0.14, 60), (0.62, 46), (1, 24)),
        });
        var scoop = height * 0.36;
        grid.Children.Add(new Border
        {
            Width = lid * 0.215 * s, Height = scoop * s, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
            CornerRadius = new CornerRadius(0, 0, scoop * 0.7 * s, scoop * 0.7 * s), Background = Aluminium((0, 74), (1, 54)),
            BorderBrush = Brush(24, 24, 26), BorderThickness = new Thickness(Math.Max(1, 1.5 * s), 0, Math.Max(1, 1.5 * s), Math.Max(1, 1.5 * s)),
        });
        return grid;
    }

    // Barre des menus d'un Mac : les menus à gauche, la batterie (portable) et l'heure à droite.
    private static Grid MenuBar(Device d, double s, double inset, SolidColorBrush ink)
    {
        var size = Math.Max(1, 13 * s); // sans taille minimale : agrandis, les menus passeraient sous l'encoche
        var bar = new Grid { Height = inset * s, VerticalAlignment = VerticalAlignment.Top };
        var menus = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 18 * s, HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20 * s, 0, 0, 0),
        };
        foreach (var title in new[] { "Safari", "File", "Edit", "View", "History", "Bookmarks", "Window", "Help" })
            menus.Children.Add(new TextBlock { Text = title, FontSize = size, FontWeight = title == "Safari" ? FontWeights.Bold : FontWeights.Normal, Foreground = ink });
        bar.Children.Add(menus);
        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 14 * s, HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 20 * s, 0),
        };
        if (d.Kind == "laptop")
            right.Children.Add(new Border { Width = 24 * s, Height = 11.5 * s, CornerRadius = new CornerRadius(3.5 * s), Background = ink, Opacity = 0.9, VerticalAlignment = VerticalAlignment.Center });
        right.Children.Add(new TextBlock { Text = "Mon 9:41", FontSize = size, Foreground = ink });
        bar.Children.Add(right);
        return bar;
    }

    // Pose l'élément (dessiné debout) couché dans un cadre aux dimensions inversées. Un Canvas ne rogne pas son contenu.
    private static Canvas Turned(FrameworkElement upright, double width, double height)
    {
        var canvas = new Canvas { Width = height, Height = width };
        Canvas.SetLeft(upright, (height - width) / 2);
        Canvas.SetTop(upright, (width - height) / 2);
        upright.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        upright.RenderTransform = new RotateTransform { Angle = -90 };
        canvas.Children.Add(upright);
        return canvas;
    }

    private static Border Piece(double width, double height, double radius, Brush fill, VerticalAlignment edge, double offset, double s) => new()
    {
        Width = width * s, Height = height * s, CornerRadius = new CornerRadius(radius * s), Background = fill,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = edge,
        Margin = edge == VerticalAlignment.Top ? new Thickness(0, offset * s, 0, 0) : new Thickness(0, 0, 0, offset * s),
    };
}
