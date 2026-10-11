namespace Browser.Core;

/// <summary>
/// Une photo pour le fond de la page d'accueil. Elle vient d'Unsplash (licence Unsplash : usage libre et gratuit) et
/// est servie par Lorem Picsum, qui ne demande ni compte ni clé. Sol ne la livre pas : elle est téléchargée quand
/// l'utilisateur la choisit.
/// </summary>
/// <param name="Id">Identifiant de la photo chez Lorem Picsum.</param>
/// <param name="Page">Page de la photo sur Unsplash : le crédit de l'auteur y renvoie.</param>
/// <param name="R">Couleur moyenne de la photo (rouge, vert, bleu) : celle de l'interface autour de la page.</param>
public sealed record StartWallpaper(int Id, string Author, string Page, byte R, byte G, byte B)
{
    public string ImageUrl => $"https://picsum.photos/id/{Id}/2560/1440";
    public string ThumbUrl => $"https://picsum.photos/id/{Id}/320/180";

    /// <summary>La couleur de la photo, assombrie : le texte de l'interface y reste blanc et lisible.</summary>
    public (byte R, byte G, byte B) Tint
    {
        get
        {
            var light = 0.299 * R + 0.587 * G + 0.114 * B;
            var scale = light > 78 ? 78 / light : 1;
            return ((byte)(R * scale), (byte)(G * scale), (byte)(B * scale));
        }
    }
}

public static class StartWallpapers
{
    public static readonly IReadOnlyList<StartWallpaper> All =
    [
        new(29, "Go Wild", "https://unsplash.com/photos/V0yAek6BgGk", 104, 115, 122),
        new(110, "Kenneth Thewissen", "https://unsplash.com/photos/D76DklsG-5U", 139, 106, 52),
        new(184, "Tim de Groot", "https://unsplash.com/photos/yNGQ830uFB4", 73, 39, 35),
        new(213, "Kelly Sikkema", "https://unsplash.com/photos/X7dy114KWs4", 183, 170, 166),
        new(218, "Monika Majkowska", "https://unsplash.com/photos/Nq8LdWC7HnM", 122, 138, 144),
        new(235, "Paul E. Harrer", "https://unsplash.com/photos/TI-B-TNYJMU", 113, 129, 131),
        new(412, "Samuel Rohl", "https://unsplash.com/photos/WvlS1yWAu8c", 105, 98, 103),
        new(426, "Ales Krivec", "https://unsplash.com/photos/01ZeHnK3F_4", 144, 158, 159),
        new(440, "Artur Pokusin", "https://unsplash.com/photos/0nv-h04oPhM", 143, 130, 116),
        new(450, "Tanvi Malik", "https://unsplash.com/photos/OeC1wIsKNpk", 130, 135, 146),
        new(475, "Casey Fyfe", "https://unsplash.com/photos/zJnpPhF4HyY", 89, 85, 82),
        new(482, "Danny Froese", "https://unsplash.com/photos/JXkCLZhhRJg", 135, 137, 137),
        new(499, "Gabriel Santiago", "https://unsplash.com/photos/qFabgklobRY", 99, 81, 78),
        new(523, "Harman Abiwardani", "https://unsplash.com/photos/WLUHO9A_xik", 115, 121, 111),
        new(813, "Roger Burkhard", "https://unsplash.com/photos/rlX9J28Pd9g", 111, 142, 186),
        new(830, "Luca Zanon", "https://unsplash.com/photos/X0OoHrPvgXE", 102, 129, 175),
        new(866, "Samuel Zeller", "https://unsplash.com/photos/9RqA6tnT0gA", 157, 149, 162),
        new(867, "Stefanus Martanto Setyo Husodo", "https://unsplash.com/photos/GKR1tBkmW3M", 127, 98, 135),
        new(871, "Stefan Kunze", "https://unsplash.com/photos/1-C334jLxG0", 71, 106, 116),
        new(872, "Brandon Lam", "https://unsplash.com/photos/Dd_7xDCuuUo", 100, 142, 169),
        new(888, "Cristian Baron", "https://unsplash.com/photos/dPFaq7RVzbQ", 97, 64, 93),
        new(896, "Jenna Beekhuis", "https://unsplash.com/photos/Bm0Ja6LZWl4", 146, 74, 53),
        new(903, "Greg Rakozy", "https://unsplash.com/photos/oMpAz-DN-9I", 79, 60, 49),
        new(910, "Sudarshan Bhat", "https://unsplash.com/photos/jgLTHYKnd5Q", 140, 162, 172),
    ];

    public static StartWallpaper? Find(int id) => All.FirstOrDefault(w => w.Id == id);

    /// <summary>La photo du jour : une par date, la même toute la journée, puis la suivante de la liste.</summary>
    public static StartWallpaper OfDay(DateOnly day) => All[day.DayNumber % All.Count];

    /// <summary>La photo à afficher selon les réglages ; null : l'image livrée avec Sol.</summary>
    public static StartWallpaper? Selected(Settings settings, DateOnly today) =>
        !settings.StartWallpaper ? null : settings.StartWallpaperDaily ? OfDay(today) : Find(settings.StartWallpaperPhoto);
}
