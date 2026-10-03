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

    public BrowserController(Window window, PageHost pages, UiBridge bridge, PopupHost popup, DockHost dock, Settings settings)
    {
        (_window, _pages, _bridge, _popup, _dock, _settings) = (window, pages, bridge, popup, dock, settings);
        Directory.CreateDirectory(AppPaths.DataDir);
        _db = new BrowserDb(AppPaths.Database);
        _capture = new CaptureService(window);

        _pages.PageChanged += (id, url, title) => { _tabs.Update(id, url, title); Refresh(); };
        _pages.RuntimeChanged += Refresh;
        _pages.NewTabRequested += url => { Open(url); Refresh(); };
        _pages.Loaded += RecordVisit;
        _pages.FaviconChanged += (id, data) => { _favicons[id] = data; PostFavicon(id); };
        _pages.ContextMenuRequested += OpenContextMenu;
        _pages.StatusChanged += (id, text) =>
        {
            if (id == _tabs.ActiveId || _split.Contains(id))
                _bridge.Post(JsonSerializer.Serialize(new { @event = "status", text }));
        };
        _pages.DownloadStarting += OnDownloadStarting;
        _window.AppWindow.Changed += (_, e) => { if (e.DidPresenterChange || e.DidSizeChange) Refresh(); };
        _window.Closed += (_, _) => { SaveSession(); _db.Dispose(); };
    }

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
            case "popup.size": _popup.SetHeight(c.Height!.Value); return;
            case "popup.close":
                if (!_suggesting) _libraryClosedAt = DateTime.UtcNow;
                HidePopup();
                return;
            case "suggest.select":
                if (_suggesting) _popup.Post(JsonSerializer.Serialize(new { select = c.Index }));
                return;
            case "menu.pick": PickMenuItem(c.Index!.Value); return;
            case "settings.open": OpenSettings(c.X!.Value, c.Y!.Value); return;
            case "settings.set": SetSetting(id, c.Index != 0); return;
            case "dock.size": if (_dockShown) _dock.SetSize(c.Width!.Value, c.Height!.Value); return;
            case "tab.menu": OpenTabMenu(id, c.X!.Value, c.Y!.Value); return;
            case "capture.menu": OpenCaptureMenu(c.X!.Value, c.Y!.Value); return;
            case "window.close": _window.Close(); return;
            case "window.drag": BeginDrag(); return;

            case "suggest.pick":
                if (_suggestTab is not null && c.Index is int i && i >= 0 && i < _suggestions.Count)
                    _pages.Navigate(_suggestTab, _suggestions[i].Url);
                break;
            case "ui.ready":
                if (_tabs.Tabs.Count == 0) RestoreSession();
                foreach (var tabId in _favicons.Keys) PostFavicon(tabId);
                break;
            case "tab.open": Open(c.Url is null ? _settings.HomeUrl : Resolve(c.Url)); break;
            case "tab.close": Close(id); break;
            case "tab.activate": _tabs.Activate(id); break;
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
            case "split.close": _split.Close(); break;
            case "split.resize": _split.Resize(c.Ratio!.Value); break;
            case "bookmark.add": if (IsWeb(c.Url!)) _db.AddBookmark(c.Url!, c.Title!); break;
            case "bookmark.remove": _db.RemoveBookmark(c.Url!); break;
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
        if (DateTime.UtcNow - _libraryClosedAt < TimeSpan.FromMilliseconds(350)) return; // reclic sur l'icône : reste fermé
        const double width = 340;
        HidePopup();
        _downloadsOpen = true;
        _popup.Show(x - width, y, width, DownloadsJson(_downloads), focus: true, animate: true, anchorRight: true);
    }

    // Panneau de réglages : des interrupteurs, appliqués tout de suite et enregistrés.
    private bool _settingsOpen;

    private string SettingsJson() => JsonSerializer.Serialize(new
    {
        popup = "settings",
        items = new[]
        {
            new { key = "scrollbars", label = "Barre de défilement fine", hint = "Apparaît seulement pendant le défilement", on = !_settings.HideScrollbars },
            new { key = "blur", label = "Flou progressif en bas des pages", hint = "", on = _settings.BlurEnabled },
            new { key = "floatingDownloads", label = "Téléchargements en bas à droite", hint = "Sinon, uniquement dans le panneau de la barre", on = _settings.FloatingDownloads },
            new { key = "onlineSuggestions", label = "Suggestions de recherche Google", hint = "Envoie à Google ce qui est tapé dans la barre d'adresse", on = _settings.OnlineSuggestions },
            new { key = "passwords", label = "Enregistrer les mots de passe", hint = "Stockage chiffré du moteur, sur cet ordinateur", on = _settings.SavePasswords },
        },
    });

    private void OpenSettings(double x, double y)
    {
        if (DateTime.UtcNow - _libraryClosedAt < TimeSpan.FromMilliseconds(350)) return; // reclic sur l'icône : reste fermé
        const double width = 340;
        HidePopup();
        _settingsOpen = true;
        _popup.Show(x - width, y, width, SettingsJson(), focus: true, animate: true, anchorRight: true);
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
            default: return;
        }
        JsonFileStore.Save(AppPaths.Settings, _settings);
        _pages.ApplySettings();
        UpdateDock();
        Refresh();
        if (_settingsOpen) _popup.Post(SettingsJson());
    }

    private void HidePopup()
    {
        _suggesting = false;
        _downloadsOpen = false;
        _settingsOpen = false;
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

    private OverlappedPresenter? Presenter => _window.AppWindow.Presenter as OverlappedPresenter;
    private string Resolve(string input) => AddressInput.Resolve(input, _settings.SearchUrlTemplate);
    private static bool IsWeb(string url) =>
        url.StartsWith("https://", StringComparison.Ordinal) || url.StartsWith("http://", StringComparison.Ordinal);

    private Tab Open(string url, bool activate = true, bool pinned = false)
    {
        var tab = _tabs.Open(url, activate, pinned);
        _pages.Add(tab);
        return tab;
    }

    private void Close(string id)
    {
        if (!_tabs.Close(id)) return;
        _pages.Remove(id);
        _favicons.Remove(id);
        _split.OnTabClosed(id);
        if (_tabs.Tabs.Count == 0) _window.Close();
    }

    // Scinde l'onglet actif avec l'onglet donné, ou avec un nouvel onglet si aucun n'est donné.
    private void Split(string? otherId)
    {
        if (_tabs.ActiveId is not { } active) return;
        if (otherId is not null && (otherId == active || _tabs.Tabs.All(t => t.Id != otherId))) return;
        var other = otherId ?? Open(_settings.HomeUrl, activate: false).Id;

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

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(2) };
    private int _suggestSeq;

    // Menus affichés dans la vue posée sur la page : un libellé et son action ; un libellé vide est un séparateur.
    private List<(string Label, Action? Run)>? _menu;
    private Action? _menuDismissed; // appelé à la fermeture du menu, avec ou sans choix

    private void ShowMenu(double x, double y, List<(string Label, Action? Run)> items, Action? dismissed = null, bool anchorRight = false)
    {
        HidePopup();
        (_menu, _menuDismissed) = (items, dismissed);
        _popup.Show(x, y, 260, JsonSerializer.Serialize(new { popup = "menu", items = items.Select(i => i.Label) }), focus: true, animate: true, anchorRight: anchorRight);
    }

    private void PickMenuItem(int index)
    {
        if (_menu is not { } menu || index < 0 || index >= menu.Count) return;
        menu[index].Run?.Invoke(); // avant la fermeture : une commande du moteur doit être choisie avant de lui rendre la main
        HidePopup();
        Refresh();
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
            (tab.Pinned ? (block ? "Détacher les deux pages" : "Détacher l'onglet") : (block ? "Épingler les deux pages" : "Épingler l'onglet"), () =>
            {
                foreach (var tabId in ids) _tabs.SetPinned(tabId, !tab.Pinned);
                KeepPairAdjacent();
            }),
            ("", null),
            ("Fermer l'onglet", () => Close(id)),
            ("Fermer les autres onglets", () =>
            {
                foreach (var other in _tabs.Tabs.Where(t => t.Id != id && !t.Pinned).Select(t => t.Id).ToList()) Close(other);
            }),
        ]);
    }

    // Bouton de capture d'écran de la barre d'outils.
    private void OpenCaptureMenu(double x, double y)
    {
        if (DateTime.UtcNow - _libraryClosedAt < TimeSpan.FromMilliseconds(350)) return; // reclic sur l'icône : reste fermé
        ShowMenu(x - 260, y,
        [
            ("Capturer une zone", () => Capture("region")),
            ("Capturer la partie visible", () => Capture("visible")),
            ("Capturer la page entière", () => Capture("full")),
        ], anchorRight: true);
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

    private void OpenContextMenu(double x, double y, Microsoft.Web.WebView2.Core.CoreWebView2ContextMenuRequestedEventArgs e)
    {
        var items = new List<(string Label, Action? Run)>();
        var link = e.ContextMenuTarget.HasLinkUri ? e.ContextMenuTarget.LinkUri : null;
        if (link is not null && IsWeb(link))
        {
            items.Add(("Ouvrir le lien dans un nouvel onglet", () => Open(link)));
            items.Add(("Ouvrir le lien en vue scindée", () => Split(Open(link, activate: false).Id)));
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
        items.Add(("Capturer une zone", () => Capture("region")));

        // Le moteur attend notre réponse : il est toujours libéré à la fermeture du menu, avec ou sans choix.
        e.Handled = true;
        var deferral = e.GetDeferral();
        ShowMenu(x, y, items, deferral.Complete);
    }

    private void OpenLibrary(double x, double y)
    {
        // Recliquer l'icône referme le panneau : le clic lui fait d'abord perdre le curseur, donc il vient de se fermer.
        // ponytail: seuil de temps ; à remplacer par un état explicite si d'autres menus s'ajoutent.
        if (DateTime.UtcNow - _libraryClosedAt < TimeSpan.FromMilliseconds(350)) return;
        const double width = 340;
        _suggesting = false;
        _popup.Show(x - width, y, width, JsonSerializer.Serialize(new
        {
            popup = "library",
            bookmarks = _db.Bookmarks().Select(b => new { url = b.Url, title = b.Title }),
            history = _db.RecentHistory(12).Select(v => new { url = v.Url, title = v.Title }),
        }), focus: true, animate: true, anchorRight: true); // la vue prend le curseur : la perdre la referme
    }

    private void RecordVisit(string id)
    {
        if (_restored.Remove(id)) return; // rouvrir une session n'est pas une visite
        if (_tabs.Tabs.FirstOrDefault(t => t.Id == id) is { } tab && IsWeb(tab.Url))
            _db.RecordVisit(tab.Url, tab.Title, DateTimeOffset.UtcNow);
    }

    // ponytail: tous les onglets restaurés chargent leur page tout de suite ; le chargement paresseux vient au chantier légèreté.
    private void RestoreSession()
    {
        var session = JsonFileStore.Load<Session>(AppPaths.Session);
        var tabs = session.Urls.Select((url, i) => Open(url, activate: false, pinned: i < session.PinnedCount)).ToList();
        if (tabs.Count == 0) { Open(_settings.HomeUrl); return; }
        foreach (var tab in tabs) _restored.Add(tab.Id);

        _tabs.Activate(tabs[Math.Clamp(session.ActiveIndex, 0, tabs.Count - 1)].Id);
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
            PinnedCount = _tabs.PinnedCount,
            ActiveIndex = IndexOf(_tabs.ActiveId) ?? 0,
            PrimaryIndex = IndexOf(_split.PrimaryId),
            SecondaryIndex = IndexOf(_split.SecondaryId),
            Ratio = _split.Ratio,
        });
    }

    private void Refresh()
    {
        var showSplit = _split.Contains(_tabs.ActiveId);
        _pages.ApplyLayout(showSplit ? _split.PrimaryId : _tabs.ActiveId, showSplit ? _split.SecondaryId : null, _split.Ratio);
        _bridge.Post(JsonSerializer.Serialize(new
        {
            tabs = _tabs.Tabs.Select(t => new
            {
                id = t.Id, url = t.Url, title = t.Title, pinned = t.Pinned,
                loading = _pages.IsLoading(t.Id),
                canGoBack = _pages.CanGoBack(t.Id),
                canGoForward = _pages.CanGoForward(t.Id),
                // Seules les pages affichées ont une étoile : inutile d'interroger la base pour les autres.
                bookmarked = (t.Id == _tabs.ActiveId || (showSplit && _split.Contains(t.Id))) && _db.IsBookmarked(t.Url),
            }),
            activeId = _tabs.ActiveId,
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
