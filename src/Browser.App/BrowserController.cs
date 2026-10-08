using System.Runtime.InteropServices;
using System.Text.Json;
using Browser.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace Browser.App;

/// <summary>Exécute les commandes de l'interface sur Browser.Core et les vues de pages, puis renvoie l'état.</summary>
public sealed partial class BrowserController
{
    private readonly MainWindow _window;
    private readonly PageHost _pages;
    private readonly UiBridge _bridge;
    private readonly PopupHost _popup;
    private readonly Settings _settings;
    private readonly SolAiService _ai = new();
    private readonly BrowserDb _db;
    private readonly CaptureService _capture;
    public PopupHost? CapturePanel { get; init; }
    private readonly TabManager _tabs = new();
    public bool UseNativeZoom => _tabs.ActiveId is { } id && _pages.IsPdf(id);
    public event Action? NativeZoomChanged;
    private bool _nativeZoom;
    private readonly SplitLayout _split = new();
    private readonly HashSet<string> _restored = new(); // onglets dont le premier chargement vient de la session
    private readonly List<string> _recentTabs = new();
    private bool _restoringSession, _closingWindow;
    private readonly DispatcherTimer _sessionSave = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly Dictionary<string, string?> _favicons = new();
    private DateTime _libraryClosedAt;
    private IReadOnlyList<Suggestion> _suggestions = [];
    private string? _suggestTab;
    private bool _suggesting; // la vue posée sur la page affiche des suggestions
    private (string Id, string Url)? _translationSource;
    private readonly Dictionary<string, string> _translationOriginals = new();
    private int _translationRequest;
    private readonly HashSet<string> _reading = new();
    private readonly HashSet<string> _readerBusy = new();
    private string? _readerScript;
    private string? _readerTheme;
    private readonly HashSet<string> _designTabs = new();
    private string? _designScript;
    private readonly Dictionary<string, Command> _paletteCommands = new();
    private int _paletteGeneration;
    private List<object>? _paletteItems;
    private readonly QuickNotes _notes = new(AppPaths.Private ? null : Path.Combine(AppPaths.DataDir, "notes.json"));
    private QuickNote _noteDraft = new("", "", "", "", DateTimeOffset.UtcNow);
    private string? _noteError;
    private QuickNote? _noteSelection;
    private bool _notesPopup;
    private bool NoteDraftChanged => (_noteDraft.Text.Length > 0 || _noteDraft.Title.Length > 0)
        && !_notes.Items.Any(n => n.Id == _noteDraft.Id && n.Text == _noteDraft.Text && n.Title == _noteDraft.Title);

    private readonly DockHost _dock;

    private readonly PanelHost _tools; // panneau du bas : outils de développement
    private readonly NetworkInspector _network;

    private readonly WelcomeHost _welcome;

