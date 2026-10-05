using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Browser.App;

/// <summary>
/// Appareil simulé, en points (l'unité que voit le site). Les marges autour de l'écran et les rayons viennent des
/// dimensions réelles de l'appareil (millimètres convertis en points à la densité de son écran).
///
/// Kind : "island" (îlot en haut de l'écran), "fold-open" (pliant ouvert : écran sans découpe, charnière au milieu),
/// "fold-closed" (pliant fermé : caméra en poinçon, charnière sur le côté), "home" (bouton d'accueil sous l'écran),
/// "tablet", "laptop" (portable Mac : écran dans son capot, encoche si CutWidth > 0, base dessous), "imac", "desktop".
/// TopInset : hauteur de la barre d'état (ou de la barre des menus d'un Mac), que le système garde pour lui ; la page
/// s'affiche dessous.
/// Chin : ce que la coque a de plus sous l'écran (bord bas plus épais d'un capot, menton de l'iMac).
/// Stand : ce qui porte l'écran, sous la coque (base d'un portable vue de face, pied de l'iMac).
/// </summary>
public sealed record Device(string Name, int Width, int Height, int PixelRatio, string Kind,
    double Radius, double BezelX, double BezelY, double ShellRadius, double TopInset, double CutWidth = 0, double CutHeight = 0,
    double Chin = 0, double Stand = 0)
{
    public const double Buttons = 4; // saillie des boutons latéraux

    public bool Tablet => Kind == "tablet";
    /// <summary>Ordinateur : ni écran tactile, ni rotation.</summary>
    public bool Computer => Kind is "desktop" or "laptop" or "imac";
    public bool SideButtons => Kind is "island" or "home" or "fold-closed";
    /// <summary>Hauteur de l'appareil sous son écran, en plus du bord : l'écran n'est plus au milieu de la coque.</summary>
    public double Foot => Chin + Stand;
    /// <summary>Ce qui dépasse de chaque côté de la coque : les boutons d'un téléphone, la base d'un portable (vue de face
    /// et de près, comme sur une photo de produit, elle paraît plus large que le capot).</summary>
    public double Side => SideButtons ? Buttons : Kind == "laptop" ? Math.Round((Width + 2 * BezelX) * 0.105) : 0;
    public double ShellWidth => Width + 2 * BezelX + 2 * Side;
    public double ShellHeight => Height + 2 * BezelY + Foot;
    /// <summary>Rayon des coins du bas de l'écran, ceux de la page. L'écran d'un portable Mac n'est arrondi qu'en haut.</summary>
    public double PageRadius => Kind == "laptop" ? 0 : Radius;

    /// <summary>Hauteur de la barre d'état. Un téléphone couché n'en affiche pas ; une tablette et un pliant ouvert la gardent.</summary>
    public double Inset(bool turned) => turned && Kind is "island" or "home" or "fold-closed" ? 0 : TopInset;

    /// <summary>Agent annoncé aux sites ; null : celui du navigateur (sur un Mac, voir <see cref="Agent"/>).</summary>
    public string? UserAgent => Kind switch
    {
        "desktop" or "laptop" or "imac" => null,
        "tablet" => "Mozilla/5.0 (iPad; CPU OS 18_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.5 Mobile/15E148 Safari/604.1",
        _ => "Mozilla/5.0 (iPhone; CPU iPhone OS 18_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.5 Mobile/15E148 Safari/604.1",
    };

    /// <summary>
    /// Agent à annoncer, connaissant celui du navigateur. Un Mac garde le moteur du navigateur et change seulement de
    /// système : annoncé comme Safari, il recevrait des pages et des vidéos prévues pour un autre moteur.
    /// </summary>
    public string? Agent(string? browser) =>
        UserAgent ?? (Kind is "laptop" or "imac" ? browser?.Replace("Windows NT 10.0; Win64; x64", "Macintosh; Intel Mac OS X 10_15_7") : browser);

    // Écrans : chiffres publiés (points et densité). Coques : dimensions des appareils. Pour l'iPhone Duo (le pliant
    // d'Apple), les marges viennent des dimensions annoncées (167,6 × 120,6 mm ouvert, 83,8 × 120,6 mm fermé) ;
    // la place exacte du poinçon de l'écran extérieur n'est pas confirmée.
    // Mac : la taille en points est celle du réglage d'origine de macOS (« par défaut »), qui n'est pas toujours la moitié
    // des pixels de la dalle (MacBook Air : 1470 × 956 pour 2560 × 1664 ; MacBook Neo : 1408 × 881 pour 2408 × 1506).
    // Sous la barre des menus d'un Mac à encoche, il reste un écran 16:10 exact. Apple ne publie ni la taille de
    // l'encoche (185 × 32 points mesurés sur MacBook Pro ; déduite pour les Air), ni la part du capot visible sous
    // l'écran, ni l'épaisseur de la base seule : ces trois valeurs sont estimées.
    // Les Mac sont dessinés comme sur une photo de produit, de face : capot noir, base d'aluminium noir (Stand, avec
    // ses patins) plus large que lui. iMac : bord de 12,5 mm, menton de 60 mm, 46,1 cm de haut avec le pied (13 cm de
    // large) ; il est noir ici ; Apple le vend avec un bord blanc.
    public static readonly IReadOnlyList<Device> All =
    [
        new("iPhone 18 Pro Max", 440, 956, 3, "island", 62, 15, 15, 77, 62, CutWidth: 81, CutHeight: 34),
        new("iPhone Air", 420, 912, 3, "island", 62, 15, 15, 77, 68, CutWidth: 124, CutHeight: 36),
        new("iPhone Duo", 904, 640, 3, "fold-open", 34, 18, 19, 52, 28),
        new("iPhone Duo cover", 474, 696, 3, "fold-closed", 44, 16, 16, 58, 54, CutWidth: 13, CutHeight: 13),
        new("iPhone SE", 375, 667, 2, "home", 0, 26, 108, 60, 20),
        new("iPad mini", 744, 1133, 2, "tablet", 22, 61, 61, 78, 24),
        new("iPad Air 11", 820, 1180, 2, "tablet", 18, 54, 53, 64, 24),
        new("iPad Air 13", 1024, 1366, 2, "tablet", 18, 47, 46, 64, 24),
        new("iPad Pro 13", 1032, 1376, 2, "tablet", 30, 44, 44, 66, 24),
        new("MacBook Neo", 1408, 881, 2, "laptop", 14, 46, 46, 44, 24, Chin: 30, Stand: 60),
        new("MacBook Air 13", 1470, 956, 2, "laptop", 16, 35, 35, 44, 37, CutWidth: 188, CutHeight: 32, Chin: 31, Stand: 56),
        new("MacBook Air 15", 1710, 1107, 2, "laptop", 16, 36, 36, 46, 38, CutWidth: 194, CutHeight: 33, Chin: 32, Stand: 58),
        new("MacBook Pro 14", 1512, 982, 2, "laptop", 18, 26, 26, 40, 37, CutWidth: 185, CutHeight: 32, Chin: 34, Stand: 72),
        new("MacBook Pro 16", 1728, 1117, 2, "laptop", 18, 25, 25, 40, 37, CutWidth: 185, CutHeight: 32, Chin: 34, Stand: 76),
        new("iMac 24", 2240, 1260, 2, "imac", 0, 54, 54, 44, 24, Chin: 260, Stand: 352),
        new("Desktop", 1440, 900, 1, "desktop", 6, 14, 14, 16, 0),
    ];

    /// <summary>Écrans comparés côte à côte si l'utilisateur n'en a pas choisi d'autres.</summary>
    public static readonly IReadOnlyList<string> DefaultComparison = ["iPhone 18 Pro Max", "iPad Air 11", "Desktop"];

    public static Device? Find(string name) => All.FirstOrDefault(d => d.Name == name);
}

