namespace Browser.Core;

/// <summary>Langue de l'interface : français ou anglais.</summary>
public static class Language
{
    public const string Fallback = "en";
    public static readonly IReadOnlyList<string> Supported = ["fr", "en"];

    /// <summary>Langue en cours, fixée au démarrage et à chaque changement du réglage.</summary>
    public static string Current { get; set; } = Fallback;

    /// <summary>
    /// Le réglage s'il nomme une langue connue ; sinon ("system" ou valeur inconnue) la langue de Windows
    /// (« fr-FR », « fr-BJ »… donnent « fr ») ; sinon l'anglais.
    /// </summary>
    public static string Resolve(string? setting, string? systemCulture)
    {
        if (setting is not null && Supported.Contains(setting)) return setting;
        var system = (systemCulture ?? "").Split('-', '_')[0].ToLowerInvariant();
        return Supported.Contains(system) ? system : Fallback;
    }

    // ponytail: deux langues, textes côte à côte ; passer à des tables par clé à la troisième langue.
    public static string T(string fr, string en) => Current == "fr" ? fr : en;
}
