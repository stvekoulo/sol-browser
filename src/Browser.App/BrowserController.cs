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
    private readonly Settings _settings;
    private readonly BrowserDb _db;
    private readonly TabManager _tabs = new();
    private readonly SplitLayout _split = new();

    public BrowserController(Window window, PageHost pages, UiBridge bridge, Settings settings)
    {
        (_window, _pages, _bridge, _settings) = (window, pages, bridge, settings);
        Directory.CreateDirectory(AppPaths.DataDir);
        _db = new BrowserDb(AppPaths.Database);

        _pages.PageChanged += (id, url, title) => { _tabs.Update(id, url, title); Refresh(); };
        _pages.RuntimeChanged += Refresh;
        _pages.NewTabRequested += url => { Open(url); Refresh(); };
        _pages.Loaded += RecordVisit;
        _window.AppWindow.Changed += (_, e) => { if (e.DidPresenterChange || e.DidSizeChange) Refresh(); };
        _window.Closed += (_, _) => { SaveSession(); _db.Dispose(); };
    }

    public void Execute(Command c)
    {
        var id = c.Id ?? "";
        switch (c.Type)
        {
            case "ui.ready": if (_tabs.Tabs.Count == 0) RestoreSession(); break;
            case "tab.open": Open(c.Url is null ? _settings.HomeUrl : Resolve(c.Url)); break;
            case "tab.close": Close(id); break;
            case "tab.activate": _tabs.Activate(id); break;
            case "tab.move": _tabs.Move(id, c.Index!.Value); break;
            case "tab.reopen": if (_tabs.ReopenClosed() is { } reopened) _pages.Add(reopened); break;
            case "nav.go": _pages.Navigate(id, Resolve(c.Input!)); break;
            case "nav.back": _pages.Back(id); break;
            case "nav.forward": _pages.Forward(id); break;
            case "nav.reload": _pages.Reload(id); break;
            case "nav.stop": _pages.Stop(id); break;
            case "split.open": Split(c.Id); break;
            case "split.close": _split.Close(); break;
            case "split.resize": _split.Resize(c.Ratio!.Value); break;
            case "window.minimize": Presenter?.Minimize(); break;
            case "window.maximize":
                if (Presenter?.State == OverlappedPresenterState.Maximized) Presenter.Restore(); else Presenter?.Maximize();
                break;
            case "window.close": _window.Close(); return;
            case "window.drag": BeginDrag(); return;
        }
        Refresh();
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

    private Tab Open(string url, bool activate = true)
    {
        var tab = _tabs.Open(url, activate);
        _pages.Add(tab);
        return tab;
    }

    private void Close(string id)
    {
        if (!_tabs.Close(id)) return;
        _pages.Remove(id);
        _split.OnTabClosed(id);
        if (_tabs.Tabs.Count == 0) _window.Close();
    }

    // Scinde l'onglet actif avec l'onglet donné, ou avec un nouvel onglet si aucun n'est donné.
    private void Split(string? otherId)
    {
        if (_tabs.ActiveId is not { } active) return;
        if (otherId is not null && (otherId == active || _tabs.Tabs.All(t => t.Id != otherId))) return;
        var other = otherId ?? Open(_settings.HomeUrl, activate: false).Id;

        // Les deux onglets de la paire restent voisins dans la barre d'onglets.
        var tabs = _tabs.Tabs.ToList();
        int a = tabs.FindIndex(t => t.Id == active), o = tabs.FindIndex(t => t.Id == other);
        _tabs.Move(other, o > a ? a + 1 : a);
        _split.Open(active, other);
    }

    private void RecordVisit(string id)
    {
        if (_tabs.Tabs.FirstOrDefault(t => t.Id == id) is { } tab
            && (tab.Url.StartsWith("https://", StringComparison.Ordinal) || tab.Url.StartsWith("http://", StringComparison.Ordinal)))
            _db.RecordVisit(tab.Url, tab.Title, DateTimeOffset.UtcNow);
    }

    // ponytail: tous les onglets restaurés chargent leur page tout de suite ; le chargement paresseux vient au chantier légèreté.
    private void RestoreSession()
    {
        var session = JsonFileStore.Load<Session>(AppPaths.Session);
        var tabs = session.Urls.Select(url => Open(url, activate: false)).ToList();
        if (tabs.Count == 0) { Open(_settings.HomeUrl); return; }

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
                id = t.Id, url = t.Url, title = t.Title,
                loading = _pages.IsLoading(t.Id),
                canGoBack = _pages.CanGoBack(t.Id),
                canGoForward = _pages.CanGoForward(t.Id),
            }),
            activeId = _tabs.ActiveId,
            split = new { primaryId = _split.PrimaryId, secondaryId = _split.SecondaryId, ratio = _split.Ratio, visible = showSplit },
            maximized = Presenter?.State == OverlappedPresenterState.Maximized,
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
