using System.Runtime.InteropServices;
using System.Text.Json;
using Browser.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace Browser.App;

public sealed partial class BrowserController
{
    private Tab OpenLink(string source, string url, bool activate = true)
    {
        var tab = _tabs.Tabs.FirstOrDefault(t => t.Id == source);
        var linked = Open(url, activate: activate, sessionId: tab?.SessionId, isPrivate: tab?.Private);
        return KeepInSourceGroup(linked, tab);
    }
    private void ChangeSession(string tabId, string sessionId)
    {
        if (_tabs.Tabs.FirstOrDefault(t => t.Id == tabId) is not { } tab || tab.Private || sessionId == _deletingSession
            || sessionId.Length > 0 && _sessions.Resolve(sessionId).Length == 0) return;
        if (tab.SessionId == sessionId) { HidePopup(); return; }
        if (_downloads.Any(d => d.TabId == tabId && DownloadPending(d.Id,d.Op)))
        { Notice(T("Téléchargement en cours", "Download in progress"), T("Attendez sa fin avant de changer la session de cet onglet.", "Wait for it to finish before changing this tab's session.")); return; }
        HidePopup(); ExtensionPopup?.Hide();
        _network.Forget(tabId); if (_network.Shown == tabId) _network.Shown = null;
        _designTabs.Remove(tabId); _translationOriginals.Remove(tabId);
        var asleep = _pages.IsAsleep(tabId);
        _pages.Remove(tabId);
        if (_tabs.SetSession(tabId, sessionId) is { } changed)
        { if (asleep) _pages.AddAsleep(changed); else _pages.Add(changed); }
        _pinnedExtensions = []; _pinsInitialized = false; _pinProfileName = null;
        Refresh();
    }
    private async Task DeleteSessionAsync(string sessionId)
    {
        if (_deletingSession is not null || _sessions.Resolve(sessionId).Length == 0 || _tabs.Tabs.Any(t => t.SessionId == sessionId)) return;
        _deletingSession = sessionId;
        if (_sessionMenuTab is { } before) _popup.Post(SessionsJson(before));
        var deleted = await _pages.DeleteSessionAsync(sessionId);
        if (deleted)
        {
            _sessions.Delete(sessionId); _tabs.ForgetSession(sessionId);
            var permissions = Path.Combine(AppPaths.DataDir, "session-permissions", sessionId + ".json");
            _sessionPermissions.Remove(sessionId);
            try { File.Delete(permissions); File.Delete(permissions + ".bak"); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        _deletingSession = null;
        if (!deleted) Notice(T("Suppression impossible", "Cannot delete session"), T("Le moteur n’a pas pu supprimer la session. Réessayez après le redémarrage de Sol.", "The engine could not delete the session. Retry after restarting Sol."));
        else if (_sessionMenuTab is { } current) _popup.Post(SessionsJson(current));
        Refresh();
    }
    private bool PrivateActive => AppPaths.Private || _tabs.Tabs.Any(t => t.Id == _tabs.ActiveId && t.Private);
    private Tab Open(string url, bool activate = true, bool pinned = false, bool asleep = false, string? sessionId = null, bool? isPrivate = null)
    {
        var privateTab = AppPaths.Private || (isPrivate ?? PrivateActive);
        var session = privateTab ? "" : sessionId ?? _tabs.Tabs.FirstOrDefault(t => t.Id == _tabs.ActiveId)?.SessionId ?? "";
        // Une identité manquante ne doit jamais charger un site avec les connexions de la session principale.
        if (session.Length > 0 && (_sessions.Resolve(session).Length == 0 || session == _deletingSession))
        { url = Settings.DefaultHomePage; session = ""; }
        var tab = _tabs.Open(url, activate, pinned, _sessions.Resolve(session), isPrivate: privateTab);
        if (asleep) _pages.AddAsleep(tab);
        else _pages.Add(tab);
        return tab;
    }

    private void Close(string id)
    {
        _designTabs.Remove(id);
        if (_asking is not null && _tabs.ActiveId == id) HidePopup();
        if (_tabs.ActiveId == id)
        {
            // La fermeture revient à une page déjà chargée ; elle ne réveille jamais un onglet déchargé.
            var next = _recentTabs.LastOrDefault(other => other != id && !_pages.IsAsleep(other) && _tabs.Tabs.Any(t => t.Id == other));
            if (next is not null) Activate(next);
            else Open(_settings.HomePage, isPrivate: false);
        }
        // Fermer un onglet épinglé, ou rangé dans un groupe, ferme sa page, pas son emplacement : il reste à sa place,
        // déchargé, et se recharge quand on y revient. Pour le retirer pour de bon : le fermer une seconde fois quand il
        // est dans un groupe (il est alors déjà déchargé), ou « Détacher l'onglet » puis fermer quand il est épinglé.
        if (_tabs.Tabs.FirstOrDefault(t => t.Id == id) is { Private: false } kept && !_shortcutTabs.ContainsValue(id)
            && (kept.Pinned || (kept.GroupId is not null && !_pages.IsAsleep(id))))
        {
            if (_tabs.ActiveId == id)
            {
                var others = _tabs.Tabs.Where(t => !t.Pinned && t.Id != id).ToList();
                if ((others.FirstOrDefault(t => !_pages.IsAsleep(t.Id)) ?? others.FirstOrDefault()) is { } next) Activate(next.Id);
                else Open(_settings.HomePage);
            }
            _pages.Remove(id);
            _pages.AddAsleep(kept);
            _restored.Add(id); // son rechargement ne sera pas une nouvelle visite
            _split.OnTabClosed(id);
            _network.Forget(id);
            return;
        }
        if (!_tabs.Close(id)) return;
        _recentTabs.Remove(id);
        _restored.Remove(id);
        _pages.Remove(id);
        if (!_tabs.Tabs.Any(t => t.Private)) _privatePermissions.Clear();
        _favicons.Remove(id);
        _translationOriginals.Remove(id);
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
        if (blank is not null && _tabs.Tabs.Count > 1 && _tabs.Tabs.FirstOrDefault(t => t.Id == blank) is { } tab
            && (tab.Url is "about:blank" or "" || tab.Url == Settings.DefaultHomePage))
            Close(blank);
    }

    // Scinde l'onglet actif avec l'onglet donné, ou avec un nouvel onglet si aucun n'est donné.
    private void Split(string? otherId)
    {
        if (_tabs.ActiveId is not { } active) return;
        if (otherId is not null && (otherId == active || _tabs.Tabs.All(t => t.Id != otherId))) return;
        var other = otherId ?? Open(Settings.DefaultHomePage, activate: false).Id;
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
        IReadOnlyList<Visit> history = q.Length == 0 || _tabs.Tabs.Any(t => t.Id == tabId && t.Private) ? [] : _db.Suggest(q, 3);

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
            var json = JsonSerializer.Serialize(new
            {
                popup = "suggestions",
                items = items.Select(v => new { url = v.Url, title = v.Title, kind = v.Kind }),
            });
            if (_suggesting && _popup.IsOpen && _suggestTab == tabId) _popup.Post(json);
            else _popup.Show(x, y, width, json, focus: false); // le curseur reste dans la barre d'adresse
            _suggestTab = tabId;
            _suggesting = true;
            _bridge.Post(JsonSerializer.Serialize(new { @event = "suggestions", id = tabId, count = items.Count,items=items.Select(v=>new{url=v.Url,value=v.Kind=="search"?v.Title:v.Url}) }));
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
        DefaultRequestHeaders = { { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Sol" } },
    };
    private int _suggestSeq;

    // Menus affichés dans la vue posée sur la page : un libellé et son action ; un libellé vide est un séparateur.
    private List<(string Label, Action? Run)>? _menu;
    private Action? _menuDismissed; // appelé à la fermeture du menu, avec ou sans choix

    // Largeur réelle d'un panneau : celle qu'il demande, à l'échelle de l'interface. Sert à l'aligner par son bord droit.
    private double Scaled(double width) => width * _settings.UiFactor;

    private void ShowMenu(double x, double y, List<(string Label, Action? Run)> items, Action? dismissed = null)
    {
        HidePopup();
        (_menu, _menuDismissed) = (items, dismissed);
        _popup.Show(x, y, 260, JsonSerializer.Serialize(new { popup = "menu", items = items.Select(i => i.Label) }), focus: true);
    }
}
