using System.Runtime.InteropServices;
using Browser.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Browser.App;

public sealed partial class MainWindow : Window
{
    // Touche (code virtuel), modificateurs (Alt 1, Ctrl 2, Maj 4), action.
    private static readonly (uint Key, uint Modifiers, string Action)[] Shortcuts =
    [
        (0x54, 2, "tab.open"),        // Ctrl+T
        (0x57, 2, "tab.close"),       // Ctrl+W
        (0x54, 2 | 4, "tab.reopen"),  // Ctrl+Maj+T
        (0x4E, 2 | 4, "window.private"), // Ctrl+Maj+N
        (0x4C, 2, "address.focus"),   // Ctrl+L
        (0x52, 2, "nav.reload"),      // Ctrl+R
        (0x74, 0, "nav.reload"),      // F5
        (0x09, 2, "tab.next"),        // Ctrl+Tab
        (0x09, 2 | 4, "tab.previous"),// Ctrl+Maj+Tab
        (0x25, 1, "nav.back"),        // Alt+Gauche
        (0x27, 1, "nav.forward"),     // Alt+Droite
        (0xBB, 2, "zoom.in"),         // Ctrl + (touche =/+)
        (0x6B, 2, "zoom.in"),         // Ctrl + (pavé numérique)
        (0xBD, 2, "zoom.out"),        // Ctrl -
        (0x6D, 2, "zoom.out"),        // Ctrl - (pavé numérique)
        (0x30, 2, "zoom.reset"),      // Ctrl 0
    ];

    private readonly nint _hwnd;
    private readonly SubclassProc _wndProc; // gardé en champ : le délégué ne doit pas être libéré
    private BrowserController? _controller;
    private UiBridge? _bridge;
    private SidebarHost? _sidebar;

    public MainWindow()
    {
        InitializeComponent();
        Title = AppPaths.Profile == Profiles.Default ? "Sol" : $"Sol — {Profiles.NameOf(AppPaths.BaseDir, AppPaths.Profile)}";
        // Pas de barre de titre système : l'interface dessine la sienne. La bordure reste, pour le redimensionnement.
        if (AppWindow.Presenter is OverlappedPresenter presenter) presenter.SetBorderAndTitleBar(true, false);
        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "Sol.ico");
        if (File.Exists(icon)) AppWindow.SetIcon(icon);
        PlaceLikePreviousWindow();

        // Les touches tapées dans une page partent dans le processus du moteur et n'atteignent pas la fenêtre.
        // Les raccourcis sont donc enregistrés auprès de Windows, uniquement tant que la fenêtre est active.
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _wndProc = WndProc;
        SetWindowSubclass(_hwnd, _wndProc, 1, 0);
        Activated += (_, e) => SetShortcuts(e.WindowActivationState != WindowActivationState.Deactivated);
        Closed += (_, _) => SetShortcuts(false);

