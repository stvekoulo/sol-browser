using System.Runtime.InteropServices;
using Browser.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Browser.App;

public sealed partial class MainWindow : Window
{
    private static readonly IReadOnlyList<BrowserShortcut> Shortcuts = BrowserShortcuts.All;

    private readonly nint _hwnd;
    private readonly SubclassProc _wndProc; // gardé en champ : le délégué ne doit pas être libéré
    private BrowserController? _controller;
    private UiBridge? _bridge;
    private SidebarHost? _sidebar;
    private readonly Settings _settings = JsonFileStore.Load<Settings>(AppPaths.Settings);

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

        _ = StartInterfaceAsync();
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
        // Le réglage de plein écran reste activé pour les profils existants. Sinon, Windows réserve la barre des tâches.
        if (_settings.FullScreen && (parts is null || args.Contains("--fullscreen"))) AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
        else if (parts is null && AppWindow.Presenter is OverlappedPresenter normal) normal.Maximize();
        else if (args.Contains("--maximized") && AppWindow.Presenter is OverlappedPresenter presenter) presenter.Maximize();
    }

    public void SetBrowserFullScreen(bool on, bool maximize = false)
    {
        AppWindow.SetPresenter(on ? AppWindowPresenterKind.FullScreen : AppWindowPresenterKind.Overlapped);
        if (!on && AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(true, false);
            if (maximize) presenter.Maximize();
        }
    }

    // Plein écran demandé par une page (une vidéo) : la fenêtre couvre l'écran et tout le reste de l'interface
    // s'efface. Échap, ou le bouton du lecteur, ramène l'interface telle qu'elle était.
    // ponytail: en vue scindée, les deux pages restent côte à côte ; donner tout l'écran à une seule si le besoin se présente.
    private (GridLength Top, GridLength Side, GridLength Panel, AppWindowPresenterKind Window, bool Maximized)? _beforeFullScreen;

    private void SetFullScreen(bool on)
    {
        if (on == _beforeFullScreen.HasValue) return;
        var none = new GridLength(0);
        if (on)
        {
            _beforeFullScreen = (Root.RowDefinitions[0].Height, SidebarColumn.Width, PanelRow.Height, AppWindow.Presenter.Kind, (AppWindow.Presenter as OverlappedPresenter)?.State == OverlappedPresenterState.Maximized);
            (Root.RowDefinitions[0].Height, SidebarColumn.Width, PanelRow.Height) = (none, none, none);
            (Ui.Visibility, PopupFrame.Visibility) = (Visibility.Collapsed, Visibility.Collapsed);
            if (AppWindow.Presenter.Kind != AppWindowPresenterKind.FullScreen) AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
        }
        else if (_beforeFullScreen is { } before)
        {
            _beforeFullScreen = null;
            if (before.Window != AppWindowPresenterKind.FullScreen) SetBrowserFullScreen(false, before.Maximized); // déjà en plein écran avant la vidéo : on y reste
            (Root.RowDefinitions[0].Height, SidebarColumn.Width, PanelRow.Height) = (before.Top, before.Side, before.Panel);
            Ui.Visibility = Visibility.Visible;
        }
    }

    private async Task InitAsync()
    {
        var settings = _settings;
        if (settings.Font == "custom") settings.Font = "sf"; // ancien nom du choix « SF Pro »
        var blur = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "inject", "blur.js"));

        UiBridge.Settings = settings;
        Root.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(UiBridge.InitialBackground);
        StartupShield.Background = Root.Background;
        var pages = new PageHost(Pages, PrimaryColumn, DividerColumn, SecondaryColumn, blur, settings);
        WirePageCursor(pages);
        pages.FullScreenChanged += SetFullScreen;
        var bridge = new UiBridge(Ui, "index.html", pages: pages);
        _bridge = bridge;
        UiBridge.SystemColorsChanged += () => DispatcherQueue.TryEnqueue(bridge.BroadcastTheme);
        _sidebar = new SidebarHost(SidebarColumn, bridge.Post);
        bridge.SidebarHost = _sidebar;
        WirePresentation(bridge,pages);
        bridge.PageColorChanged += (r, g, b) =>
        {
            Root.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, r, g, b));
            pages.SetPageColor(r, g, b); // la barre d'état d'un appareil simulé prend la couleur de la page
        };
        var popupBridge = new UiBridge(Popup, "index.html#popup");
        bridge.AttachPopup(popupBridge);
        var popup = new PopupHost(PopupFrame, Popup, popupBridge);
        var captureBridge = new UiBridge(CaptureControls, "index.html#capture");
        bridge.AttachPopup(captureBridge);
        var capture = new PopupHost(CaptureFrame, CaptureControls, captureBridge);
        Root.SizeChanged += (_, _) => { popup.Rescale(); capture.Rescale(); };
        popup.PageLeft = () => SidebarColumn.ActualWidth;
        popup.PageVertical = () => (Pages.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point(0, 0)).Y, Pages.ActualHeight);
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
        _controller = new BrowserController(this, pages, bridge, popup, dock, panel, welcome, settings) { ExtensionPopup = new ExtensionPopupHost(this), CapturePanel = capture };
        _controller.NativeZoomChanged += () => SetShortcuts(_shortcutsActive);
        WireDivider();
        void ApplyInterfaceScale()
        {
            if (_beforeFullScreen is null) Root.RowDefinitions[0].Height = new GridLength(52 * settings.UiFactor);
            _sidebar.Resize(settings.SidebarWidth * settings.UiFactor);
        }
        _controller.InterfaceScaleChanged += ApplyInterfaceScale;
        ApplyInterfaceScale();
        WireSidebarGrip(settings);
        WireGestures();

        await bridge.InitAsync(uiFolder, _controller.Execute);
        await popupBridge.InitAsync(uiFolder, _controller.Execute);
        await captureBridge.InitAsync(uiFolder, _controller.Execute);
        popup.Hide();
        dock.Hide();
    }

    private bool _shortcutsActive;
    private void SetShortcuts(bool active)
    {
        _shortcutsActive = active;
        for (var i = 0; i < Shortcuts.Count; i++)
        {
            UnregisterHotKey(_hwnd, i);
            if (active && !(_controller?.UseNativeZoom == true && Shortcuts[i].Action.StartsWith("zoom.")))
                RegisterHotKey(_hwnd, i, Shortcuts[i].Modifiers | 0x4000 /* MOD_NOREPEAT */, Shortcuts[i].Key);
        }
    }

    private nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam, nuint id, nuint data)
    {
        if (msg == 0x0312 /* WM_HOTKEY */ && wParam >= 0 && wParam < Shortcuts.Count)
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
