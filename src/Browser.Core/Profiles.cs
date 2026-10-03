using System.Text.RegularExpressions;

namespace Browser.Core;

/// <summary>
/// Profils du navigateur. Chaque profil a son propre dossier de données (cookies, mots de passe, historique,
/// favoris, session, réglages) et tourne dans sa propre fenêtre, lancée avec <c>--profile=nom</c>.
/// </summary>
public static partial class Profiles
{
    public const string Default = "defaut";

    // Le nom sert de nom de dossier : lettres minuscules, chiffres et tirets uniquement, donc aucun chemin possible.
    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,31}$")]
    private static partial Regex ValidName();

    public static bool IsValid(string name) => ValidName().IsMatch(name);

    /// <summary>Profil demandé sur la ligne de commande ; le profil par défaut si l'argument manque ou est invalide.</summary>
    public static string FromArgs(IEnumerable<string> args)
    {
        const string prefix = "--profile=";
        var name = args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];
        return name is not null && IsValid(name) ? name : Default;
    }

    public static string DataDir(string baseDir, string profile) =>
        profile == Default ? baseDir : Path.Combine(baseDir, "profiles", profile);

    /// <summary>Le profil par défaut, puis les profils créés, dans l'ordre alphabétique.</summary>
    public static IReadOnlyList<string> List(string baseDir)
    {
        var dir = Path.Combine(baseDir, "profiles");
        IEnumerable<string> created = Directory.Exists(dir)
            ? Directory.GetDirectories(dir).Select(d => Path.GetFileName(d)!).Where(n => IsValid(n) && n != Default).Order(StringComparer.Ordinal)
            : [];
        return [Default, .. created];
    }

    /// <summary>Nom du prochain profil : « profil-2 », « profil-3 »…</summary>
    public static string NextName(IReadOnlyCollection<string> existing)
    {
        for (var n = 2; ; n++)
            if (!existing.Contains($"profil-{n}")) return $"profil-{n}";
    }

    /// <summary>Nom affiché : « Défaut », « Profil 2 »…</summary>
    public static string Display(string name) =>
        name == Default ? "Défaut" : name.StartsWith("profil-", StringComparison.Ordinal) ? "Profil " + name["profil-".Length..] : name;
}
