using System.Runtime.InteropServices;
using System.Text.Json;
using Browser.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace Browser.App;

public sealed partial class BrowserController
{

    private void OpenPalette()
    {
        HidePopup();
        var generation = ++_paletteGeneration;
        var items = new List<object>();
        var active = _tabs.Tabs.FirstOrDefault(t => t.Id == _tabs.ActiveId);
        var x = (_window.Content as FrameworkElement)?.ActualWidth / 2 ?? 450;
        void Add(string title, Command command, string shortcut = "", string hint = "", string group = "actions", string keywords = "")
        {
            var key = $"{generation}:{items.Count}";
            _paletteCommands[key] = command;
            items.Add(new { id = key, title, hint, shortcut = shortcut.Length > 0 ? shortcut : BrowserShortcuts.TextFor(command.Type, Language.Current == "en"), group, keywords });
        }
        Add(T("Nouvel onglet", "New tab"), new("tab.open"), keywords: "onglet tab new nouveau");
        Add(T("Espaces de travail enregistrés", "Saved workspaces"), new("workspace.open"), keywords: "workspace snapshot disposition maquettes projet");
        Add(T("Rouvrir l’onglet fermé", "Reopen closed tab"), new("tab.reopen"), keywords: "reopen rouvrir fermer onglet");
        Add(T("Navigation privée", "Private browsing"), new("window.private"), keywords: "private incognito privee");
        Add(T("Ouvrir les notes", "Open notes"), new("notes.manage"), keywords: "notes scratchpad");
        Add(T("Ajouter une note rapide", "Add a quick note"), new("notes.open"), keywords: "notes create ajouter");
        Add(T("Historique", "History"), new("history.open", X: x, Y: 70), keywords: "history historique visites");
        Add(T("Favoris", "Bookmarks"), new("library.open", X: x, Y: 70), keywords: "bookmarks favoris");
        Add(T("Téléchargements", "Downloads"), new("downloads.open", X: x, Y: 70), keywords: "downloads telechargements");
        Add(T("Extensions", "Extensions"), new("extensions.open"), keywords: "extensions modules");
        Add(T("Réglages", "Settings"), new("settings.open", X: x, Y: 70), keywords: "settings parametres preferences");
        Add(T("Espaces et profils", "Spaces and profiles"), new("profile.menu", X: x, Y: 70), keywords: "profile espace compte");
        if (active is { Private: false }) Add(T("Session de cet onglet", "Tab session"), new("session.menu", Id: active.Id, X: x, Y: 70), keywords: "compte session cookies admin user invite");
        Add(T("Plein écran", "Full screen"), new("window.maximize"), keywords: "fullscreen plein ecran");
        if (active is not null)
        {
            Add(T("Fermer l’onglet", "Close tab"), new("tab.close", Id: active.Id), keywords: "close fermer onglet");
            Add(T("Recharger la page", "Reload page"), new("nav.reload", Id: active.Id), keywords: "reload refresh actualiser");
            if (_pages.CanGoBack(active.Id)) Add(T("Page précédente", "Previous page"), new("nav.back", Id: active.Id));
            if (_pages.CanGoForward(active.Id)) Add(T("Page suivante", "Next page"), new("nav.forward", Id: active.Id));
            Add(T("Afficher côte à côte", "Show side by side"), new("split.open", Id: active.Id), keywords: "split cote a cote partager");
            if (_split.Contains(active.Id)) Add(T("Quitter la vue côte à côte", "Exit side by side"), new("split.close"), keywords: "split close fermer");
            if (_pages.CoreOf(active.Id) is not null)
            {
                Add(T("Capture d’écran", "Screenshot"), new("capture.menu"), keywords: "capture screenshot image");
                Add(T("Inspecteur réseau", "Network inspector"), new("network.toggle"), keywords: "network reseau requetes");
                Add(T("DevTools natifs", "Native DevTools"), new("devtools.open"), keywords: "devtools debug elements css storage performance f12");
            }
            if (IsWebAddress(active.Url) && !active.Url.StartsWith("https://start.sol/", StringComparison.OrdinalIgnoreCase) && !_pages.IsPdf(active.Id))
            {
                Add(_reading.Contains(active.Id) ? T("Quitter le mode lecteur", "Exit reader mode") : T("Mode lecteur", "Reader mode"), new("reader.toggle", Id: active.Id), keywords: "reader lecture lire ecouter voix");
                Add(T("Traduire la page", "Translate page"), new("translation.open", Id: active.Id, X: x, Y: 70), keywords: "translate traduction francais anglais");
                Add(T("Inspecteur de design", "Design inspector"), new("design.toggle", Id: active.Id), keywords: "design css police couleur inspecter survol");
                Add(T("Lecture à voix haute", "Read aloud"), new("reader.listen", Id: active.Id), keywords: "speech ecouter voix pause reprendre");
                if (_reading.Contains(active.Id)) Add(T("Arrêter la lecture vocale", "Stop reading aloud"), new("reader.stop", Id: active.Id));
                Add(_db.IsBookmarked(active.Url) ? T("Retirer des favoris", "Remove bookmark") : T("Ajouter aux favoris", "Add bookmark"), new("bookmark.toggle", Id: active.Id), keywords: "bookmark favoris");
            }
        }
        for (var i = 0; i < _tabs.Tabs.Count; i++)
        {
            var tab = _tabs.Tabs[i];
            var slot = i < 8 ? i + 1 : i == _tabs.Tabs.Count - 1 ? 9 : 0;
            Add(tab.Title, new("tab.activate", Id: tab.Id), shortcut: BrowserShortcuts.TextFor($"tab.{slot}", Language.Current == "en"), hint: tab.Url == QuickNotes.PageUrl ? "sol://notes" : tab.Url, group: "tabs", keywords: "onglet tab");
        }
        _paletteItems = items;
        _popup.ShowCentered(500, PaletteJson(), top: 80);
    }

