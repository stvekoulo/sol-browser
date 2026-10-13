using System.Text.Json;

namespace Browser.Core;

public sealed class Settings
{
    public int NotesArchiveDays { get; set; }
    public bool SolAiEnabled { get; set; }
    private string _solAiModel = "";
    public string SolAiModel { get => _solAiModel; set => _solAiModel = value ?? ""; }
    public bool SolAiAdaptation { get; set; }
    public bool SolAiThinking { get; set; } = true;
    private int _solAiKeepMinutes = 45;
    public int SolAiKeepMinutes { get => _solAiKeepMinutes; set => _solAiKeepMinutes = value is >= 0 and <= 10080 ? value : 45; }
    private int _solAiTone, _solAiLength;
    public int SolAiTone { get => _solAiTone; set => _solAiTone = value is >= 0 and <= 2 ? value : 0; }
    public int SolAiLength { get => _solAiLength; set => _solAiLength = value is >= 0 and <= 2 ? value : 0; }
    public bool FullScreen { get; set; } = true;
    private double _uiScale = 1;
    /// <summary>Taille de l'interface choisie dans les réglages : 100, 90 ou 80 %.</summary>
    public double UiScale { get => _uiScale; set => _uiScale = value is 1 or 0.9 or 0.8 ? value : 1; }
    /// <summary>
    /// Facteur réellement appliqué : 100 % à 0,92, 90 % à 0,89, 80 % à 0,86.
    /// Les tailles sont proches pour garder une interface confortable.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public double UiFactor => _uiScale switch { 0.9 => 0.89, 0.8 => 0.86, _ => 0.92 };
    private double _pageZoom = 1;
    /// <summary>Zoom commun aux pages du profil, conservé au redémarrage (50 à 200 %).</summary>
    public double PageZoom
    {
        get => _pageZoom;
        set => _pageZoom = double.IsFinite(value) ? Math.Clamp(value, 0.5, 2) : 1;
    }

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
    /// <summary>La couleur de l'interface suit ce que la page affiche à l'instant (vidéo, défilement), pas seulement la couleur du site.</summary>
    public bool LiveColor { get; set; } = true;
    /// <summary>Propose d'enregistrer les mots de passe et les remplit ; le stockage chiffré est celui du moteur.</summary>
    public bool SavePasswords { get; set; } = true;
    /// <summary>Envoie la saisie de la barre d'adresse au moteur de recherche pour obtenir des complétions.</summary>
    public bool OnlineSuggestions { get; set; } = true;
    public string SuggestUrlTemplate { get; set; } = "https://suggestqueries.google.com/complete/search?client=firefox&ie=utf-8&oe=utf-8&q={0}";
    public const string DefaultHomePage = "https://start.sol/index.html#start";
    /// <summary>Page ouverte dans un nouvel onglet. (Nouveau nom : l'ancien réglage « HomeUrl » valait toujours about:blank.)</summary>
    private string _homePage = DefaultHomePage;
    public string HomePage
    {
        get => _homePage;
        set => _homePage = string.IsNullOrWhiteSpace(value) || value is "https://www.google.com" or "https://www.google.com/" ? DefaultHomePage : value;
    }
    /// <summary>Affiche l'image d'arrière-plan sur la page d'accueil d'un nouvel onglet.</summary>
    public bool StartWallpaper { get; set; }
    /// <summary>Photo choisie pour ce fond (voir StartWallpapers) ; 0 : l'image livrée avec Sol.</summary>
    public int StartWallpaperPhoto { get; set; }
    /// <summary>Une photo différente chaque jour, à la place de la photo choisie.</summary>
    public bool StartWallpaperDaily { get; set; }
    /// <summary>Langue de l'interface : "system" (celle de Windows), "fr" ou "en".</summary>
    public string Language { get; set; } = "system";
    public int Spelling { get; set; } = 3; // 0 : désactivée, 1 : français, 2 : anglais, 3 : les deux
    /// <summary>L'accueil de première ouverture (nom, import, apparence) a été vu.</summary>
    public bool Onboarded { get; set; }
    /// <summary>Nom que l'utilisateur donne au profil par défaut ; vide : « Défaut ».</summary>
    public string ProfileName { get; set; } = "";
    /// <summary>
    /// Police de l'interface : "default" (celle d'origine), "system" (celle de Windows), "inter" ou "sf" (SF Pro),
    /// ces deux dernières livrées avec le navigateur, et leurs versions grasses "inter-bold" et "sf-bold".
    /// </summary>
    public string Font { get; set; } = "default";
    /// <summary>Active uBlock Origin Lite pour bloquer les publicités et les traqueurs.</summary>
    /// <summary>Met en veille les onglets inactifs, puis les décharge de la mémoire s'ils le restent longtemps.</summary>
    public bool MemorySaver { get; set; } = true;
    /// <summary>Quitter un onglet où une vidéo joue la garde à l'écran dans un lecteur flottant.</summary>
    public bool AutoPip { get; set; } = true;
    /// <summary>Largeur de la carte latérale, réglée en tirant son bord droit.</summary>
    public double SidebarWidth { get; set; } = 296;
    /// <summary>Hauteur du panneau des outils de développement, réglée en tirant son bord haut.</summary>
    public double PanelHeight { get; set; } = 300;
    /// <summary>Écrans affichés côte à côte par la comparaison d'écrans ; vide : le choix par défaut.</summary>
    public List<string> CompareDevices { get; set; } = new();
    private List<string> _pinnedExtensions = new();
    public List<string> PinnedExtensions { get => _pinnedExtensions; set => _pinnedExtensions = value?.Where(ExtensionCatalog.IsId).Distinct().ToList() ?? []; }
    /// <summary>Dernière recherche de mises à jour des extensions du store (UTC) : une par demi-journée suffit.</summary>
    public DateTime ExtensionsChecked { get; set; }
    /// <summary>Raccourcis de la carte latérale. Ceux-ci sont proposés au départ ; l'utilisateur les modifie ou les retire.</summary>
    public List<SiteShortcut> Shortcuts { get; set; } =
    [
        new() { Url = "https://github.com", Name = "GitHub" },
        new() { Url = "https://www.youtube.com", Name = "YouTube" },
        new() { Url = "https://devbenin.bj", Name = "DevBenin" },
    ];
}