/// <summary>
/// Dessine un appareil. « Below » passe sous la vue web : la coque, et l'écran avec sa barre d'état. « Above » passe
/// dessus : l'îlot ou le poinçon, et la barre d'accueil.
/// </summary>
public static class DeviceFrame
{
    private static SolidColorBrush Brush(byte r, byte g, byte b, byte a = 255) => new(Windows.UI.Color.FromArgb(a, r, g, b));

    /// <param name="s">Échelle d'affichage : l'appareil entier tient dans la fenêtre.</param>
    /// <param name="turned">Appareil tourné d'un quart de tour : son haut passe à gauche.</param>
    /// <param name="page">Couleur de fond de la page : celle de la barre d'état, comme sur l'appareil.</param>
    public static (FrameworkElement Below, FrameworkElement Above) Build(Device d, double s, bool turned, Windows.UI.Color page)
    {
        var metal = Brush(104, 104, 110);
        var shell = new Grid { Width = d.ShellWidth * s, Height = d.ShellHeight * s };
        var side = d.Side * s;
        var round = d.ShellRadius * s;
        shell.Children.Add(new Border
        {
            Margin = new Thickness(side, 0, side, d.Stand * s),
            CornerRadius = d.Kind == "laptop" ? new CornerRadius(round, round, 0, 0) : new CornerRadius(round), // un capot s'arrête droit sur sa base
            Background = Brush(10, 10, 12), BorderBrush = d.Kind is "laptop" or "imac" ? Brush(50, 50, 54) : metal, BorderThickness = new Thickness(Math.Max(1.5, 3 * s)),
        });

        // Boutons latéraux, placés en proportion de la hauteur.
        (bool Left, double Top, double Height)[] buttons = d.Kind switch
        {
            "island" => [(true, 0.17, 0.035), (true, 0.235, 0.065), (true, 0.315, 0.065), (false, 0.27, 0.1)],
            "home" => [(true, 0.15, 0.03), (true, 0.215, 0.06), (true, 0.29, 0.06), (false, 0.215, 0.085)],
            "fold-closed" => [(false, 0.2, 0.07), (false, 0.29, 0.07), (false, 0.4, 0.1)], // volume, puis bouton Touch ID
            _ => [],
        };
        foreach (var (left, top, height) in buttons)
            shell.Children.Add(new Border
            {
                Width = Device.Buttons * s + 1, Height = height * d.ShellHeight * s, CornerRadius = new CornerRadius(2 * s), Background = metal,
                HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, top * d.ShellHeight * s, 0, 0),
            });

