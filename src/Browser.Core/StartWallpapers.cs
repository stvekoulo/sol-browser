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
        new(102, "Ben Moore", "https://unsplash.com/photos/pJILiyPdrXI", 216, 180, 139),
        new(103, "Ilham Rahmansyah", "https://unsplash.com/photos/DwTZwZYi9Ww", 145, 138, 121),
        new(104, "Dyaa Eldin", "https://unsplash.com/photos/2fl-ocJ5MOA", 223, 220, 186),
        new(106, "Arvee Marie", "https://unsplash.com/photos/YnfGtpt2gf4", 141, 149, 157),
        new(107, "Lukas Schweizer", "https://unsplash.com/photos/9VWOr22LhVI", 134, 140, 114),
        new(108, "Florian Klauer", "https://unsplash.com/photos/t1mqA3V3-7g", 228, 222, 219),
        new(109, "Zwaddi", "https://unsplash.com/photos/YvYBOSiBJE8", 161, 135, 99),
        new(111, "Gabe Rodriguez", "https://unsplash.com/photos/eLUegVAjN7s", 123, 111, 102),
        new(112, "Zugr", "https://unsplash.com/photos/kmF_Aq8gkp0", 134, 118, 90),
        new(113, "Zugr", "https://unsplash.com/photos/yZf1quatKCA", 136, 148, 155),
        new(114, "Brian Gonzalez", "https://unsplash.com/photos/llYg8Ni43fc", 141, 146, 132),
        new(116, "Anton Sulsky", "https://unsplash.com/photos/YcfCXxo7rpc", 81, 80, 75),
        new(119, "Nadir Balcikli", "https://unsplash.com/photos/wE9nUW7tMmk", 208, 208, 208),
        new(120, "Guillaume", "https://unsplash.com/photos/_DA3D5P71qs", 32, 34, 19),
        new(121, "Radio Pink", "https://unsplash.com/photos/p-bkdO43shE", 115, 115, 115),
        new(122, "Vadim Sherbakov", "https://unsplash.com/photos/xS_RzdD5CFE", 25, 51, 51),
        new(123, "Mark Doda", "https://unsplash.com/photos/tS9hJOnmKK8", 89, 91, 100),
        new(124, "Anton Sulsky", "https://unsplash.com/photos/fj0tFloTPGQ", 61, 113, 113),
        new(126, "Zugr", "https://unsplash.com/photos/asrWX-lU3RE", 111, 110, 114),
        new(127, "Marcin Czerwinski", "https://unsplash.com/photos/rf-0DQu5M6Y", 123, 117, 45),
        new(128, "Matteo Minelli", "https://unsplash.com/photos/hlnucYOsL-c", 90, 95, 79),
        new(129, "Charlie Foster", "https://unsplash.com/photos/A88emaZe7d8", 187, 162, 120),
        new(130, "Ryan Jacques", "https://unsplash.com/photos/ywiAen4L4qA", 218, 218, 218),
        new(131, "Charlie Foster", "https://unsplash.com/photos/yPaskTQiNjA", 221, 219, 166),
        new(132, "Peter Besser", "https://unsplash.com/photos/gq4kE8KRP8c", 118, 91, 76),
        new(133, "Dietmar Becker", "https://unsplash.com/photos/8Zt0xOOK4nI", 149, 145, 109),
        new(134, "Charlie Foster", "https://unsplash.com/photos/Osl4I3IS9Cw", 126, 120, 97),
        new(135, "Yuriy Khimanin", "https://unsplash.com/photos/o4H20aIIAt8", 97, 98, 118),
        new(136, "Marcin Czerwinski", "https://unsplash.com/photos/2wugfiddtXw", 91, 84, 60),
        new(137, "Vladimir Kramer", "https://unsplash.com/photos/xzZtV9ED5Bs", 97, 70, 50),
        new(141, "Greg Shield", "https://unsplash.com/photos/v9eNihIWh8k", 109, 113, 124),
        new(142, "Vadim Sherbakov", "https://unsplash.com/photos/KSyemQIWwP8", 116, 119, 110),
        new(143, "Steve Richey", "https://unsplash.com/photos/6xqAK6oAeHA", 139, 130, 118),
        new(144, "Mouly Kumar", "https://unsplash.com/photos/TuOiIpkIea8", 138, 138, 138),
        new(145, "Lucas Boesche", "https://unsplash.com/photos/VkuuTRkcRqw", 99, 96, 63),
        new(146, "Florian Klauer", "https://unsplash.com/photos/GG0jOrmwqtw", 159, 143, 138),
        new(149, "Guillaume", "https://unsplash.com/photos/revxuIor0nY", 19, 42, 50),
        new(151, "Edoardo Loru", "https://unsplash.com/photos/xPfj_Kdcal4", 114, 114, 114),
        new(152, "Steven Spassov", "https://unsplash.com/photos/tVIqMgGlAG0", 103, 89, 113),
        new(153, "Charlie Foster", "https://unsplash.com/photos/xpkmxDGPz0Y", 108, 91, 71),
        new(154, "Christopher Sardegna", "https://unsplash.com/photos/x_jTtMOOMd4", 160, 150, 152),
        new(155, "Christopher Sardegna", "https://unsplash.com/photos/4f7r1LuPYj8", 174, 160, 164),
        new(158, "Daniel Robert", "https://unsplash.com/photos/MRxD-J9-4ps", 136, 140, 161),
        new(159, "Shyamanta Baruah", "https://unsplash.com/photos/kxqvE41_07k", 91, 130, 104),
        new(160, "Thom", "https://unsplash.com/photos/Zdcq3iKly6g", 95, 102, 111),
        new(161, "Chloe Benko-Prieur", "https://unsplash.com/photos/ti4uz330CwU", 119, 97, 83),
        new(163, "Linh Nguyen", "https://unsplash.com/photos/oFAVqfTSby8", 105, 102, 100),
        new(165, "Linh Nguyen", "https://unsplash.com/photos/xjXz8GKXcTI", 99, 147, 144),
    ];

    public static StartWallpaper? Find(int id) => All.FirstOrDefault(w => w.Id == id);

    /// <summary>La photo du jour : une par date, la même toute la journée, puis la suivante de la liste.</summary>
    public static StartWallpaper OfDay(DateOnly day) => All[day.DayNumber % All.Count];

    /// <summary>La photo à afficher selon les réglages ; null : l'image livrée avec Sol.</summary>
    public static StartWallpaper? Selected(Settings settings, DateOnly today) =>
        !settings.StartWallpaper ? null : settings.StartWallpaperDaily ? OfDay(today) : Find(settings.StartWallpaperPhoto);
}
