using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Browser.App;

/// <summary>
/// Appareil simulé. Tailles en points (ceux que voit le site). Kind : "island" (îlot en haut de l'écran),
/// "punch" (caméra en poinçon), "home" (bouton d'accueil sous l'écran), "tablet".
/// </summary>
public sealed record Device(string Name, int Width, int Height, double Radius, double BezelX, double BezelY, string Kind, int PixelRatio)
{
    public const double Buttons = 4; // saillie des boutons latéraux d'un téléphone

    public bool Tablet => Kind == "tablet";
    public double ShellWidth => Width + 2 * BezelX + (Tablet ? 0 : 2 * Buttons);
    public double ShellHeight => Height + 2 * BezelY;
    public string UserAgent => Tablet
        ? "Mozilla/5.0 (iPad; CPU OS 18_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.5 Mobile/15E148 Safari/604.1"
        : "Mozilla/5.0 (iPhone; CPU iPhone OS 18_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.5 Mobile/15E148 Safari/604.1";

    // ponytail: l'iPhone 18 Pro Max reprend l'écran du 17 Pro Max, et les deux écrans de l'iPhone Fold sont des
    // estimations ; à corriger avec les chiffres d'Apple.
    public static readonly IReadOnlyList<Device> All =
    [
        new("iPhone 18 Pro Max", 440, 956, 60, 10, 10, "island", 3),
        new("iPhone Air", 420, 912, 60, 9, 9, "island", 3),
        new("iPhone Fold", 640, 904, 36, 9, 9, "punch", 3),
        new("iPhone Fold cover", 474, 696, 44, 9, 9, "punch", 3),
        new("iPhone SE", 375, 667, 0, 14, 78, "home", 2),
        new("iPad mini", 744, 1133, 22, 22, 22, "tablet", 2),
        new("iPad Air 11", 820, 1180, 20, 22, 22, "tablet", 2),
        new("iPad Pro 13", 1032, 1376, 24, 18, 18, "tablet", 2),
    ];

    public static Device? Find(string name) => All.FirstOrDefault(d => d.Name == name);
}

/// <summary>Dessine la coque d'un appareil : ce qui passe sous la vue web (corps, boutons) et ce qui passe dessus (îlot, barre).</summary>
public static class DeviceFrame
{
    private static SolidColorBrush Brush(byte r, byte g, byte b, byte a = 255) => new(Windows.UI.Color.FromArgb(a, r, g, b));

    /// <param name="s">Échelle d'affichage : l'appareil entier tient dans la fenêtre.</param>
    public static (FrameworkElement Below, FrameworkElement Above) Build(Device d, double s)
    {
        var metal = Brush(96, 96, 102);
        var shell = new Grid { Width = d.ShellWidth * s, Height = d.ShellHeight * s, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var side = d.Tablet ? 0 : Device.Buttons * s;
        shell.Children.Add(new Border
        {
            Margin = new Thickness(side, 0, side, 0),
            CornerRadius = new CornerRadius((d.Kind == "home" ? 48 : d.Radius + Math.Min(d.BezelX, d.BezelY)) * s),
            Background = Brush(10, 10, 12), BorderBrush = metal, BorderThickness = new Thickness(Math.Max(1.5, 2.5 * s)),
        });

        if (!d.Tablet)
        {
            // Bouton Action et volume à gauche, bouton latéral à droite ; placés en proportion de la hauteur.
            foreach (var (left, top, height) in new[] { (true, 0.17, 0.035), (true, 0.235, 0.065), (true, 0.315, 0.065), (false, 0.27, 0.1) })
                shell.Children.Add(new Border
                {
                    Width = Device.Buttons * s + 1, Height = height * d.ShellHeight * s, CornerRadius = new CornerRadius(2 * s), Background = metal,
                    HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, top * d.ShellHeight * s, 0, 0),
                });
        }
        if (d.Kind == "home")
        {
            shell.Children.Add(Piece(50, 5, 2.5, Brush(44, 44, 48), VerticalAlignment.Top, (d.BezelY - 5) / 2, s)); // écouteur
            var button = Piece(48, 48, 24, Brush(10, 10, 12), VerticalAlignment.Bottom, (d.BezelY - 48) / 2, s);
            (button.BorderBrush, button.BorderThickness) = (Brush(70, 70, 76), new Thickness(1.5));
            shell.Children.Add(button);
        }
        if (d.Tablet) shell.Children.Add(Piece(7, 7, 3.5, Brush(40, 40, 46), VerticalAlignment.Top, (d.BezelY - 7) / 2, s)); // caméra

        // Par-dessus l'écran, sans jamais prendre les clics.
        var over = new Grid { Width = Math.Round(d.Width * s), Height = Math.Round(d.Height * s), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        if (d.Kind == "island") over.Children.Add(Piece(124, 36, 18, Brush(0, 0, 0), VerticalAlignment.Top, 12, s));
        if (d.Kind == "punch") over.Children.Add(Piece(13, 13, 6.5, Brush(0, 0, 0), VerticalAlignment.Top, 12, s));
        if (d.Kind != "home") over.Children.Add(Piece(d.Tablet ? 220 : 140, 5, 2.5, Brush(128, 128, 128, 150), VerticalAlignment.Bottom, 8, s)); // barre d'accueil
        return (shell, over);
    }

    private static Border Piece(double width, double height, double radius, Brush fill, VerticalAlignment edge, double offset, double s) => new()
    {
        Width = width * s, Height = height * s, CornerRadius = new CornerRadius(radius * s), Background = fill,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = edge,
        Margin = edge == VerticalAlignment.Top ? new Thickness(0, offset * s, 0, 0) : new Thickness(0, 0, 0, offset * s),
    };
}
