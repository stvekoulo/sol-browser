using System.Runtime.InteropServices;
using System.Text.Json;
using Browser.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace Browser.App;

public sealed partial class BrowserController
{

    private void SaveSession()
    {
        if (AppPaths.Private) return; // navigation privée : la fenêtre ne laisse pas d'onglets derrière elle
        var tabs = _tabs.Tabs.Where(t => !t.Private).ToList();
        var groups = _tabs.Groups.Where(g => tabs.Any(t => t.GroupId == g.Id)).ToList();
        int? IndexOf(string? id) => tabs.FindIndex(t => t.Id == id) is >= 0 and var i ? i : null;
        JsonFileStore.Save(AppPaths.Session, new Session
        {
            Urls = tabs.Select(t => t.Url).ToList(),
            SessionIds = tabs.Select(t => t.SessionId).ToList(),
            TranslationOriginals = tabs.Select(t => _translationOriginals.GetValueOrDefault(t.Id, "")).ToList(),
            Titles = tabs.Select(t => t.Title).ToList(),
            PinnedCount = tabs.Count(t => t.Pinned),
            Icons = tabs.Select(t => _favicons.GetValueOrDefault(t.Id) is { Length: <= MaxSessionIcon } icon ? icon : "").ToList(),
            Devices = tabs.Select(t => _pages.DeviceState(t.Id) is var (device, landscape) ? device + (landscape ? "|L" : "") : "").ToList(),
            ShortcutOf = tabs.Select(t => _settings.Shortcuts.FindIndex(s => _shortcutTabs.GetValueOrDefault(s) == t.Id) is >= 0 and var rank ? rank : (int?)null).ToList(),
            Groups = groups.Select(g => new SessionGroup { Name = g.Name, Color = g.Color, Icon = g.Icon, Collapsed = g.Collapsed }).ToList(),
            GroupOf = tabs.Select(t => t.GroupId is null ? (int?)null : groups.FindIndex(g => g.Id == t.GroupId)).ToList(),
            ActiveIndex = IndexOf(_tabs.ActiveId) ?? 0,
            PrimaryIndex = IndexOf(_split.PrimaryId),
            SecondaryIndex = IndexOf(_split.SecondaryId),
            Ratio = _split.Ratio,
        });
    }

    private string? _lastActive;
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
    private string? StartIcon(string url) => _startIcons.GetValueOrDefault(SiteShortcut.DomainOf(url)) ?? (_tabs.Tabs.FirstOrDefault(t => SiteShortcut.DomainOf(t.Url) == SiteShortcut.DomainOf(url)) is { } tab
        && _favicons.GetValueOrDefault(tab.Id) is { } icon ? icon : _settings.Shortcuts.FirstOrDefault(s => SiteShortcut.DomainOf(s.Url) == SiteShortcut.DomainOf(url))?.Icon);

    private const int MaxStartFavorites = 300;
    private readonly Dictionary<string, string> _startPosted = new(); // dernier contenu envoyé à chaque page d'accueil
    // La page d'accueil porte des images (icônes, aperçus) : elle n'est renvoyée que si son contenu a changé.
    private void PostStartPages(IEnumerable<Tab> starts)
    {
        foreach (var tab in starts)
        {
            var json = StartJson(tab.Private);
            if (_startPosted.GetValueOrDefault(tab.Id) != json) _pages.PostStart(tab.Id, _startPosted[tab.Id] = json);
        }
        foreach (var gone in _startPosted.Keys.Where(id => _tabs.Tabs.All(t => t.Id != id)).ToList()) _startPosted.Remove(gone);
    }

    // « Voir tout » : les icônes des favoris au-delà des premiers, quelques sites à la fois, sans rafraîchir tout le
    // navigateur à chaque icône reçue.
    private bool _fetchingFavoriteIcons;
    private async Task FetchFavoriteIconsAsync()
    {
        if (_fetchingFavoriteIcons) return;
        _fetchingFavoriteIcons = true;
        try
        {
            var missing = _db.Bookmarks().Take(MaxStartFavorites).Select(b => b.Url).Where(IsWeb)
                .DistinctBy(SiteShortcut.DomainOf).Where(url => !_startIcons.ContainsKey(SiteShortcut.DomainOf(url))).ToList();
            foreach (var batch in missing.Chunk(4))
            {
                foreach (var url in batch) _startIcons[SiteShortcut.DomainOf(url)] = null;
                var icons = await Task.WhenAll(batch.Select(FetchSiteIconAsync));
                if (_closingWindow) return;
                for (var i = 0; i < batch.Length; i++) _startIcons[SiteShortcut.DomainOf(batch[i])] = icons[i];
                PostStartPages(_tabs.Tabs.Where(t => t.Url == Settings.DefaultHomePage && !_pages.IsAsleep(t.Id)).ToList());
            }
        }
        finally { _fetchingFavoriteIcons = false; }
    }