    private string PaletteJson() => JsonSerializer.Serialize(new
    {
        popup = "palette", items = _paletteItems,
        listHeight = Math.Clamp((((_window.Content as FrameworkElement)?.ActualHeight ?? 640) - 120) / _settings.UiFactor - 130, 60, 340),
    });

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
    // Raccourcis de la carte latérale : enregistrés dans les réglages, modifiables et retirables par clic droit.
    private SiteShortcut? ShortcutFor(string? url) =>
        SiteShortcut.DomainOf(url) is { Length: > 0 } domain ? _settings.Shortcuts.FirstOrDefault(s => SiteShortcut.DomainOf(s.Url) == domain) : null;

    // Page ouverte « dans » un raccourci : un onglet sans ligne dans la liste, que seul son raccourci affiche.
    private readonly Dictionary<SiteShortcut, string> _shortcutTabs = new();

    private void OpenShortcut(int index)
    {
        if (index < 0 || index >= _settings.Shortcuts.Count) return;
        var shortcut = _settings.Shortcuts[index];
        if (PrivateActive) { Open(shortcut.Url); return; }
        if (_shortcutTabs.TryGetValue(shortcut, out var open)) Activate(open);
        else _shortcutTabs[shortcut] = Open(shortcut.Url, pinned: true).Id; // épinglé : jamais groupé, épargné par « fermer les autres »
    }

    // Le site annonce son nom (« GitHub », « YouTube »…) : le raccourci le prend à la place du nom tiré de l'adresse.
    private async Task LearnShortcutNameAsync(string tabId)
    {
        if (_pages.IsPrivate(tabId)) return;
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
        if (_pages.IsPrivate(tabId)) return;
        if (data is null || data.Length > MaxShortcutIcon * 2) return;
        if (ShortcutFor(_tabs.Tabs.FirstOrDefault(t => t.Id == tabId)?.Url) is not { } shortcut || shortcut.Icon == data) return;
        shortcut.Icon = data;
        SaveShortcuts();
    }

    private const int MaxShortcutIcon = 100_000; // octets

