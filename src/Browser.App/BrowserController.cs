using System.Runtime.InteropServices;
using System.Text.Json;
using Browser.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace Browser.App;

/// <summary>Exécute les commandes de l'interface sur Browser.Core et les vues de pages, puis renvoie l'état.</summary>
public sealed class BrowserController
{
    private readonly Window _window;
    private readonly PageHost _pages;
    private readonly UiBridge _bridge;
    private readonly PopupHost _popup;
    private readonly Settings _settings;
    private readonly BrowserDb _db;
    private readonly CaptureService _capture;
    private readonly TabManager _tabs = new();
    private readonly SplitLayout _split = new();
    private readonly HashSet<string> _restored = new(); // onglets dont le premier chargement vient de la session
    private readonly Dictionary<string, string?> _favicons = new();
    private DateTime _libraryClosedAt;
    private IReadOnlyList<Suggestion> _suggestions = [];
    private string? _suggestTab;
    private bool _suggesting; // la vue posée sur la page affiche des suggestions

    private readonly DockHost _dock;

    private readonly PanelHost _tools; // panneau du bas : outils de développement
    private readonly NetworkInspector _network;

    public BrowserController(Window window, PageHost pages, UiBridge bridge, PopupHost popup, DockHost dock, PanelHost panel, Settings settings)
    {
        (_window, _pages, _bridge, _popup, _dock, _tools, _settings) = (window, pages, bridge, popup, dock, panel, settings);
        _network = new NetworkInspector(panel.Post);
        Directory.CreateDirectory(AppPaths.DataDir);
        _db = new BrowserDb(AppPaths.Database);
        _capture = new CaptureService(window);

        _pages.PageChanged += (id, url, title) => { _tabs.Update(id, url, title); Refresh(); };
        _pages.RuntimeChanged += Refresh;
        _pages.NewTabRequested += url => { Open(url); Refresh(); };
        _pages.Loaded += RecordVisit;
        _pages.Loaded += id => _ = LearnShortcutNameAsync(id);
        _pages.FaviconChanged += (id, data) => { _favicons[id] = data; PostFavicon(id); LearnShortcutIcon(id, data); };
        _pages.ContextMenuRequested += OpenContextMenu;
        _pages.DeviceAreaMenuRequested += (tabId, x, y) => ShowMenu(x, y, DeviceEntries(tabId, x, y));
        _pages.StatusChanged += (id, text) =>
        {
            if (id == _tabs.ActiveId || _split.Contains(id))
                _bridge.Post(JsonSerializer.Serialize(new { @event = "status", text }));
        };
        _pages.DownloadStarting += OnDownloadStarting;
        _pages.PermissionRequested += OnPermissionRequested;
        _window.AppWindow.Changed += (_, e) => { if (e.DidPresenterChange || e.DidSizeChange) Refresh(); };
        _window.Closed += (_, _) => { SaveSession(); _db.Dispose(); };
        ApplyComparison();
    }

    private static string T(string fr, string en) => Language.T(fr, en);
    private static string ProfileName(string id) => id == Profiles.Default ? T("Défaut", "Default") : Profiles.NameOf(AppPaths.BaseDir, id);

