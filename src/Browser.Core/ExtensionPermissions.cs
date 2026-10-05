namespace Browser.Core;

/// <summary>
/// Accès demandés par une extension, dits en clair. Les permissions sans conséquence pour l'utilisateur (stockage,
/// minuteries, menus…) ne sont pas listées, comme dans les autres navigateurs ; une permission inconnue est donnée telle
/// quelle plutôt que passée sous silence.
/// </summary>
public static class ExtensionPermissions
{
    private static readonly HashSet<string> Silent =
    [
        "activeTab", "alarms", "background", "commands", "contextMenus", "declarativeContent", "dns", "favicon", "fontSettings",
        "gcm", "idle", "offscreen", "power", "runtime", "scripting", "search", "sidePanel", "storage", "system.cpu",
        "system.display", "system.memory", "system.storage", "tts", "unlimitedStorage", "userScripts", "webRequestBlocking",
        "webRequestAuthProvider", "declarativeNetRequestWithHostAccess", "declarativeNetRequestFeedback", "enterprise.platformKeys",
    ];

    private static readonly Dictionary<string, (string French, string English)> Known = new()
    {
        ["tabs"] = ("Lire votre historique de navigation", "Read your browsing history"),
        ["webNavigation"] = ("Lire votre historique de navigation", "Read your browsing history"),
        ["topSites"] = ("Lire votre historique de navigation", "Read your browsing history"),
        ["sessions"] = ("Lire votre historique de navigation", "Read your browsing history"),
        ["tabGroups"] = ("Voir et gérer vos groupes d’onglets", "View and manage your tab groups"),
        ["history"] = ("Lire et modifier votre historique de navigation", "Read and change your browsing history"),
        ["bookmarks"] = ("Lire et modifier vos favoris", "Read and change your bookmarks"),
        ["readingList"] = ("Lire et modifier votre liste de lecture", "Read and change your reading list"),
        ["downloads"] = ("Gérer vos téléchargements", "Manage your downloads"),
        ["downloads.open"] = ("Gérer vos téléchargements", "Manage your downloads"),
        ["cookies"] = ("Lire et modifier les cookies des sites auxquels elle a accès", "Read and change cookies on the sites it can access"),
        ["nativeMessaging"] = ("Communiquer avec des applications installées sur cet ordinateur", "Communicate with apps installed on this computer"),
        ["notifications"] = ("Afficher des notifications", "Display notifications"),
        ["clipboardRead"] = ("Lire ce que vous copiez et collez", "Read data you copy and paste"),
        ["clipboardWrite"] = ("Modifier ce que vous copiez et collez", "Modify data you copy and paste"),
        ["geolocation"] = ("Connaître votre position", "Detect your physical location"),
        ["management"] = ("Gérer vos autres extensions", "Manage your other extensions"),
        ["proxy"] = ("Faire passer votre connexion par un autre serveur", "Route your connection through another server"),
        ["debugger"] = ("Lire et modifier tout ce que font les pages, par les outils de développement", "Read and change everything pages do, through the developer tools"),
        ["webRequest"] = ("Observer les requêtes réseau des pages", "Observe network requests made by pages"),
        ["declarativeNetRequest"] = ("Bloquer du contenu sur les pages", "Block content on any page"),
        ["privacy"] = ("Modifier vos réglages de confidentialité", "Change your privacy settings"),
        ["contentSettings"] = ("Modifier les autorisations des sites (cookies, JavaScript, caméra…)", "Change site permissions (cookies, JavaScript, camera…)"),
        ["browsingData"] = ("Effacer vos données de navigation", "Clear your browsing data"),
        ["tabCapture"] = ("Capturer le contenu de vos onglets", "Capture the content of your tabs"),
        ["desktopCapture"] = ("Capturer le contenu de votre écran", "Capture the content of your screen"),
        ["pageCapture"] = ("Enregistrer les pages que vous visitez", "Save the pages you visit"),
        ["identity"] = ("Connaître votre adresse e-mail", "Know your email address"),
        ["identity.email"] = ("Connaître votre adresse e-mail", "Know your email address"),
        ["ttsEngine"] = ("Lire tout texte prononcé par la synthèse vocale", "Read all text spoken using synthesized speech"),
        ["printerProvider"] = ("Recevoir les documents que vous imprimez", "Receive the documents you print"),
        ["vpnProvider"] = ("Faire passer votre connexion par un autre serveur", "Route your connection through another server"),
    };

    /// <param name="permissions">Permissions, accès aux sites et adresses des scripts de contenu du manifeste.</param>
    public static IReadOnlyList<string> Describe(IEnumerable<string>? permissions, bool french)
    {
        var lines = new List<string>();
        var hosts = new List<string>();
        var everywhere = false;
        foreach (var permission in permissions ?? [])
        {
            if (Silent.Contains(permission)) continue;
            if (Known.TryGetValue(permission, out var text)) lines.Add(french ? text.French : text.English);
            else if (permission == "<all_urls>" || permission.Contains("://")) // un motif d'adresses : schéma://hôte/chemin
            {
                var host = permission.Contains("://") ? permission[(permission.IndexOf("://", StringComparison.Ordinal) + 3)..].Split('/')[0] : "*";
                if (host is "*" or "" || permission.StartsWith("file:", StringComparison.Ordinal)) everywhere = true;
                else hosts.Add(host.StartsWith("*.", StringComparison.Ordinal) ? host[2..] : host);
            }
            else lines.Add(permission);
        }
        if (everywhere) lines.Insert(0, french ? "Lire et modifier toutes vos données sur tous les sites web" : "Read and change all your data on all websites");
        else if (hosts.Count > 0)
        {
            var named = hosts.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var list = string.Join(", ", named.Take(4)) + (named.Count > 4 ? (french ? $" et {named.Count - 4} autres sites" : $" and {named.Count - 4} other sites") : "");
            lines.Insert(0, (french ? "Lire et modifier vos données sur " : "Read and change your data on ") + list);
        }
        return lines.Distinct().ToList();
    }
}
