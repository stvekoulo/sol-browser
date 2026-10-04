using System.Text.RegularExpressions;

namespace Browser.Core;

/// <summary>Une police déposée par l'utilisateur : son fichier, sa graisse et son style, lus dans le nom du fichier.</summary>
public sealed record FontFace(string File, int Weight, bool Italic);

public static partial class FontFiles
{
    private static readonly string[] Extensions = [".otf", ".ttf", ".woff2", ".woff"];

    // Du plus précis au plus général : « semibold » avant « bold », « ultralight » avant « light ».
    private static readonly (string Word, int Weight)[] Weights =
    [
        ("ultralight", 200), ("extralight", 200), ("semibold", 600), ("demibold", 600), ("extrabold", 800), ("ultrabold", 800),
        ("thin", 100), ("hairline", 100), ("light", 300), ("regular", 400), ("book", 400), ("medium", 500), ("heavy", 800),
        ("black", 900), ("bold", 700),
    ];

    // Lettres, chiffres, espaces et quelques signes : le nom part tel quel dans une adresse.
    [GeneratedRegex(@"^[\w .()\-]{1,120}$")]
    private static partial Regex SafeName();

    /// <summary>Les polices d'un dossier. Un fichier sans graisse reconnaissable est pris pour la graisse normale.</summary>
    public static IReadOnlyList<FontFace> In(string folder)
    {
        if (!Directory.Exists(folder)) return [];
        return Directory.GetFiles(folder).Select(Path.GetFileName).OfType<string>()
            .Where(name => Extensions.Contains(Path.GetExtension(name).ToLowerInvariant()) && SafeName().IsMatch(name))
            .Order(StringComparer.OrdinalIgnoreCase).Select(Describe).ToList();
    }

    public static FontFace Describe(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant().Replace("-", "").Replace("_", "").Replace(" ", "");
        var italic = name.Contains("italic") || name.Contains("oblique");
        var weight = Weights.FirstOrDefault(w => name.Contains(w.Word)).Weight;
        return new FontFace(fileName, weight == 0 ? 400 : weight, italic);
    }
}
