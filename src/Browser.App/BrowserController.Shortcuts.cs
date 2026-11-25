using System.Runtime.InteropServices;
using System.Text.Json;
using Browser.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace Browser.App;

public sealed partial class BrowserController
{
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
        var icon = KnownSiteIcon(url) ?? (await FetchSiteIconAsync(url))?.Data;
        if (icon is null || shortcut.Icon is not null || !_settings.Shortcuts.Contains(shortcut) || shortcut.Url != url) return;
        shortcut.Icon = icon;
        SaveShortcuts();
    }

    private static async Task<SiteIcon?> FetchSiteIconAsync(string url)
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
            if (bytes.Length is > 0 and <= MaxShortcutIcon
                && await PrepareSiteIconAsync($"data:{type};base64,{Convert.ToBase64String(bytes)}") is { } icon) return icon;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { }
        return null;
    }
    private void Activate(string id)
    {
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
