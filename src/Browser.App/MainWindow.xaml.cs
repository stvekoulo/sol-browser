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
        Title = AppPaths.Profile == Profiles.Default ? "Navigateur" : $"Navigateur — {Profiles.NameOf(AppPaths.BaseDir, AppPaths.Profile)}";
        // Pas de barre de titre système : l'interface dessine la sienne. La bordure reste, pour le redimensionnement.
        if (AppWindow.Presenter is OverlappedPresenter presenter) presenter.SetBorderAndTitleBar(true, false);
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
        if (args.Contains("--maximized") && AppWindow.Presenter is OverlappedPresenter presenter) presenter.Maximize();
    }

    // La liste de blocage se met à jour en arrière-plan, sans retarder le démarrage ; jamais si le blocage est coupé.
    private static async Task RefreshBlockListAsync(PageHost pages, Settings settings)
    {
        if (settings.BlockAds && await AdBlock.UpdateAsync() is { } fresh) pages.Blocker = fresh;
    }

    private async Task InitAsync()
    {
        var settings = JsonFileStore.Load<Settings>(AppPaths.Settings);
        var blur = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "inject", "blur.js"));

        UiBridge.Settings = settings;
        var pages = new PageHost(Pages, PrimaryColumn, DividerColumn, SecondaryColumn, blur, settings);
        pages.Blocker = AdBlock.Load();
        _ = RefreshBlockListAsync(pages, settings);
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
        var dock = new DockHost(DockFrame, Dock, dockBridge);
        var uiFolder = Path.Combine(AppContext.BaseDirectory, "ui");
        var panelBridge = new UiBridge(Panel, "index.html#panel");
        bridge.AttachPopup(panelBridge);
        var panel = new PanelHost(PanelRow, panelBridge, () => panelBridge.InitAsync(uiFolder, c => _controller!.Execute(c)));
        _controller = new BrowserController(this, pages, bridge, popup, dock, panel, settings);
        WireDivider();
        _sidebar.Resize(settings.SidebarWidth);
        WireSidebarGrip(settings);
        WireGestures();

        await bridge.InitAsync(uiFolder, _controller.Execute);
        _ = AdBlock.ApplyExtensionAsync(Ui.CoreWebView2.Profile, settings.BlockAds);
        await popupBridge.InitAsync(uiFolder, _controller.Execute);
        await dockBridge.InitAsync(uiFolder, _controller.Execute);
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
            _sidebar?.PointerAt(p.X, p.Y, menuOpen: PopupFrame.Visibility == Visibility.Visible);
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
            if ((now - _swipeAt).TotalMilliseconds > 350) { _swipe = 0; _swipeDone = false; } // nouveau geste
            _swipeAt = now;
            _swipe += p.MouseWheelDelta;
            if (_swipeDone) return;
            // ponytail: seuil fixe, et une page qui défile elle-même horizontalement déclenche aussi le geste ;
            // à affiner en ne réagissant qu'en bout de défilement.
            var progress = Math.Min(1, Math.Abs(_swipe) / SwipeThreshold);
            ShowSwipeHint(back: _swipe < 0, progress);
            if (progress < 1) return;
            _swipeDone = true;
            _controller?.Shortcut(_swipe < 0 ? "nav.back" : "nav.forward");
            HideSwipeHint(committed: true);
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
