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
        var blur = settings.BlurEnabled
            ? File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "inject", "blur.js"))
            : null;

        var pages = new PageHost(Pages, PrimaryColumn, DividerColumn, SecondaryColumn, blur);
        var bridge = new UiBridge(Ui);
        var popupBridge = new UiBridge(Popup, "index.html#popup");
        var popup = new PopupHost(PopupFrame, Popup, popupBridge);
        _controller = new BrowserController(this, pages, bridge, popup, settings);
        WireDivider();

        var uiFolder = Path.Combine(AppContext.BaseDirectory, "ui");
        await bridge.InitAsync(uiFolder, _controller.Execute);
        await popupBridge.InitAsync(uiFolder, _controller.Execute);
        popup.Hide();
    }

    // Séparateur de la vue scindée : le glisser change la part de chaque page.
    // Dès que la souris quitte la poignée, elle survole une page web, qui absorbe les mouvements : la capture
    // XAML ne suffit pas. On capture donc la souris au niveau de la fenêtre et on suit WM_MOUSEMOVE.
    private bool _draggingDivider;

    private void WireDivider()
    {
        Divider.PointerPressed += (_, e) =>
        {
            Log("divider pressed");
            _draggingDivider = true;
            SetCapture(_hwnd);
            e.Handled = true;
        };
    }

    private bool HandleDividerDrag(uint msg, nint lParam)
    {
        if (!_draggingDivider) return false;
        switch (msg)
        {
            case 0x0200: // WM_MOUSEMOVE : x en pixels dans la zone cliente
                if (GetClientRect(_hwnd, out var rect) && rect.Right > 0)
                    _controller?.Execute(new Command("split.resize", Ratio: (double)(short)(lParam & 0xFFFF) / rect.Right));
                return true;
            case 0x0202: // WM_LBUTTONUP
                Log("divider released");
                _draggingDivider = false;
                ReleaseCapture();
                return true;
            case 0x0215: // WM_CAPTURECHANGED
                Log("divider capture lost");
                _draggingDivider = false;
                return false;
        }
        return false;
    }

    // Journal de diagnostic, écrit seulement si NAVIGATEUR_DEBUG=1.
    private static readonly bool Debug = Environment.GetEnvironmentVariable("NAVIGATEUR_DEBUG") == "1";
    private static void Log(string line)
    {
        if (Debug) File.AppendAllText(Path.Combine(AppPaths.DataDir, "debug.log"), $"{DateTime.Now:HH:mm:ss.fff} {line}\n");
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
        if (HandleDividerDrag(msg, lParam)) return 0;
        return DefSubclassProc(hwnd, msg, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern nint SetCapture(nint hwnd);
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint hwnd, out Rect rect);

    private delegate nint SubclassProc(nint hwnd, uint msg, nint wParam, nint lParam, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(nint hwnd, SubclassProc proc, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint hwnd, uint msg, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint hwnd, int id);
}