/// <summary>Raccourci vers un site : son adresse, un nom facultatif, et l'icône du site (en data:) une fois connue.</summary>
public sealed class SiteShortcut
{
    public string Url { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Icon { get; set; }
    /// <summary>Nom saisi par l'utilisateur : le nom annoncé par le site ne le remplace pas.</summary>
    public bool NameLocked { get; set; }

    /// <summary>Le site d'une adresse, sans « www. » ; vide si ce n'est pas une adresse.</summary>
    public static string DomainOf(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Host.Length == 0) return "";
        return uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..].ToLowerInvariant() : uri.Host.ToLowerInvariant();
    }

    /// <summary>
    /// Nom d'un site d'après son adresse, en attendant celui que le site annonce : « fr.wikipedia.org » donne « Wikipedia ».
    /// </summary>
    public static string NameOf(string? url)
    {
        var labels = DomainOf(url).Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (labels.Length == 0) return "";
        // ponytail: suffixes en deux parties (« co.uk », « com.br ») reconnus par une courte liste, pas par la liste publique complète.
        var skip = labels.Length >= 3 && labels[^2] is "co" or "com" or "org" or "net" or "gov" or "ac" or "edu" ? 3 : 2;
        var label = labels[Math.Max(0, labels.Length - skip)];
        return char.ToUpperInvariant(label[0]) + label[1..];
    }
}

public sealed class Session
{
    public List<string> SessionIds { get; set; } = new();
    public List<string> TranslationOriginals { get; set; } = new();
    public List<string> Urls { get; set; } = new();
    /// <summary>Titre de chaque adresse de <see cref="Urls"/> : affiché tant que la page n'a pas été rechargée.</summary>
    public List<string> Titles { get; set; } = new();
    /// <summary>Les premières adresses de <see cref="Urls"/> sont des onglets épinglés.</summary>
    public int PinnedCount { get; set; }
    public int ActiveIndex { get; set; }
    public int? PrimaryIndex { get; set; }
    public int? SecondaryIndex { get; set; }
    public double Ratio { get; set; } = 0.5;
    /// <summary>Groupes d'onglets, et pour chaque adresse de <see cref="Urls"/> le rang de son groupe (ou null).</summary>
    public List<SessionGroup> Groups { get; set; } = new();
    public List<int?> GroupOf { get; set; } = new();
    /// <summary>Pour chaque adresse de <see cref="Urls"/>, le rang du raccourci dans lequel elle est ouverte (ou null).</summary>
    public List<int?> ShortcutOf { get; set; } = new();
    /// <summary>Pour chaque adresse, l'appareil simulé (vide : aucun) ; suivi de « |L » s'il est tourné.</summary>
    public List<string> Devices { get; set; } = new();
    /// <summary>Pour chaque adresse, l'icône du site (en data:, ou vide) : un onglet rouvert l'affiche avant d'être rechargé.</summary>
    public List<string> Icons { get; set; } = new();
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
    private static readonly JsonSerializerOptions Options = new() { RespectNullableAnnotations = true };
    public static T Load<T>(string path, bool recoverBackup = true) where T : new()
    {
        foreach (var candidate in recoverBackup ? new[] { path, path + ".bak" } : new[] { path })
        {
            if (!File.Exists(candidate)) continue;
            try { return JsonSerializer.Deserialize<T>(File.ReadAllText(candidate), Options) ?? throw new JsonException("Empty JSON document"); }
            catch (JsonException)
            {
                try { File.Move(candidate, candidate + ".corrupt", overwrite: true); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { System.Diagnostics.Trace.WriteLine(e); }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { System.Diagnostics.Trace.WriteLine(e); }
        }
        return new T();
    }

    public static bool Save<T>(string path, T value)
    {
        try
        {
            path = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, value, Options);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(path)) File.Replace(tmp, path, path + ".bak");
            else File.Move(tmp, path);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.WriteLine(e);
            return false; // garder le fichier précédent si le disque est plein ou le profil verrouillé
        }
    }
}