    public void Execute(Command c)
    {
        var id = c.Id ?? "";
        if (c.Type is not ("popup.size" or "dock.size" or "split.resize")) DebugLog.Write($"commande {c.Type} {c.Id} {c.Index}");
        switch (c.Type)
        {
            // Vue posée sur la page : ces commandes ne changent pas l'état des onglets.
            case "suggest": Suggest(id, c.Query!, c.X!.Value, c.Y!.Value, c.Width!.Value); return;
            case "library.open": OpenLibrary(c.X!.Value, c.Y!.Value); return;
            case "downloads.open": OpenDownloads(c.X!.Value, c.Y!.Value); return;
            case "download.cancel": FindDownload(id)?.Cancel(); return; // le panneau reste ouvert
            case "download.open" or "download.reveal":
                if (FindDownload(id) is { State: Microsoft.Web.WebView2.Core.CoreWebView2DownloadState.Completed } done && File.Exists(done.ResultFilePath))
                {
                    // Ouvrir un fichier téléchargé l'exécute avec son application : uniquement sur clic de l'utilisateur.
                    var start = c.Type == "download.open"
                        ? new System.Diagnostics.ProcessStartInfo(done.ResultFilePath) { UseShellExecute = true }
                        : new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{done.ResultFilePath}\"");
                    try { System.Diagnostics.Process.Start(start); }
                    catch (System.ComponentModel.Win32Exception) { /* aucune application associée */ }
                }
                HidePopup();
                return;
            case "popup.size":
                _popup.SetHeight(c.Height!.Value);
                if (_panelNext is not null) (_panel, _panelNext) = (_panelNext, null); // le panneau demandé est maintenant affiché
                return;
            case "popup.close":
                if (!_suggesting) _libraryClosedAt = DateTime.UtcNow;
                HidePopup();
                return;
            case "suggest.select":
                if (_suggesting) _popup.Post(JsonSerializer.Serialize(new { select = c.Index }));
                return;
            case "menu.pick": PickMenuItem(c.Index!.Value); return;
            case "settings.open": OpenSettings(c.X!.Value, c.Y!.Value); return;
            case "settings.set":
                if (id == "theme") SetTheme(c.Index!.Value);
                else if (id == "language") SetLanguage(c.Index!.Value);
                else if (id == "engine") SetEngine(c.Index!.Value);
                else SetSetting(id, c.Index != 0);
                return;
            case "settings.text": if (id == "home") SetHomePage(c.Input!); return;
            case "dock.size": if (_dockShown) _dock.SetSize(c.Width!.Value, c.Height!.Value); return;
            case "shortcut.menu": OpenShortcutMenu(c.Index!.Value, c.X!.Value, c.Y!.Value); return;
            case "shortcut.update": UpdateShortcut(id, c.Title!, c.Input!); break;
            case "shortcut.open": OpenShortcut(c.Index!.Value); break;
            case "network.toggle": ToggleNetwork(); return;
            case "network.ready": _network.Post(); return;
            case "network.clear": if (_network.Shown is { } listed) _network.Clear(listed); return;
            case "network.detail":
                if (_network.Shown is { } inspected && _pages.CoreOf(inspected) is { } inspectedCore) _ = _network.PostDetailAsync(inspected, inspectedCore, id);
                return;
            case "device.compare": SetComparison(id, c.Query!); return;
            case "device.rotate": _pages.RotateDevice(id); return;
            case "device.set": _pages.SetDevice(id, c.Query!.Length == 0 ? null : c.Query); HidePopup(); return;
            case "tab.menu": OpenTabMenu(id, c.X!.Value, c.Y!.Value); return;
            case "permission.answer": AnswerPermission(c.Index != 0); return;
            case "permissions.forget": _permissions.Forget(id); if (_settingsOpen) _popup.Post(SettingsJson()); return;
            case "permissions.clear": _permissions.Clear(); if (_settingsOpen) _popup.Post(SettingsJson()); return;
            case "profile.menu": OpenProfileMenu(c.X!.Value, c.Y!.Value); return;
            case "profile.new": AskProfileName(); return;
            case "profile.create": SwitchProfile(Profiles.Create(AppPaths.BaseDir, c.Title!)); return;
            case "profile.edit":
                if (id != Profiles.Default && Profiles.List(AppPaths.BaseDir).Contains(id))
                    AskName(T("Renommer le profil", "Rename profile"), "", T("Nom du profil", "Profile name"), T("Renommer", "Rename"), Profiles.NameOf(AppPaths.BaseDir, id), "profile.rename", id);
                return;
            case "profile.rename":
                if (Profiles.Rename(AppPaths.BaseDir, id, c.Title!) && id == AppPaths.Profile) _window.Title = $"Navigateur — {ProfileName(id)}";
                break;
            case "profile.switch": SwitchProfile(id); return;
            case "profile.delete": DeleteProfile(id); return;
            case "capture.menu": OpenCaptureMenu(c.X!.Value, c.Y!.Value); return;
            case "window.close": _window.Close(); return;
            case "window.drag": BeginDrag(); return;

            case "suggest.pick":
                if (_suggestTab is not null && c.Index is int i && i >= 0 && i < _suggestions.Count)
                    _pages.Navigate(_suggestTab, _suggestions[i].Url);
                break;
            case "ui.ready":
                if (_tabs.Tabs.Count == 0)
                {
                    RestoreSession();
                    foreach (var shortcut in _settings.Shortcuts.Where(s => s.Icon is null)) _ = FetchShortcutIconAsync(shortcut);
                }
                foreach (var tabId in _favicons.Keys) PostFavicon(tabId);
                break;
            case "tab.open": Open(c.Url is null ? _settings.HomePage : Resolve(c.Url)); break;
            case "tab.close": Close(id); break;
            case "tab.activate": Activate(id); break;
            case "group.new": EditGroup(tabId: id, group: null); return;
            // query = couleur « #rrggbb », input = icône ; une valeur invalide est ignorée par TabManager.
            case "group.create": _tabs.CreateGroup(id, c.Title!, c.Query, icon: c.Input); break;
            case "group.update": _tabs.UpdateGroup(id, name: c.Title!, color: c.Query, icon: c.Input); break;
            case "group.add": _tabs.AddToGroup(id, c.Query!); KeepPairAdjacent(); break;
            case "group.remove": _tabs.RemoveFromGroup(id); KeepPairAdjacent(); break;
            case "group.toggle":
                if (_tabs.Groups.FirstOrDefault(g => g.Id == id) is { } toggled) _tabs.UpdateGroup(id, collapsed: !toggled.Collapsed);
                break;
            case "group.menu": OpenGroupMenu(id, c.X!.Value, c.Y!.Value); return;
            case "tab.move":
                if (_tabs.Move(id, c.Index!.Value)) KeepPairAdjacent();
                break;
            case "tab.reopen": if (_tabs.ReopenClosed() is { } reopened) _pages.Add(reopened); break;
            case "nav.go": _pages.Navigate(id, Resolve(c.Input!)); break;
            case "nav.back": _pages.Back(id); break;
            case "nav.forward": _pages.Forward(id); break;
            case "nav.reload": _pages.Reload(id); break;
            case "nav.stop": _pages.Stop(id); break;
            case "split.open": Split(c.Id); break;
            case "split.close": CloseSplit(); break;
            case "split.resize": _split.Resize(c.Ratio!.Value); break;
            case "bookmark.add": if (IsWeb(c.Url!)) _db.AddBookmark(c.Url!, c.Title!); break;
            case "bookmark.remove": _db.RemoveBookmark(c.Url!); break;
            case "zoom.in" or "zoom.out" or "zoom.reset": Zoom(c.Id ?? _tabs.ActiveId, c.Type); return;
            case "window.minimize": Presenter?.Minimize(); break;
            case "window.maximize":
                if (Presenter?.State == OverlappedPresenterState.Maximized) Presenter.Restore(); else Presenter?.Maximize();
                break;
        }
        HidePopup();
        Refresh();
    }

    // Téléchargements de la session, du plus récent au plus ancien.
    private readonly List<(string Id, Microsoft.Web.WebView2.Core.CoreWebView2DownloadOperation Op)> _downloads = new();
    private bool _downloadsOpen;
    private DateTime _downloadsPosted;

    private Microsoft.Web.WebView2.Core.CoreWebView2DownloadOperation? FindDownload(string id) =>
        _downloads.FirstOrDefault(d => d.Id == id).Op;

    private void OnDownloadStarting(string tabId, Microsoft.Web.WebView2.Core.CoreWebView2DownloadStartingEventArgs e)
    {
        e.Handled = true; // pas de fenêtre de téléchargement du moteur : le navigateur affiche la sienne
        var op = e.DownloadOperation;
        _downloads.Insert(0, ($"d{_downloads.Count + 1}", op));
        var id = _downloads[0].Id;
        op.BytesReceivedChanged += (_, _) => DownloadsChanged(throttle: true);
        op.StateChanged += async (_, _) =>
        {
            _finishedAt[id] = DateTime.UtcNow;
            // Un onglet ouvert seulement pour ce téléchargement n'a rien à afficher : il se referme une fois le fichier arrivé.
            // Pas avant : fermer la vue d'origine interrompt le téléchargement (constaté).
            if (op.State != Microsoft.Web.WebView2.Core.CoreWebView2DownloadState.InProgress
                && _tabs.Tabs.Count > 1 && _pages.CoreOf(tabId) is { CanGoBack: false, Source: "about:blank" })
                Close(tabId);
            DownloadsChanged(throttle: false);
            await Task.Delay(DockLinger + TimeSpan.FromMilliseconds(200));
            UpdateDock(); // le fichier terminé quitte l'encart
        };
        DownloadsChanged(throttle: false);
    }

    private void DownloadsChanged(bool throttle)
    {
        // La progression arrive très souvent : quatre mises à jour par seconde suffisent.
        if (throttle && (DateTime.UtcNow - _downloadsPosted).TotalMilliseconds < 250) return;
        _downloadsPosted = DateTime.UtcNow;
        Refresh();
        if (_downloadsOpen) _popup.Post(DownloadsJson(_downloads));
        UpdateDock();
    }

    // Encart en bas à droite : les téléchargements en cours, et ceux qui viennent de finir pendant quelques secondes.
    private static readonly TimeSpan DockLinger = TimeSpan.FromSeconds(6);
    private readonly Dictionary<string, DateTime> _finishedAt = new();
    private bool _dockShown;

    private void UpdateDock()
    {
        var shown = _settings.FloatingDownloads
            ? _downloads.Where(d => d.Op.State == Microsoft.Web.WebView2.Core.CoreWebView2DownloadState.InProgress
                || (_finishedAt.TryGetValue(d.Id, out var at) && DateTime.UtcNow - at < DockLinger)).ToList()
            : [];
        var changed = _dockShown != shown.Count > 0;
        _dockShown = shown.Count > 0;
        if (_dockShown) _dock.Post(DownloadsJson(shown)); // l'encart répond par sa taille, ce qui l'affiche
        else _dock.Hide();
        if (changed) Refresh(); // le bouton de la barre apparaît quand l'encart disparaît, et inversement
    }

    private static string DownloadsJson(IEnumerable<(string Id, Microsoft.Web.WebView2.Core.CoreWebView2DownloadOperation Op)> downloads) => JsonSerializer.Serialize(new
    {
        popup = "downloads",
        items = downloads.Select(d => new
        {
            id = d.Id,
            name = Path.GetFileName(d.Op.ResultFilePath),
            received = d.Op.BytesReceived,
            total = d.Op.TotalBytesToReceive,
            state = d.Op.State.ToString(),
        }),
    });

    private void OpenDownloads(double x, double y)
    {
        if (ClosesPanel("downloads")) return;
        const double width = 340;
        HidePopup();
        _downloadsOpen = true;
        _popup.Show(x - width, y, width, DownloadsJson(_downloads), focus: true);
    }

    // Demandes d'accès des sites. Une réponse est retenue pour ce site et ce type d'accès ; fermer la question
    // sans répondre refuse pour cette fois seulement.
    private readonly PermissionStore _permissions = new(AppPaths.Permissions);
    private (Microsoft.Web.WebView2.Core.CoreWebView2PermissionRequestedEventArgs Args, Windows.Foundation.Deferral Deferral, string Origin)? _asking;

    private static string PermissionLabel(Microsoft.Web.WebView2.Core.CoreWebView2PermissionKind kind) => kind switch
    {
        Microsoft.Web.WebView2.Core.CoreWebView2PermissionKind.Microphone => T("utiliser votre micro", "use your microphone"),
        Microsoft.Web.WebView2.Core.CoreWebView2PermissionKind.Camera => T("utiliser votre caméra", "use your camera"),
        Microsoft.Web.WebView2.Core.CoreWebView2PermissionKind.Geolocation => T("connaître votre position", "know your location"),
        Microsoft.Web.WebView2.Core.CoreWebView2PermissionKind.Notifications => T("vous envoyer des notifications", "send you notifications"),
        Microsoft.Web.WebView2.Core.CoreWebView2PermissionKind.ClipboardRead => T("lire le contenu du presse-papiers", "read the clipboard"),
        Microsoft.Web.WebView2.Core.CoreWebView2PermissionKind.MultipleAutomaticDownloads => T("télécharger plusieurs fichiers à la suite", "download several files in a row"),
        Microsoft.Web.WebView2.Core.CoreWebView2PermissionKind.FileReadWrite => T("lire et modifier des fichiers de cet ordinateur", "read and change files on this computer"),
        Microsoft.Web.WebView2.Core.CoreWebView2PermissionKind.OtherSensors => T("lire les capteurs de mouvement", "read motion sensors"),
        _ => T($"utiliser une fonction sensible ({kind})", $"use a sensitive feature ({kind})"),
    };

    private void OnPermissionRequested(Microsoft.Web.WebView2.Core.CoreWebView2PermissionRequestedEventArgs e)
    {
        e.SavesInProfile = false; // les réponses sont gardées par le navigateur, pas par le moteur
        var origin = PermissionStore.OriginOf(e.Uri);
        // Pas de site identifiable, ou une question déjà affichée : refus, sans rien retenir.
        if (origin is null || _asking is not null)
        {
            e.State = Microsoft.Web.WebView2.Core.CoreWebView2PermissionState.Deny;
            return;
        }
        if (_permissions.Get(origin, e.PermissionKind.ToString()) is { } known)
        {
            e.State = known ? Microsoft.Web.WebView2.Core.CoreWebView2PermissionState.Allow : Microsoft.Web.WebView2.Core.CoreWebView2PermissionState.Deny;
            return;
        }

        HidePopup();
        _asking = (e, e.GetDeferral(), origin);
        _popup.ShowCentered(360, JsonSerializer.Serialize(new
        {
            popup = "permission",
            site = new Uri(origin).Host,
            what = PermissionLabel(e.PermissionKind),
            secure = origin.StartsWith("https://", StringComparison.Ordinal),
        }), top: 70);
    }

    private void AnswerPermission(bool allow)
    {
        if (_asking is not var (args, deferral, origin)) return;
        _asking = null;
        _permissions.Set(origin, args.PermissionKind.ToString(), allow);
        args.State = allow ? Microsoft.Web.WebView2.Core.CoreWebView2PermissionState.Allow : Microsoft.Web.WebView2.Core.CoreWebView2PermissionState.Deny;
        deferral.Complete();
        HidePopup();
    }

    // Panneau de réglages : des interrupteurs, appliqués tout de suite et enregistrés.
    private bool _settingsOpen;

    private string SettingsJson() => JsonSerializer.Serialize(new
    {
        popup = "settings",
        zoom = _tabs.ActiveId is { } activeId ? (int)Math.Round(_pages.ZoomOf(activeId) * 100) : 100,
        theme = Math.Max(0, Array.IndexOf(Themes, _settings.Theme)),
        language = Math.Max(0, Array.IndexOf(Languages, _settings.Language)),
        home = _settings.HomePage,
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
        // Sous-menu : où la couleur du site teinte l'interface.
        tint = new[]
        {
            new { key = "tint.topbar", label = T("Barre du haut et boutons", "Top bar and buttons"), hint = "", on = _settings.TintTopbar },
            new { key = "tint.sidebar", label = T("Carte latérale", "Sidebar"), hint = "", on = _settings.TintSidebar },
            new { key = "tint.menus", label = T("Menus et panneaux", "Menus and panels"), hint = "", on = _settings.TintMenus },
            new { key = "tint.background", label = T("Arrière-plan de l'interface", "Interface background"), hint = T("Le fond de la barre et de la colonne prend une teinte légère", "The bar and sidebar background takes a light tint"), on = _settings.TintBackground },
        },
        items = new[]
        {
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
        const double width = 340;
        HidePopup();
        _settingsOpen = true;
        _popup.Show(x - width, y, width, SettingsJson(), focus: true);
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

    private static readonly string[] Languages = ["system", "fr", "en"];

    private void SetLanguage(int index)
    {
        if (index < 0 || index >= Languages.Length) return;
        _settings.Language = Languages[index];
        JsonFileStore.Save(AppPaths.Settings, _settings);
        Language.Current = Language.Resolve(_settings.Language, System.Globalization.CultureInfo.CurrentUICulture.Name);
        _bridge.BroadcastTheme(); // la langue voyage avec le thème : toutes les vues la reçoivent
        Refresh();
        if (_settingsOpen) _popup.Post(SettingsJson());
    }

    // Page d'accueil : une adresse web. Champ vidé, ou autre chose qu'une adresse web : retour à la page par défaut.
    private void SetHomePage(string input)
    {
        var url = input.Trim().Length == 0 ? "" : Resolve(input);
        _settings.HomePage = IsWeb(url) ? url : Settings.DefaultHomePage;
        JsonFileStore.Save(AppPaths.Settings, _settings);
        if (_settingsOpen) _popup.Post(SettingsJson());
    }

    private void SetSetting(string key, bool on)
    {
        switch (key)
        {
            case "scrollbars": _settings.HideScrollbars = !on; break;
            case "blur": _settings.BlurEnabled = on; break;
            case "floatingDownloads": _settings.FloatingDownloads = on; break;
            case "onlineSuggestions": _settings.OnlineSuggestions = on; break;
            case "passwords": _settings.SavePasswords = on; break;
            case "tint.topbar": _settings.TintTopbar = on; break;
            case "tint.sidebar": _settings.TintSidebar = on; break;
            case "tint.menus": _settings.TintMenus = on; break;
            case "tint.background": _settings.TintBackground = on; break;
            default: return;
        }
        JsonFileStore.Save(AppPaths.Settings, _settings);
        _pages.ApplySettings();
        _bridge.BroadcastTheme();
        UpdateDock();
        Refresh();
        if (_settingsOpen) _popup.Post(SettingsJson());
    }

    // Panneau ouvert par un bouton de la barre : recliquer ce bouton le referme.
    private string? _panel, _panelNext, _panelLast;

    private bool ClosesPanel(string name)
    {
        // Ouvert : on le ferme. Ou bien ce même clic vient de le fermer en lui faisant perdre le curseur : on ne le rouvre pas.
        var open = _panel == name && _popup.IsOpen;
        var justClosed = _panelLast == name && DateTime.UtcNow - _libraryClosedAt < TimeSpan.FromMilliseconds(350);
        if (open) HidePopup();
        if (open || justClosed) return true;
        _panelNext = name;
        return false;
    }

    private void HidePopup()
    {
        if (_panel is not null) _panelLast = _panel;
        _panel = null;
        _suggesting = false;
        _downloadsOpen = false;
        _settingsOpen = false;
        _profilesOpen = false;
        // Question d'accès fermée sans réponse : refus pour cette fois, rien n'est retenu.
        if (_asking is var (pendingArgs, pendingDeferral, _))
        {
            _asking = null;
            pendingArgs.State = Microsoft.Web.WebView2.Core.CoreWebView2PermissionState.Deny;
            pendingDeferral.Complete();
        }
        var dismissed = _menuDismissed;
        (_menu, _menuDismissed) = (null, null);
        dismissed?.Invoke();
        _popup.Hide();
    }

    /// <summary>Raccourci clavier : agit sur l'onglet actif.</summary>
    public void Shortcut(string action)
    {
        var active = _tabs.ActiveId;
        var index = _tabs.Tabs.ToList().FindIndex(t => t.Id == active);
        switch (action)
        {
            case "tab.next" or "tab.previous" when _tabs.Tabs.Count > 0:
                var step = action == "tab.next" ? 1 : _tabs.Tabs.Count - 1;
                Execute(new Command("tab.activate", Id: _tabs.Tabs[(index + step) % _tabs.Tabs.Count].Id));
                break;
            case "address.focus": _bridge.Post("""{"event":"focus-address"}"""); break;
            case "tab.open" or "tab.reopen": Execute(new Command(action)); break;
            default: if (active is not null) Execute(new Command(action, Id: active)); break;
        }
    }

    // Crans de zoom, comme dans les navigateurs courants.
    private static readonly double[] ZoomSteps = [0.5, 0.67, 0.75, 0.8, 0.9, 1, 1.1, 1.25, 1.5, 1.75, 2];

    private void Zoom(string? id, string action)
    {
        if (id is null) return;
        var current = _pages.ZoomOf(id);
        var zoom = action switch
        {
            "zoom.in" => ZoomSteps.FirstOrDefault(z => z > current + 0.001, ZoomSteps[^1]),
            "zoom.out" => ZoomSteps.LastOrDefault(z => z < current - 0.001, ZoomSteps[0]),
            _ => 1,
        };
        _pages.SetZoom(id, zoom); // l'état est renvoyé à l'interface par RuntimeChanged
        if (_settingsOpen) _popup.Post(SettingsJson());
    }

    private OverlappedPresenter? Presenter => _window.AppWindow.Presenter as OverlappedPresenter;
    private string Resolve(string input) => AddressInput.Resolve(input, _settings.SearchUrlTemplate);
    private static bool IsWeb(string url) =>
        url.StartsWith("https://", StringComparison.Ordinal) || url.StartsWith("http://", StringComparison.Ordinal);

    private Tab Open(string url, bool activate = true, bool pinned = false, bool asleep = false)
    {
        var tab = _tabs.Open(url, activate, pinned);
        if (asleep) _pages.AddAsleep(tab);
        else _pages.Add(tab);
        return tab;
    }

    private void Close(string id)
    {
        if (!_tabs.Close(id)) return;
        _pages.Remove(id);
        _favicons.Remove(id);
        _split.OnTabClosed(id);
        _network.Forget(id);
        foreach (var shortcut in _shortcutTabs.Where(p => p.Value == id).Select(p => p.Key).ToList()) _shortcutTabs.Remove(shortcut);
        // Dernier onglet fermé : la fenêtre reste ouverte sur un onglet vide. Elle ne se ferme que par son bouton.
        if (_tabs.Tabs.Count == 0) Open(_settings.HomePage);
    }

    // Onglet vide ouvert seulement pour remplir la moitié droite d'une vue scindée.
    private string? _splitBlankTab;

    private void CloseSplit()
    {
        var blank = _splitBlankTab;
        _splitBlankTab = null;
        _split.Close();
        // Resté vide : il n'a servi à rien, il repart avec la vue scindée.
        if (blank is not null && _tabs.Tabs.Count > 1 && _tabs.Tabs.FirstOrDefault(t => t.Id == blank) is { Url: "about:blank" or "" })
            Close(blank);
    }

    // Scinde l'onglet actif avec l'onglet donné, ou avec un nouvel onglet si aucun n'est donné.
    private void Split(string? otherId)
    {
        if (_tabs.ActiveId is not { } active) return;
        if (otherId is not null && (otherId == active || _tabs.Tabs.All(t => t.Id != otherId))) return;
        var other = otherId ?? Open("about:blank", activate: false).Id;
        _splitBlankTab = otherId is null ? other : null;

        _split.Open(active, other);
        KeepPairAdjacent();
    }

    // Les deux onglets d'une vue scindée restent voisins dans la barre d'onglets, le second juste après le premier.
    private void KeepPairAdjacent()
    {
        if (!_split.IsSplit) return;
        var tabs = _tabs.Tabs.ToList();
        int p = tabs.FindIndex(t => t.Id == _split.PrimaryId), s = tabs.FindIndex(t => t.Id == _split.SecondaryId);
        if (s != p + 1) _tabs.Move(_split.SecondaryId!, s > p ? p + 1 : p);
        _tabs.MatchGroup(_split.SecondaryId!, _split.PrimaryId!);
    }

    private void PostFavicon(string id) =>
        _bridge.Post(JsonSerializer.Serialize(new { @event = "favicon", id, data = _favicons.GetValueOrDefault(id) }));

    private void Suggest(string tabId, string query, double x, double y, double width)
    {
        var q = query.Trim();
        var seq = ++_suggestSeq;
        _suggestTab = tabId;
        IReadOnlyList<Visit> history = q.Length == 0 ? [] : _db.Suggest(q, 3);

        // Une requête vide (champ vidé ou quitté) ne referme que les suggestions, pas un autre panneau ouvert entre-temps.
        // Pas tout de suite : cliquer une suggestion fait d'abord perdre le curseur à la barre d'adresse, et une
        // fermeture immédiate avalerait ce clic. Si rien n'est venu entre-temps, les suggestions se referment.
        if (q.Length == 0)
        {
            if (_suggesting) _ = CloseSuggestionsSoonAsync();
            return;

            async Task CloseSuggestionsSoonAsync()
            {
                await Task.Delay(180);
                if (seq == _suggestSeq && _suggesting) HidePopup();
            }
        }
        ShowSuggestions(Suggestions.Build(q, history, _settings.SearchUrlTemplate));

        // Les résultats locaux s'affichent tout de suite ; les complétions du moteur de recherche s'y ajoutent à leur arrivée.
        // Ce qui ressemble à une adresse (avec « / » ou « : ») n'est pas envoyé.
        if (_settings.OnlineSuggestions && q.Length >= 2 && !q.Contains('/') && !q.Contains(':'))
            _ = AddOnlineSuggestionsAsync(seq, q, history);

        void ShowSuggestions(IReadOnlyList<Suggestion> items)
        {
            _suggestions = items;
            _suggesting = true;
            _popup.Show(x, y, width, JsonSerializer.Serialize(new
            {
                popup = "suggestions",
                items = items.Select(v => new { url = v.Url, title = v.Title, kind = v.Kind }),
            }), focus: false); // le curseur reste dans la barre d'adresse
            _bridge.Post(JsonSerializer.Serialize(new { @event = "suggestions", id = tabId, count = items.Count }));
        }

        async Task AddOnlineSuggestionsAsync(int forSeq, string text, IReadOnlyList<Visit> local)
        {
            try
            {
                var json = await Http.GetStringAsync(string.Format(_settings.SuggestUrlTemplate, Uri.EscapeDataString(text)));
                if (forSeq != _suggestSeq || !_suggesting) return; // l'utilisateur a continué à taper ou a quitté le champ
                ShowSuggestions(Suggestions.Build(text, local, _settings.SearchUrlTemplate, Suggestions.ParseGoogle(json)));
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
            {
                // hors ligne ou service lent : on garde les suggestions locales
            }
        }
    }

    // Certains sites refusent une requête sans agent : celui-ci ne dit rien de l'ordinateur.
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(4),
        DefaultRequestHeaders = { { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Navigateur" } },
    };
    private int _suggestSeq;

    // Menus affichés dans la vue posée sur la page : un libellé et son action ; un libellé vide est un séparateur.
    private List<(string Label, Action? Run)>? _menu;
    private Action? _menuDismissed; // appelé à la fermeture du menu, avec ou sans choix

    private void ShowMenu(double x, double y, List<(string Label, Action? Run)> items, Action? dismissed = null)
    {
        HidePopup();
        (_menu, _menuDismissed) = (items, dismissed);
        _popup.Show(x, y, 260, JsonSerializer.Serialize(new { popup = "menu", items = items.Select(i => i.Label) }), focus: true);
    }

    private void PickMenuItem(int index)
    {
        if (_menu is not { } menu || index < 0 || index >= menu.Count) return;
        _afterMenu = null;
        menu[index].Run?.Invoke(); // avant la fermeture : une commande du moteur doit être choisie avant de lui rendre la main
        HidePopup();
        Refresh();
        var after = _afterMenu;
        _afterMenu = null;
        after?.Invoke();
    }

    // Clic droit sur un onglet de la barre.
    private void OpenTabMenu(string id, double x, double y)
    {
        if (_tabs.Tabs.FirstOrDefault(t => t.Id == id) is not { } tab) return;
        // Les deux onglets d'une vue scindée s'épinglent ensemble.
        var ids = _split.Contains(id) ? new[] { _split.PrimaryId!, _split.SecondaryId! } : [id];
        var block = ids.Length > 1;
        ShowMenu(x, y,
        [
            (tab.Pinned ? (block ? T("Détacher les deux pages", "Unpin both pages") : T("Détacher l'onglet", "Unpin tab")) : (block ? T("Épingler les deux pages", "Pin both pages") : T("Épingler l'onglet", "Pin tab")), () =>
            {
                foreach (var tabId in ids) _tabs.SetPinned(tabId, !tab.Pinned);
                KeepPairAdjacent();
            }),
            .. GroupEntries(tab),
            .. ShortcutEntry(tab),
            ("", null),
            (T("Fermer l'onglet", "Close tab"), () => Close(id)),
            (T("Fermer les autres onglets", "Close other tabs"), () =>
            {
                foreach (var other in _tabs.Tabs.Where(t => t.Id != id && !t.Pinned).Select(t => t.Id).ToList()) Close(other);
            }),
        ]);
    }

    // Raccourcis de la carte latérale : enregistrés dans les réglages, modifiables et retirables par clic droit.
    private SiteShortcut? ShortcutFor(string? url) =>
        SiteShortcut.DomainOf(url) is { Length: > 0 } domain ? _settings.Shortcuts.FirstOrDefault(s => SiteShortcut.DomainOf(s.Url) == domain) : null;

    // Page ouverte « dans » un raccourci : un onglet sans ligne dans la liste, que seul son raccourci affiche.
    private readonly Dictionary<SiteShortcut, string> _shortcutTabs = new();

    private void OpenShortcut(int index)
    {
        if (index < 0 || index >= _settings.Shortcuts.Count) return;
        var shortcut = _settings.Shortcuts[index];
        if (_shortcutTabs.TryGetValue(shortcut, out var open)) Activate(open);
        else _shortcutTabs[shortcut] = Open(shortcut.Url, pinned: true).Id; // épinglé : jamais groupé, épargné par « fermer les autres »
    }

    // Le site annonce son nom (« GitHub », « YouTube »…) : le raccourci le prend à la place du nom tiré de l'adresse.
    private async Task LearnShortcutNameAsync(string tabId)
    {
        if (ShortcutFor(_tabs.Tabs.FirstOrDefault(t => t.Id == tabId)?.Url) is not { NameLocked: false } shortcut || _pages.CoreOf(tabId) is not { } core) return;
        try
        {
            var raw = await core.ExecuteScriptAsync(
                """(document.querySelector('meta[property="og:site_name"]') || document.querySelector('meta[name="application-name"]'))?.content || '' """);
            // Texte venu de la page : gardé court et sans caractère de contrôle, affiché comme du texte.
            var name = new string((JsonSerializer.Deserialize<string>(raw) ?? "").Where(ch => !char.IsControl(ch)).ToArray()).Trim();
            if (name.Length is 0 or > MaxShortcutName || name == shortcut.Name || shortcut.NameLocked || !_settings.Shortcuts.Contains(shortcut)) return;
            shortcut.Name = name;
            SaveShortcuts();
        }
        catch (Exception) { /* page fermée ou script refusé : le nom reste celui de l'adresse */ }
    }

    private void SaveShortcuts()
    {
        JsonFileStore.Save(AppPaths.Settings, _settings);
        Refresh();
    }

    // Entrée du menu d'un onglet : son site rejoint les raccourcis, s'il n'y est pas déjà.
    private List<(string Label, Action? Run)> ShortcutEntry(Tab tab)
    {
        if (!IsWeb(tab.Url) || ShortcutFor(tab.Url) is not null) return [];
        return [(T("Ajouter aux raccourcis", "Add to shortcuts"), () =>
        {
            _settings.Shortcuts.Add(new SiteShortcut { Url = tab.Url, Name = SiteShortcut.NameOf(tab.Url), Icon = _favicons.GetValueOrDefault(tab.Id) });
            SaveShortcuts();
            _ = LearnShortcutNameAsync(tab.Id);
        })];
    }

    private void OpenShortcutMenu(int index, double x, double y)
    {
        if (index < 0 || index >= _settings.Shortcuts.Count) return;
        var shortcut = _settings.Shortcuts[index];
        var open = _shortcutTabs.GetValueOrDefault(shortcut);
        ShowMenu(x, y,
        [
            .. open is null ? [] : new (string, Action?)[] { (T("Fermer la page du raccourci", "Close the shortcut's page"), () => Close(open)), ("", null) },
            (T("Modifier le raccourci", "Edit shortcut"), () => _afterMenu = () =>
            {
                HidePopup();
                _popup.ShowCentered(340, JsonSerializer.Serialize(new { popup = "shortcut-edit", id = index.ToString(), name = shortcut.Name, url = shortcut.Url }));
            }),
            (T("Retirer le raccourci", "Remove shortcut"), () =>
            {
                Release(shortcut);
                _settings.Shortcuts.Remove(shortcut);
                SaveShortcuts();
            }),
        ]);
    }

    // Le raccourci lâche sa page : elle n'est pas fermée, elle devient un onglet ordinaire de la liste.
    private void Release(SiteShortcut shortcut)
    {
        if (_shortcutTabs.Remove(shortcut, out var open)) _tabs.SetPinned(open, false);
    }

    private const int MaxShortcutName = 24;

    private void UpdateShortcut(string id, string name, string input)
    {
        if (!int.TryParse(id, out var index) || index < 0 || index >= _settings.Shortcuts.Count) return;
        var url = Resolve(input);
        if (!IsWeb(url) || SiteShortcut.DomainOf(url).Length == 0) return; // seulement une adresse web
        var shortcut = _settings.Shortcuts[index];
        var moved = SiteShortcut.DomainOf(url) != SiteShortcut.DomainOf(shortcut.Url);
        if (moved)
        {
            // Autre site : la page ouverte reste celle de l'ancien, en onglet ordinaire.
            shortcut.Icon = null;
            Release(shortcut);
        }
        shortcut.Url = url;

        var typed = new string(name.Where(ch => !char.IsControl(ch)).ToArray()).Trim();
        if (typed.Length > MaxShortcutName) typed = typed[..MaxShortcutName].Trim();
        // Nom laissé vide, ou nom automatique laissé tel quel : il reste automatique (adresse, puis nom annoncé par le site).
        if (typed.Length == 0 || (typed == shortcut.Name && !shortcut.NameLocked))
        {
            shortcut.NameLocked = false;
            if (moved || typed.Length == 0) shortcut.Name = SiteShortcut.NameOf(url);
        }
        else (shortcut.Name, shortcut.NameLocked) = (typed, true);

        SaveShortcuts();
        if (shortcut.Icon is null) _ = FetchShortcutIconAsync(shortcut);
    }

    // Un onglet vient d'afficher l'icône de son site : le raccourci de ce site la prend (c'est la plus fidèle).
    private void LearnShortcutIcon(string tabId, string? data)
    {
        if (data is null || data.Length > MaxShortcutIcon * 2) return;
        if (ShortcutFor(_tabs.Tabs.FirstOrDefault(t => t.Id == tabId)?.Url) is not { } shortcut || shortcut.Icon == data) return;
        shortcut.Icon = data;
        SaveShortcuts();
    }

    private const int MaxShortcutIcon = 100_000; // octets

    // Site jamais visité : son icône est demandée à l'adresse habituelle, /favicon.ico. Rien d'autre n'est envoyé.
    private async Task FetchShortcutIconAsync(SiteShortcut shortcut)
    {
        if (!IsWeb(shortcut.Url) || !Uri.TryCreate(shortcut.Url, UriKind.Absolute, out var site)) return;
        try
        {
            using var response = await Http.GetAsync(new Uri(site, "/favicon.ico"));
            var type = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!response.IsSuccessStatusCode || !type.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return;
            if (response.Content.Headers.ContentLength is > MaxShortcutIcon) return;
            var bytes = await response.Content.ReadAsByteArrayAsync();
            // Entre-temps, un onglet a pu fournir l'icône, ou le raccourci a pu changer de site ou disparaître.
            if (bytes.Length is 0 or > MaxShortcutIcon || shortcut.Icon is not null || !_settings.Shortcuts.Contains(shortcut)) return;
            if (SiteShortcut.DomainOf(shortcut.Url) != SiteShortcut.DomainOf(site.AbsoluteUri)) return;
            shortcut.Icon = $"data:{type};base64,{Convert.ToBase64String(bytes)}";
            SaveShortcuts();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            // hors ligne ou site sans icône : le raccourci garde sa lettre, jusqu'à la première visite
        }
    }

    private void Activate(string id)
    {
        if (!_tabs.Activate(id)) return;
        // Un onglet rangé dans un groupe replié : le groupe se déplie pour le montrer.
        if (_tabs.Tabs.First(t => t.Id == id).GroupId is { } groupId && _tabs.Groups.First(g => g.Id == groupId).Collapsed)
            _tabs.UpdateGroup(groupId, collapsed: false);
    }

    // Entrées « groupe » du menu d'un onglet. Un onglet épinglé ne se groupe pas.
    private List<(string Label, Action? Run)> GroupEntries(Tab tab)
    {
        var entries = new List<(string Label, Action? Run)>();
        if (tab.Pinned) return entries;
        entries.Add(("", null));
        if (tab.GroupId is not null) entries.Add((T("Retirer du groupe", "Remove from group"), () => { _tabs.RemoveFromGroup(tab.Id); KeepPairAdjacent(); }));
        foreach (var group in _tabs.Groups.Where(g => g.Id != tab.GroupId))
            entries.Add((T($"Ajouter au groupe « {group.Name} »", $"Add to group “{group.Name}”"), () => { _tabs.AddToGroup(tab.Id, group.Id); KeepPairAdjacent(); }));
        // L'action ouvre une saisie : elle ne doit pas être refermée par la fermeture du menu.
        entries.Add((T("Nouveau groupe avec cet onglet", "New group with this tab"), () => _afterMenu = () => Execute(new Command("group.new", Id: tab.Id))));
        return entries;
    }

    // Clic droit sur l'en-tête d'un groupe.
    private void OpenGroupMenu(string groupId, double x, double y)
    {
        if (_tabs.Groups.FirstOrDefault(g => g.Id == groupId) is not { } group) return;
        var members = _tabs.Tabs.Where(t => t.GroupId == groupId).Select(t => t.Id).ToList();
        ShowMenu(x, y,
        [
            (T("Modifier le groupe (nom, couleur, icône)", "Edit group (name, color, icon)"), () => _afterMenu = () => EditGroup(tabId: null, group)),
            ("", null),
            (T("Dissocier le groupe", "Ungroup"), () => _tabs.Ungroup(groupId)),
            (T("Fermer les onglets du groupe", "Close the group's tabs"), () => { foreach (var member in members) Close(member); }),
        ]);
    }

    // Fenêtre d'édition d'un groupe : nom, couleur libre, icône. Sert aussi à en créer un autour d'un onglet.
    private void EditGroup(string? tabId, TabGroup? group)
    {
        HidePopup();
        _popup.ShowCentered(340, JsonSerializer.Serialize(new
        {
            popup = "group-edit",
            create = group is null,
            id = group?.Id ?? tabId,
            name = group?.Name ?? "",
            color = group?.Color ?? TabManager.GroupColors[_tabs.Groups.Count % TabManager.GroupColors.Count],
            icon = group?.Icon ?? "",
            presets = TabManager.GroupColors,
        }), top: 64);
    }

    // Action à lancer une fois le menu refermé (ouvrir une saisie depuis un menu, par exemple).
    private Action? _afterMenu;

    // Fenêtre de saisie d'un nom, au milieu de la zone des pages. La réponse revient sous la forme d'une commande.
    private void AskName(string title, string hint, string placeholder, string submit, string value, string command, string? id = null)
    {
        HidePopup();
        _popup.ShowCentered(340, JsonSerializer.Serialize(new { popup = "prompt", title, hint, placeholder, submit, value, command, id }));
    }

    // Profils : chacun a ses données. Le panneau en change, en crée ou en supprime.
    private bool _profilesOpen;

    private string ProfilesJson() => JsonSerializer.Serialize(new
    {
        popup = "profiles",
        items = Profiles.List(AppPaths.BaseDir).Select(id => new
        {
            id,
            name = ProfileName(id),
            current = id == AppPaths.Profile,
            // Ni le profil par défaut, ni celui de cette fenêtre, ni un profil ouvert dans une autre fenêtre.
            renamable = id != Profiles.Default,
            deletable = id != Profiles.Default && id != AppPaths.Profile && !IsProfileOpen(id),
        }),
    });

    private void OpenProfileMenu(double x, double y)
    {
        if (ClosesPanel("profile")) return;
        HidePopup();
        _profilesOpen = true;
        _popup.Show(x - 300, y, 300, ProfilesJson(), focus: true);
    }

    private void AskProfileName() =>
        AskName(T("Nouveau profil", "New profile"), T("Ses onglets, son historique, ses favoris et ses mots de passe restent séparés des autres profils.", "Its tabs, history, bookmarks and passwords stay separate from other profiles."),
            T("Nom du profil", "Profile name"), T("Créer et ouvrir", "Create and open"), "", "profile.create");

    private void DeleteProfile(string id)
    {
        if (id == AppPaths.Profile || IsProfileOpen(id)) return;
        try { Profiles.Delete(AppPaths.BaseDir, id); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* fichiers encore utilisés : le profil reste */ }
        if (_profilesOpen) _popup.Post(ProfilesJson());
    }

    private static bool IsProfileOpen(string id)
    {
        if (!Mutex.TryOpenExisting($"Navigateur.Profil.{id}", out var held)) return false;
        held.Dispose();
        return true;
    }

    // Changer de profil : une fenêtre du nouveau profil prend la place de celle-ci, au même endroit.
    // (Les données d'un profil sont fixées au démarrage du moteur : on ne peut pas en changer en cours de route.)
    private void SwitchProfile(string id)
    {
        if (id == AppPaths.Profile || !Profiles.List(AppPaths.BaseDir).Contains(id)) return;
        if (Environment.ProcessPath is not { } exe) return;
        var app = _window.AppWindow;
        var start = new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = false };
        start.ArgumentList.Add($"--profile={id}");
        start.ArgumentList.Add($"--bounds={app.Position.X},{app.Position.Y},{app.Size.Width},{app.Size.Height}");
        if (Presenter?.State == OverlappedPresenterState.Maximized) start.ArgumentList.Add("--maximized");
        // La nouvelle fenêtre repart de l'environnement d'origine, pas de celui que ce profil s'est réglé.
        foreach (var (variable, value) in App.LaunchEnvironment)
        {
            if (value is null) start.Environment.Remove(variable);
            else start.Environment[variable] = value;
        }
        try { System.Diagnostics.Process.Start(start); }
        catch (System.ComponentModel.Win32Exception) { return; } // rien ne s'est ouvert : on garde cette fenêtre
        _window.Close();
    }

    // Bouton de capture d'écran de la barre d'outils.
    private void OpenCaptureMenu(double x, double y)
    {
        if (ClosesPanel("capture")) return;
        ShowMenu(x - 260, y,
        [
            (T("Capturer une zone", "Capture an area"), () => Capture("region")),
            (T("Capturer la partie visible", "Capture the visible part"), () => Capture("visible")),
            (T("Capturer la page entière", "Capture the full page"), () => Capture("full")),
        ]);
    }

    private void Capture(string mode)
    {
        if (_tabs.Tabs.FirstOrDefault(t => t.Id == _tabs.ActiveId) is { } tab && _pages.CoreOf(tab.Id) is { } core)
            _ = _capture.RunAsync(core, mode, tab.Title);
    }

    // Clic droit dans une page : le moteur fournit ses commandes (copier, coller, inspecter…), on les affiche dans notre vue.
    private static readonly HashSet<string> NativeCommands =
    [
        "back", "forward", "reload", "cut", "copy", "paste", "selectAll",
        "copyLinkLocation", "saveLinkAs", "copyImage", "copyImageLocation", "saveImageAs",
        "saveAs", "print", "viewPageSource", "inspectElement",
    ];

    private static string DeviceLabel(string name) => name switch
    {
        "iPhone Duo cover" => T("iPhone Duo (plié)", "iPhone Duo (folded)"),
        "Desktop" => T("Ordinateur", "Desktop"),
        PageHost.Comparison => T("Comparaison", "Comparison"),
        _ => name,
    };

    // Comparaison d'écrans : de deux à quatre appareils, choisis par l'utilisateur et retenus dans les réglages.
    private const int MinCompared = 2, MaxCompared = 4;

    private void ApplyComparison()
    {
        var known = Device.All.Select(d => d.Name).Where(_settings.CompareDevices.Contains).ToList(); // dans l'ordre du catalogue
        _pages.ComparisonDevices = known.Count is >= MinCompared and <= MaxCompared ? known : Device.DefaultComparison;
    }

    private void AskComparison(string tabId)
    {
        HidePopup();
        _popup.ShowCentered(340, JsonSerializer.Serialize(new
        {
            popup = "compare-pick", id = tabId, min = MinCompared, max = MaxCompared,
            devices = Device.All.Select(d => new { name = d.Name, label = DeviceLabel(d.Name), size = $"{d.Width} × {d.Height}", on = _pages.ComparisonDevices.Contains(d.Name) }),
        }), top: 64);
    }

    private void SetComparison(string tabId, string names)
    {
        var chosen = names.Split('|').Where(n => Device.Find(n) is not null).Distinct().ToList();
        if (chosen.Count is < MinCompared or > MaxCompared) return;
        _settings.CompareDevices = chosen;
        JsonFileStore.Save(AppPaths.Settings, _settings);
        ApplyComparison();
        HidePopup();
        _pages.SetDevice(tabId, PageHost.Comparison);
    }

    // Entrées « simulateur » d'un menu. Chaque action s'exécute une fois le menu refermé (le moteur a alors repris la main).
    private List<(string Label, Action? Run)> DeviceEntries(string tabId, double x, double y)
    {
        var simulated = _pages.DeviceOf(tabId) is not null;
        List<(string Label, Action? Run)> items =
        [
            (simulated ? T("Changer d'appareil…", "Change device…") : T("Voir en mobile…", "View as mobile…"), () => _afterMenu = () =>
                ShowMenu(x, y,
                [
                    .. Device.All.Where(d => !d.Desktop).Select(d => (DeviceLabel(d.Name), (Action?)(() => _afterMenu = () => _pages.SetDevice(tabId, d.Name)))),
                    ("", null),
                    (T("Comparer plusieurs écrans", "Compare several screens"), () => _afterMenu = () => _pages.SetDevice(tabId, PageHost.Comparison)),
                    (T("Choisir les écrans à comparer…", "Choose the screens to compare…"), () => _afterMenu = () => AskComparison(tabId)),
                ])),
        ];
        if (simulated) items.Add((T("Pivoter l'appareil", "Rotate device"), () => _afterMenu = () => _pages.RotateDevice(tabId)));
        if (simulated) items.Add((T("Revenir à la vue ordinateur", "Back to desktop view"), () => _afterMenu = () => _pages.SetDevice(tabId, null)));
        return items;
    }

    // Inspecteur réseau : panneau du bas, qui suit l'onglet affiché.
    private void ToggleNetwork()
    {
        if (_tools.IsOpen)
        {
            _tools.Hide();
            _network.Shown = null;
        }
        else
        {
            _ = _tools.ShowAsync();
            FollowNetwork();
        }
        Refresh();
    }

    private void FollowNetwork()
    {
        if (!_tools.IsOpen || _tabs.ActiveId is not { } active || _pages.CoreOf(active) is not { } core) return;
        if (_network.Shown == active) return;
        _network.Shown = active;
        _ = _network.ListenAsync(active, core);
        _network.Post();
    }

    private void OpenContextMenu(string tabId, double x, double y, Microsoft.Web.WebView2.Core.CoreWebView2ContextMenuRequestedEventArgs e)
    {
        var items = new List<(string Label, Action? Run)>();
        var link = e.ContextMenuTarget.HasLinkUri ? e.ContextMenuTarget.LinkUri : null;
        if (link is not null && IsWeb(link))
        {
            items.Add((T("Ouvrir le lien dans un nouvel onglet", "Open link in a new tab"), () => Open(link)));
            items.Add((T("Ouvrir le lien en vue scindée", "Open link in split view"), () => Split(Open(link, activate: false).Id)));
            items.Add(("", null));
        }
        foreach (var item in e.MenuItems)
        {
            if (item.Kind == Microsoft.Web.WebView2.Core.CoreWebView2ContextMenuItemKind.Separator)
            {
                if (items.Count > 0 && items[^1].Label != "") items.Add(("", null));
            }
            else if (item.Kind == Microsoft.Web.WebView2.Core.CoreWebView2ContextMenuItemKind.Command && item.IsEnabled && NativeCommands.Contains(item.Name))
            {
                var commandId = item.CommandId;
                items.Add((item.Label.Replace("&", ""), () => e.SelectedCommandId = commandId));
            }
        }
        if (items.Count > 0 && items[^1].Label != "") items.Add(("", null));
        items.Add((T("Capturer une zone", "Capture an area"), () => Capture("region")));
        items.AddRange(DeviceEntries(tabId, x, y));
        items.Add((_tools.IsOpen ? T("Masquer l'inspecteur réseau", "Hide network inspector") : T("Inspecter le réseau", "Inspect network"), () => _afterMenu = ToggleNetwork));

        // Le moteur attend notre réponse : il est toujours libéré à la fermeture du menu, avec ou sans choix.
        e.Handled = true;
        var deferral = e.GetDeferral();
        ShowMenu(x, y, items, deferral.Complete);
    }

    private void OpenLibrary(double x, double y)
    {
        // Recliquer l'icône referme le panneau : le clic lui fait d'abord perdre le curseur, donc il vient de se fermer.
        // ponytail: seuil de temps ; à remplacer par un état explicite si d'autres menus s'ajoutent.
        if (ClosesPanel("library")) return;
        const double width = 340;
        _suggesting = false;
        _popup.Show(x - width, y, width, JsonSerializer.Serialize(new
        {
            popup = "library",
            bookmarks = _db.Bookmarks().Select(b => new { url = b.Url, title = b.Title }),
            history = _db.RecentHistory(12).Select(v => new { url = v.Url, title = v.Title }),
        }), focus: true); // la vue prend le curseur : la perdre la referme
    }

    private void RecordVisit(string id)
    {
        if (_restored.Remove(id)) return; // rouvrir une session n'est pas une visite
        if (_tabs.Tabs.FirstOrDefault(t => t.Id == id) is { } tab && IsWeb(tab.Url))
            _db.RecordVisit(tab.Url, tab.Title, DateTimeOffset.UtcNow);
    }

    // Les onglets rouverts restent endormis : seul celui qu'on affiche charge sa page (voir PageHost.AddAsleep).
    private void RestoreSession()
    {
        var session = JsonFileStore.Load<Session>(AppPaths.Session);
        var tabs = session.Urls.Select((url, i) => Open(url, activate: false, pinned: i < session.PinnedCount, asleep: true)).ToList();
        if (tabs.Count == 0) { Open(_settings.HomePage); return; }
        foreach (var tab in tabs) _restored.Add(tab.Id);
        for (var i = 0; i < tabs.Count && i < session.Titles.Count; i++)
            if (session.Titles[i].Length > 0) _tabs.Update(tabs[i].Id, title: session.Titles[i]);
        // Pages qui étaient ouvertes dans un raccourci : elles y retournent.
        for (var i = 0; i < tabs.Count && i < session.ShortcutOf.Count; i++)
            if (tabs[i].Pinned && session.ShortcutOf[i] is { } rank && rank >= 0 && rank < _settings.Shortcuts.Count)
                _shortcutTabs.TryAdd(_settings.Shortcuts[rank], tabs[i].Id);
        // Appareil simulé : repris quand l'onglet se réveille.
        for (var i = 0; i < tabs.Count && i < session.Devices.Count; i++)
            if (session.Devices[i].Split('|') is [{ Length: > 0 } device, .. var rest]) _pages.WantDevice(tabs[i].Id, device, rest.Contains("L"));

        // Groupes : recréés autour de leurs onglets, qui étaient voisins à l'enregistrement.
        for (var g = 0; g < session.Groups.Count; g++)
        {
            TabGroup? group = null;
            for (var i = 0; i < tabs.Count && i < session.GroupOf.Count; i++)
            {
                if (session.GroupOf[i] != g) continue;
                if (group is null) group = _tabs.CreateGroup(tabs[i].Id, session.Groups[g].Name, session.Groups[g].Color, session.Groups[g].Collapsed, session.Groups[g].Icon);
                else _tabs.AddToGroup(tabs[i].Id, group.Id);
            }
        }

        Activate(tabs[Math.Clamp(session.ActiveIndex, 0, tabs.Count - 1)].Id);
        bool Valid(int? i) => i is >= 0 && i < tabs.Count;
        if (Valid(session.PrimaryIndex) && Valid(session.SecondaryIndex)
            && _split.Open(tabs[session.PrimaryIndex!.Value].Id, tabs[session.SecondaryIndex!.Value].Id))
            _split.Resize(session.Ratio);
    }

    private void SaveSession()
    {
        var tabs = _tabs.Tabs.ToList();
        int? IndexOf(string? id) => tabs.FindIndex(t => t.Id == id) is >= 0 and var i ? i : null;
        JsonFileStore.Save(AppPaths.Session, new Session
        {
            Urls = tabs.Select(t => t.Url).ToList(),
            Titles = tabs.Select(t => t.Title).ToList(),
            PinnedCount = _tabs.PinnedCount,
            Devices = tabs.Select(t => _pages.DeviceState(t.Id) is var (device, landscape) ? device + (landscape ? "|L" : "") : "").ToList(),
            ShortcutOf = tabs.Select(t => _settings.Shortcuts.FindIndex(s => _shortcutTabs.GetValueOrDefault(s) == t.Id) is >= 0 and var rank ? rank : (int?)null).ToList(),
            Groups = _tabs.Groups.Select(g => new SessionGroup { Name = g.Name, Color = g.Color, Icon = g.Icon, Collapsed = g.Collapsed }).ToList(),
            GroupOf = tabs.Select(t => t.GroupId is null ? (int?)null : _tabs.Groups.ToList().FindIndex(g => g.Id == t.GroupId)).ToList(),
            ActiveIndex = IndexOf(_tabs.ActiveId) ?? 0,
            PrimaryIndex = IndexOf(_split.PrimaryId),
            SecondaryIndex = IndexOf(_split.SecondaryId),
            Ratio = _split.Ratio,
        });
    }

    private void Refresh()
    {
        var showSplit = _split.Contains(_tabs.ActiveId);
        FollowNetwork();
        _pages.ApplyLayout(showSplit ? _split.PrimaryId : _tabs.ActiveId, showSplit ? _split.SecondaryId : null, _split.Ratio);
        _bridge.Post(JsonSerializer.Serialize(new
        {
            tabs = _tabs.Tabs.Select(t => new
            {
                id = t.Id, url = t.Url, title = t.Title, pinned = t.Pinned,
                groupId = t.GroupId,
                device = _pages.DeviceOf(t.Id) is { } simulated ? DeviceLabel(simulated) : null, // simulateur : appareil affiché
                zoom = (int)Math.Round(_pages.ZoomOf(t.Id) * 100),
                loading = _pages.IsLoading(t.Id),
                canGoBack = _pages.CanGoBack(t.Id),
                canGoForward = _pages.CanGoForward(t.Id),
                // Seules les pages affichées ont une étoile : inutile d'interroger la base pour les autres.
                bookmarked = (t.Id == _tabs.ActiveId || (showSplit && _split.Contains(t.Id))) && _db.IsBookmarked(t.Url),
            }),
            groups = _tabs.Groups.Select(g => new { id = g.Id, name = g.Name, color = g.Color, icon = g.Icon, collapsed = g.Collapsed }),
            activeId = _tabs.ActiveId,
            shortcuts = _settings.Shortcuts.Select(s => new { url = s.Url, name = s.Name, icon = s.Icon, tabId = _shortcutTabs.GetValueOrDefault(s) }),
            profile = ProfileName(AppPaths.Profile),
            split = new { primaryId = _split.PrimaryId, secondaryId = _split.SecondaryId, ratio = _split.Ratio, visible = showSplit },
            maximized = Presenter?.State == OverlappedPresenterState.Maximized,
            downloads = new
            {
                count = _downloads.Count,
                docked = _dockShown, // l'encart en bas à droite les affiche : le bouton de la barre fait double emploi
                active = _downloads.Count(d => d.Op.State == Microsoft.Web.WebView2.Core.CoreWebView2DownloadState.InProgress),
            },
        }));
    }

    // Déplacement de la fenêtre depuis la barre de titre dessinée par l'interface.
    private void BeginDrag()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        ReleaseCapture();
        SendMessage(hwnd, 0x00A1 /* WM_NCLBUTTONDOWN */, 2 /* HTCAPTION */, 0);
    }

    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern nint SendMessage(nint hwnd, uint msg, nint wParam, nint lParam);
}