        switch (d.Kind)
        {
            case "home":
                shell.Children.Add(Piece(62, 6, 3, Brush(46, 46, 50), VerticalAlignment.Top, (d.BezelY - 6) / 2, s)); // écouteur
                var button = Piece(60, 60, 30, Brush(10, 10, 12), VerticalAlignment.Bottom, (d.BezelY - 60) / 2, s);
                (button.BorderBrush, button.BorderThickness) = (Brush(74, 74, 80), new Thickness(Math.Max(1, 2 * s)));
                shell.Children.Add(button);
                break;
            case "tablet":
                shell.Children.Add(Piece(9, 9, 4.5, Brush(44, 44, 50), VerticalAlignment.Top, (d.BezelY - 9) / 2, s)); // caméra
                break;
            case "laptop":
                if (d.CutWidth == 0) shell.Children.Add(Piece(9, 9, 4.5, Brush(44, 44, 50), VerticalAlignment.Top, (d.BezelY - 9) / 2, s)); // caméra dans le bord
                shell.Children.Add(Base(d, s));
                break;
            case "imac": // sous l'écran et son bord : le menton, puis le pied (13 cm de large, soit 558 points), qui passe derrière
                shell.Children.Insert(0, Piece(558, d.Stand + d.Chin / 2, 0, Aluminium((0, 20), (0.55, 40), (1, 56)), VerticalAlignment.Bottom, 0, s));
                shell.Children.Add(Piece(558, 26, 3, Aluminium((0, 78), (1, 44)), VerticalAlignment.Bottom, 0, s)); // la semelle
                shell.Children.Add(Piece(9, 9, 4.5, Brush(44, 44, 50), VerticalAlignment.Top, (d.BezelY - 9) / 2, s)); // caméra
                shell.Children.Add(new Border
                {
                    Height = d.Chin * s, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, d.Stand * s),
                    CornerRadius = new CornerRadius(0, 0, round, round), Background = Aluminium((0, 60), (1, 42)),
                });
                break;
            case "fold-open": // la charnière : un trait au milieu, visible dans les marges du haut et du bas
                shell.Children.Add(new Border { Width = Math.Max(1, 2 * s), Background = Brush(58, 58, 64), HorizontalAlignment = HorizontalAlignment.Center });
                break;
            case "fold-closed": // la charnière : le dos arrondi, à gauche
                shell.Children.Add(new Border
                {
                    Width = 7 * s, Margin = new Thickness(side + 3 * s, d.ShellRadius * s * 0.55, 0, d.ShellRadius * s * 0.55),
                    HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(3.5 * s), Background = Brush(70, 70, 76),
                });
                break;
        }

        // L'écran, droit quelle que soit l'orientation : fond de la page, barre d'état en haut.
        double uprightWidth = Math.Round(d.Width * s), uprightHeight = Math.Round(d.Height * s);
        double screenWidth = turned ? uprightHeight : uprightWidth, screenHeight = turned ? uprightWidth : uprightHeight;
        var inset = d.Inset(turned);
        // Centré avec, en marge basse, ce que l'appareil a de plus sous son écran : l'écran reste à sa place dans la coque.
        var screen = new Grid
        {
            Width = screenWidth, Height = screenHeight, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, d.Foot * s),
        };
        screen.Children.Add(new Border { CornerRadius = new CornerRadius(d.Radius * s, d.Radius * s, d.PageRadius * s, d.PageRadius * s), Background = new SolidColorBrush(page) });
        if (inset > 0) screen.Children.Add(d.Kind is "laptop" or "imac" ? MenuBar(d, s, inset, page) : StatusBar(d, s, inset, page));

        var below = new Grid { Width = turned ? shell.Height : shell.Width, Height = turned ? shell.Width : shell.Height };
        below.Children.Add(turned ? Turned(shell, shell.Width, shell.Height) : shell);
        below.Children.Add(screen);

        // Par-dessus l'écran, sans jamais prendre les clics. La découpe tourne avec l'appareil ; la barre d'accueil reste en bas.
        var upright = new Grid { Width = uprightWidth, Height = uprightHeight };
        var cutTop = Math.Max(8, (d.TopInset - d.CutHeight) / 2);
        if (d.Kind == "island") upright.Children.Add(Piece(d.CutWidth, d.CutHeight, d.CutHeight / 2, Brush(0, 0, 0), VerticalAlignment.Top, cutTop, s));
        if (d.Kind == "fold-closed")
        {
            var punch = Piece(d.CutWidth, d.CutHeight, d.CutHeight / 2, Brush(0, 0, 0), VerticalAlignment.Top, cutTop, s);
            (punch.HorizontalAlignment, punch.Margin) = (HorizontalAlignment.Left, new Thickness(26 * s, cutTop * s, 0, 0));
            upright.Children.Add(punch);
        }
        if (d.Kind == "laptop" && d.CutWidth > 0) // l'encoche : elle descend du bord, de sa couleur, et loge la caméra
        {
            var notch = new Grid { Width = d.CutWidth * s, Height = d.CutHeight * s, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
            notch.Children.Add(new Border { CornerRadius = new CornerRadius(0, 0, 8 * s, 8 * s), Background = Brush(10, 10, 12) });
            notch.Children.Add(new Border { Width = 8 * s, Height = 8 * s, CornerRadius = new CornerRadius(4 * s), Background = Brush(38, 40, 52), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            upright.Children.Add(notch);
        }
        var over = new Grid { Width = screenWidth, Height = screenHeight, IsHitTestVisible = false };
        over.Children.Add(turned ? Turned(upright, uprightWidth, uprightHeight) : upright);
        if (d.Kind != "home" && !d.Computer)
            over.Children.Add(Piece(d.Kind is "tablet" or "fold-open" ? 220 : 140, 5, 2.5, Brush(128, 128, 128, 160), VerticalAlignment.Bottom, 8, s));
        return (below, over);
    }

    // Barre d'état : l'heure à gauche, le réseau et la batterie à droite, dans la couleur lisible sur le fond de la page.
    private static Grid StatusBar(Device d, double s, double inset, Windows.UI.Color page)
    {
        var dark = 0.299 * page.R + 0.587 * page.G + 0.114 * page.B < 128;
        var ink = dark ? Brush(255, 255, 255) : Brush(0, 0, 0);
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
    private static Grid MenuBar(Device d, double s, double inset, Windows.UI.Color page)
    {
        var dark = 0.299 * page.R + 0.587 * page.G + 0.114 * page.B < 128;
        var ink = dark ? Brush(255, 255, 255) : Brush(0, 0, 0);
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