    public BrowserController(MainWindow window, PageHost pages, UiBridge bridge, PopupHost popup, DockHost dock, PanelHost panel, WelcomeHost welcome, Settings settings)
    {
        _welcome = welcome;
        (_window, _pages, _bridge, _popup, _dock, _tools, _settings) = (window, pages, bridge, popup, dock, panel, settings);
        _ai.SetKeepMinutes(settings.SolAiKeepMinutes);
        _pages.PagePressed += DismissAiPopup;
        _network = new NetworkInspector(panel.Post);
        _sessionSave.Tick += (_, _) => { _sessionSave.Stop(); SaveSession(); };
        Directory.CreateDirectory(AppPaths.DataDir);
        // Sol AI reste désactivé pendant cette phase, y compris après une mise à jour.
        if (_settings.SolAiEnabled)
        {
            _settings.SolAiEnabled = false;
            JsonFileStore.Save(AppPaths.Settings, _settings);
        }
        _db = new BrowserDb(AppPaths.Database);
        _capture = new CaptureService(window, json => CapturePanel?.Post(json));
        _ai.Changed += () => _window.DispatcherQueue.TryEnqueue(() =>
        {
            if (_closingWindow) return;
            if(_ai.Optimization is { } optimization && _ai.Models.Any(m=>m.Id==optimization.Id && m.Cached) && _settings.SolAiModel!=optimization.Id) {
                _settings.SolAiModel=optimization.Id;JsonFileStore.Save(AppPaths.Settings,_settings);
            }
            if (!_ai.Models.Any(m => m.Id == _settings.SolAiModel && m.Cached))
            {
                var installed = _ai.Models.FirstOrDefault(m => m.Cached)?.Id ?? "";
                if (_settings.SolAiModel != installed) { _settings.SolAiModel = installed; JsonFileStore.Save(AppPaths.Settings, _settings); }
            }
            if (_settingsOpen) _popup.Post(SettingsJson());
            PublishAi();
        });

        _pages.PageChanged += (id, url, title) =>
        {
            if (url is not null && _reading.Remove(id)) _ = EndReaderOnNavigationAsync(id);
            if (url is not null && _designTabs.Remove(id)) _ = EndDesignOnNavigationAsync(id);
            if (url is not null && !PageTranslation.IsGooglePage(url) && _translationOriginals.GetValueOrDefault(id) != url)
                _translationOriginals.Remove(id);
            _tabs.Update(id, url, title); Refresh();
        };
        _pages.RuntimeChanged += Refresh;
        _sessions.Seed();
        _pages.RouteNavigation = RouteSiteNavigation;
        _pages.NavigationSettled += ApplySiteNavigation;
        _pages.CreatePopupTab = (source, url) =>
        {
            var opener = _tabs.Tabs.FirstOrDefault(t => t.Id == source);
            if (opener is null) return null;
            var tab = _tabs.Open(url, sessionId: opener.SessionId);
            Refresh();
            return tab;
        };
        _pages.PopupClosed += id => { Close(id); Refresh(); };
        _pages.SessionFailed += id => Notice(T("Session indisponible", "Session unavailable"), T("Sol n’a pas pu ouvrir cette session. Réessayez en activant l’onglet ; aucune autre session n’a été utilisée.", "Sol could not open this session. Activate the tab to retry; no other session was used."));
        _pages.Loaded += RecordVisit;
        _pages.StoreInstallRequested += url => _ = InstallStoreExtensionAsync(url);
        _pages.StoreManageRequested += () => _ = OpenExtensionsAsync();
        _pages.StartRequested += (id, command) =>
        {
            if (_closingWindow) return;
            if (command.Type == "start.ready")
            {
                _bridge.BroadcastTheme();
                _pages.PostStart(id, StartJson());
                foreach (var url in _db.Bookmarks().Select(b => b.Url).Concat(_settings.Shortcuts.Select(s => s.Url)).Distinct().Take(24)) FetchStartIcon(url);
                if (!AppPaths.Private)
                    foreach (var visit in _db.RecentHistory(12)) FetchStartIcon(visit.Url);
                LoadStartPreviews();
            }
            else if (command.Type == "start.navigate") _pages.Navigate(id, Resolve(command.Input!));
            else if (_tabs.Groups.Any(g => g.Id == command.Id) && _tabs.Tabs.FirstOrDefault(t => t.GroupId == command.Id) is { } tab)
            { Activate(tab.Id); Refresh(); }
        };
        _pages.NotesRequested += (id, command) =>
        {
            if (_closingWindow) return;
            if (command.Type == "notes.ready") { _bridge.BroadcastTheme(); _pages.PostStart(id, NotesJson()); }
            else Execute(command);
        };
        _pages.PageFocused += id => { if (!_closingWindow && _tabs.ActiveId != id) { Activate(id); Refresh(); } };
        _bridge.ThemePublished += json =>
        {
            if (_closingWindow) return;
            _readerTheme = json;
            foreach (var readerId in _reading.ToArray()) _ = UpdateReaderThemeAsync(readerId);
            foreach (var designId in _designTabs.ToArray()) _ = UpdateDesignThemeAsync(designId);
            using var theme = JsonDocument.Parse(json);
            _pages.SpellingAccent = theme.RootElement.TryGetProperty("accent", out var accent) && accent.ValueKind == JsonValueKind.String
                ? accent.GetString()! : "#6495ed";
            foreach (var tab in _tabs.Tabs.Where(t => t.Url is Settings.DefaultHomePage or QuickNotes.PageUrl)) _pages.PostStart(tab.Id, json);
        };
        _pages.Discarded += id => { _restored.Add(id); _network.Forget(id); Refresh(); }; // son rechargement ne sera pas une nouvelle visite
        _pages.KeepAwake = id => _split.Contains(id) || _downloads.Any(d => d.TabId == id && d.Op.State == Microsoft.Web.WebView2.Core.CoreWebView2DownloadState.InProgress);
        _pages.PipReturnRequested += id => {
            Activate(id); Refresh();
            if (Presenter?.State == OverlappedPresenterState.Minimized) Presenter.Restore();
            _window.Activate();
        };
        _pages.Loaded += id =>
        {
            if (!_pinsInitialized) { _ = RefreshPinnedExtensionsAsync(); _ = PrepareExtensionsAsync(); _ = UpdateExtensionsLaterAsync(); }
            _ = LearnShortcutNameAsync(id);
            if (_tabs.Tabs.FirstOrDefault(t => t.Id == id) is { } tab && IsWeb(tab.Url) && tab.Url is not (Settings.DefaultHomePage or QuickNotes.PageUrl)) FetchStartIcon(tab.Url);
            Refresh();
            if (_tabs.Tabs.Any(t => t.Url == Settings.DefaultHomePage && !_pages.IsAsleep(t.Id))) LoadStartPreviews();
        };
        _pages.FaviconChanged += (id, data) =>
        {
            if (_tabs.Tabs.FirstOrDefault(t => t.Id == id) is { } tab)
                data = _startIcons.GetValueOrDefault(SiteShortcut.DomainOf(tab.Url)) ?? data;
            _favicons[id] = data; PostFavicon(id); LearnShortcutIcon(id, data);
        };
        _pages.ContextMenuRequested += OpenContextMenu;
        _pages.DeviceAreaMenuRequested += (tabId, x, y) => ShowMenu(x, y, DeviceEntries(tabId, x, y));
        _pages.StatusChanged += (id, text) =>
        {
            if (id == _tabs.ActiveId || _split.Contains(id))
                _bridge.Post(JsonSerializer.Serialize(new { @event = "status", text }));
        };
        _pages.DownloadStarting += OnDownloadStarting;
        _pages.PermissionRequested += OnPermissionRequested;
        _pages.Navigating += core => { if (_asking is { } pending && pending.Core == core) HidePopup(); };
        _window.AppWindow.Changed += (_, e) => { if (e.DidPresenterChange || e.DidSizeChange) { _popup.Rescale(); if (_paletteItems is not null) _popup.Post(PaletteJson()); Refresh(); } };
        // Mise à jour de Sol : l'encart en bas à droite et les réglages suivent le téléchargement, puis proposent de redémarrer.
        Program.UpdateChanged += () => _window.DispatcherQueue.TryEnqueue(() =>
        {
            UpdateDock();
            if (_settingsOpen) _popup.Post(SettingsJson());
        });
        _window.Closed += (_, _) =>
        {
            _closingWindow = true;
            _ai.Dispose();
            _pages.StopSpelling();
            _capture.CancelSelection();
            _startPreviewCancel.Cancel();
            _sessionSave.Stop();
            HidePopup(); // terminer les demandes de permission encore en attente
            SaveSession();
            _db.Dispose();
            // Fenêtre privée : ses données sont effacées dès maintenant, par un processus qui attend la fin de celui-ci.
            if (AppPaths.Private && Environment.ProcessPath is { } exe)
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, $"--cleanup={AppPaths.Profile}") { UseShellExecute = false, CreateNoWindow = true }); }
                catch (System.ComponentModel.Win32Exception) { /* elles seront effacées au prochain démarrage */ }
            }
        };
        ApplyComparison();
        Profiles.DeleteClosedTemporary(AppPaths.BaseDir, IsProfileOpen); // sessions isolées dont la fenêtre est fermée
        if(_settings.SolAiEnabled)_ai.Prepare(_settings.SolAiModel);
    }

    private static string T(string fr, string en) => Language.T(fr, en);
    private string ProfileName(string id) =>
        id != Profiles.Default ? Profiles.NameOf(AppPaths.BaseDir, id) : _settings.ProfileName.Length > 0 ? _settings.ProfileName : T("Défaut", "Default");

    public void Execute(Command c)
    {
        if (c.Type.StartsWith("ai.", StringComparison.Ordinal)) return; // Sol AI temporairement masqué.
        var id = c.Id ?? "";
        if (c.Type is not ("popup.size" or "dock.size" or "split.resize")) DebugLog.Write($"commande {c.Type} {c.Id} {c.Index}");
        switch (c.Type)
        {
            case "ai.enable":
                if (!_ai.Available || c.Index is not (0 or 1)) return;
                _settings.SolAiEnabled = c.Index == 1;
                if (!_settings.SolAiEnabled) { _ai.Cancel(); _ai.Release(); }
                JsonFileStore.Save(AppPaths.Settings, _settings);
                if (_settingsOpen) _popup.Post(SettingsJson());
                if (_settings.SolAiEnabled) _ = PrepareAiAsync("catalog",null);
                Refresh(); PublishNotes();
                return;
            case "ai.select":
                if (!_settings.SolAiEnabled || _ai.Busy) return;
                if (!_ai.CanUse(id)) { if (_settingsOpen) _popup.Post(SettingsJson()); return; }
                _settings.SolAiModel = id; JsonFileStore.Save(AppPaths.Settings, _settings);
                if (_settingsOpen) _popup.Post(SettingsJson()); return;
            case "ai.catalog":
                if (_settings.SolAiEnabled) _ = _ai.RunAsync("catalog"); return;
            case "ai.download" or "ai.remove" or "ai.check" or "ai.optimize":
                if (!_settings.SolAiEnabled || _ai.Busy) return;
                if(c.Type is "ai.download" or "ai.optimize")_ = PrepareAiAsync(c.Type[3..],id);
                else _ = _ai.RunAsync(c.Type[3..], id); return;
            case "ai.cancel": _ai.Cancel(); return;
            case "ai.open": _ = OpenPageAiAsync(id); return;
            case "ai.notes": OpenNoteAi(c); return;
            case "ai.run": _ = RunTextAiAsync(c); return;
            case "ai.apply": _ = ApplyTextAiAsync(); return;
            case "ai.copy": CopyAi(); return;
            case "ai.note": NoteAi(); return;
            case "ai.settings": OpenAiSettings(); return;
            case "palette.open": OpenPalette(); return;
            case "palette.pick":
                if (_paletteCommands.TryGetValue(id, out var chosen))
                { HidePopup(); Execute(chosen); }
                return;
            case "reader.toggle": _ = ToggleReaderAsync(id); break;
            case "design.toggle": _ = ToggleDesignAsync(id); return;
            case "reader.listen" or "reader.stop": _ = ReaderSpeechAsync(id, c.Type == "reader.stop"); return;
            case "bookmark.toggle":
                if (_tabs.Tabs.FirstOrDefault(t => t.Id == id) is { } mark && IsWeb(mark.Url) && !mark.Url.StartsWith("https://start.sol/", StringComparison.OrdinalIgnoreCase))
                { if (_db.IsBookmarked(mark.Url)) _db.RemoveBookmark(mark.Url); else _db.AddBookmark(mark.Url, mark.Title); }
                break;
            case "notes.manage":
                HidePopup();
                if (_tabs.Tabs.FirstOrDefault(t => t.Url == QuickNotes.PageUrl) is { } notesTab) Activate(notesTab.Id);
                else Open(QuickNotes.PageUrl);
                Refresh(); return;
            case "notes.visit":
                if (_notes.Items.FirstOrDefault(n => n.Id == id)?.Url is { Length: > 0 } noteUrl && Uri.TryCreate(noteUrl, UriKind.Absolute, out var noteUri) && noteUri.Scheme is "https" or "http" && noteUri.UserInfo.Length == 0)
                { Open(noteUrl); Refresh(); } return;
            case "notes.link":
                if (c.Url is { Length: <= 4096 } link && Uri.TryCreate(link, UriKind.Absolute, out var linkUri) && linkUri.Scheme is "http" or "https" && linkUri.UserInfo.Length == 0)
                { Open(link); Refresh(); } return;
            case "notes.open": OpenNotes(c.X ?? 450, c.Y ?? 60); return;
            case "notes.draft":
                if (c.Title!.Length <= 120 && c.Input!.Length <= 50_000 && c.Id == _noteDraft.Id)
                    _noteDraft = _noteDraft with { Title = c.Title, Text = c.Input };
                return;
            case "notes.edit":
                if (NoteDraftChanged)
                { _noteError = T("Enregistrez ou abandonnez le brouillon avant de changer de note.", "Save or discard your draft before changing notes."); PublishNotes(); return; }
                _noteDraft = _notes.Items.FirstOrDefault(n => n.Id == id) ?? new("", "", "", "", DateTimeOffset.UtcNow);
                _noteError = null; PublishNotes(); return;
            case "notes.discard":
                _noteDraft = new("", "", "", "", DateTimeOffset.UtcNow); _noteError = null; PublishNotes(); return;
            case "notes.save":
                if (_notes.Save(_noteDraft.Id, _noteDraft.Title, _noteDraft.Text, _noteDraft.Url))
                { _noteDraft = _notesPopup ? new("", "", "", "", DateTimeOffset.UtcNow) : _notes.Items[0]; _noteError = null; }
                else _noteError = T("La note n’a pas été enregistrée. Vérifiez son contenu et l’espace disque.", "The note could not be saved. Check its content and disk space.");
                PublishNotes(); return;
            case "notes.delete":
                if (!_notes.Delete(id)) _noteError = T("Impossible de supprimer cette note.", "Could not delete this note.");
                else { _noteError = null; if (_noteDraft.Id == id) _noteDraft = new("", "", "", "", DateTimeOffset.UtcNow); }
                PublishNotes(); return;
            case "notes.export": _ = ExportNotesAsync(); return;
            case "notes.select":
                if (_noteSelection is not { } passage) return;
                if (id.Length == 0)
                {
                    if (NoteDraftChanged)
                    { Notice(T("Un brouillon est en cours", "A draft is open"), T("Enregistrez votre brouillon dans les notes rapides avant de créer une nouvelle note.", "Save your quick notes draft before creating a new note.")); return; }
                    _noteDraft = passage; _noteSelection = null;
                }
                else if (_notes.Items.FirstOrDefault(n => n.Id == id) is { } destination)
                {
                    var added = destination.Text + "\n\n" + passage.Text + (passage.Url.Length > 0 ? "\nSource : " + passage.Url : "");
                    if (!_notes.Save(id, destination.Title, added, destination.Url))
                    { Notice(T("Ajout impossible", "Could not add passage"), T("La note est trop longue ou le disque est indisponible. Son contenu est conservé.", "The note is too long or the disk is unavailable. Its content is kept.")); return; }
                    if (_noteDraft.Id == id) _noteDraft = _noteDraft with { Text = _noteDraft.Text + "\n\n" + passage.Text + (passage.Url.Length > 0 ? "\nSource : " + passage.Url : ""), Updated = DateTimeOffset.UtcNow };
                    _noteSelection = null;
                }
                else return;
                _noteError = null; OpenNotes(450, 60); return;
            // Vue posée sur la page : ces commandes ne changent pas l'état des onglets.
            case "suggest": Suggest(id, c.Query!, c.X!.Value, c.Y!.Value, c.Width!.Value); return;
            case "library.open": OpenLibrary(c.X!.Value, c.Y!.Value); return;
            case "history.open": OpenLibrary(c.X!.Value, c.Y!.Value, history: true); return;
            case "library.search":
                if (_popup.IsOpen && (_panel == "library" || _panelNext == "library")) _popup.Post(LibraryJson(c.Query!, c.Index!.Value));
                return;
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
            case "spelling.languages":
                _ = Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:regionlanguage"));
                return;
            case "translation.open":
                _ = OpenTranslationAsync(id, c.X!.Value, c.Y!.Value);
                return;
            case "translation.run":
                if (_translationSource is { } requested && requested.Id == id
                    && _tabs.Tabs.FirstOrDefault(t => t.Id == id) is { } sourceTab
                    && (sourceTab.Url == requested.Url || PageTranslation.IsGooglePage(sourceTab.Url) && PageTranslation.IsGooglePage(requested.Url))
                    && PageTranslation.GoogleUrl(PageTranslation.IsGooglePage(sourceTab.Url) ? _translationOriginals.GetValueOrDefault(id, sourceTab.Url) : sourceTab.Url, c.Query!) is { } translatedUrl)
                {
                    if (!PageTranslation.IsGooglePage(sourceTab.Url)) _translationOriginals[id] = sourceTab.Url;
                    HidePopup(); _pages.Navigate(id, translatedUrl, route: false); Refresh();
                }
                return;
            case "translation.original":
                if (_translationSource is { } page && page.Id == id && _translationOriginals.TryGetValue(id, out var originalUrl)
                    && _tabs.Tabs.FirstOrDefault(t => t.Id == id) is { } originalTab
                    && (originalTab.Url == page.Url || PageTranslation.IsGooglePage(originalTab.Url)))
                { HidePopup(); _pages.Navigate(id, originalUrl); Refresh(); }
                return;
            case "settings.set":
                if (id == "aiThinking")
                {
                    if (c.Index is not (0 or 1)) return;
                    _settings.SolAiThinking = c.Index == 1; JsonFileStore.Save(AppPaths.Settings, _settings);
                    if (_settingsOpen) _popup.Post(SettingsJson());
                    PublishAi(); return;
                }
                if (id == "aiKeepMinutes")
                {
                    if (c.Index is not (>= 0 and <= 10080)) return;
                    _settings.SolAiKeepMinutes = c.Index.Value; _ai.SetKeepMinutes(c.Index.Value);
                    JsonFileStore.Save(AppPaths.Settings, _settings);
                    if (_settingsOpen) _popup.Post(SettingsJson());
                    return;
                }
                if (id is "aiTone" or "aiLength" or "aiAdaptation" or "aiReset")
                {
                    if (c.Index is not (0 or 1 or 2)) return;
                    if (id == "aiTone") _settings.SolAiTone = c.Index.Value;
                    else if (id == "aiLength") _settings.SolAiLength = c.Index.Value;
                    else if (id == "aiAdaptation") _settings.SolAiAdaptation = c.Index == 1;
                    else { _settings.SolAiTone = 0; _settings.SolAiLength = 0; _settings.SolAiAdaptation = false; }
                    JsonFileStore.Save(AppPaths.Settings, _settings);
                    if (_settingsOpen) _popup.Post(SettingsJson());
                    return;
                }
                if (id == "spelling")
                {
                    if (c.Index is not (0 or 1 or 2 or 3)) return;
                    _settings.Spelling = c.Index.Value;
                    JsonFileStore.Save(AppPaths.Settings, _settings);
                    _pages.ApplySettings();
                    if (_settingsOpen) _popup.Post(SettingsJson());
                    return;
                }
                if (id == "uiScale")
                {
                    if (c.Index is not (0 or 1 or 2)) return;
                    _settings.UiScale = new[] { 1d, 0.9, 0.8 }[c.Index.Value];
                    JsonFileStore.Save(AppPaths.Settings, _settings);
                    InterfaceScaleChanged?.Invoke();
                    _bridge.BroadcastTheme();
                    _popup.Rescale();
                    if (_paletteItems is not null) _popup.Post(PaletteJson());
                    if (_settingsOpen) _popup.Post(SettingsJson());
                    return;
                }
                if (id == "theme") SetTheme(c.Index!.Value);
                else if (id == "language") SetLanguage(c.Index!.Value);
                else if (id == "font") SetFont(c.Index!.Value);
                else if (id == "engine") SetEngine(c.Index!.Value);
                else SetSetting(id, c.Index != 0);
                return;
            case "settings.text": if (id == "home") SetHomePage(c.Input!); return;
            case "dock.ready": _dock.Ready(); return;
            case "dock.size":
                if (!_dockShown && (_updateDismissed || Program.Update is null)) return;
                _dock.SetSize(c.Width!.Value, c.Height!.Value);
                return;
            case "shortcut.menu": OpenShortcutMenu(c.Index!.Value, c.X!.Value, c.Y!.Value); return;
            case "shortcut.update": UpdateShortcut(id, c.Title!, c.Input!); break;
            case "shortcut.open": OpenShortcut(c.Index!.Value); break;
            case "network.toggle": ToggleNetwork(); return;
            // Le panneau vient d'être créé : il reçoit les couleurs de la page (envoyées avant qu'il n'écoute), puis sa liste.
            case "network.ready": _bridge.BroadcastTheme(); _network.Post(); return;
            case "network.clear": if (_network.Shown is { } listed) _network.Clear(listed); return;
            case "network.replay": if (_network.Shown == id) _ = _network.ReplayAsync(id, c.Input!); return;
            case "network.cancel": if (_network.Shown == id) _network.CancelReplay(id); return;
            case "network.mock" or "network.unmock":
                if (_network.Shown == id && _pages.CoreOf(id) is { } mockCore) _ = _network.MockAsync(id, mockCore, c.Type == "network.mock" ? c.Input : null);
                return;
            case "network.detail":
                if (_network.Shown is { } inspected && _pages.CoreOf(inspected) is { } inspectedCore) _ = _network.PostDetailAsync(inspected, inspectedCore, id);
                return;
            case "welcome.open": HidePopup(); _ = _welcome.ShowAsync(); return;
            case "welcome.ready": PostWelcome(); return;
            case "welcome.name":
                _settings.ProfileName = new string(c.Title!.Where(ch => !char.IsControl(ch)).ToArray()).Trim() is { Length: <= 40 } name ? name : "";
                JsonFileStore.Save(AppPaths.Settings, _settings);
                Refresh();
                return;
            case "welcome.done":
                _settings.Onboarded = true;
                JsonFileStore.Save(AppPaths.Settings, _settings);
                _welcome.Hide();
                Refresh();
                return;
            case "import.run": _ = ImportAsync(id, c.Query!); return;
            case "import.csv": _ = ImportCsvAsync(fromSettings: c.Id == "settings"); return;
            case "extensions.open": _ = OpenExtensionsAsync(); return;
            case "extension.add": _ = AddExtensionAsync(id); return;
            case "extension.folder": _ = AddExtensionFolderAsync(); return;
            case "extension.store":
                HidePopup(); Open("https://chromewebstore.google.com/"); break;
            case "extension.current":
                if (_tabs.ActiveId is { } storeTab && _pages.CoreOf(storeTab) is { } storeCore)
                    _ = StoreExtension(storeCore.Source) is { } shownId && _installedExtensions.Contains(shownId) ? OpenExtensionsAsync() : InstallStoreExtensionAsync(storeCore.Source);
                return;
            case "extensions.update": _ = UpdateExtensionsAsync(manual: true); return;
            case "extension.update": if (ExtensionCatalog.IsId(id) && _extensionUpdates.ContainsKey(id)) _ = OfferStoreExtensionAsync(id); return;
            case "extension.install": _ = InstallStoreExtensionAsync(c.Title!); return;
            case "extension.confirm": _ = ConfirmStoreExtensionAsync(id); return;
            case "extension.pin": _ = PinExtensionAsync(id); return;
            case "extension.reload": _ = ReloadExtensionAsync(id); return;
            case "extension.options":
                if (ExtensionCatalog.IsId(id) && ExtensionCatalog.Read(ExtensionFolder(id), id)?.Options is { } options)
                { HidePopup(); Open($"chrome-extension://{id}/{options.TrimStart('/')}"); } break;
            case "extension.toggle": _ = ChangeExtensionAsync(id, remove: false); return;
            case "extension.remove": _ = ChangeExtensionAsync(id, remove: true); return;
            case "extensions.menu": _ = OpenExtensionsMenuAsync(c.X!.Value, c.Y!.Value); return;
            case "extension.show":
                if (ExtensionCatalog.IsId(id) && ExtensionCatalog.Read(ExtensionFolder(id), id) is { } shownExtension)
                {
                    HidePopup();
                    ShowExtension(shownExtension, c.X ?? (_window.Content is FrameworkElement root ? root.ActualWidth - 14 : 1200), c.Y ?? Scaled(58));
                    break;
                }
                return;
            case "update.later": _updateDismissed = true; UpdateDock(); return;
            case "update.check": _updateDismissed = false; _ = Program.CheckForUpdateAsync(); return;
            case "update.restart":
                if (Program.Update is not { Ready: true }) return;
                SaveSession();
                JsonFileStore.Save(AppPaths.Settings, _settings);
                Program.RestartToUpdate();
                return;
            case "backup.export": AskBackupKey(export: true); return;
            case "backup.export.key": _ = ExportBackupAsync(c.Title!); return;
            case "backup.import": _ = PickBackupAsync(); return;
            case "backup.restore": RestoreBackup(c.Title!); return;
            case "device.compare": SetComparison(id, c.Query!); return;
            case "device.rotate": _pages.RotateDevice(id); return;
            case "device.set": _pages.SetDevice(id, c.Query!.Length == 0 ? null : c.Query); HidePopup(); return;
            case "tab.menu": OpenTabMenu(id, c.X!.Value, c.Y!.Value); return;
            case "session.menu": OpenSessionMenu(id, c.X!.Value, c.Y!.Value); return;
            case "session.set": ChangeSession(id, c.Query!); return;
            case "session.new": AskName(T("Nouvelle session", "New session"), T("Cookies, connexions et stockage de site séparés. Historique, favoris et notes restent ceux du profil Sol.", "Separate cookies, sign-ins and site storage. History, bookmarks and notes belong to the Sol profile."), T("Nom de la session", "Session name"), T("Créer", "Create"), "", "session.create", id); return;
            case "session.create":
                if (_sessions.Create(c.Title!) is { } created && (id.Length > 0 ? id : _tabs.ActiveId) is { } current) ChangeSession(current, created);
                else Notice(T("Session non créée", "Session not created"), T("Utilisez un nom et au maximum 30 sessions.", "Use a name and at most 30 sessions."));
                return;
            case "session.rename":
                _sessions.Rename(id, c.Title!); HidePopup(); Refresh(); return;
            case "session.delete": _ = DeleteSessionAsync(id); return;
            case "permission.answer": AnswerPermission(c.Index != 0); return;
            case "permissions.forget": _permissions.Forget(id); if (_settingsOpen) _popup.Post(SettingsJson()); return;
            case "permissions.clear": _permissions.Clear(); if (_settingsOpen) _popup.Post(SettingsJson()); return;
            case "site-data.clear": if (_settingsOpen) _ = ClearSiteDataAsync(); return;
            case "site.reset": _ = ResetSiteAsync(); return;
            case "profile.menu": OpenProfileMenu(c.X!.Value, c.Y!.Value); return;
            case "profile.new": AskProfileName(); return;
            case "profile.create": SwitchProfile(Profiles.Create(AppPaths.BaseDir, c.Title!)); return;
            case "profile.edit":
                if (id != Profiles.Default && Profiles.List(AppPaths.BaseDir).Contains(id))
                    AskName(T("Renommer le profil", "Rename profile"), "", T("Nom du profil", "Profile name"), T("Renommer", "Rename"), Profiles.NameOf(AppPaths.BaseDir, id), "profile.rename", id);
                return;
            case "profile.rename":
                if (Profiles.Rename(AppPaths.BaseDir, id, c.Title!) && id == AppPaths.Profile) _window.Title = $"Sol — {ProfileName(id)}";
                break;
            // Fenêtre privée : un profil jetable dans une nouvelle fenêtre, à côté de celle-ci, où le moteur n'écrit rien.
            case "profile.isolated" or "window.private":
                HidePopup();
                OpenProfile(Profiles.CreateTemporary(AppPaths.BaseDir, T("Navigation privée", "Private browsing")), replace: false, isPrivate: true);
                return;
            case "profile.switch": SwitchProfile(id); return;
            case "profile.delete": DeleteProfile(id); return;
            case "capture.menu": OpenCaptureMenu(); return;
            case "capture.size": CapturePanel?.SetHeight(c.Height!.Value); return;
            case "capture.visibility": CapturePanel?.SetVisible(c.Index == 1); return;
            case "capture.selection": CapturePanel?.SetSelection(c.Index == 1); return;
            case "capture.close": _capture.CancelSelection(); CapturePanel?.Hide(); return;
            case "capture.region": _capture.Select(new Windows.Graphics.Imaging.BitmapBounds { X = (uint)Math.Max(0,c.X!.Value), Y = (uint)Math.Max(0,c.Y!.Value), Width = (uint)Math.Max(0,c.Width!.Value), Height = (uint)Math.Max(0,c.Height!.Value) }); return;
            case "capture.run":
                if (c.Query is "region" or "visible" or "full")
                {
                    CapturePanel?.Hide();
                    if (c.Index == 1) _ = _capture.RunWindowAsync((FrameworkElement)_window.Content, c.Query);
                    else Capture(c.Query);
                } return;
            case "window.close": _window.Close(); return;
            case "window.drag": BeginDrag(); return;

            case "suggest.pick":
                if (_suggestTab is not null && c.Index is int i && i >= 0 && i < _suggestions.Count)
                    _pages.Navigate(_suggestTab, _suggestions[i].Url);
                break;
            case "ui.ready":
                _bridge.BroadcastTheme();
                if (_tabs.Tabs.Count == 0)
                {
                    RestoreSession();
                    foreach (var shortcut in _settings.Shortcuts.Where(s => s.Icon is null)) _ = FetchShortcutIconAsync(shortcut);
                    // Première ouverture de ce profil : l'accueil (pas dans une fenêtre privée, qui ne garde rien).
                    if (!_settings.Onboarded && !AppPaths.Private) _ = _welcome.ShowAsync();
                    // Lancé par Windows pour ouvrir un lien ou un fichier (PDF, page) ; puis les demandes des lancements suivants.
                    foreach (var target in WindowsBrowser.Targets(Environment.GetCommandLineArgs())) Open(target);
                    WindowsBrowser.Listen(target => _window.DispatcherQueue.TryEnqueue(() =>
                    {
                        if (target.Length > 0) { Open(target); Refresh(); }
                        if (Presenter?.State == OverlappedPresenterState.Minimized) Presenter.Restore();
                        _window.Activate();
                        SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(_window));
                    }));
                }
                foreach (var tabId in _favicons.Keys) PostFavicon(tabId);
                break;
            case "tab.open":
                Open(c.Url is null ? _settings.HomePage : Resolve(c.Url));
                HidePopup();
                Refresh();
                if (c.Url is null) _bridge.FocusAddress();
                return;
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
            case "split.resize": _split.Resize(c.Ratio!.Value); HidePopup(); break;
            case "split.menu":
                if (_split.Contains(_tabs.ActiveId))
                    _popup.Show(c.X!.Value - Scaled(260), c.Y!.Value, 260, JsonSerializer.Serialize(new { popup = "split-layout", ratio = _split.Ratio }), focus: true, above: true);
                return;
            case "bookmark.add": if (IsWeb(c.Url!)) _db.AddBookmark(c.Url!, c.Title!); break;
            case "bookmark.remove": _db.RemoveBookmark(c.Url!); break;
            case "zoom.in" or "zoom.out" or "zoom.reset": Zoom(c.Id ?? _tabs.ActiveId, c.Type); return;
            case "window.minimize":
                if (Presenter is { } overlapped) overlapped.Minimize();
                else ShowWindow(WinRT.Interop.WindowNative.GetWindowHandle(_window), 6); // plein écran : réduction demandée à Windows
                break;
            case "window.maximize": // plein écran ou fenêtre
                if (_settings.FullScreen)
                    _window.SetBrowserFullScreen(!IsFullScreen);
                else if (Presenter is { } normal)
                {
                    if (normal.State == OverlappedPresenterState.Maximized) normal.Restore();
                    else normal.Maximize();
                }
                break;
        }
        HidePopup();
        Refresh();
    }

    // Téléchargements de la session, du plus récent au plus ancien.
    private readonly List<(string Id, Microsoft.Web.WebView2.Core.CoreWebView2DownloadOperation Op, string TabId)> _downloads = new();
    private bool _downloadsOpen;
    private DateTime _downloadsPosted;

    private Microsoft.Web.WebView2.Core.CoreWebView2DownloadOperation? FindDownload(string id) =>
        _downloads.FirstOrDefault(d => d.Id == id).Op;

    private void OnDownloadStarting(string tabId, Microsoft.Web.WebView2.Core.CoreWebView2DownloadStartingEventArgs e)
    {
        e.Handled = true; // pas de fenêtre de téléchargement du moteur : le navigateur affiche la sienne
        // Vérification dans un profil jetable : les fichiers de test restent avec ses données.
        if (Environment.GetEnvironmentVariable("NAVIGATEUR_TEST_DOWNLOAD_DIR") is { Length: > 0 } dir && Directory.Exists(dir))
            e.ResultFilePath = Path.Combine(dir, Path.GetFileName(e.ResultFilePath));
        var op = e.DownloadOperation;
        _downloads.Insert(0, ($"d{_downloads.Count + 1}", op, tabId));
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
    private bool _updateDismissed; // « Plus tard » : l'encart ne reparle plus de la mise à jour, les réglages si

    private void UpdateDock()
    {
        var shown = _settings.FloatingDownloads
            ? _downloads.Where(d => d.Op.State == Microsoft.Web.WebView2.Core.CoreWebView2DownloadState.InProgress
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

    private static string DownloadsJson(IEnumerable<(string Id, Microsoft.Web.WebView2.Core.CoreWebView2DownloadOperation Op, string TabId)> downloads) => JsonSerializer.Serialize(new
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
        const double width = 420;
        HidePopup();
        _downloadsOpen = true;
        _popup.Show(x - Scaled(width), y, width, DownloadsJson(_downloads), focus: true, above: true); // depuis le bas de la carte latérale
    }

    // Demandes d'accès des sites. Une réponse est retenue pour ce site et ce type d'accès ; fermer la question
    // sans répondre refuse pour cette fois seulement.
    private readonly PermissionStore _defaultPermissions = new(AppPaths.Permissions);
    private readonly Dictionary<string, PermissionStore> _sessionPermissions = new();
    private PermissionStore _permissions => _tabs.Tabs.FirstOrDefault(t => t.Id == _tabs.ActiveId)?.SessionId is { Length: > 0 } session
        ? _sessionPermissions.TryGetValue(session, out var store) ? store : _sessionPermissions[session] = new(Path.Combine(AppPaths.DataDir, "session-permissions", session + ".json"))
        : _defaultPermissions;
    private (Microsoft.Web.WebView2.Core.CoreWebView2PermissionRequestedEventArgs Args, Windows.Foundation.Deferral Deferral, string Origin, Microsoft.Web.WebView2.Core.CoreWebView2 Core, string? PageOrigin)? _asking;

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

    private void OnPermissionRequested(Microsoft.Web.WebView2.Core.CoreWebView2 core, Microsoft.Web.WebView2.Core.CoreWebView2PermissionRequestedEventArgs e)
    {
        e.SavesInProfile = false; // les réponses sont gardées par le navigateur, pas par le moteur
        var origin = PermissionStore.OriginOf(e.Uri);
        // Pas de site identifiable, ou une question déjà affichée : refus, sans rien retenir.
        if (origin is null || _asking is not null || _tabs.ActiveId is not { } active || _pages.CoreOf(active) != core)
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
        _asking = (e, e.GetDeferral(), origin, core, PermissionStore.OriginOf(core.Source));
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
        if (_asking is not var (args, deferral, origin, core, pageOrigin)) return;
        _asking = null;
        if (_tabs.ActiveId is not { } active || _pages.CoreOf(active) != core || PermissionStore.OriginOf(core.Source) != pageOrigin)
        { args.State = Microsoft.Web.WebView2.Core.CoreWebView2PermissionState.Deny; deferral.Complete(); HidePopup(); return; }
        args.State = allow ? Microsoft.Web.WebView2.Core.CoreWebView2PermissionState.Allow : Microsoft.Web.WebView2.Core.CoreWebView2PermissionState.Deny;
        deferral.Complete();
        _permissions.Set(origin, args.PermissionKind.ToString(), allow);
        HidePopup();
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
        if (_downloads.Any(d => d.TabId == id && d.Op.State == Microsoft.Web.WebView2.Core.CoreWebView2DownloadState.InProgress))
        { Notice(T("Téléchargement en cours", "Download in progress"), T("Attendez sa fin avant de vider les données de ce site.", "Wait for it to finish before clearing this site's data.")); return; }
        _resettingSite = true;
        HidePopup();
        try
        {
            if (await _pages.ResetSiteAsync(id, origin))
            {
                var permissions = session.Length == 0 ? _defaultPermissions : _sessionPermissions.TryGetValue(session, out var stored)
                    ? stored : _sessionPermissions[session] = new(Path.Combine(AppPaths.DataDir, "session-permissions", session + ".json"));
                permissions.Forget(origin);
            }
            else if (!_closingWindow) Notice(T("Effacement incomplet", "Incomplete clearing"), T("Certaines données n’ont pas pu être effacées. Réessayez sur ce site.", "Some data could not be cleared. Retry on this site."));
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

    // Mémoire occupée par le navigateur et les processus de son moteur, pour le réglage d'économie de mémoire.
    private string MemoryHint()
    {
        // Mémoire privée : celle que chaque processus occupe en propre (la mémoire partagée serait comptée plusieurs fois).
        using var self = System.Diagnostics.Process.GetCurrentProcess();
        long bytes = self.PrivateMemorySize64;
        try
        {
            if (_tabs.ActiveId is { } active && _pages.CoreOf(active) is { } core)
                foreach (var info in core.Environment.GetProcessInfos())
                {
                    try { using var process = System.Diagnostics.Process.GetProcessById(info.ProcessId); bytes += process.PrivateMemorySize64; }
                    catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { /* processus déjà parti */ }
                }
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException) { /* moteur indisponible : mémoire du navigateur seul */ }
        var megabytes = bytes / 1024 / 1024;
        var asleep = _pages.AsleepCount;
        return T($"Met en veille les onglets inactifs, puis les décharge après 30 minutes. Utilisé : {megabytes} Mo, {asleep} onglet{(asleep > 1 ? "s" : "")} déchargé{(asleep > 1 ? "s" : "")}.",
            $"Suspends inactive tabs, then unloads them after 30 minutes. In use: {megabytes} MB, {asleep} tab{(asleep == 1 ? "" : "s")} unloaded.");
    }

    // Accueil : ce qu'il affiche au départ (réglages en cours, navigateurs trouvés sur l'ordinateur).
    private IReadOnlyList<ImportSource> _importSources = [];
    private const int ImportedHistory = 5000; // pages d'historique reprises, les plus récentes

    private void PostWelcome()
    {
        _importSources = BrowserImport.Detect(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
        _bridge.BroadcastTheme();
        _welcome.Post(JsonSerializer.Serialize(new
        {
            welcome = "state",
            first = !_settings.Onboarded,
            name = _settings.ProfileName,
            theme = Math.Max(0, Array.IndexOf(Themes, _settings.Theme)),
            font = Math.Max(0, Array.IndexOf(Fonts, _settings.Font)),
            language = Math.Max(0, Array.IndexOf(Languages, _settings.Language)),
            engine = Math.Max(0, Array.FindIndex(Engines, e => e.Search == _settings.SearchUrlTemplate)),
            engines = Engines.Select(e => e.Name),
            pip = _settings.AutoPip,
            browsers = _importSources.Select(s => new { id = s.Id, name = s.Name, profiles = s.Profiles.Select(p => new { folder = p.Folder, name = p.Name }) }),
        }));
    }

    // Reprend les favoris, l'historique et les mots de passe d'un profil d'un autre navigateur. La lecture des fichiers se fait hors du fil
    // de l'interface ; seul un profil détecté par le navigateur lui-même est accepté.
    private async Task ImportAsync(string browser, string folder)
    {
        if (BrowserImport.ProfileDir(_importSources, browser, folder) is not { } dir) return;
        var temp = Path.Combine(AppPaths.DataDir, "import");
        var (bookmarks, visits) = await Task.Run(() => (BrowserImport.ReadBookmarks(dir), BrowserImport.ReadHistory(dir, ImportedHistory, temp)));
        // Mots de passe : lus maintenant, écrits dans la base du moteur au prochain démarrage (il la garde verrouillée).
        var (logins, locked) = await Task.Run(() =>
        {
            var read = PasswordImport.Read(BrowserImport.UserData(_importSources, browser)!, dir, temp);
            PasswordImport.Queue(AppPaths.PendingPasswords, read.Logins);
            return (read.Logins.Count, read.Unreadable);
        });
        try { _db.Import(bookmarks, visits); }
        catch (Microsoft.Data.Sqlite.SqliteException) { (bookmarks, visits) = ([], []); }
        _welcome.Post(JsonSerializer.Serialize(new { welcome = "imported", id = browser, folder, bookmarks = bookmarks.Count, history = visits.Count, passwords = logins, locked }));
        Refresh();
    }

    private static readonly string[] Fonts = ["default", "system", "inter", "inter-bold", "sf", "sf-bold"];

    private void SetFont(int index)
    {
        if (index < 0 || index >= Fonts.Length) return;
        _settings.Font = Fonts[index];
        JsonFileStore.Save(AppPaths.Settings, _settings);
        _bridge.BroadcastTheme(); // la police voyage avec le thème : toutes les vues la reçoivent
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
            case "fullscreen":
                _settings.FullScreen = on;
                _window.SetBrowserFullScreen(on, maximize: !on);
                break;
            case "pip": _settings.AutoPip = on; if (!on) _pages.ExitPip(); break;
            case "memory": _settings.MemorySaver = on; break;
            case "scrollbars": _settings.HideScrollbars = !on; break;
            case "blur": _settings.BlurEnabled = on; break;
            case "floatingDownloads": _settings.FloatingDownloads = on; break;
            case "onlineSuggestions": _settings.OnlineSuggestions = on; break;
            case "passwords": _settings.SavePasswords = on; break;
            case "startWallpaper": _settings.StartWallpaper = on; break;
            case "tint.topbar": _settings.TintTopbar = on; break;
            case "tint.sidebar": _settings.TintSidebar = on; break;
            case "tint.menus": _settings.TintMenus = on; break;
            case "tint.background": _settings.TintBackground = on; break;
            case "tint.live": _settings.LiveColor = on; break;
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

    public void DismissAiPopup() { if (_aiPopup) HidePopup(); }
    private void HidePopup()
    {
        if (_aiPopup) { _aiPopup = false; _ai.Cancel(); _aiContext = null; }
        _siteReset = null;
        _paletteCommands.Clear();
        _paletteItems = null;
        _notesPopup = false;
        _sessionMenuTab = null;
        _translationRequest++;
        _translationSource = null;
        if (_pendingExtension is { } staged)
        {
            _pendingExtension = null;
            try { if (Directory.Exists(staged.Folder)) Directory.Delete(staged.Folder, true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { DebugLog.Write("extension annulée : " + e.Message); }
        }
        if (_panel is not null) _panelLast = _panel;
        _panel = null;
        _suggesting = false;
        _downloadsOpen = false;
        _settingsOpen = false;
        _profilesOpen = false;
        _extensionsOpen = false;
        // Question d'accès fermée sans réponse : refus pour cette fois, rien n'est retenu.
        if (_asking is var (pendingArgs, pendingDeferral, _, _, _))
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
    /// <summary>La page active s'est-elle servie d'un balayage horizontal (carrousel, zone qui défile) tout récemment ?</summary>
    public Task<bool> PageUsedSidewaysAsync(double withinMs) =>
        _tabs.ActiveId is { } id ? _pages.UsedSidewaysAsync(id, withinMs) : Task.FromResult(false);

    public void Shortcut(string action)
    {
        var active = _tabs.ActiveId;
        var index = _tabs.Tabs.ToList().FindIndex(t => t.Id == active);
        switch (action)
        {
            case "history.open" or "downloads.open" or "settings.open" or "profile.menu" or "session.menu" or "translation.open" or "extensions.open":
                _bridge.Post(JsonSerializer.Serialize(new { @event = "invoke-action", data = action == "extensions.open" ? "extensions.menu" : action }));
                break;
            case "library.open":
                Execute(new Command(action, Id: active, X: (_window.Content as FrameworkElement)?.ActualWidth / 2 ?? 450, Y: 70));
                break;
            case string slot when slot.StartsWith("tab.", StringComparison.Ordinal) && int.TryParse(slot.AsSpan(4), out var number) && number is >= 1 and <= 9:
                if (_tabs.Tabs.Count > 0 && (number == 9 || number <= _tabs.Tabs.Count))
                    Execute(new Command("tab.activate", Id: _tabs.Tabs[number == 9 ? _tabs.Tabs.Count - 1 : number - 1].Id));
                break;
            case "tab.next" or "tab.previous" when _tabs.Tabs.Count > 0:
                var step = action == "tab.next" ? 1 : _tabs.Tabs.Count - 1;
                Execute(new Command("tab.activate", Id: _tabs.Tabs[(index + step) % _tabs.Tabs.Count].Id));
                break;
            case "address.focus": _bridge.Post("""{"event":"focus-address"}"""); break;
            case "tab.open" or "tab.reopen" or "window.private": Execute(new Command(action)); break;
            default: if (active is not null) Execute(new Command(action, Id: active)); break;
        }
    }

    // Crans de zoom, comme dans les navigateurs courants.
    private static readonly double[] ZoomSteps = [0.5, 0.67, 0.75, 0.8, 0.9, 1, 1.1, 1.25, 1.5, 1.75, 2];

    private void Zoom(string? id, string action)
    {
        if (id is null) return;
        if (_tabs.Tabs.FirstOrDefault(t => t.Id == id) is not { } tab || tab.Url is Settings.DefaultHomePage or QuickNotes.PageUrl) return;
        if (_pages.IsPdf(id)) return; // le lecteur PDF possède son zoom ; il ne change pas celui des sites
        var current = _settings.PageZoom;
        var zoom = action switch
        {
            "zoom.in" => ZoomSteps.FirstOrDefault(z => z > current + 0.001, ZoomSteps[^1]),
            "zoom.out" => ZoomSteps.LastOrDefault(z => z < current - 0.001, ZoomSteps[0]),
            _ => 1,
        };
        _pages.SetZoom(zoom); // toutes les pages du profil, présentes et futures
        JsonFileStore.Save(AppPaths.Settings, _settings);
        if (_settingsOpen) _popup.Post(SettingsJson());
        foreach (var designId in _designTabs.ToArray()) _ = UpdateDesignThemeAsync(designId);
    }

    private OverlappedPresenter? Presenter => _window.AppWindow.Presenter as OverlappedPresenter;
    private bool IsFullScreen => _window.AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen;
    private string Resolve(string input) => AddressInput.Resolve(input, _settings.SearchUrlTemplate);
    private static bool IsWeb(string url) =>
        url.StartsWith("https://", StringComparison.Ordinal) || url.StartsWith("http://", StringComparison.Ordinal);

    private readonly TabSessions _sessions = new(Path.Combine(AppPaths.DataDir, "tab-sessions.json"));
    private string? _deletingSession;
    private string? _sessionMenuTab;
    private readonly Dictionary<string, string> _sessionTargets = new();
    private string SessionsJson(string tabId) => JsonSerializer.Serialize(new
    {
        popup = "sessions", id = tabId,
        current = _tabs.Tabs.FirstOrDefault(t => t.Id == tabId)?.SessionId ?? "",
        items = _sessions.Items.Select(s => new { id = s.Id, name = s.Name, count = _tabs.Tabs.Count(t => t.SessionId == s.Id) }),
        deleting = _deletingSession,
    });
    private void OpenSessionMenu(string tabId, double x, double y)
    {
        if (!_tabs.Tabs.Any(t => t.Id == tabId)) return;
        if (ClosesPanel("session-" + tabId)) return;
        HidePopup(); _sessionMenuTab = tabId;
        _popup.Show(x, y, 350, SessionsJson(tabId), focus: true);
    }
    private bool RouteSiteNavigation(string tabId, string url, string? sourceUrl, bool waitForResponse)
    {
        _sessionTargets.Remove(tabId);
        if (_tabs.Tabs.FirstOrDefault(t => t.Id == tabId) is not { } tab || TabSessions.SameSite(sourceUrl ?? tab.Url, url)
            || TabSessions.SiteOf(url) is null && url != Settings.DefaultHomePage) return false;
        var session = _sessions.ForSite(url);
        if (tab.SessionId == session) return false;
        _sessionTargets[tabId] = url;
        // Attendre que le moteur ait accepté la réponse bloquée avant de fermer sa vue.
        if (waitForResponse) return true;
        ApplySiteNavigation(tabId);
        return true;
    }
    private void ApplySiteNavigation(string tabId)
    {
        if (!_sessionTargets.TryGetValue(tabId, out var url)) return;
        var core = _pages.CoreOf(tabId);
        _window.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (_closingWindow || _sessionTargets.GetValueOrDefault(tabId) != url || _pages.CoreOf(tabId) != core) return;
            _sessionTargets.Remove(tabId);
            ChangeSession(tabId, _sessions.ForSite(url), url);
        });
    }
    private Tab OpenLink(string source, string url, bool activate = true)
    {
        var tab = _tabs.Tabs.FirstOrDefault(t => t.Id == source);
        return Open(url, activate: activate, sessionId: tab is not null && TabSessions.SameSite(tab.Url, url) ? tab.SessionId : null);
    }
    private void ChangeSession(string tabId, string sessionId, string? url = null)
    {
        if (_tabs.Tabs.FirstOrDefault(t => t.Id == tabId) is not { } tab || sessionId == _deletingSession
            || sessionId.Length > 0 && _sessions.Resolve(sessionId).Length == 0) return;
        if (tab.SessionId == sessionId) { if (url is null) _sessions.SetSite(tab.Url, sessionId); HidePopup(); return; }
        if (_downloads.Any(d => d.TabId == tabId && d.Op.State == Microsoft.Web.WebView2.Core.CoreWebView2DownloadState.InProgress))
        { Notice(T("Téléchargement en cours", "Download in progress"), T("Attendez sa fin avant de changer la session de cet onglet.", "Wait for it to finish before changing this tab's session.")); return; }
        if (url is null) _sessions.SetSite(tab.Url, sessionId);
        _sessionTargets.Remove(tabId);
        HidePopup(); ExtensionPopup?.Hide();
        _network.Forget(tabId); if (_network.Shown == tabId) _network.Shown = null;
        _designTabs.Remove(tabId); _translationOriginals.Remove(tabId);
        var asleep = _pages.IsAsleep(tabId);
        _pages.Remove(tabId);
        if (url is not null) _tabs.Update(tabId, url, url);
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
    private Tab Open(string url, bool activate = true, bool pinned = false, bool asleep = false, string? sessionId = null)
    {
        var session = sessionId ?? (url.StartsWith("chrome-extension://", StringComparison.Ordinal)
            ? _tabs.Tabs.FirstOrDefault(t => t.Id == _tabs.ActiveId)?.SessionId ?? "" : _sessions.ForSite(url));
        // Une identité manquante ne doit jamais charger un site avec les connexions de la session principale.
        if (session.Length > 0 && (_sessions.Resolve(session).Length == 0 || session == _deletingSession))
        { url = Settings.DefaultHomePage; session = ""; }
        var tab = _tabs.Open(url, activate, pinned, _sessions.Resolve(session));
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
            else Open(_settings.HomePage);
        }
        // Fermer un onglet épinglé, ou rangé dans un groupe, ferme sa page, pas son emplacement : il reste à sa place,
        // déchargé, et se recharge quand on y revient. Pour le retirer pour de bon : le fermer une seconde fois quand il
        // est dans un groupe (il est alors déjà déchargé), ou « Détacher l'onglet » puis fermer quand il est épinglé.
        if (_tabs.Tabs.FirstOrDefault(t => t.Id == id) is { } kept && !_shortcutTabs.ContainsValue(id)
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
        if (active is not null) Add(T("Session de ce site", "Site session"), new("session.menu", Id: active.Id, X: x, Y: 70), keywords: "compte session cookies admin user invite");
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
    private void OpenTabMenu(string id, double x, double y)
    {
        if (_tabs.Tabs.FirstOrDefault(t => t.Id == id) is not { } tab) return;
        // Les deux onglets d'une vue scindée s'épinglent ensemble.
        var ids = _split.Contains(id) ? new[] { _split.PrimaryId!, _split.SecondaryId! } : [id];
        var block = ids.Length > 1;
        ShowMenu(x, y,
        [
            (T("Session de ce site…", "Site session…"), () => _afterMenu = () => OpenSessionMenu(id, x, y)),
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
        if (_asking is not null && _tabs.ActiveId != id) HidePopup();
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
    private void AskName(string title, string hint, string placeholder, string submit, string value, string command, string? id = null, bool secret = false)
    {
        HidePopup();
        _popup.ShowCentered(340, JsonSerializer.Serialize(new { popup = "prompt", title, hint, placeholder, submit, value, command, id, secret }));
    }

    private void Notice(string title, string hint)
    {
        HidePopup();
        _popup.ShowCentered(340, JsonSerializer.Serialize(new { popup = "notice", title, hint }));
    }

    private async Task ToggleDesignAsync(string id)
    {
        if (_pages.CoreOf(id) is not { } core || !IsWebAddress(core.Source) || core.Source.StartsWith("https://start.sol/") || _pages.IsPdf(id)) return;
        HidePopup();
        var source = core.Source;
        try
        {
            _designScript ??= File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "inject", "design.js"));
            var result = await core.ExecuteScriptAsync("(() => { const designOptions=" + JsonSerializer.Serialize(new { en = Language.Current == "en" }) + ";return (\n" + _designScript + "\n);})()");
            if (core.Source != source) return;
            if (result == "true") { _designTabs.Add(id); await UpdateDesignThemeAsync(id); }
            else _designTabs.Remove(id);
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or COMException)
        { Notice(T("Inspection indisponible", "Inspection unavailable"), T("Réessayez après le chargement de la page.", "Try again after the page has loaded.")); }
    }

    private async Task UpdateDesignThemeAsync(string id)
    {
        if (_readerTheme is null || _pages.CoreOf(id) is not { } core) return;
        try
        {
            await core.ExecuteScriptAsync("document.getElementById('sol-design-view')?.solTheme?.(" + _readerTheme + "," + JsonSerializer.Serialize(_settings.UiFactor / _settings.PageZoom) + ")");
        }
        catch (Exception e) when (e is InvalidOperationException or COMException) { /* document replaced */ }
    }

    private async Task EndDesignOnNavigationAsync(string id)
    {
        if (_pages.CoreOf(id) is not { } core) return;
        try { await core.ExecuteScriptAsync("(() => {const h=document.getElementById('sol-design-view');if(h?.solSource!==location.href)h?.solClose?.();})()"); }
        catch (Exception e) when (e is InvalidOperationException or COMException) { /* document replaced */ }
    }

    private async Task ToggleReaderAsync(string id)
    {
        if (!_readerBusy.Add(id)) return;
        try
        {
            if (_pages.CoreOf(id) is not { } core || !IsWebAddress(core.Source) || core.Source.StartsWith("https://start.sol/") || _pages.IsPdf(id)) return;
            var source = core.Source;
            _readerScript ??= File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "inject", "Readability.js"))
                + "\nreturn (\n" + File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "inject", "reader.js")) + "\n);";
            var options = JsonSerializer.Serialize(new { en = Language.Current == "en" });
            var result = await core.ExecuteScriptAsync("(() => { const readerOptions = " + options + ";" + _readerScript + "; })()");
            if (core.Source != source) return;
            if (result == "true") _reading.Add(id);
            else _reading.Remove(id);
            if (result is not ("true" or "false")) Notice(T("Mode lecteur indisponible", "Reader mode unavailable"), T("Cette page ne contient pas d’article lisible, ou son contenu est trop volumineux.", "This page has no readable article, or its content is too large."));
            Refresh();
            if (result == "true") await UpdateReaderThemeAsync(id);
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or COMException)
        { Notice(T("Lecture impossible", "Could not open reader"), T("Réessayez après le chargement de la page.", "Try again after the page has loaded.")); }
        finally { _readerBusy.Remove(id); }
    }

    private async Task UpdateReaderThemeAsync(string id)
    {
        if (_readerTheme is null || _pages.CoreOf(id) is not { } core) return;
        try
        {
            await core.ExecuteScriptAsync("(() => { const t=" + _readerTheme + "; const h=document.getElementById('sol-reader-view'); if(!h)return; const dark=t.mode==='dark'||(t.mode==='system'?t.systemDark:t.mode==='light'?false:t.dark); h.style.setProperty('--bg',dark?'#121214':'#eef0e2');h.style.setProperty('--fg',dark?'#ececef':'#171719');if(t.accent)h.style.setProperty('--accent',t.accent);h.style.colorScheme=dark?'dark':'light'; })()");
        }
        catch (Exception e) when (e is InvalidOperationException or COMException) { /* navigation replaced the document */ }
    }

    private async Task ReaderSpeechAsync(string id, bool stop)
    {
        if (_pages.CoreOf(id) is not { } core) return;
        var source = core.Source;
        if (!_reading.Contains(id) && !stop) await ToggleReaderAsync(id);
        if (!_reading.Contains(id) || core.Source != source) return;
        HidePopup();
        try
        {
            await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", JsonSerializer.Serialize(new
            {
                expression = $"document.getElementById('sol-reader-view')?.{(stop ? "solStop" : "solListen")}?.()",
                userGesture = !stop, awaitPromise = true,
            }));
        }
        catch (Exception e) when (e is InvalidOperationException or COMException)
        { Notice(T("Lecture indisponible", "Reading unavailable"), T("Réessayez après le chargement de la page.", "Try again after the page has loaded.")); }
    }

    private async Task EndReaderOnNavigationAsync(string id)
    {
        if (_pages.CoreOf(id) is not { } core) return;
        try { await core.ExecuteScriptAsync("(() => {const h=document.getElementById('sol-reader-view');if(h?.solArticle && h.solArticle.url!==location.href){h.solRestore();h.remove();}})()"); }
        catch (Exception e) when (e is InvalidOperationException or COMException) { /* the old document is gone */ }
    }

    private static bool IsWebAddress(string url) =>
        url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase);

    // Dossier du moteur pour ce profil : sa base de mots de passe et la clé qui les chiffre.
    private static string EngineData => Path.Combine(Environment.GetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER") ?? AppPaths.WebData, "EBWebView");

    private async Task<string?> PickFileAsync(string label, string extension)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker { SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(extension);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(_window));
        return (await picker.PickSingleFileAsync())?.Path;
    }

    // Mots de passe exportés en CSV par un autre navigateur : pour ceux qu'il garde pour lui seul sur le disque.
    private async Task ImportCsvAsync(bool fromSettings)
    {
        if (fromSettings) HidePopup();
        if (AppPaths.Private || await PickFileAsync("CSV", ".csv") is not { } file) return;
        IReadOnlyList<ImportedLogin> logins = [];
        try
        {
            logins = PasswordImport.ReadCsv(await File.ReadAllTextAsync(file));
            PasswordImport.Queue(AppPaths.PendingPasswords, logins);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { logins = []; }
        _welcome.Post(JsonSerializer.Serialize(new { welcome = "csv", passwords = logins.Count }));
        // Depuis les réglages, l'accueil n'est pas à l'écran : le résultat s'affiche dans une fenêtre de message.
        if (fromSettings)
            Notice(T("Mots de passe", "Passwords"), logins.Count > 0
                ? T($"{logins.Count} mot{(logins.Count > 1 ? "s" : "")} de passe importé{(logins.Count > 1 ? "s" : "")}. Ils seront disponibles au prochain démarrage de Sol.", $"{logins.Count} password{(logins.Count == 1 ? "" : "s")} imported. They will be available the next time Sol starts.")
                : T("Aucun mot de passe trouvé dans ce fichier.", "No password found in this file."));
    }

    // Sauvegarde du profil dans un fichier chiffré par une clé que l'utilisateur choisit et garde : Sol ne la retient pas.
    private string? _restoreFile;

    private void AskBackupKey(bool export, bool tooShort = false)
    {
        if (AppPaths.Private) return;
        if (export)
            AskName(T("Clé de sauvegarde", "Backup key"),
                tooShort ? T($"Au moins {Backup.MinKeyLength} caractères.", $"At least {Backup.MinKeyLength} characters.")
                         : T("Choisissez une clé et gardez-la : sans elle, la sauvegarde est illisible, et Sol ne peut pas la retrouver.", "Choose a key and keep it: without it the backup cannot be read, and Sol cannot recover it."),
                T("Clé de sauvegarde", "Backup key"), T("Exporter", "Export"), "", "backup.export.key", secret: true);
        else
            AskName(T("Clé de sauvegarde", "Backup key"), T("La clé choisie au moment de l'export.", "The key chosen when the backup was exported."),
                T("Clé de sauvegarde", "Backup key"), T("Restaurer", "Restore"), "", "backup.restore", secret: true);
    }

    private async Task ExportBackupAsync(string key)
    {
        if (AppPaths.Private) return;
        if (key.Length < Backup.MinKeyLength) { AskBackupKey(export: true, tooShort: true); return; }
        HidePopup();
        var picker = new Windows.Storage.Pickers.FileSavePicker
        {
            SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary,
            SuggestedFileName = $"Sol {DateTime.Now:yyyy-MM-dd}",
        };
        picker.FileTypeChoices.Add(T("Sauvegarde Sol", "Sol backup"), [".solbackup"]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(_window));
        if (await picker.PickSaveFileAsync() is not { } target) return;
        try
        {
            SaveSession();
            JsonFileStore.Save(AppPaths.Settings, _settings);
            var temp = Path.Combine(AppPaths.DataDir, "import");
            Directory.CreateDirectory(temp);
            var copy = Path.Combine(temp, $"backup-{Guid.NewGuid():N}.db");
            _db.BackupTo(copy);
            var (sealedFile, passwords) = await Task.Run(() =>
            {
                var files = new Dictionary<string, byte[]> { ["browser.db"] = File.ReadAllBytes(copy) };
                File.Delete(copy);
                foreach (var name in Backup.ProfileFiles)
                    if (name != "browser.db" && File.Exists(Path.Combine(AppPaths.DataDir, name))) files[name] = File.ReadAllBytes(Path.Combine(AppPaths.DataDir, name));
                var logins = PasswordImport.Read(EngineData, Path.Combine(EngineData, "Default"), temp).Logins;
                files[Backup.Passwords] = JsonSerializer.SerializeToUtf8Bytes(logins);
                return (Backup.Seal(files, key), logins.Count);
            });
            await File.WriteAllBytesAsync(target.Path, sealedFile);
            Notice(T("Sauvegarde exportée", "Backup exported"),
                T($"Réglages, onglets, favoris, historique et {passwords} mot{(passwords > 1 ? "s" : "")} de passe. Gardez le fichier et sa clé.", $"Settings, tabs, bookmarks, history and {passwords} password{(passwords == 1 ? "" : "s")}. Keep the file and its key."));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            Notice(T("Export impossible", "Export failed"), e.Message);
        }
    }

    private async Task PickBackupAsync()
    {
        if (AppPaths.Private) return;
        HidePopup();
        if (await PickFileAsync(T("Sauvegarde Sol", "Sol backup"), ".solbackup") is not { } file) return;
        _restoreFile = file;
        AskBackupKey(export: false);
    }

    private void RestoreBackup(string key)
    {
        if (AppPaths.Private || _restoreFile is not { } file) return;
        Dictionary<string, byte[]>? files = null;
        try { files = Backup.Open(File.ReadAllBytes(file), key); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        if (files is null)
        {
            Notice(T("Restauration impossible", "Restore failed"), T("La clé est incorrecte, ou ce fichier n'est pas une sauvegarde de Sol.", "The key is wrong, or this file is not a Sol backup."));
            return;
        }
        _restoreFile = null;
        try
        {
            Backup.Stage(AppPaths.DataDir, files);
            if (files.TryGetValue(Backup.Passwords, out var json) && JsonSerializer.Deserialize<List<ImportedLogin>>(json) is { } logins)
                PasswordImport.Queue(AppPaths.PendingPasswords, logins);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            Notice(T("Restauration impossible", "Restore failed"), e.Message);
            return;
        }
        Notice(T("Sauvegarde prête", "Backup ready"), T("Fermez puis rouvrez Sol : la sauvegarde remplacera les réglages, onglets, favoris et historique actuels, et ajoutera ses mots de passe.", "Close and reopen Sol: the backup will replace the current settings, tabs, bookmarks and history, and add its passwords."));
    }

    // Extensions. Le moteur les charge depuis un dossier : Sol reprend celles des autres navigateurs de l'ordinateur,
    // ou un dossier désigné, et en garde sa copie dans son dossier de données. Chaque profil de Sol a les siennes.
    private bool _extensionsOpen;

    private ExtensionPopupHost? _extensionPopup;
    /// <summary>Encart flottant où s'ouvre la fenêtre d'une extension.</summary>
    public ExtensionPopupHost? ExtensionPopup
    {
        get => _extensionPopup;
        init { _extensionPopup = value; if (value is not null) value.TabRequested += url => { Open(url); Refresh(); }; }
    }

    // La fenêtre de l'extension s'ouvre dans l'encart, sous la barre ; une extension qui n'a que des réglages les ouvre en onglet.
    private async void ShowExtension(ExtensionInfo extension, double right, double top)
    {
        try
        {
            if (AppPaths.Private || EngineProfile is not { } profile || !(await profile.GetBrowserExtensionsAsync()).Any(e => e.Id == extension.Id && e.IsEnabled)) return;
            if (extension.Popup is { } popup && ExtensionPopup is { } host) await host.ShowAsync($"chrome-extension://{extension.Id}/{popup.TrimStart('/')}", right, top, profile.ProfileName);
            else if ((extension.Popup ?? extension.Options) is { } page) { Open($"chrome-extension://{extension.Id}/{page.TrimStart('/')}"); Refresh(); }
            // Ni fenêtre ni réglages : elle agit au clic sur son bouton, ou dans un panneau latéral. Le moteur ne transmet pas ce clic.
            else Notice(extension.Name, T("Cette extension n’a pas de fenêtre à ouvrir : elle agit au clic sur son bouton ou dans un panneau latéral, ce que le moteur de Sol ne sait pas encore faire. Ses autres fonctions (dans les pages, par clic droit) restent actives.", "This extension has no window to open: it acts when its button is clicked or in a side panel, which Sol's engine cannot do yet. Its other features (in pages, by right-click) remain active."));
        }
        catch (Exception e) when (e is InvalidOperationException or COMException or ArgumentException)
        { Notice(T("Extension indisponible", "Extension unavailable"), e.Message); }
    }

    // Bouton de la barre : les extensions actives qui ont une fenêtre, puis l'accès à leur gestion.
    private async Task OpenExtensionsMenuAsync(double x, double y)
    {
        if (AppPaths.Private) return;
        if (ClosesPanel("extensions")) return;
        var entries = new List<(string Label, Action? Run)>();
        if (EngineProfile is { } profile)
            foreach (var installed in await profile.GetBrowserExtensionsAsync())
                if (installed.IsEnabled && installed.Id != AdBlock.ExtensionId && ExtensionCatalog.Read(ExtensionFolder(installed.Id), installed.Id) is { } info && (info.Popup ?? info.Options) is not null)
                    entries.Add((info.Name, () => _afterMenu = () => ShowExtension(info, x, y)));
        if (entries.Count == 0) { await OpenExtensionsAsync(); return; }
        entries.Add(("", null));
        entries.Add((T("Gérer les extensions…", "Manage extensions…"), () => _afterMenu = () => _ = OpenExtensionsAsync()));
        ShowMenu(x - Scaled(260), y, entries);
    }
    private Dictionary<string, ExtensionInfo> _extensionOffers = new();
    private List<object> _pinnedExtensions = [];
    private bool _pinsInitialized;
    private string? _pinProfileName;
    private static string ExtensionsDir => Path.Combine(AppPaths.DataDir, "extensions");
    private static string ExtensionFolder(string id)
    {
        var folder = Path.Combine(ExtensionsDir, id);
        return Directory.Exists(folder) ? folder : Path.Combine(AppPaths.BaseDir, "extensions", id); // anciennes installations partagées
    }
    private Microsoft.Web.WebView2.Core.CoreWebView2Profile? EngineProfile => _tabs.ActiveId is { } active ? _pages.CoreOf(active)?.Profile : null;

    private async Task<string> ExtensionsJsonAsync()
    {
        var installed = new List<object>();
        var have = new HashSet<string>();
        if (EngineProfile is { } profile)
            foreach (var extension in await profile.GetBrowserExtensionsAsync())
            {
                // Le bloqueur intégré a son propre réglage ; seules les extensions rangées dans le dossier de Sol sont listées.
                if (ExtensionCatalog.Read(ExtensionFolder(extension.Id), extension.Id) is not { } info || extension.Id == AdBlock.ExtensionId) continue;
                have.Add(extension.Id);
                installed.Add(new { id = extension.Id, name = info.Name, version = info.Version, enabled = extension.IsEnabled, page = (info.Popup ?? info.Options) is not null, options = info.Options is not null,
                    pinned = _settings.PinnedExtensions.Contains(extension.Id), icon = ExtensionCatalog.Icon(info.Folder),
                    store = info.Store, update = _extensionUpdates.GetValueOrDefault(extension.Id) });
            }
        // Les extensions des autres navigateurs : beaucoup de petits fichiers à lire, hors du fil de l'interface.
        _extensionOffers = await Task.Run(() => BrowserImport.Detect(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData))
            .SelectMany(source => ExtensionCatalog.Scan(source.UserData, source.Name))
            // Les composants internes du navigateur Comet ne sont pas des extensions transférables.
            .Where(e => !have.Contains(e.Id) && !e.Name.StartsWith("Comet", StringComparison.OrdinalIgnoreCase))
            .GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.First()));
        return JsonSerializer.Serialize(new
        {
            popup = "extensions",
            session = _tabs.Tabs.FirstOrDefault(t => t.Id == _tabs.ActiveId)?.SessionId is { Length: > 0 } session ? _sessions.Name(session) : T("Principale", "Main"),
            updates = _extensionCheck,
            installed,
            available = _extensionOffers.Values.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).Select(e => new { id = e.Id, name = e.Name, version = e.Version, from = e.From }),
        });
    }

    private async Task OpenExtensionsAsync()
    {
        if (AppPaths.Private || EngineProfile is not { } profile) return;
        HidePopup();
        var json = await ExtensionsJsonAsync();
        if (_closingWindow || EngineProfile?.ProfileName != profile.ProfileName) return;
        _extensionsOpen = true;
        _popup.ShowCentered(560, json, top: 80);
    }

    private async Task RefreshExtensionsAsync()
    {
        await RefreshPinnedExtensionsAsync();
        if (_extensionsOpen) _popup.Post(await ExtensionsJsonAsync());
    }

    private async Task RefreshPinnedExtensionsAsync()
    {
        if (AppPaths.Private || EngineProfile is not { } profile) return;
        _pinsInitialized = true;
        _pinProfileName = profile.ProfileName;
        var extensions = await profile.GetBrowserExtensionsAsync();
        if (EngineProfile?.ProfileName != profile.ProfileName) return;
        _installedExtensions = extensions.Where(e => e.Id != AdBlock.ExtensionId && e.Id != PageZoom.ExtensionId).Select(e => e.Id).ToHashSet();
        foreach (var gone in _extensionUpdates.Keys.Where(id => !_installedExtensions.Contains(id)).ToList()) _extensionUpdates.Remove(gone);
        _pages.RefreshStore();
        _pinnedExtensions = _settings.PinnedExtensions.Distinct().Where(ExtensionCatalog.IsId)
            .Select(id => extensions.FirstOrDefault(e => e.Id == id && e.IsEnabled))
            .Where(e => e is not null).Select(e => ExtensionCatalog.Read(ExtensionFolder(e!.Id), e.Id))
            .Where(info => info is not null).Select(info => (object)new { id = info!.Id, name = info.Name, icon = ExtensionCatalog.Icon(info.Folder) }).ToList();
        Refresh();
    }

    private async Task PinExtensionAsync(string id)
    {
        if (AppPaths.Private || !ExtensionCatalog.IsId(id) || EngineProfile is not { } profile
            || !(await profile.GetBrowserExtensionsAsync()).Any(e => e.Id == id)) return;
        if (!_settings.PinnedExtensions.Remove(id)) _settings.PinnedExtensions.Add(id);
        JsonFileStore.Save(AppPaths.Settings, _settings);
        await RefreshExtensionsAsync();
    }

    private bool _installingExtension;
    private ExtensionInfo? _pendingExtension;
    private readonly SemaphoreSlim _extensionChanges = new(1, 1);
    private HashSet<string> _installedExtensions = [];
    // Mises à jour trouvées qui demandent de nouveaux accès : identifiant → version. Elles attendent un accord.
    private readonly Dictionary<string, string> _extensionUpdates = new();
    private string _extensionCheck = ""; // "", "checking", "current", "updated", "failed"

    private static string? StoreExtension(string url)
    {
        try { return ExtensionPackage.StoreId(url); }
        catch (ArgumentException) { return null; }
    }

    // Récupère le paquet signé d'une extension du store et le déballe dans un dossier provisoire, que l'appelant supprime.
    private static async Task<ExtensionInfo> DownloadStoreExtensionAsync(string id, string staging)
    {
        var version = (await AdBlock.EnvironmentAsync()).BrowserVersionString.Split(' ')[0];
        using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var response = await client.GetAsync(ExtensionPackage.DownloadUri(id, version), System.Net.Http.HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        // Extension retirée du store, payante, ou réservée à un autre navigateur : le store répond sans paquet.
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            throw new InvalidDataException(T("Le Chrome Web Store ne fournit pas cette extension (retirée, payante ou réservée à Chrome).", "The Chrome Web Store does not provide this extension (removed, paid or Chrome-only)."));
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync();
        using var package = new MemoryStream();
        var buffer = new byte[65536]; int count;
        while ((count = await stream.ReadAsync(buffer, timeout.Token)) != 0)
        {
            if (package.Length + count > 32 * 1024 * 1024) throw new InvalidDataException(T("Extension trop volumineuse (maximum 32 Mo).", "Extension too large (32 MB at most)."));
            package.Write(buffer, 0, count);
        }
        ExtensionPackage.Extract(package.ToArray(), id, staging);
        return ExtensionCatalog.Read(staging, id)!;
    }

    private Task InstallStoreExtensionAsync(string input)
    {
        if (StoreExtension(input) is { } id) return OfferStoreExtensionAsync(id);
        Notice(T("Installation impossible", "Installation failed"), T("Ouvrez la fiche d’une extension sur le Chrome Web Store.", "Open an extension page on the Chrome Web Store."));
        return Task.CompletedTask;
    }

    // Télécharge l'extension, puis demande l'accord de l'utilisateur en lui montrant les accès qu'elle demande.
    // Sert aussi à une mise à jour qui demande de nouveaux accès.
    private async Task OfferStoreExtensionAsync(string id)
    {
        if (_installingExtension || AppPaths.Private || EngineProfile is not { } profile) return;
        _installingExtension = true;
        var staging = Path.Combine(ExtensionsDir, "store-" + Guid.NewGuid().ToString("N"));
        try
        {
            Notice(T("Téléchargement de l’extension", "Downloading extension"), T("Le paquet signé est récupéré depuis le Chrome Web Store…", "Fetching the signed package from the Chrome Web Store…"));
            var offered = await DownloadStoreExtensionAsync(id, staging);
            if (_closingWindow || EngineProfile?.ProfileName != profile.ProfileName) return;
            HidePopup();
            _pendingExtension = offered;
            _popup.ShowCentered(440, JsonSerializer.Serialize(new
            {
                popup = "extension-confirm", id, name = offered.Name, version = offered.Version, icon = ExtensionCatalog.Icon(staging),
                update = _installedExtensions.Contains(id),
                access = ExtensionPermissions.Describe(offered.Permissions, Language.Current == "fr"),
            }));
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or System.Net.Http.HttpRequestException or TaskCanceledException
            or System.Security.Cryptography.CryptographicException or JsonException or InvalidOperationException or OverflowException or OperationCanceledException)
        { Notice(T("Installation impossible", "Installation failed"), e is System.Net.Http.HttpRequestException or OperationCanceledException ? T("Le Chrome Web Store ne répond pas. Vérifiez la connexion, puis réessayez.", "The Chrome Web Store is not responding. Check the connection, then try again.") : e.Message); }
        finally
        {
            _installingExtension = false;
            try { if (_pendingExtension?.Folder != staging && Directory.Exists(staging)) Directory.Delete(staging, true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { DebugLog.Write("extension temporaire : " + e.Message); }
        }
    }

    private async Task ConfirmStoreExtensionAsync(string id)
    {
        if (_pendingExtension is not { } pending || pending.Id != id) return;
        _pendingExtension = null;
        try
        {
            if (!await InstallExtensionAsync(pending.Folder, id)) return;
            _extensionUpdates.Remove(id);
            await OpenExtensionsAsync();
        }
        finally
        {
            try { if (Directory.Exists(pending.Folder)) Directory.Delete(pending.Folder, true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { DebugLog.Write("extension temporaire : " + e.Message); }
        }
    }

    // « 4.9 » et « 4.9.0.0 » sont la même version.
    private static Version Full(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));

    // Au démarrage, une fois les pages lancées : au plus une recherche par demi-journée.
    private async Task UpdateExtensionsLaterAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(40));
        if (!_closingWindow && DateTime.UtcNow - _settings.ExtensionsChecked > TimeSpan.FromHours(12)) await UpdateExtensionsAsync(manual: false);
    }

    // Mises à jour des extensions venues du store : Sol demande leur dernière version, télécharge le paquet signé et
    // remplace l'extension si elle ne demande aucun accès de plus. Sinon elle attend l'accord de l'utilisateur.
    private async Task UpdateExtensionsAsync(bool manual)
    {
        if (AppPaths.Private || _extensionCheck == "checking" || EngineProfile is not { } profile) return;
        async Task ShowAsync(string state) { _extensionCheck = state; if (_extensionsOpen) _popup.Post(await ExtensionsJsonAsync()); }
        await ShowAsync("checking");
        var updated = 0;
        try
        {
            var mine = (await profile.GetBrowserExtensionsAsync()).Where(e => e.Id != AdBlock.ExtensionId)
                .Select(e => ExtensionCatalog.Read(ExtensionFolder(e.Id), e.Id)).OfType<ExtensionInfo>().Where(e => e.Store).ToList();
            if (mine.Count > 0)
            {
                var engine = (await AdBlock.EnvironmentAsync()).BrowserVersionString.Split(' ')[0];
                using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                var latest = ExtensionPackage.ParseUpdates(await client.GetStringAsync(ExtensionPackage.UpdateCheckUri(mine.Select(e => e.Id), engine)));
                foreach (var current in mine)
                {
                    if (!latest.TryGetValue(current.Id, out var version) || !Version.TryParse(current.Version, out var have) || Full(version) <= Full(have)) continue;
                    var staging = Path.Combine(ExtensionsDir, "store-" + Guid.NewGuid().ToString("N"));
                    try
                    {
                        var next = await DownloadStoreExtensionAsync(current.Id, staging);
                        if (_closingWindow || EngineProfile?.ProfileName != profile.ProfileName) { await ShowAsync(""); return; }
                        var before = ExtensionPermissions.Describe(current.Permissions, french: true);
                        if (ExtensionPermissions.Describe(next.Permissions, french: true).Except(before).Any()) _extensionUpdates[current.Id] = next.Version;
                        else if (await InstallExtensionAsync(staging, current.Id, quiet: true)) { _extensionUpdates.Remove(current.Id); updated++; }
                    }
                    catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or System.Net.Http.HttpRequestException
                        or System.Security.Cryptography.CryptographicException or JsonException or InvalidOperationException or OverflowException or OperationCanceledException)
                    { DebugLog.Write($"extension {current.Id} : mise à jour impossible : {e.Message}"); }
                    finally
                    {
                        try { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
                        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { DebugLog.Write("extension temporaire : " + e.Message); }
                    }
                }
            }
            _settings.ExtensionsChecked = DateTime.UtcNow;
            JsonFileStore.Save(AppPaths.Settings, _settings);
            await ShowAsync(updated > 0 ? "updated" : "current");
        }
        catch (Exception e) when (e is System.Net.Http.HttpRequestException or OperationCanceledException or ArgumentException or InvalidOperationException or COMException)
        {
            DebugLog.Write("extensions : recherche de mises à jour impossible : " + e.Message);
            await ShowAsync(manual ? "failed" : "");
        }
    }

    // Les extensions installées par une version précédente de Sol reçoivent l'amorce (voir ExtensionShim), puis sont rechargées.
    private async Task PrepareExtensionsAsync()
    {
        if (AppPaths.Private || EngineProfile is not { } profile) return;
        await _extensionChanges.WaitAsync();
        try
        {
            foreach (var extension in await profile.GetBrowserExtensionsAsync())
            {
                var folder = ExtensionFolder(extension.Id);
                if (extension.Id == AdBlock.ExtensionId || !File.Exists(Path.Combine(folder, "manifest.json"))) continue;
                try
                {
                    if (!ExtensionShim.Apply(folder)) continue;
                    var enabled = extension.IsEnabled;
                    await (await profile.AddBrowserExtensionAsync(folder)).EnableAsync(enabled);
                    DebugLog.Write($"extension {extension.Id} : amorce posée, rechargée");
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException or COMException)
                { DebugLog.Write($"extension {extension.Id} : amorce impossible : {e.Message}"); }
            }
        }
        finally { _extensionChanges.Release(); }
    }

    private async Task ReloadExtensionAsync(string id)
    {
        if (AppPaths.Private || EngineProfile is not { } profile || !ExtensionCatalog.IsId(id) || id == AdBlock.ExtensionId) return;
        await _extensionChanges.WaitAsync();
        try
        {
            var extension = (await profile.GetBrowserExtensionsAsync()).FirstOrDefault(e => e.Id == id);
            if (extension is null) return;
            var enabled = extension.IsEnabled;
            ExtensionPopup?.Hide();
            var loaded = await profile.AddBrowserExtensionAsync(ExtensionFolder(id));
            await loaded.EnableAsync(enabled);
            await RefreshExtensionsAsync();
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or COMException)
        { Notice(T("Extension indisponible", "Extension unavailable"), e.Message); }
        finally { _extensionChanges.Release(); }
    }

    // Copie le dossier dans celui de Sol, sous l'identifiant de l'extension, puis l'installe dans le profil.
    /// <param name="quiet">Mise à jour en arrière-plan : un échec est noté au journal, sans déranger.</param>
    private async Task<bool> InstallExtensionAsync(string source, string? knownId, bool quiet = false)
    {
        if (AppPaths.Private || EngineProfile is not { } profile) return false;
        await _extensionChanges.WaitAsync();
        string? backup = null, destination = null;
        var wasEnabled = true;
        try
        {
            var id = knownId;
            if (id is null)
            {
                // Dossier désigné à la main : l'identifiant n'est connu qu'une fois l'extension chargée.
                var trial = ExtensionCatalog.CopyTo(source, Path.Combine(ExtensionsDir, "ajout"));
                ExtensionCatalog.EnsureKey(trial); // l’identité ne dépend plus du déplacement du dossier
                var loaded = await profile.AddBrowserExtensionAsync(trial);
                id = loaded.Id;
                await loaded.RemoveAsync();
                source = trial;
            }
            var folder = Path.Combine(ExtensionsDir, id);
            destination = folder;
            wasEnabled = (await profile.GetBrowserExtensionsAsync()).FirstOrDefault(e => e.Id == id)?.IsEnabled ?? true;
            if (Directory.Exists(folder) && !string.Equals(Path.GetFullPath(source), Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase))
            {
                backup = folder + "-previous-" + Guid.NewGuid().ToString("N");
                ExtensionCatalog.CopyTo(folder, backup);
            }
            if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase)) ExtensionCatalog.CopyTo(source, folder);
            ExtensionShim.Apply(folder); // la vision des onglets d'un navigateur à une fenêtre, avant le code de l'extension
            var installed = await profile.AddBrowserExtensionAsync(folder);
            if (installed.Id != id)
            {
                await installed.RemoveAsync();
                throw new InvalidOperationException("L’identité de l’extension a changé. Installez sa version signée depuis le store pour préserver son accès natif.");
            }
            await installed.EnableAsync(wasEnabled);
            _pages.ApplySettings();
            await RefreshExtensionsAsync();
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or JsonException or System.Runtime.InteropServices.COMException)
        {
            if (backup is not null && destination is not null)
            {
                try
                {
                    ExtensionCatalog.CopyTo(backup, destination);
                    await (await profile.AddBrowserExtensionAsync(destination)).EnableAsync(wasEnabled);
                }
                catch (Exception restore) when (restore is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or COMException)
                { DebugLog.Write("extension : restauration incomplète, copie conservée : " + restore.Message); backup = null; }
            }
            if (quiet) DebugLog.Write("extension : mise à jour refusée : " + e.Message);
            else Notice(T("Extension refusée", "Extension rejected"), T($"Le moteur n'a pas pu charger cette extension : {e.Message}", $"The engine could not load this extension: {e.Message}"));
            return false;
        }
        finally
        {
            try { if (backup is not null && Directory.Exists(backup)) Directory.Delete(backup, true); }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { DebugLog.Write("extension ancienne copie : " + cleanup.Message); }
            _extensionChanges.Release();
        }
    }

    private Task AddExtensionAsync(string id) =>
        _extensionOffers.TryGetValue(id, out var offer) ? InstallExtensionAsync(offer.Folder, offer.Id) : Task.CompletedTask;

    private async Task AddExtensionFolderAsync()
    {
        var picker = new Windows.Storage.Pickers.FolderPicker { SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Downloads };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(_window));
        if (await picker.PickSingleFolderAsync() is not { } folder) return;
        if (ExtensionCatalog.Read(folder.Path, "") is null)
        {
            Notice(T("Ce n'est pas une extension", "Not an extension"), T("Ce dossier ne contient pas de fichier manifest.json d'extension.", "This folder has no extension manifest.json file."));
            return;
        }
        await InstallExtensionAsync(folder.Path, knownId: null);
    }

    private async Task ChangeExtensionAsync(string id, bool remove)
    {
        if (AppPaths.Private || EngineProfile is not { } profile || !ExtensionCatalog.IsId(id) || id == AdBlock.ExtensionId) return;
        await _extensionChanges.WaitAsync();
        try
        {
            if ((await profile.GetBrowserExtensionsAsync()).FirstOrDefault(e => e.Id == id) is not { } extension) return;
            if (remove)
            {
                await extension.RemoveAsync();
                // ponytail: fichiers et préférence d'épinglage partagés ; conserver le code pour les autres sessions.
                // Nettoyer les fichiers seulement après vérification de tous les profils moteur si l'espace devient un problème.
            }
            else await extension.EnableAsync(!extension.IsEnabled);
            ExtensionPopup?.Hide();
            _pages.ApplySettings();
            await RefreshExtensionsAsync();
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException or ArgumentException)
        { Notice(T("Extension indisponible", "Extension unavailable"), e.Message); }
        finally { _extensionChanges.Release(); }
    }

    // Profils : chacun a ses données. Le panneau en change, en crée ou en supprime.
    private bool _profilesOpen;

    private string ProfilesJson() => JsonSerializer.Serialize(new
    {
        popup = "profiles",
        // Les sessions isolées des autres fenêtres ne sont pas des profils où l'on revient.
        items = Profiles.List(AppPaths.BaseDir).Where(id => id == AppPaths.Profile || !Profiles.IsTemporary(AppPaths.BaseDir, id)).Select(id => new
        {
            id,
            name = ProfileName(id),
            current = id == AppPaths.Profile,
            // Ni le profil par défaut, ni celui de cette fenêtre, ni un profil ouvert dans une autre fenêtre.
            renamable = id != Profiles.Default && !Profiles.IsTemporary(AppPaths.BaseDir, id),
            deletable = id != Profiles.Default && id != AppPaths.Profile && !IsProfileOpen(id),
        }),
    });

    private void OpenProfileMenu(double x, double y)
    {
        if (ClosesPanel("profile")) return;
        HidePopup();
        _profilesOpen = true;
        _popup.Show(x - Scaled(300), y, 300, ProfilesJson(), focus: true);
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

    private static bool IsProfileOpen(string id) => App.IsProfileOpen(id);

    // Changer de profil : une fenêtre du nouveau profil prend la place de celle-ci, au même endroit.
    // (Les données d'un profil sont fixées au démarrage du moteur : on ne peut pas en changer en cours de route.)
    private void SwitchProfile(string id) => OpenProfile(id, replace: true);

    // replace : la nouvelle fenêtre prend la place de celle-ci. Sinon elle s'ouvre à côté, légèrement décalée.
    private void OpenProfile(string id, bool replace, bool isPrivate = false)
    {
        if (id == AppPaths.Profile || !Profiles.List(AppPaths.BaseDir).Contains(id)) return;
        if (Environment.ProcessPath is not { } exe) return;
        var app = _window.AppWindow;
        var shift = replace ? 0 : 36;
        var start = new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = false };
        start.ArgumentList.Add($"--profile={id}");
        if (isPrivate) start.ArgumentList.Add("--private");
        start.ArgumentList.Add($"--bounds={app.Position.X + shift},{app.Position.Y + shift},{app.Size.Width},{app.Size.Height}");
        if (replace && Presenter?.State == OverlappedPresenterState.Maximized) start.ArgumentList.Add("--maximized");
        if (replace && IsFullScreen) start.ArgumentList.Add("--fullscreen");
        // La nouvelle fenêtre repart de l'environnement d'origine, pas de celui que ce profil s'est réglé.
        foreach (var (variable, value) in App.LaunchEnvironment)
        {
            if (value is null) start.Environment.Remove(variable);
            else start.Environment[variable] = value;
        }
        try { System.Diagnostics.Process.Start(start); }
        catch (System.ComponentModel.Win32Exception) { return; } // rien ne s'est ouvert : on garde cette fenêtre
        if (replace) _window.Close();
    }

    // Bouton de capture d'écran de la barre d'outils.
    private void OpenCaptureMenu()
    {
        if (_capture.Busy) return;
        if (CapturePanel?.IsOpen == true) { CapturePanel.Hide(); return; }
        HidePopup();
        CapturePanel?.ShowBottom(640, JsonSerializer.Serialize(new { @event = "capture-open", mode = "visible" }));
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
        "saveAs", "print", "viewPageSource", "inspectElement", "spellCheck",
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

    private void SaveSession()
    {
        if (AppPaths.Private) return; // navigation privée : la fenêtre ne laisse pas d'onglets derrière elle
        var tabs = _tabs.Tabs.ToList();
        int? IndexOf(string? id) => tabs.FindIndex(t => t.Id == id) is >= 0 and var i ? i : null;
        JsonFileStore.Save(AppPaths.Session, new Session
        {
            Urls = tabs.Select(t => t.Url).ToList(),
            SessionIds = tabs.Select(t => t.SessionId).ToList(),
            TranslationOriginals = tabs.Select(t => _translationOriginals.GetValueOrDefault(t.Id, "")).ToList(),
            Titles = tabs.Select(t => t.Title).ToList(),
            PinnedCount = _tabs.PinnedCount,
            Icons = tabs.Select(t => _favicons.GetValueOrDefault(t.Id) is { Length: <= MaxSessionIcon } icon ? icon : "").ToList(),
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

    private string? _lastActive;
    private readonly Dictionary<string, string> _startPreviews = new();
    private readonly HashSet<string> _startPreviewFailed = new();
    private readonly CancellationTokenSource _startPreviewCancel = new();
    private bool _loadingStartPreviews;
    private bool _startPreviewsPending;
    // Un aperçu se fait en chargeant le site dans une vue cachée : du réseau, du processeur, et une visite que
    // l'utilisateur n'a pas faite. Une fois par jour suffit, et une seule tentative par site et par session.
    private static readonly TimeSpan StartPreviewAge = TimeSpan.FromDays(1);
    private readonly HashSet<string> _startPreviewTried = new();
    private readonly Dictionary<string, string?> _startIcons = new();
    private async void FetchStartIcon(string url)
    {
        var domain = SiteShortcut.DomainOf(url);
        if (_startIcons.ContainsKey(domain)) return;
        _startIcons[domain] = null;
        var icon = await FetchSiteIconAsync(url);
        if (_closingWindow) return;
        _startIcons[domain] = icon;
        if (icon is not null)
            foreach (var tab in _tabs.Tabs.Where(t => SiteShortcut.DomainOf(t.Url) == domain))
            {
                _favicons[tab.Id] = icon;
                PostFavicon(tab.Id);
                LearnShortcutIcon(tab.Id, icon);
            }
        Refresh();
    }
    private async void LoadStartPreviews()
    {
        if (AppPaths.Private || _closingWindow) return;
        if (_loadingStartPreviews) { _startPreviewsPending = true; return; }
        _loadingStartPreviews = true;
        var directory = Path.Combine(AppPaths.DataDir, "start-previews");
        try
        {
            Directory.CreateDirectory(directory);
            var sites = _db.FrequentSites(80).Where(v => IsWeb(v.Url) && v.Url != Settings.DefaultHomePage)
                .DistinctBy(v => SiteShortcut.DomainOf(v.Url)).Take(6).ToList();
            var files = sites.ToDictionary(s => s.Url, s => Path.Combine(directory,
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(s.Url))) + ".png"));
            _startPreviews.Clear();
            // Show every cached image before any site can delay the queue, including stale images during refresh.
            foreach (var site in sites)
                if (File.Exists(files[site.Url]))
                    _startPreviews[site.Url] = "data:image/png;base64," + Convert.ToBase64String(await File.ReadAllBytesAsync(files[site.Url], _startPreviewCancel.Token));
            Refresh();
            using var slots = new SemaphoreSlim(2);
            await Task.WhenAll(sites.Where(s => (!File.Exists(files[s.Url]) || File.GetLastWriteTimeUtc(files[s.Url]) <= DateTime.UtcNow - StartPreviewAge)
                && !_startPreviewTried.Contains(s.Url)).Select(async site =>
            {
                await slots.WaitAsync(_startPreviewCancel.Token);
                try
                {
                    if (_closingWindow || !_tabs.Tabs.Any(t => t.Url == Settings.DefaultHomePage && !_pages.IsAsleep(t.Id))) return;
                    if (!_startPreviewTried.Add(site.Url)) return;
                    var image = await _pages.PreviewUrlAsync(site.Url, _startPreviewCancel.Token);
                    if (_closingWindow) return;
                    if (image is null)
                    {
                        if (!_startPreviews.ContainsKey(site.Url)) _startPreviewFailed.Add(site.Url);
                    }
                    else
                    {
                        _startPreviewFailed.Remove(site.Url);
                        _startPreviews[site.Url] = image;
                        await File.WriteAllBytesAsync(files[site.Url], Convert.FromBase64String(image["data:image/png;base64,".Length..]), _startPreviewCancel.Token);
                    }
                    Refresh();
                }
                finally { slots.Release(); }
            }));
            foreach (var file in new DirectoryInfo(directory).GetFiles("*.png").OrderByDescending(f => f.LastWriteTimeUtc).Skip(24)) file.Delete();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or OperationCanceledException) { }
        finally
        {
            _loadingStartPreviews = false;
            if (_startPreviewsPending) { _startPreviewsPending = false; LoadStartPreviews(); }
        }
    }
    private string? StartIcon(string url) => _startIcons.GetValueOrDefault(SiteShortcut.DomainOf(url)) ?? (_tabs.Tabs.FirstOrDefault(t => SiteShortcut.DomainOf(t.Url) == SiteShortcut.DomainOf(url)) is { } tab
        && _favicons.GetValueOrDefault(tab.Id) is { } icon ? icon : _settings.Shortcuts.FirstOrDefault(s => SiteShortcut.DomainOf(s.Url) == SiteShortcut.DomainOf(url))?.Icon);

    private string StartJson()
    {
        IReadOnlyList<Visit> history = AppPaths.Private ? [] : _db.RecentHistory(13).Where(v => v.Url != Settings.DefaultHomePage).Take(12).ToList();
        var frequent = AppPaths.Private ? [] : _db.FrequentSites(80).Where(v => IsWeb(v.Url) && v.Url != Settings.DefaultHomePage)
            .DistinctBy(v => SiteShortcut.DomainOf(v.Url)).Take(6).ToList();
        return JsonSerializer.Serialize(new
        {
            @event = "start", privateMode = AppPaths.Private, lang = Language.Current, wallpaper = _settings.StartWallpaper,
            favorites = _db.Bookmarks().Take(24).Select(b => new { url = b.Url, title = b.Title, icon = StartIcon(b.Url) })
                .Concat(_settings.Shortcuts.Select(s => new { url = s.Url, title = s.Name, icon = StartIcon(s.Url) })).DistinctBy(b => b.url).Take(24),
            frequent = frequent.Select(v => new { url = v.Url, title = v.Title, preview = _startPreviews.GetValueOrDefault(v.Url), previewFailed = _startPreviewFailed.Contains(v.Url) }),
            history = history.Select(v => new { url = v.Url, title = v.Title, last = v.Last.ToUnixTimeMilliseconds(), icon = StartIcon(v.Url) }),
            groups = _tabs.Groups.Select(g => new { id = g.Id, name = g.Name, color = g.Color, count = _tabs.Tabs.Count(t => t.GroupId == g.Id) }),
        });
    }

    private void Refresh()
    {
        if (_closingWindow || _restoringSession) return;
        var activeProfile = EngineProfile?.ProfileName;
        if (_pinProfileName != activeProfile)
        {
            ExtensionPopup?.Hide(); _pinnedExtensions = []; _pinsInitialized = false; _pinProfileName = activeProfile;
            if (activeProfile is not null) _ = RefreshPinnedExtensionsAsync();
        }
        if (_nativeZoom != UseNativeZoom) { _nativeZoom = UseNativeZoom; NativeZoomChanged?.Invoke(); }
        var starts = _tabs.Tabs.Where(t => t.Url == Settings.DefaultHomePage && !_pages.IsAsleep(t.Id)).ToList();
        if (starts.Count > 0) { var json = StartJson(); foreach (var tab in starts) _pages.PostStart(tab.Id, json); }
        // Regrouper les changements ; un crash ne perd plus toute la session depuis le démarrage.
        if (!AppPaths.Private) { _sessionSave.Stop(); _sessionSave.Start(); }
        if (_tabs.ActiveId is { } recent && _recentTabs.LastOrDefault() != recent)
        {
            _recentTabs.Remove(recent);
            _recentTabs.Add(recent);
        }
        var showSplit = _split.Contains(_tabs.ActiveId);
        FollowNetwork();
        // On vient de quitter un onglet : s'il joue une vidéo, elle reste à l'écran dans le lecteur flottant.
        if (_lastActive != _tabs.ActiveId)
        {
            var left = _lastActive;
            _lastActive = _tabs.ActiveId;
            if (_settings.AutoPip && left is not null && !(showSplit && _split.Contains(left)) && _tabs.Tabs.Any(t => t.Id == left))
                _ = _pages.TryEnterPipAsync(left);
        }
        _pages.ApplyLayout(showSplit ? _split.PrimaryId : _tabs.ActiveId, showSplit ? _split.SecondaryId : null, _split.Ratio);
        _bridge.Post(JsonSerializer.Serialize(new
        {
            tabs = _tabs.Tabs.Select(t => new
            {
                id = t.Id, url = t.Url, title = t.Title, pinned = t.Pinned,
                sessionId = t.SessionId, sessionName = t.SessionId.Length == 0 ? T("Session actuelle", "Current session") : _sessions.Name(t.SessionId),
                groupId = t.GroupId,
                asleep = _pages.IsAsleep(t.Id), // déchargé : affiché atténué, il se recharge quand on y revient
                device = _pages.DeviceOf(t.Id) is { } simulated ? DeviceLabel(simulated) : null, // simulateur : appareil affiché
                zoom = t.Url is Settings.DefaultHomePage or QuickNotes.PageUrl || _pages.IsPdf(t.Id) ? 100 : (int)Math.Round(_settings.PageZoom * 100),
                loading = _pages.IsLoading(t.Id),
                canGoBack = _pages.CanGoBack(t.Id),
                canGoForward = _pages.CanGoForward(t.Id),
                // Seules les pages affichées ont une étoile : inutile d'interroger la base pour les autres.
                bookmarked = (t.Id == _tabs.ActiveId || (showSplit && _split.Contains(t.Id))) && _db.IsBookmarked(t.Url),
                storeDetail = _pages.CoreOf(t.Id) is { } storePage && PageHost.IsStoreDetail(storePage.Source),
                storeInstalled = _pages.CoreOf(t.Id) is { } storeShown && StoreExtension(storeShown.Source) is { } storeId && _installedExtensions.Contains(storeId),
                translated = _translationOriginals.ContainsKey(t.Id) && PageTranslation.IsGooglePage(t.Url),
                reader = _reading.Contains(t.Id),
                ai = _settings.SolAiEnabled && _ai.Available,
            }),
            groups = _tabs.Groups.Select(g => new { id = g.Id, name = g.Name, color = g.Color, icon = g.Icon, collapsed = g.Collapsed }),
            activeId = _tabs.ActiveId,
            privateMode = AppPaths.Private, // fenêtre de navigation privée
            shield = false,
            shortcuts = _settings.Shortcuts.Select(s => new { url = s.Url, name = s.Name, icon = s.Icon, tabId = _shortcutTabs.GetValueOrDefault(s) }),
            extensions = _pinnedExtensions,
            profile = ProfileName(AppPaths.Profile),
            split = new { primaryId = _split.PrimaryId, secondaryId = _split.SecondaryId, ratio = _split.Ratio, visible = showSplit },
            maximized = IsFullScreen || Presenter?.State == OverlappedPresenterState.Maximized,
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
        if (IsFullScreen) return; // une fenêtre en plein écran ne se déplace pas
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        ReleaseCapture();
        SendMessage(hwnd, 0x00A1 /* WM_NCLBUTTONDOWN */, 2 /* HTCAPTION */, 0);
    }

    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern nint SendMessage(nint hwnd, uint msg, nint wParam, nint lParam);
}
