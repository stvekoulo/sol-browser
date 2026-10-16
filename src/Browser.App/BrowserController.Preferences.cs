using System.Runtime.InteropServices;
using System.Text.Json;
using Browser.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace Browser.App;

public sealed partial class BrowserController
{

    private void UpdateDock()
    {
        var shown = _settings.FloatingDownloads
            ? _downloads.Where(d => DownloadPending(d.Id,d.Op)
                || (_finishedAt.TryGetValue(d.Id, out var at) && DateTime.UtcNow - at < DockLinger)).ToList()
            : [];
        var changed = _dockShown != shown.Count > 0;
        _dockShown = shown.Count > 0;
        // Mise à jour de Sol en téléchargement ou prête : l'encart l'annonce, jusqu'à « Plus tard ».
        var update = _updateDismissed ? null : Program.Update;
        if (_dockShown || update is not null)
        {
            var content = System.Text.Json.Nodes.JsonNode.Parse(DownloadsJson(shown))!;
            content["update"] = update is null ? null : JsonSerializer.SerializeToNode(new { version = update.Version, percent = update.Percent, ready = update.Ready });
            _dock.Post(content.ToJsonString()); // l'encart répond par sa taille, ce qui l'affiche
        }
        else
        {
            _dock.Hide();
        }
        if (changed) Refresh(); // le bouton de la barre apparaît quand l'encart disparaît, et inversement
    }

    private string DownloadsJson(IEnumerable<(string Id, Microsoft.Web.WebView2.Core.CoreWebView2DownloadOperation Op, string TabId)> downloads) => JsonSerializer.Serialize(new
    {
        popup = "downloads",
        items = downloads.Select(d => new
        {
            id = d.Id,
            name = Path.GetFileName(d.Op.ResultFilePath),
            received = d.Op.BytesReceived,
            total = d.Op.TotalBytesToReceive>0?(long?)d.Op.TotalBytesToReceive:null,
            state = d.Op.State.ToString(),
            paused=DownloadPaused(d.Id,d.Op),canceled=_canceledDownloads.Contains(d.Id)||d.Op.State==Microsoft.Web.WebView2.Core.CoreWebView2DownloadState.Interrupted&&d.Op.InterruptReason==Microsoft.Web.WebView2.Core.CoreWebView2DownloadInterruptReason.UserCanceled&&!DownloadPaused(d.Id,d.Op),
            canResume=!_canceledDownloads.Contains(d.Id)&&d.Op.CanResume,reason=d.Op.InterruptReason.ToString(),
        }),
    });

    private void OpenDownloads(double x, double y)
    {
        if (ClosesPanel("downloads")) return;
        const double width = 420;
        HidePopup();
        _downloadsOpen = true;
        _popup.Show(x - Scaled(width), y, width, DownloadsJson(_downloads), focus: true, above: true); // depuis le bas de la carte latérale
    }

    private string _siteData = "idle";
    private (string Id, string Origin, string Session)? _siteReset;
    private bool _resettingSite;
    private void AskResetSite(string id)
    {
        if (_resettingSite || _tabs.Tabs.FirstOrDefault(t => t.Id == id) is not { } tab ||
            _pages.CoreOf(id) is not { } core || PermissionStore.OriginOf(core.Source) is not { } origin || core.Source.StartsWith("https://start.sol/")) return;
        HidePopup();
        _siteReset = (id, origin, tab.SessionId);
        _popup.ShowCentered(390, JsonSerializer.Serialize(new { popup = "site-reset", site = new Uri(origin).Host,
            session = tab.SessionId.Length == 0 ? T("Session actuelle", "Current session") : _sessions.Name(tab.SessionId) }));
    }
    private async Task ResetSiteAsync()
    {
        if (_resettingSite || _siteReset is not var (id, origin, session)) return;
        _siteReset = null;
        if (!_popup.IsOpen || _tabs.Tabs.FirstOrDefault(t => t.Id == id)?.SessionId != session || PermissionStore.OriginOf(_pages.CoreOf(id)?.Source ?? "") != origin) return;
        if (_downloads.Any(d => d.TabId == id && DownloadPending(d.Id,d.Op)))
        { Notice(T("Téléchargement en cours", "Download in progress"), T("Attendez sa fin avant de vider les données de ce site.", "Wait for it to finish before clearing this site's data.")); return; }
        _resettingSite = true;
        HidePopup();
        try
        {
            if (!PermissionFor(session).Forget(origin) || !await _pages.ResetSiteAsync(id, origin))
                if (!_closingWindow) Notice(T("Effacement incomplet", "Incomplete clearing"), T("Certaines données n’ont pas pu être effacées. Réessayez sur ce site.", "Some data could not be cleared. Retry on this site."));
        }
        finally { _resettingSite = false; }
    }
    private async Task ClearSiteDataAsync()
    {
        if (_siteData == "clearing") return;
        _siteData = "clearing";
        _popup.Post(SettingsJson());
        _siteData = _tabs.ActiveId is { } active && await _pages.ClearSiteDataAsync(active) ? "done" : "failed";
        if (_settingsOpen && !_closingWindow) _popup.Post(SettingsJson());
    }

