using System.Runtime.InteropServices;
using Browser.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

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
    ];

    private readonly nint _hwnd;
    private readonly SubclassProc _wndProc; // gardé en champ : le délégué ne doit pas être libéré
    private BrowserController? _controller;

    public MainWindow()
    {
        InitializeComponent();
        Title = "Navigateur";
        // Pas de barre de titre système : l'interface dessine la sienne. La bordure reste, pour le redimensionnement.
        if (AppWindow.Presenter is OverlappedPresenter presenter) presenter.SetBorderAndTitleBar(true, false);

        // Les touches tapées dans une page partent dans le processus du moteur et n'atteignent pas la fenêtre.
        // Les raccourcis sont donc enregistrés auprès de Windows, uniquement tant que la fenêtre est active.
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _wndProc = WndProc;
        SetWindowSubclass(_hwnd, _wndProc, 1, 0);
        Activated += (_, e) => SetShortcuts(e.WindowActivationState != WindowActivationState.Deactivated);
        Closed += (_, _) => SetShortcuts(false);

        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        var settings = JsonFileStore.Load<Settings>(AppPaths.Settings);
        var blur = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "inject", "blur.js"));

        var pages = new PageHost(Pages, PrimaryColumn, DividerColumn, SecondaryColumn, blur, settings);
        var bridge = new UiBridge(Ui, "index.html", Sidebar, SidebarColumn, pages);
        bridge.PageColorChanged += (r, g, b) =>
            Root.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, r, g, b));
        var popupBridge = new UiBridge(Popup, "index.html#popup");
        bridge.AttachPopup(popupBridge);
        var popup = new PopupHost(PopupFrame, Popup, popupBridge);
        var dockBridge = new UiBridge(Dock, "index.html#dock");
        bridge.AttachPopup(dockBridge);
        var dock = new DockHost(DockFrame, Dock, dockBridge);
        _controller = new BrowserController(this, pages, bridge, popup, dock, settings);
        WireDivider();

        var uiFolder = Path.Combine(AppContext.BaseDirectory, "ui");
        await bridge.InitAsync(uiFolder, _controller.Execute);
        await popupBridge.InitAsync(uiFolder, _controller.Execute);
        await dockBridge.InitAsync(uiFolder, _controller.Execute);
        popup.Hide();
        dock.Hide();
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
