using System.Runtime.InteropServices;
using System.Text.Json;
using Browser.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace Browser.App;

public sealed partial class BrowserController
{
    private bool ExecuteCommands2(Command c)
    {
        var id = c.Id ?? "";
        switch (c.Type)
        {
            case "profile.rename":
                if (Profiles.Rename(AppPaths.BaseDir, id, c.Title!) && id == AppPaths.Profile) _window.Title = $"Sol — {ProfileName(id)}";
                break;
            case "window.private":
                HidePopup(); Open(Settings.DefaultHomePage, isPrivate: true); return true;
            case "profile.isolated":
                HidePopup();
                OpenProfile(Profiles.CreateTemporary(AppPaths.BaseDir, T("Navigation privée", "Private browsing")), replace: false, isPrivate: true);
                return true;
            case "profile.switch": SwitchProfile(id); return true;
            case "profile.delete": DeleteProfile(id); return true;
            case "capture.menu": OpenCaptureMenu(); return true;
            case "capture.size": CapturePanel?.SetHeight(c.Height!.Value); return true;
            case "capture.visibility": CapturePanel?.SetVisible(c.Index == 1); return true;
            case "capture.selection": CapturePanel?.SetSelection(c.Index == 1); return true;
            case "capture.close": _capture.Cancel(); CapturePanel?.Hide(); return true;
            case "capture.save": _capture.Save(); return true;
            case "capture.region": _capture.Select(new Windows.Graphics.Imaging.BitmapBounds { X = (uint)Math.Max(0,c.X!.Value), Y = (uint)Math.Max(0,c.Y!.Value), Width = (uint)Math.Max(0,c.Width!.Value), Height = (uint)Math.Max(0,c.Height!.Value) }); return true;
            case "capture.run":
                if (c.Query is "region" or "visible" or "full")
                {
                    CapturePanel?.Hide();
                    Capture(c.Query, browser: c.Index == 1);
                } return true;
            case "window.close": _ = CloseWithSessionAsync(); return true;
            case "window.drag": BeginDrag(); return true;

            case "suggest.pick":
                if (_suggestTab is not null && c.Index is int i && i >= 0 && i < _suggestions.Count)
                    _pages.Navigate(_suggestTab, _suggestions[i].Url);
                break;
            case "ui.ready":
                _statePosted = null; // une vue de l'interface vient de (re)démarrer : elle n'a encore rien reçu
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
                return true;
            case "tab.close": Close(id); break;
            case "tab.activate": _selectedTabs.Clear(); Activate(id); break;
            case "group.new": EditGroup(tabId: id, group: null); return true;
            // query = couleur « #rrggbb », input = icône ; une valeur invalide est ignorée par TabManager.
            case "group.create": CreateSelectedGroup(c); break;
            case "group.update": _tabs.UpdateGroup(id, name: c.Title!, color: c.Query, icon: c.Input); break;
            case "group.add": _tabs.AddToGroup(id, c.Query!); KeepPairAdjacent(); break;
            case "group.remove": _tabs.RemoveFromGroup(id); KeepPairAdjacent(); break;
            case "group.toggle":
                if (_tabs.Groups.FirstOrDefault(g => g.Id == id) is { } toggled) _tabs.UpdateGroup(id, collapsed: !toggled.Collapsed);
                break;
            case "group.menu": OpenGroupMenu(id, c.X!.Value, c.Y!.Value); return true;
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
                return true;
            case "bookmark.add": if (IsWeb(c.Url!)) _db.AddBookmark(c.Url!, c.Title!); break;
            case "bookmark.remove": _db.RemoveBookmark(c.Url!); break;
            case "zoom.in" or "zoom.out" or "zoom.reset" or "zoom.set": Zoom(c.Id ?? _tabs.ActiveId, c.Type, c.Index); return true;
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
            default: return false;
        }
        HidePopup(); Refresh();
        return true;
    }
}