    // Panneau de réglages : des interrupteurs, appliqués tout de suite et enregistrés.
    private bool _settingsOpen;
    public event Action? InterfaceScaleChanged;

    private string SettingsJson() => JsonSerializer.Serialize(new
    {
        popup = "settings",
        ai = _ai.State(_settings.SolAiEnabled, _settings.SolAiModel),
        aiPreferences = new { tone = _settings.SolAiTone, length = _settings.SolAiLength, thinking = _settings.SolAiThinking, keepMinutes = _settings.SolAiKeepMinutes, adaptation = _settings.SolAiAdaptation, context = AiNavigation(), isPrivate = AppPaths.Private },
        version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "",
        update = Program.Update is { } update ? new { version = update.Version, percent = update.Percent, ready = update.Ready } : null,
        check = Program.Check,
        zoom = (int)Math.Round(_settings.PageZoom * 100),
        uiScale = _settings.UiScale,
        theme = Math.Max(0, Array.IndexOf(Themes, _settings.Theme)),
        language = Math.Max(0, Array.IndexOf(Languages, _settings.Language)),
        spelling = _settings.Spelling,
        spellingLanguages = _pages.SpellingLanguages,
        shortcuts = BrowserShortcuts.All.GroupBy(s => s.Action).Select(g => new
        {
            action = g.Key, title = T(g.First().French, g.First().English),
            keys = string.Join(" / ", g.Select(s => s.Text(Language.Current == "en")).Distinct()),
        }),
        home = _settings.HomePage == Settings.DefaultHomePage ? "" : _settings.HomePage,
        font = Math.Max(0, Array.IndexOf(Fonts, _settings.Font)),
        // Fond du nouvel onglet : photo choisie (0 : image de Sol), ou une nouvelle chaque jour.
        wallpaperOn = _settings.StartWallpaper, wallpaper = _settings.StartWallpaperPhoto, wallpaperDaily = _settings.StartWallpaperDaily,
        wallpapers = StartWallpapers.All.Select(w => new { id = w.Id, thumb = w.ThumbUrl, author = w.Author }),
        engine = Math.Max(0, Array.FindIndex(Engines, e => e.Search == _settings.SearchUrlTemplate)),
        engines = Engines.Select(e => e.Name),
        // Réponses retenues, site par site : chacune peut être oubliée séparément.
        sites = _permissions.All.GroupBy(a => a.Origin).Select(g => new
        {
            origin = g.Key,
            host = Uri.TryCreate(g.Key, UriKind.Absolute, out var site) ? site.Host : g.Key,
            summary = string.Join(" · ", g.Select(a => T($"{KindName(a.Kind)} : {(a.Allowed ? "autorisé" : "refusé")}", $"{KindName(a.Kind)}: {(a.Allowed ? "allowed" : "denied")}"))),
        }),
        permissions = _permissions.Count, // réponses retenues aux demandes d'accès des sites
        siteData = _siteData,
        // Sous-menu : où la couleur du site teinte l'interface.
        tint = new[]
        {
            new { key = "tint.topbar", label = T("Barre du haut et boutons", "Top bar and buttons"), hint = "", on = _settings.TintTopbar },
            new { key = "tint.sidebar", label = T("Carte latérale", "Sidebar"), hint = "", on = _settings.TintSidebar },
            new { key = "tint.menus", label = T("Menus et panneaux", "Menus and panels"), hint = "", on = _settings.TintMenus },
            new { key = "tint.background", label = T("Arrière-plan de l'interface", "Interface background"), hint = T("Le fond de la barre et de la colonne prend une teinte légère", "The bar and sidebar background takes a light tint"), on = _settings.TintBackground },
            new { key = "tint.live", label = T("Couleurs en temps réel", "Live colours"), hint = T("La couleur suit ce qui est affiché : la vidéo en cours, la partie de la page à l'écran", "The colour follows what is shown: the playing video, the visible part of the page"), on = _settings.LiveColor },
        },
        items = new[]
        {
            new { key = "fullscreen", label = T("Plein écran du navigateur", "Browser full screen"), hint = T("Désactivez pour garder la barre des tâches et une fenêtre redimensionnable", "Turn off to keep the taskbar and a resizable window"), on = _settings.FullScreen },
            new { key = "startWallpaper", label = T("Image d'arrière-plan du nouvel onglet", "New tab background image"), hint = T("Affiche l'image de fond sur la page d'accueil ; sinon, suit le thème du navigateur", "Shows the background image on the start page; otherwise follows the browser theme"), on = _settings.StartWallpaper },
            new { key = "pip", label = T("Lecteur flottant", "Floating player"), hint = T("En quittant un onglet où une vidéo joue, elle reste à l'écran", "Leaving a tab with a playing video keeps it on screen"), on = _settings.AutoPip },
            new { key = "memory", label = T("Économie de mémoire", "Memory saver"), hint = MemoryHint(), on = _settings.MemorySaver },
            new { key = "scrollbars", label = T("Barre de défilement fine", "Thin scrollbar"), hint = T("Apparaît seulement pendant le défilement", "Only appears while scrolling"), on = !_settings.HideScrollbars },
            new { key = "blur", label = T("Flou progressif en bas des pages", "Progressive blur at the bottom of pages"), hint = "", on = _settings.BlurEnabled },
            new { key = "floatingDownloads", label = T("Téléchargements en bas à droite", "Downloads at the bottom right"), hint = T("Sinon, uniquement dans le panneau de la barre", "Otherwise, only in the toolbar panel"), on = _settings.FloatingDownloads },
            new { key = "onlineSuggestions", label = T("Suggestions de recherche", "Search suggestions"), hint = T("Envoie au moteur de recherche ce qui est tapé dans la barre d'adresse", "Sends what you type in the address bar to the search engine"), on = _settings.OnlineSuggestions },
            new { key = "passwords", label = T("Enregistrer les mots de passe", "Save passwords"), hint = T("Stockage chiffré du moteur, sur cet ordinateur", "Encrypted engine storage, on this computer"), on = _settings.SavePasswords },
        },
    });