        _ = InitAsync();
    }

    // Après un changement de profil, la nouvelle fenêtre prend la place de l'ancienne (--bounds=x,y,largeur,hauteur).
    private void PlaceLikePreviousWindow()
    {
        var args = Environment.GetCommandLineArgs();
        const string prefix = "--bounds=";
        var parts = args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..].Split(',');
        if (parts is { Length: 4 } && parts.All(p => int.TryParse(p, out _)))
        {
            var n = parts.Select(int.Parse).ToArray();
            // Valeurs plausibles seulement : une fenêtre minuscule ou hors de portée serait introuvable.
            if (n[2] is >= 400 and <= 10000 && n[3] is >= 300 and <= 10000 && Math.Abs(n[0]) < 20000 && Math.Abs(n[1]) < 20000)
                AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(n[0], n[1], n[2], n[3]));
        }
        // Sol s'ouvre en plein écran (sans la barre des tâches) ; seule une fenêtre qui en remplace une autre reprend
        // sa place. Le bouton d'agrandissement de l'interface repasse en fenêtre, et inversement.
        if (parts is null || args.Contains("--fullscreen")) AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
        else if (args.Contains("--maximized") && AppWindow.Presenter is OverlappedPresenter presenter) presenter.Maximize();
    }

    // Plein écran demandé par une page (une vidéo) : la fenêtre couvre l'écran et tout le reste de l'interface
    // s'efface. Échap, ou le bouton du lecteur, ramène l'interface telle qu'elle était.
    // ponytail: en vue scindée, les deux pages restent côte à côte ; donner tout l'écran à une seule si le besoin se présente.
    private (GridLength Top, GridLength Side, GridLength Panel, Visibility Sidebar, AppWindowPresenterKind Window)? _beforeFullScreen;

    private void SetFullScreen(bool on)
    {
        if (on == _beforeFullScreen.HasValue) return;
        var none = new GridLength(0);
        if (on)
        {
            _beforeFullScreen = (Root.RowDefinitions[0].Height, SidebarColumn.Width, PanelRow.Height, Sidebar.Visibility, AppWindow.Presenter.Kind);
            (Root.RowDefinitions[0].Height, SidebarColumn.Width, PanelRow.Height) = (none, none, none);
            (Ui.Visibility, Sidebar.Visibility, PopupFrame.Visibility) = (Visibility.Collapsed, Visibility.Collapsed, Visibility.Collapsed);
            if (AppWindow.Presenter.Kind != AppWindowPresenterKind.FullScreen) AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
        }
        else if (_beforeFullScreen is { } before)
        {
            _beforeFullScreen = null;
            if (before.Window != AppWindowPresenterKind.FullScreen) AppWindow.SetPresenter(before.Window); // déjà en plein écran avant la vidéo : on y reste
            (Root.RowDefinitions[0].Height, SidebarColumn.Width, PanelRow.Height) = (before.Top, before.Side, before.Panel);
            (Ui.Visibility, Sidebar.Visibility) = (Visibility.Visible, before.Sidebar);
        }
    }

    private async Task InitAsync()
    {
        var settings = JsonFileStore.Load<Settings>(AppPaths.Settings);
        if (settings.Font == "custom") settings.Font = "sf"; // ancien nom du choix « SF Pro »
        AdBlock.PrepareExtension(); // avant la création du moteur
        var blur = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "inject", "blur.js"));

        UiBridge.Settings = settings;
        Root.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(UiBridge.InitialBackground);
        var pages = new PageHost(Pages, PrimaryColumn, DividerColumn, SecondaryColumn, blur, settings);
        pages.FullScreenChanged += SetFullScreen;
        var bridge = new UiBridge(Ui, "index.html", Sidebar, SidebarColumn, pages);
        _bridge = bridge;
        UiBridge.SystemColorsChanged += () => DispatcherQueue.TryEnqueue(bridge.BroadcastTheme);
        _sidebar = new SidebarHost(Sidebar, SidebarColumn, bridge.Post);
        bridge.SidebarHost = _sidebar;
        bridge.PageColorChanged += (r, g, b) =>
        {
            Root.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, r, g, b));
            pages.SetPageColor(r, g, b); // la barre d'état d'un appareil simulé prend la couleur de la page
        };
        var popupBridge = new UiBridge(Popup, "index.html#popup");
        bridge.AttachPopup(popupBridge);
        var popup = new PopupHost(PopupFrame, Popup, popupBridge);
        popup.PageLeft = () => SidebarColumn.ActualWidth;
        var dockBridge = new UiBridge(Dock, "index.html#dock");
        bridge.AttachPopup(dockBridge);
        var uiFolder = Path.Combine(AppContext.BaseDirectory, "ui");
        var dock = new DockHost(DockFrame, Dock, dockBridge, () => dockBridge.InitAsync(uiFolder, c => _controller!.Execute(c)));
        var panelBridge = new UiBridge(Panel, "index.html#panel");
        bridge.AttachPopup(panelBridge);
        var panel = new PanelHost(PanelRow, panelBridge, () => panelBridge.InitAsync(uiFolder, c => _controller!.Execute(c)), settings);
        WirePanelGrip(panel, settings);
        var welcomeBridge = new UiBridge(Welcome, "index.html#welcome");
        bridge.AttachPopup(welcomeBridge);
        var welcome = new WelcomeHost(Welcome, welcomeBridge, () => welcomeBridge.InitAsync(uiFolder, c => _controller!.Execute(c)));
        _controller = new BrowserController(this, pages, bridge, popup, dock, panel, welcome, settings) { ExtensionPopup = new ExtensionPopupHost(ExtensionFrame, Extension) };
        WireDivider();
        _sidebar.Resize(settings.SidebarWidth);
        WireSidebarGrip(settings);
        WireGestures();

        await bridge.InitAsync(uiFolder, _controller.Execute);
        await AdBlock.ApplyExtensionAsync(Ui.CoreWebView2.Profile, settings.BlockAds);
        if (settings.BlockAds) _ = AdBlock.UpdateExtensionAsync();
        await popupBridge.InitAsync(uiFolder, _controller.Execute);
        popup.Hide();
        dock.Hide();
    }

    // Gestes : boutons latéraux de la souris, et balayage horizontal à deux doigts du pavé tactile, pour page
    // précédente et page suivante. Le moteur ne reconnaît pas ce balayage dans ce type de vue : on le lit ici.
    private const double SwipeThreshold = 480;
    private double _swipe;
    private DateTime _swipeAt;
    private bool _swipeDone;
    private DispatcherTimer? _swipeIdle;

    // Pastille à flèche qui sort du bord pendant le balayage : elle avance avec le geste, et la page change
    // quand elle est entièrement sortie. Un simple élément XAML posé sur la page, sans effet sur la souris.
    private DateTime _swipeStart;
    private bool _swipeProbed;
    private int _swipeId;

    // Demande à la page si elle a utilisé ce balayage. Si oui, le geste est abandonné ; sinon, s'il est allé au bout,
    // la navigation a lieu.
    private async Task SettleSwipeAsync(int gesture, double sinceMs, string? navigate)
    {
        var used = _controller is not null && await _controller.PageUsedSidewaysAsync(sinceMs);
        if (gesture != _swipeId) return; // un autre geste a commencé entre-temps
        if (used) { _swipeDone = true; HideSwipeHint(committed: false); return; }
        if (navigate is null) return;
        _controller?.Shortcut(navigate);
        HideSwipeHint(committed: true);
    }

    private void ShowSwipeHint(bool back, double progress)
    {
        SwipeHint.HorizontalAlignment = back ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        SwipeGlyph.Glyph = back ? "" : ""; // chevron gauche, chevron droit
        var eased = 1 - Math.Pow(1 - progress, 3);     // la pastille sort vite puis ralentit
        SwipeShift.X = (back ? -1 : 1) * (64 - 84 * eased);
        SwipeHint.Opacity = Math.Min(1, progress * 1.6);
        SwipeScale.ScaleX = SwipeScale.ScaleY = 0.7 + 0.3 * eased;

        // Geste abandonné avant le seuil : la pastille rentre.
        _swipeIdle ??= CreateSwipeIdleTimer();
        _swipeIdle.Start();
    }

    private DispatcherTimer CreateSwipeIdleTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) =>
        {
            if ((DateTime.UtcNow - _swipeAt).TotalMilliseconds < 300) return;
            timer.Stop();
            if (!_swipeDone) HideSwipeHint(committed: false);
        };
        return timer;
    }

    private void HideSwipeHint(bool committed)
    {
        _swipeIdle?.Stop();
        var ease = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(committed ? 380 : 220);
        var story = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        void Animate(DependencyObject target, string property, double to, TimeSpan begin)
        {
            var animation = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation { To = to, Duration = duration, BeginTime = begin, EasingFunction = ease };
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animation, target);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animation, property);
            story.Children.Add(animation);
        }
        // Validé : la pastille grossit un instant avant de s'effacer. Abandonné : elle retourne dans le bord.
        var back = SwipeHint.HorizontalAlignment == HorizontalAlignment.Left;
        Animate(SwipeHint, "Opacity", 0, committed ? TimeSpan.FromMilliseconds(120) : TimeSpan.Zero);
        if (committed) { Animate(SwipeScale, "ScaleX", 1.25, TimeSpan.Zero); Animate(SwipeScale, "ScaleY", 1.25, TimeSpan.Zero); }
        else Animate(SwipeShift, "X", back ? -64 : 64, TimeSpan.Zero);
        // À la fin, les valeurs redeviennent modifiables directement pour le prochain geste.
        story.Completed += (_, _) => { story.Stop(); SwipeHint.Opacity = 0; };
        story.Begin();
    }

    private void WireGestures()
    {
        // handledEventsToo : les vues web marquent ces événements comme traités, on les observe quand même.
        Root.AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, e) =>
        {
            var p = e.GetCurrentPoint(Root).Properties;
            if (p.IsXButton1Pressed) _controller?.Shortcut("nav.back");
            else if (p.IsXButton2Pressed) _controller?.Shortcut("nav.forward");
        }), true);

        // Carte latérale fermée : approcher le curseur du bord gauche la fait apparaître par-dessus la page,
        // sans la redimensionner ; elle se retire quand le curseur s'en éloigne.
        Root.AddHandler(UIElement.PointerMovedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, e) =>
        {
            var p = e.GetCurrentPoint(Root).Position;
            if (!_beforeFullScreen.HasValue) _sidebar?.PointerAt(p.X, p.Y, menuOpen: PopupFrame.Visibility == Visibility.Visible);
        }), true);

        Root.AddHandler(UIElement.PointerWheelChangedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, e) =>
        {
            var p = e.GetCurrentPoint(Root).Properties;
            // Ctrl + molette : zoom de la page.
            if (!p.IsHorizontalMouseWheel && e.KeyModifiers.HasFlag(Windows.System.VirtualKeyModifiers.Control))
            {
                _controller?.Shortcut(p.MouseWheelDelta > 0 ? "zoom.in" : "zoom.out");
                return;
            }
            if (!p.IsHorizontalMouseWheel) return;
            var now = DateTime.UtcNow;
            if ((now - _swipeAt).TotalMilliseconds > 350) { _swipe = 0; _swipeDone = false; _swipeProbed = false; _swipeStart = now; _swipeId++; } // nouveau geste
            _swipeAt = now;
            _swipe += p.MouseWheelDelta;
            if (_swipeDone) return;
            // Un balayage dont la page se sert elle-même (carrousel, zone qui défile de côté) n'est pas un retour en
            // arrière : la page est consultée une première fois dès que le geste se dessine, puis avant de naviguer.
            // Tout le geste est alors écarté, même si le carrousel arrive en bout de course en chemin.
            // ponytail: seuil fixe ; un carrousel placé dans un cadre d'un autre site n'est pas vu.
            var progress = Math.Min(1, Math.Abs(_swipe) / SwipeThreshold);
            var since = (now - _swipeStart).TotalMilliseconds + 600;
            if (progress >= 0.2 && !_swipeProbed)
            {
                _swipeProbed = true;
                _ = SettleSwipeAsync(_swipeId, since, navigate: null);
            }
            ShowSwipeHint(back: _swipe < 0, progress);
            if (progress < 1) return;
            _swipeDone = true;
            _ = SettleSwipeAsync(_swipeId, since, navigate: _swipe < 0 ? "nav.back" : "nav.forward");
        }), true);
    }

    // Séparateur de la vue scindée : scrutation directe de la souris tant que le bouton est enfoncé.
    private DispatcherTimer? _dividerTimer;
    private double _dividerRatio;

    private void WireDivider()
    {
        Divider.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            _dividerTimer ??= CreateDividerTimer();
            _dividerTimer.Start();
        };
    }

    // Bord droit de la carte latérale : même scrutation de la souris que le séparateur. La largeur choisie est
    // enregistrée au relâchement.
    private void WireSidebarGrip(Settings settings)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            var button = GetSystemMetrics(23 /* SM_SWAPBUTTON */) != 0 ? 0x02 : 0x01;
            if ((GetAsyncKeyState(button) & 0x8000) == 0)
            {
                timer.Stop();
                settings.SidebarWidth = _sidebar!.Width;
                JsonFileStore.Save(AppPaths.Settings, settings);
                return;
            }
            if (!GetCursorPos(out var point) || !ScreenToClient(_hwnd, ref point)) return;
            // Le curseur est en pixels de l'écran, la carte en unités d'affichage.
            _sidebar!.Resize(point.X / Root.XamlRoot.RasterizationScale + 3);
        };
        // Deux pressions rapprochées : retour à la largeur d'origine. (Compté ici : l'évènement de double-clic de
        // l'interface n'arrive pas sur cette poignée, dont la pression est déjà prise pour le glisser.)
        long lastPress = 0;
        SidebarGrip.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            var now = Environment.TickCount64;
            var doubleClick = now - lastPress <= GetDoubleClickTime();
            lastPress = doubleClick ? 0 : now;
            if (!doubleClick) { timer.Start(); return; }
            timer.Stop();
            _sidebar!.Resize(SidebarHost.DefaultWidth);
            settings.SidebarWidth = _sidebar.Width;
            JsonFileStore.Save(AppPaths.Settings, settings);
        };
    }

    [DllImport("user32.dll")] private static extern uint GetDoubleClickTime();

    // Bord haut du panneau du bas : même scrutation de la souris ; la hauteur choisie est enregistrée au relâchement.
    private void WirePanelGrip(PanelHost panel, Settings settings)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            var button = GetSystemMetrics(23 /* SM_SWAPBUTTON */) != 0 ? 0x02 : 0x01;
            if ((GetAsyncKeyState(button) & 0x8000) == 0)
            {
                timer.Stop();
                JsonFileStore.Save(AppPaths.Settings, settings);
                return;
            }
            if (!GetCursorPos(out var point) || !ScreenToClient(_hwnd, ref point) || !GetClientRect(_hwnd, out var rect)) return;
            // Le curseur est en pixels de l'écran, le panneau en unités d'affichage ; les pages gardent au moins 160 de haut.
            var scale = Root.XamlRoot.RasterizationScale;
            panel.Resize(Math.Min((rect.Bottom - point.Y) / scale + 4, rect.Bottom / scale - 52 - 160));
        };
        PanelGrip.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            timer.Start();
        };
    }

    private DispatcherTimer CreateDividerTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            // Bouton principal : le droit si l'utilisateur a inversé les boutons de sa souris.
            var button = GetSystemMetrics(23 /* SM_SWAPBUTTON */) != 0 ? 0x02 : 0x01;
            if ((GetAsyncKeyState(button) & 0x8000) == 0) { timer.Stop(); return; }
            if (!GetCursorPos(out var point) || !ScreenToClient(_hwnd, ref point) || !GetClientRect(_hwnd, out var rect)) return;

            var sideWidth = SidebarColumn.Width.Value;
            var pagesWidth = rect.Right - sideWidth;
            if (pagesWidth <= 0) return;
            var ratio = (point.X - sideWidth) / pagesWidth;
            if (Math.Abs(ratio - _dividerRatio) < 0.002) return;
            _dividerRatio = ratio;
            _controller?.Execute(new Command("split.resize", Ratio: ratio));
        };
        return timer;
    }

    private void SetShortcuts(bool active)
    {
        for (var i = 0; i < Shortcuts.Length; i++)
        {
            UnregisterHotKey(_hwnd, i);
            if (active) RegisterHotKey(_hwnd, i, Shortcuts[i].Modifiers | 0x4000 /* MOD_NOREPEAT */, Shortcuts[i].Key);
        }
    }

    private nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam, nuint id, nuint data)
    {
        if (msg == 0x0312 /* WM_HOTKEY */ && wParam >= 0 && wParam < Shortcuts.Length)
        {
            var action = Shortcuts[(int)wParam].Action;
            if (action == "address.focus") Ui.Focus(FocusState.Programmatic);
            _controller?.Shortcut(action);
            return 0;
        }
        return DefSubclassProc(hwnd, msg, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(nint hwnd, ref Point point);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);

    private delegate nint SubclassProc(nint hwnd, uint msg, nint wParam, nint lParam, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(nint hwnd, SubclassProc proc, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint hwnd, uint msg, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint hwnd, int id);
}