    // Préférer les icônes déclarées par le site aux petites icônes historiques.
    private async Task FetchShortcutIconAsync(SiteShortcut shortcut)
    {
        var url = shortcut.Url;
        var icon = await FetchSiteIconAsync(url);
        if (icon is null || shortcut.Icon is not null || !_settings.Shortcuts.Contains(shortcut) || shortcut.Url != url) return;
        shortcut.Icon = icon;
        SaveShortcuts();
    }

    private static async Task<string?> FetchSiteIconAsync(string url)
    {
        if (!IsWeb(url) || !Uri.TryCreate(url, UriKind.Absolute, out var site)) return null;
        var candidates = new List<(Uri Url, int Size)>();
        try
        {
            using var page = await Http.GetAsync(site, HttpCompletionOption.ResponseHeadersRead);
            if (page.IsSuccessStatusCode && page.Content.Headers.ContentType?.MediaType == "text/html")
            {
                using var stream = await page.Content.ReadAsStreamAsync();
                using var reader = new StreamReader(stream);
                var buffer = new char[131072];
                var count = await reader.ReadBlockAsync(buffer, 0, buffer.Length);
                var html = new string(buffer, 0, count);
                foreach (System.Text.RegularExpressions.Match link in System.Text.RegularExpressions.Regex.Matches(html, @"<link\b[^>]*>", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    var attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (System.Text.RegularExpressions.Match attr in System.Text.RegularExpressions.Regex.Matches(link.Value, "([\\w-]+)\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s>]+))"))
                        attrs[attr.Groups[1].Value] = System.Net.WebUtility.HtmlDecode(attr.Groups[2].Success ? attr.Groups[2].Value : attr.Groups[3].Success ? attr.Groups[3].Value : attr.Groups[4].Value);
                    if (!attrs.TryGetValue("rel", out var rel) || !rel.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(r => r.Equals("icon", StringComparison.OrdinalIgnoreCase) || r.Equals("apple-touch-icon", StringComparison.OrdinalIgnoreCase))) continue;
                    if (!attrs.TryGetValue("href", out var href) || !Uri.TryCreate(page.RequestMessage?.RequestUri ?? site, href, out var iconUrl) || !IsWeb(iconUrl.AbsoluteUri)) continue;
                    var sizes = attrs.GetValueOrDefault("sizes", "");
                    var size = sizes.Equals("any", StringComparison.OrdinalIgnoreCase) || iconUrl.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ? int.MaxValue
                        : System.Text.RegularExpressions.Regex.Matches(sizes, @"\d+(?=x)").Select(m => int.TryParse(m.Value, out var n) ? n : 0).DefaultIfEmpty(rel.Contains("apple", StringComparison.OrdinalIgnoreCase) ? 180 : 16).Max();
                    candidates.Add((iconUrl, size));
                }
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException) { }
        var urls = candidates.OrderByDescending(c => c.Size).Select(c => c.Url).Concat(new[] { new Uri(site, "/apple-touch-icon.png"), new Uri(site, "/favicon.ico") }).Distinct().Take(6);
        foreach (var iconUrl in urls)
        try
        {
            using var response = await Http.GetAsync(iconUrl);
            var type = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!response.IsSuccessStatusCode || !type.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                || response.Content.Headers.ContentLength is > MaxShortcutIcon) continue;
            var bytes = await response.Content.ReadAsByteArrayAsync();
            if (bytes.Length is > 0 and <= MaxShortcutIcon) return $"data:{type};base64,{Convert.ToBase64String(bytes)}";
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { }
        return null;
    }
    private void Activate(string id)
    {
        if(_pages.PreviewSourceId is {} source&&source!=id)_pages.ClosePreview();
        if (_asking is not null && _tabs.ActiveId != id) HidePopup();
        if (!_tabs.Activate(id)) return;
        // Un onglet rangé dans un groupe replié : le groupe se déplie pour le montrer.
        if (!_restoringSession && _tabs.Tabs.First(t => t.Id == id).GroupId is { } groupId && _tabs.Groups.First(g => g.Id == groupId).Collapsed)
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
}