    private void OpenSettings(double x, double y)
    {
        if (ClosesPanel("settings")) return;
        const double width = 420;
        HidePopup();
        _settingsOpen = true;
        _popup.Show(x - Scaled(width), y, width, SettingsJson(), focus: true);
    }

    private static readonly string[] Themes = ["page", "light", "dark", "system"];

    private void SetTheme(int index)
    {
        if (index < 0 || index >= Themes.Length) return;
        _settings.Theme = Themes[index];
        JsonFileStore.Save(AppPaths.Settings, _settings);
        _pages.ApplySettings(); // les sites reçoivent aussi la préférence clair ou sombre
        _bridge.BroadcastTheme();
        if (_settingsOpen) _popup.Post(SettingsJson());
    }

    private static string KindName(string kind) => kind switch
    {
        "Microphone" => T("Micro", "Microphone"), "Camera" => T("Caméra", "Camera"), "Geolocation" => T("Position", "Location"),
        "ClipboardRead" => T("Presse-papiers", "Clipboard"), "MultipleAutomaticDownloads" => T("Téléchargements multiples", "Multiple downloads"),
        "FileReadWrite" => T("Fichiers", "Files"), "OtherSensors" => T("Capteurs", "Sensors"),
        _ => kind,
    };

    // Moteurs de recherche proposés : adresse de recherche, et adresse des complétions (même format de réponse pour les trois).
    private static readonly (string Name, string Search, string Suggest)[] Engines =
    [
        ("Google", "https://www.google.com/search?q={0}", "https://suggestqueries.google.com/complete/search?client=firefox&ie=utf-8&oe=utf-8&q={0}"),
        ("DuckDuckGo", "https://duckduckgo.com/?q={0}", "https://duckduckgo.com/ac/?q={0}&type=list"),
        ("Bing", "https://www.bing.com/search?q={0}", "https://api.bing.com/osjson.aspx?query={0}"),
    ];

    private void SetEngine(int index)
    {
        if (index < 0 || index >= Engines.Length) return;
        (_settings.SearchUrlTemplate, _settings.SuggestUrlTemplate) = (Engines[index].Search, Engines[index].Suggest);
        JsonFileStore.Save(AppPaths.Settings, _settings);
        if (_settingsOpen) _popup.Post(SettingsJson());
    }
}
