using System.Text.Json;

namespace Browser.Core;

public sealed class Settings
{
    public string SearchUrlTemplate { get; set; } = "https://www.google.com/search?q={0}";
    public bool BlurEnabled { get; set; } = true;
    /// <summary>Masque les barres de défilement des pages. Désactivé : une barre fine apparaît pendant le défilement.</summary>
    public bool HideScrollbars { get; set; } = true;
    /// <summary>Affiche les téléchargements en cours dans un encart en bas à droite. Désactivé : seulement le panneau de la barre.</summary>
    public bool FloatingDownloads { get; set; } = true;
    /// <summary>
    /// Thème de l'interface : "page" (suit la page visitée, clair ou sombre comme elle), "light", "dark",
    /// ou "system" (suit Windows). La couleur du site s'applique dans tous les cas.
    /// </summary>
    public string Theme { get; set; } = "page";
    /// <summary>Où la couleur du site visité teinte l'interface. Tout coupé : l'interface reste neutre.</summary>
    public bool TintTopbar { get; set; } = true;
    public bool TintSidebar { get; set; } = true;
    public bool TintMenus { get; set; } = true;
    public bool TintBackground { get; set; } = true;
    /// <summary>Propose d'enregistrer les mots de passe et les remplit ; le stockage chiffré est celui du moteur.</summary>
    public bool SavePasswords { get; set; } = true;
    /// <summary>Envoie la saisie de la barre d'adresse au moteur de recherche pour obtenir des complétions.</summary>
    public bool OnlineSuggestions { get; set; } = true;
    public string SuggestUrlTemplate { get; set; } = "https://suggestqueries.google.com/complete/search?client=firefox&ie=utf-8&oe=utf-8&q={0}";
    public string HomeUrl { get; set; } = "about:blank";
}

public sealed class Session
{
    public List<string> Urls { get; set; } = new();
    /// <summary>Les premières adresses de <see cref="Urls"/> sont des onglets épinglés.</summary>
    public int PinnedCount { get; set; }
    public int ActiveIndex { get; set; }
    public int? PrimaryIndex { get; set; }
    public int? SecondaryIndex { get; set; }
    public double Ratio { get; set; } = 0.5;
    /// <summary>Groupes d'onglets, et pour chaque adresse de <see cref="Urls"/> le rang de son groupe (ou null).</summary>
    public List<SessionGroup> Groups { get; set; } = new();
    public List<int?> GroupOf { get; set; } = new();
}

public sealed class SessionGroup
{
    public string Name { get; set; } = "";
    public string Color { get; set; } = "";
    public string Icon { get; set; } = "";
    public bool Collapsed { get; set; }
}

public static class JsonFileStore
{
    public static T Load<T>(string path) where T : new()
    {
        if (!File.Exists(path)) return new T();
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path)) ?? new T();
        }
        catch (JsonException)
        {
            File.Move(path, path + ".corrupt", overwrite: true);
            return new T();
        }
    }

    public static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value));
        File.Move(tmp, path, overwrite: true);
    }
}