    private string StartJson(bool isPrivate = false)
    {
        isPrivate |= AppPaths.Private;
        IReadOnlyList<Visit> history = isPrivate ? [] : _db.RecentHistory(13).Where(v => v.Url != Settings.DefaultHomePage).Take(12).ToList();
        return JsonSerializer.Serialize(new
        {
            @event = "start", privateMode = isPrivate, lang = Language.Current, wallpaper = _settings.StartWallpaper,
            // Une photo va suivre (message « start-wallpaper ») : la page n'affiche pas l'image de Sol en l'attendant.
            wallpaperPhoto = CurrentWallpaper is not null,
            // Les favoris du profil, dans l'ordre de la bibliothèque : quatre rangées, puis le reste sur demande.
            favorites = _db.Bookmarks().Take(MaxStartFavorites).Select(b => new { url = b.Url, title = b.Title, icon = StartIcon(b.Url) }),
            history = history.Select(v => new { url = v.Url, title = v.Title, last = v.Last.ToUnixTimeMilliseconds(), icon = StartIcon(v.Url) }),
            groups = _tabs.Groups.Select(g => new { id = g.Id, name = g.Name, color = g.Color, count = _tabs.Tabs.Count(t => t.GroupId == g.Id) }),
            categories = StartCategories(isPrivate),
        });
    }

    private void Refresh()
    {
        if (_closingWindow || _restoringSession) return;
        ProtectPrivateAi();
        var activeProfile = EngineProfile?.ProfileName;
        if (_pinProfileName != activeProfile)
        {
            ExtensionPopup?.Hide(); _pinnedExtensions = []; _pinsInitialized = false; _pinProfileName = activeProfile;
            if (activeProfile is not null) _ = RefreshPinnedExtensionsAsync();
        }
        if (_nativeZoom != UseNativeZoom) { _nativeZoom = UseNativeZoom; NativeZoomChanged?.Invoke(); }
        var starts = _tabs.Tabs.Where(t => t.Url == Settings.DefaultHomePage && !_pages.IsAsleep(t.Id)).ToList();
        if (starts.Count > 0) PostStartPages(starts);
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
        var state = JsonSerializer.Serialize(new
        {
            tabs = _tabs.Tabs.Select(t => new
            {
                id = t.Id, url = t.Url, title = t.Title, pinned = t.Pinned, privateMode = t.Private || AppPaths.Private,
                sessionId = t.SessionId, sessionName = t.Private ? T("Navigation privée", "Private browsing") : t.SessionId.Length == 0 ? T("Session actuelle", "Current session") : _sessions.Name(t.SessionId),
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
                ai = AiReady,
            }),
            groups = _tabs.Groups.Select(g => new { id = g.Id, name = g.Name, color = g.Color, icon = g.Icon, collapsed = g.Collapsed }),
            activeId = _tabs.ActiveId,
            privateMode = PrivateActive,
            aiAvailable = !PrivateActive, chatOpen = Chat?.IsOpen == true, aiTabs = TabGroupAiState(),
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
        });
        // Une page qui charge rafraîchit plusieurs fois de suite sans que rien de visible ne change : la barre et la
        // carte latérale ne sont redessinées que si leur état a changé.
        if (state == _statePosted) return;
        _bridge.Post(_statePosted = state);
    }
    private string? _statePosted;

    // Déplacement de la fenêtre depuis la barre de titre dessinée par l'interface.
    private void BeginDrag()
    {
        // La boucle de déplacement Windows commence après la sortie de l'événement WebView2.
        _window.DispatcherQueue.TryEnqueue(() =>
        {
            var button = GetSystemMetrics(23 /* SM_SWAPBUTTON */) != 0 ? 0x02 : 0x01;
            if (IsFullScreen || _closingWindow || (GetAsyncKeyState(button) & 0x8000) == 0 || !GetCursorPos(out var point)) return;
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
            ReleaseCapture();
            SendMessage(hwnd, 0x00A1 /* WM_NCLBUTTONDOWN */, 2 /* HTCAPTION */, (nint)((point.X & 0xffff) | ((point.Y & 0xffff) << 16)));
        });
    }

    [StructLayout(LayoutKind.Sequential)] private struct DragPoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out DragPoint point);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern nint SendMessage(nint hwnd, uint msg, nint wParam, nint lParam);
}
