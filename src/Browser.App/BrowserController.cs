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
    private readonly TabManager _tabs = new();
    private readonly SplitLayout _split = new();
    private readonly Settings _settings = JsonFileStore.Load<Settings>(AppPaths.Settings);

    public BrowserController(Window window, PageHost pages, UiBridge bridge)
    {
        (_window, _pages, _bridge) = (window, pages, bridge);
        _pages.PageChanged += (id, url, title) => { _tabs.Update(id, url, title); Refresh(); };
        _pages.RuntimeChanged += Refresh;
        _pages.NewTabRequested += url => { Open(url); Refresh(); };
        _window.AppWindow.Changed += (_, e) => { if (e.DidPresenterChange || e.DidSizeChange) Refresh(); };
    }

    public void Execute(Command c)
    {
        var id = c.Id ?? "";
        switch (c.Type)
        {
            case "ui.ready": if (_tabs.Tabs.Count == 0) Open(_settings.HomeUrl); break;
            case "tab.open": Open(c.Url is null ? _settings.HomeUrl : Resolve(c.Url)); break;
            case "tab.close": Close(id); break;
            case "tab.activate":
                if (_tabs.Activate(id) && _split.SecondaryId == id) _split.Close();
                break;
            case "tab.move": _tabs.Move(id, c.Index!.Value); break;
            case "tab.reopen": if (_tabs.ReopenClosed() is { } reopened) _pages.Add(reopened); break;
            case "nav.go": _pages.Navigate(id, Resolve(c.Input!)); break;
            case "nav.back": _pages.Back(id); break;
            case "nav.forward": _pages.Forward(id); break;
            case "nav.reload": _pages.Reload(id); break;
            case "nav.stop": _pages.Stop(id); break;
            case "split.open": if (_tabs.Tabs.Any(t => t.Id == id)) _split.Open(id, _tabs.ActiveId); break;
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

    private OverlappedPresenter? Presenter => _window.AppWindow.Presenter as OverlappedPresenter;
    private string Resolve(string input) => AddressInput.Resolve(input, _settings.SearchUrlTemplate);

    private void Open(string url) => _pages.Add(_tabs.Open(url));

    private void Close(string id)
    {
        if (!_tabs.Close(id)) return;
        _pages.Remove(id);
        _split.OnTabClosed(id);
        if (_split.SecondaryId == _tabs.ActiveId) _split.Close();
        if (_tabs.Tabs.Count == 0) _window.Close();
    }

    private void Refresh()
    {
        _pages.ApplyLayout(_tabs.ActiveId, _split.SecondaryId, _split.Ratio);
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
            split = new { secondaryId = _split.SecondaryId, ratio = _split.Ratio },
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
