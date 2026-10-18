using System.Runtime.InteropServices;
using System.Text.Json;
using Browser.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace Browser.App;

public sealed partial class BrowserController
{
    private readonly MainWindow _window;
    private readonly PageHost _pages;
    private readonly UiBridge _bridge;
    private readonly PopupHost _popup;
    private readonly Settings _settings;
    private readonly BrowserDb _db;
    private readonly CaptureService _capture;
    public PopupHost? CapturePanel { get; init; }
    public ChatHost? Chat { get; init; }
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
    private readonly NoteFiles _noteFiles = new(AppPaths.Private ? null : Path.Combine(AppPaths.DataDir, "notes-assets.json"));
    private QuickNote _noteDraft = new("", "", "", "", DateTimeOffset.UtcNow);
    private string? _noteError;
    private QuickNote? _noteSelection;
    private bool _notesPopup;
    private bool NoteDraftChanged => (_noteDraft.Id.Length > 0 || _noteDraft.Text.Length > 0 || _noteDraft.Title.Length > 0 || _noteDraft.Files.Length > 0)
        && !_notes.Items.Any(n => n.Id == _noteDraft.Id && n.Text == _noteDraft.Text && n.Title == _noteDraft.Title && n.Kind == _noteDraft.Kind &&
            n.Tags.SequenceEqual(_noteDraft.Tags) && n.Files.SequenceEqual(_noteDraft.Files) && n.Due == _noteDraft.Due);

    private readonly DockHost _dock;

    private readonly PanelHost _tools; // panneau du bas : outils de développement
    private readonly NetworkInspector _network;

    private readonly WelcomeHost _welcome;

    public BrowserController(MainWindow window, PageHost pages, UiBridge bridge, PopupHost popup, DockHost dock, PanelHost panel, WelcomeHost welcome, Settings settings)
    {
        _welcome = welcome;
        (_window, _pages, _bridge, _popup, _dock, _tools, _settings) = (window, pages, bridge, popup, dock, panel, settings);
        _pages.PagePressed += DismissAiPopup;
        WireSelection();
        _network = new NetworkInspector(panel.Post);
        _sessionSave.Tick += (_, _) => { _sessionSave.Stop(); SaveSession(); };
        StartNoteMaintenance();
        Directory.CreateDirectory(AppPaths.DataDir);
        _db = new BrowserDb(AppPaths.Database);
        _capture = new CaptureService(window, json => CapturePanel?.Post(json), pages.RestoreCaptureViewportAsync);
        _chatGpt.PreferredModel = _settings.SolAiChatModel;
        _chatGpt.Changed += () => _window.DispatcherQueue.TryEnqueue(() =>
        {
            if (_closingWindow) return;
            if (AiReady && !_chatGpt.SigningIn && !_settings.SolAiWelcomeShown)
            {
                _settings.SolAiWelcomeShown = true; JsonFileStore.Save(AppPaths.Settings, _settings);
                Notice("Sol AI", T("Connecté avec votre forfait ChatGPT. Les questions et le texte choisi sont envoyés à OpenAI quand vous lancez une action. Vous pouvez gérer l’usage depuis Sol AI.",
                    "Connected with your ChatGPT plan. Questions and selected text are sent to OpenAI when you start an action. Manage usage from Sol AI."));
            }
            if (_settingsOpen) _popup.Post(SettingsJson());
            PublishAi(); PublishChat(); PublishNotes(); Refresh();
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
        _pages.CreatePopupTab = (source, url) =>
        {
            var opener = _tabs.Tabs.FirstOrDefault(t => t.Id == source);
            if (opener is null) return null;
            var tab = _tabs.Open(url, sessionId: opener.SessionId, isPrivate: opener.Private);
            Refresh();
            return tab;
        };
        _pages.PopupClosed += id =>
        {
            // Le moteur referme la fenêtre devenue téléchargement : sa vue pilote encore le transfert.
            if(_downloads.Any(d=>d.TabId==id&&DownloadPending(d.Id,d.Op))){DebugLog.Write($"popup.close.deferred {id}");return;}
            Close(id);Refresh();
        };
        _pages.SessionFailed += id => Notice(T("Session indisponible", "Session unavailable"), T("Sol n’a pas pu ouvrir cette session. Réessayez en activant l’onglet ; aucune autre session n’a été utilisée.", "Sol could not open this session. Activate the tab to retry; no other session was used."));
        _pages.Loaded += RecordVisit;
        _pages.StoreInstallRequested += url => _ = InstallStoreExtensionAsync(url);
        _pages.StoreManageRequested += () => _ = OpenExtensionsAsync();
        _pages.ExtensionsChanged += () => _ = RefreshExtensionsAsync();
        _pages.StartRequested += (id, command) =>
        {
            if (_closingWindow) return;
            if (command.Type == "start.ready")
            {
                StartReady(id);
            }
            else if (command.Type == "start.navigate") _pages.Navigate(id, Resolve(command.Input!));
            else if (command.Type == "start.favorites") _ = FetchFavoriteIconsAsync();
            else if (StartAction(id, command)) { }
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
        _pages.KeepAwake = id => _split.Contains(id) || _downloads.Any(d => d.TabId == id && DownloadPending(d.Id,d.Op));
        _pages.PipReturnRequested += id => {
            Activate(id); Refresh();
            if (Presenter?.State == OverlappedPresenterState.Minimized) Presenter.Restore();
            _window.Activate();
        };
        _pages.Loaded += id =>
        {
            if (!_pinsInitialized) { _ = RefreshPinnedExtensionsAsync(); _ = UpdateExtensionsLaterAsync(); }
            _ = LearnShortcutNameAsync(id);
            if (_tabs.Tabs.FirstOrDefault(t => t.Id == id) is { Private: false } tab && IsWeb(tab.Url) && tab.Url is not (Settings.DefaultHomePage or QuickNotes.PageUrl)) FetchStartIcon(tab.Url);
            Refresh();
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
        WireGitLabSessionClosing();
        _window.Closed += (_, _) =>
        {
            _closingWindow = true;
            _noteMaintenance.Stop();
            CancelAi();
            CancelChat(); CancelCategories(); _chatGpt.CancelSignIn();
            _categoriesTimer.Stop();
            _pages.StopSpelling();
            _capture.Cancel();
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
        StartAiCategories();
    }

    private static string T(string fr, string en) => Language.T(fr, en);
    private string ProfileName(string id) =>
        id != Profiles.Default ? Profiles.NameOf(AppPaths.BaseDir, id) : _settings.ProfileName.Length > 0 ? _settings.ProfileName : T("Défaut", "Default");

    public void Execute(Command c)
    {
        if (c.Type is not ("popup.size" or "dock.size" or "split.resize")) DebugLog.Write($"commande {c.Type} {c.Id} {c.Index}");
        if (SidebarCommand(c)) return;
        if (ChatCommand(c)) return;
        if (TabGroupAiCommand(c)) return;
        if (ExecuteCommands0(c)) return;
        if (ExecuteCommands1(c)) return;
        if (ExecuteCommands2(c)) return;
        HidePopup(); Refresh();
    }

}
