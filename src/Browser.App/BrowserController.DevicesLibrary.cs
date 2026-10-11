using System.Runtime.InteropServices;
using System.Text.Json;
using Browser.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace Browser.App;

public sealed partial class BrowserController
{

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
                    .. Device.All.Where(d => d.Kind != "desktop").Select(d => (DeviceLabel(d.Name), (Action?)(() => _afterMenu = () => _pages.SetDevice(tabId, d.Name)))),
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
            foreach (var mockId in _network.ActiveMocks)
                if (_pages.CoreOf(mockId) is { } core) _ = _network.MockAsync(mockId,core,null);
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

    private async void OpenContextMenu(string tabId, double x, double y, Microsoft.Web.WebView2.Core.CoreWebView2ContextMenuRequestedEventArgs e)
    {
        e.Handled = true;
        var deferral = e.GetDeferral();
        try
        {
            var items = new List<(string Label, Action? Run)>();
            var aiText = _settings.SolAiEnabled && _ai.Available ? await _pages.CaptureAiAsync(tabId) : null;
            if (_settings.SolAiEnabled && _ai.Available && !e.ContextMenuTarget.IsEditable && e.ContextMenuTarget.HasSelection && e.ContextMenuTarget.SelectionText is { Length: > 0 and <= 50_000 } aiSelection)
                aiText = new(aiSelection, false, "", "", _tabs.Tabs.FirstOrDefault(t => t.Id == tabId)?.Title ?? "");
            var correction = e.ContextMenuTarget.IsEditable && e.ContextMenuTarget.FrameUri == _pages.CoreOf(tabId)?.Source
                ? await _pages.SpellingSuggestions(tabId) : (Word: "", Suggestions: Array.Empty<string>());
            foreach (var suggestion in correction.Suggestions)
            {
                var replacement = suggestion;
                items.Add((replacement, () => _pages.CorrectSpelling(tabId, correction.Word, replacement)));
            }
            if (items.Count > 0) items.Add(("", null));
            var link = e.ContextMenuTarget.HasLinkUri ? e.ContextMenuTarget.LinkUri : null;
            if (e.ContextMenuTarget.HasSelection && e.ContextMenuTarget.SelectionText is { Length: > 0 and <= 50_000 } selection)
            {
                var source = _tabs.Tabs.FirstOrDefault(t => t.Id == tabId);
                items.Add((T("Ajouter à une note…", "Add to a note…"), () => _afterMenu = () =>
                {
                    var url = source?.Url ?? "";
                    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0 || url.Length > 4096) url = "";
                    _noteSelection = new("", (source?.Title ?? "")[..Math.Min(source?.Title.Length ?? 0, 120)], selection, url, DateTimeOffset.UtcNow);
                    HidePopup();
                    _popup.Show(x, y, 360, JsonSerializer.Serialize(new { popup = "notes-select",
                        items = _notes.Items.Select(n => new { id = n.Id, title = n.Title.Length > 0 ? n.Title : n.Text[..Math.Min(80, n.Text.Length)] }) }), focus: true);
                }));
            }
            if (link is not null && IsWeb(link))
            {
                items.Add((T("Ouvrir le lien dans un nouvel onglet", "Open link in a new tab"), () => OpenLink(tabId, link)));
                items.Add((T("Ouvrir le lien en vue scindée", "Open link in split view"), () => Split(OpenLink(tabId, link, activate: false).Id)));
                items.Add(("", null));
            }
            foreach (var item in e.MenuItems)
            {
                if (item.Kind == Microsoft.Web.WebView2.Core.CoreWebView2ContextMenuItemKind.Separator)
                {
                    if (items.Count > 0 && items[^1].Label != "") items.Add(("", null));
                }
                else if (item.Kind == Microsoft.Web.WebView2.Core.CoreWebView2ContextMenuItemKind.Command && item.IsEnabled && NativeCommands.Contains(item.Name) && !string.IsNullOrWhiteSpace(item.Label))
                {
                    var commandId = item.CommandId;
                    items.Add((item.Label.Replace("&", ""), () => e.SelectedCommandId = commandId));
                }
            }
            if (items.Count > 0 && items[^1].Label != "") items.Add(("", null));
            if (_settings.SolAiEnabled && _ai.Available && (aiText is { Text.Length: > 0 } || aiText?.Editable == true || !e.ContextMenuTarget.IsEditable))
                items.Add(("Sol AI…", () => _afterMenu = () => _ = OpenPageAiAsync(tabId, aiText)));
            items.Add((T("Capturer une zone", "Capture an area"), () => Capture("region")));
            items.AddRange(DeviceEntries(tabId, x, y));
            if (PermissionStore.OriginOf(_pages.CoreOf(tabId)?.Source ?? "") is not null && !_pages.CoreOf(tabId)!.Source.StartsWith("https://start.sol/"))
                items.Add((T("Vider la session de ce site…", "Clear this site's session…"), () => _afterMenu = () => AskResetSite(tabId)));
            items.Add((_tools.IsOpen ? T("Masquer l'inspecteur réseau", "Hide network inspector") : T("Inspecter le réseau", "Inspect network"), () => _afterMenu = ToggleNetwork));

            // Le moteur attend notre réponse : il est toujours libéré à la fermeture du menu, avec ou sans choix.
            ShowMenu(x, y, items, deferral.Complete);
        }
        catch (Exception error) when (error is COMException or InvalidOperationException)
        { deferral.Complete(); }
    }

    private async Task OpenTranslationAsync(string id, double x, double y)
    {
        if (_tabs.Tabs.FirstOrDefault(t => t.Id == id) is not { } tab || tab.Url == Settings.DefaultHomePage || !IsWeb(tab.Url)) return;
        HidePopup();
        var request = _translationRequest;
        var url = tab.Url;
        string? pageLanguage = null;
        try
        {
            if (_pages.CoreOf(id) is { } core)
            {
                using var result = JsonDocument.Parse(await core.ExecuteScriptAsync("""
                    (() => {
                      const translated = location.hostname === 'translate.google.com' || location.hostname.endsWith('.translate.goog');
                      const query = new URLSearchParams(location.search);
                      return (translated && (query.get('_x_tr_tl') || query.get('tl'))) || document.documentElement.lang ||
                        document.body?.getAttribute('lang') || document.querySelector('meta[http-equiv="content-language" i]')?.content || '';
                    })()
                    """));
                if (result.RootElement.ValueKind == JsonValueKind.String) pageLanguage = result.RootElement.GetString();
            }
        }
        catch (Exception e) when (e is COMException or InvalidOperationException or JsonException) { /* langue inconnue : celle de Sol */ }
        if (_closingWindow || request != _translationRequest || _tabs.ActiveId != id || _tabs.Tabs.FirstOrDefault(t => t.Id == id)?.Url != url) return;
        _translationSource = (id, url);
        var original = PageTranslation.IsGooglePage(url) ? _translationOriginals.GetValueOrDefault(id) : null;
        var target = PageTranslation.TargetLanguage(pageLanguage, Language.Current);
        _popup.Show(x - Scaled(360), y, 360, JsonSerializer.Serialize(new
        {
            popup = "translation", id, target, original = original is not null,
            available = PageTranslation.GoogleUrl(original ?? url, target) is not null,
        }), focus: true);
    }

    private string NotesJson() => JsonSerializer.Serialize(new { popup = "notes", items = _notes.Items,
        draft = _noteDraft, error = _noteError, privateMode = AppPaths.Private, aiEnabled = _settings.SolAiEnabled && _ai.Available }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

    private void OpenNotes(double x, double y)
    {
        HidePopup();
        _notesPopup = true;
        _popup.ShowCentered(430, NotesJson());
    }

    private void PublishNotes()
    {
        var json = NotesJson();
        if (_notesPopup) _popup.Post(json);
        foreach (var tab in _tabs.Tabs.Where(t => t.Url == QuickNotes.PageUrl)) _pages.PostStart(tab.Id, json);
    }

    private async Task ExportNotesAsync()
    {
        var text = _notes.Export();
        if (text.Length == 0) return;
        HidePopup();
        try
        {
            var picker = new Windows.Storage.Pickers.FileSavePicker
            { SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary, SuggestedFileName = "Sol notes" };
            picker.FileTypeChoices.Add(T("Texte", "Text"), [".txt"]);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(_window));
            if (await picker.PickSaveFileAsync() is { } file) await Windows.Storage.FileIO.WriteTextAsync(file, text);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or COMException)
        { Notice(T("Export impossible", "Export failed"), T("Les notes sont conservées. Choisissez un autre emplacement.", "Your notes are kept. Choose another location.")); }
    }

    private void OpenLibrary(double x, double y, bool history = false)
    {
        // Recliquer l'icône referme le panneau : le clic lui fait d'abord perdre le curseur, donc il vient de se fermer.
        // ponytail: seuil de temps ; à remplacer par un état explicite si d'autres menus s'ajoutent.
        if (ClosesPanel("library")) return;
        const double width = 340;
        _suggesting = false;
        _popup.Show(x - Scaled(width), y, width, LibraryJson("", 0, history ? "history" : null), focus: true, above: true);
    }

    private string LibraryJson(string query, int requestedPage, string? tab = null)
    {
        const int pageSize = 100;
        var total = _db.HistoryCount(query);
        var page = Math.Clamp(requestedPage, 0, Math.Max(0, (total - 1) / pageSize));
        return JsonSerializer.Serialize(new
        {
            popup = "library",
            tab, query, page, pageSize, total,
            bookmarks = _db.Bookmarks().Where(b => b.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || b.Url.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Select(b => new { url = b.Url, title = b.Title }),
            history = _db.History(query, pageSize, page * pageSize).Select(v => new { url = v.Url, title = v.Title }),
        });
    }

    private void RecordVisit(string id)
    {
        if (_closingWindow) return;
        if (_restored.Remove(id)) return; // rouvrir une session n'est pas une visite
        if (AppPaths.Private) return;     // navigation privée : aucun historique
        if (_tabs.Tabs.FirstOrDefault(t => t.Id == id) is { } tab && IsWeb(tab.Url) && tab.Url is not (Settings.DefaultHomePage or QuickNotes.PageUrl))
            _db.RecordVisit(tab.Url, tab.Title, DateTimeOffset.UtcNow);
    }

    // Les onglets rouverts restent endormis : seul celui qu'on affiche charge sa page (voir PageHost.AddAsleep).
    private void RestoreSession()
    {
        _restoringSession = true;
        try
        {
        var session = JsonFileStore.Load<Session>(AppPaths.Session);
        var tabs = session.Urls.Select((url, i) => Open(url, activate: false, pinned: i < session.PinnedCount, asleep: true,
            sessionId: i < session.SessionIds.Count ? session.SessionIds[i] : "")).ToList();
        if (tabs.Count == 0) { Open(_settings.HomePage); return; }
        foreach (var tab in tabs) _restored.Add(tab.Id);
        for (var i = 0; i < tabs.Count && i < session.TranslationOriginals.Count; i++)
            if (PageTranslation.GoogleUrl(session.TranslationOriginals[i], "fr") is not null)
                _translationOriginals[tabs[i].Id] = session.TranslationOriginals[i];
        for (var i = 0; i < tabs.Count && i < session.Titles.Count; i++)
            if (session.Titles[i].Length > 0) _tabs.Update(tabs[i].Id, title: session.Titles[i]);
        // Pages qui étaient ouvertes dans un raccourci : elles y retournent.
        for (var i = 0; i < tabs.Count && i < session.ShortcutOf.Count; i++)
            if (tabs[i].Pinned && session.ShortcutOf[i] is { } rank && rank >= 0 && rank < _settings.Shortcuts.Count)
                _shortcutTabs.TryAdd(_settings.Shortcuts[rank], tabs[i].Id);
        // Icônes des sites : affichées tout de suite, sans attendre que chaque onglet soit rechargé.
        for (var i = 0; i < tabs.Count && i < session.Icons.Count; i++)
            if (session.Icons[i].StartsWith("data:image/", StringComparison.Ordinal)) _favicons[tabs[i].Id] = session.Icons[i];
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

        if (session.ActiveIndex >= 0 && session.ActiveIndex < tabs.Count) Activate(tabs[session.ActiveIndex].Id);
        else Open(_settings.HomePage);
        // Les anciennes vues scindées ne chargent pas une seconde page inactive au redémarrage.
        }
        finally { _restoringSession = false; }
    }

    private const int MaxSessionIcon = 48_000; // caractères : une icône plus lourde n'est pas gardée dans la session
}
