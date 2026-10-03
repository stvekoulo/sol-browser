using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Browser.Core;

/// <summary>
/// Profils du navigateur. Chaque profil a son propre dossier de données (cookies, mots de passe, historique,
/// favoris, session, réglages). La fenêtre est lancée avec <c>--profile=identifiant</c>.
/// L'identifiant sert de nom de dossier ; le nom choisi par l'utilisateur est rangé dans ce dossier.
/// </summary>
public static partial class Profiles
{
    public const string Default = "defaut";
    private const string NameFile = "profile-name.txt";
    private const int MaxNameLength = 40;

    // Lettres minuscules, chiffres et tirets uniquement : un identifiant ne peut jamais désigner un autre chemin.
    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,31}$")]
    private static partial Regex ValidId();

    public static bool IsValid(string id) => ValidId().IsMatch(id);

    /// <summary>Profil demandé sur la ligne de commande ; le profil par défaut si l'argument manque ou est invalide.</summary>
    public static string FromArgs(IEnumerable<string> args)
    {
        const string prefix = "--profile=";
        var id = args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];
        return id is not null && IsValid(id) ? id : Default;
    }

    public static string DataDir(string baseDir, string id) =>
        id == Default ? baseDir : Path.Combine(baseDir, "profiles", id);

    /// <summary>Le profil par défaut, puis les profils créés, dans l'ordre alphabétique.</summary>
    public static IReadOnlyList<string> List(string baseDir)
    {
        var dir = Path.Combine(baseDir, "profiles");
        IEnumerable<string> created = Directory.Exists(dir)
            ? Directory.GetDirectories(dir).Select(d => Path.GetFileName(d)!).Where(n => IsValid(n) && n != Default).Order(StringComparer.Ordinal)
            : [];
        return [Default, .. created];
    }

    /// <summary>Nom affiché : celui choisi à la création, sinon « Défaut » ou l'identifiant.</summary>
    public static string NameOf(string baseDir, string id)
    {
        if (id == Default) return "Défaut";
        var file = Path.Combine(DataDir(baseDir, id), NameFile);
        try
        {
            if (File.Exists(file) && Clean(File.ReadAllText(file)) is { Length: > 0 } name) return name;
        }
        catch (IOException) { /* fichier illisible : on retombe sur l'identifiant */ }
        return id;
    }

    /// <summary>Crée un profil au nom donné et renvoie son identifiant.</summary>
    public static string Create(string baseDir, string name)
    {
        var clean = Clean(name);
        var existing = List(baseDir);
        var stem = Slug(clean) is { Length: > 0 } slug ? slug : "profil";
        var id = stem;
        for (var n = 2; !IsValid(id) || existing.Contains(id); n++) id = $"{stem}-{n}";

        var dir = DataDir(baseDir, id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, NameFile), clean.Length > 0 ? clean : id);
        return id;
    }

    /// <summary>Supprime un profil et toutes ses données. Jamais le profil par défaut.</summary>
    public static bool Delete(string baseDir, string id)
    {
        if (id == Default || !IsValid(id)) return false;
        var dir = DataDir(baseDir, id);
        if (!Directory.Exists(dir)) return false;
        Directory.Delete(dir, recursive: true);
        return true;
    }

    private static string Clean(string name)
    {
        var text = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return text.Length > MaxNameLength ? text[..MaxNameLength].Trim() : text;
    }

    // « Travail & Études » → « travail-etudes »
    private static string Slug(string name)
    {
        var plain = new StringBuilder();
        foreach (var c in name.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            var lower = char.ToLowerInvariant(c);
            plain.Append(lower is (>= 'a' and <= 'z') or (>= '0' and <= '9') ? lower : '-');
        }
        var slug = Regex.Replace(plain.ToString(), "-+", "-").Trim('-');
        return slug.Length > 24 ? slug[..24].Trim('-') : slug;
    }
}
