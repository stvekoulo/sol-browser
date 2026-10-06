using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Browser.App;

/// <summary>
/// Fenêtre d'une extension (la page qu'ouvre son bouton). Comme dans Edge, c'est une vraie fenêtre de Windows, sans
/// habillage de Sol : sans barre de titre, posée sous le bouton, aux coins et à l'ombre dessinés par le système. Elle
/// peut dépasser de la fenêtre du navigateur. L'extension décide de sa taille, de 25 × 25 à 800 × 600, et la fenêtre
/// la suit tant qu'elle est ouverte ; elle se referme dès qu'on clique ailleurs.
/// La vue partage le moteur et le profil des pages : l'extension y est chez elle.
/// </summary>
public sealed class ExtensionPopupHost(Window owner)
{
    private const double MinSize = 25, MaxWidth = 800, MaxHeight = 600;
    private const int Gap = 8; // distance gardée avec les bords de l'écran, en pixels
    private readonly nint _owner = WinRT.Interop.WindowNative.GetWindowHandle(owner);
    private Window? _window;
    private WebView2? _view;
    private Task? _initialization;
    private (double Right, double Top) _anchor; // sous le bouton, en pixels d'écran
    private bool _ownerClosed;
    private int _run;

    // Mesurée après la création du DOM : au démarrage du document, html/head peuvent ne pas encore exister.
    // La racine épouse le contenu ; ses dimensions ne doivent pas rester celles de la vue provisoire.
    private const string Measure = """
        (() => {
          const root = document.documentElement, body = document.body;
          if (!root || !body || document.readyState === 'loading') return JSON.stringify([0, 0]);
          const declared = root.computedStyleMap();
          for (const axis of ['width', 'height']) {
            const value = declared.get(axis).toString();
            // Conserver les dimensions explicites de l'extension ; seules les dimensions de viewport deviennent intrinsèques.
            if (value === 'auto' || value.endsWith('%')) root.style.setProperty(axis, 'max-content', 'important');
          }
          const box = root.getBoundingClientRect(), content = body.getBoundingClientRect();
          const css = getComputedStyle(body);
          return JSON.stringify([
            Math.ceil(Math.max(box.width, content.right + (parseFloat(css.marginRight) || 0), body.scrollWidth + content.left)),
            Math.ceil(Math.max(box.height, content.bottom + (parseFloat(css.marginBottom) || 0), body.scrollHeight + content.top))
          ]);
        })()
        """;

    /// <summary>Un lien de l'extension demande un nouvel onglet.</summary>
    public event Action<string>? TabRequested;

    public bool IsOpen { get; private set; }

    private double Scale => owner.Content?.XamlRoot?.RasterizationScale ?? 1;

    /// <param name="right">Bord droit de la fenêtre (celui du bouton), dans la fenêtre du navigateur.</param>
    public async Task ShowAsync(string url, double right, double top)
    {
        var run = ++_run;
        var origin = new Point();
        ClientToScreen(_owner, ref origin);
        _anchor = (origin.X + right * Scale, origin.Y + top * Scale);
        try { await (_initialization ??= InitializeAsync()); }
        catch { _initialization = null; throw; }
        if (run != _run || _window is null || _view is null) return;
        Place(320, 200); // en attendant que la page dise sa taille
        _view.CoreWebView2.Navigate(url);
        IsOpen = true;
        _window.AppWindow.Show();
        _window.Activate();
        _view.Focus(FocusState.Programmatic);
        _ = FollowSizeAsync(run);
    }

    private async Task InitializeAsync()
    {
        _view = new WebView2
        {
            DefaultBackgroundColor = UiBridge.InitialBackground,
            UseSystemFocusVisuals = false,
            FocusVisualPrimaryThickness = new Thickness(0),
            FocusVisualSecondaryThickness = new Thickness(0),
        };
        _window = new Window { Content = _view, Title = "Sol" };
        var app = _window.AppWindow;
        app.IsShownInSwitchers = false;
        if (app.Presenter is OverlappedPresenter presenter)
        {
            (presenter.IsResizable, presenter.IsMaximizable, presenter.IsMinimizable) = (false, false, false);
            presenter.SetBorderAndTitleBar(false, false); // en dernier : les options du presenter peuvent recréer le cadre.
        }
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        // WinUI conserve parfois WS_DLGFRAME même sans barre de titre : ce cadre ajoute 3 px blancs par côté.
        SetWindowLong(hwnd, -16, GetWindowLong(hwnd, -16) & ~0x00C40000); // WS_CAPTION | WS_THICKFRAME
        SetWindowPos(hwnd, 0, 0, 0, 0, 0, 0x0037); // recalcul du cadre, sans déplacement, taille ni activation
        // Fenêtre du navigateur pour propriétaire : elle reste au-dessus de lui, sans entrée dans la barre des tâches.
        if (nint.Size == 8) SetWindowLongPtr(hwnd, -8, _owner); else SetWindowLong(hwnd, -8, (int)_owner);
        var rounded = 2; // coins et ombre du système (Windows 11) ; sans effet avant
        var applied = DwmSetWindowAttribute(hwnd, 33, ref rounded, sizeof(int));
        if (applied != 0) DebugLog.Write($"fenêtre d'extension : coins du système refusés (0x{applied:X8})");
        var noBorder = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE : coins et ombre natifs, sans contour blanc.
        applied = DwmSetWindowAttribute(hwnd, 34, ref noBorder, sizeof(int)); // DWMWA_BORDER_COLOR (Windows 11)
        if (applied != 0) DebugLog.Write($"fenêtre d'extension : suppression du contour refusée (0x{applied:X8})");
        _window.Activated += (_, e) => { if (e.WindowActivationState == WindowActivationState.Deactivated) Hide(); }; // clic ailleurs
        app.Closing += (_, e) => { if (!_ownerClosed) { e.Cancel = true; Hide(); } }; // Alt+F4 : elle se cache, elle resservira
        owner.Closed += (_, _) => { _ownerClosed = true; _window.Close(); };

        // Une vue ne démarre son moteur qu'une fois affichée : la fenêtre se montre d'abord, sans prendre le curseur.
        Place(320, 200);
        app.Show(activateWindow: false);
        await _view.EnsureCoreWebView2Async(await AdBlock.EnvironmentAsync());
        var core = _view.CoreWebView2;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.IsWebMessageEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.NewWindowRequested += (_, e) => { e.Handled = true; Hide(); TabRequested?.Invoke(e.Uri); };
        core.WindowCloseRequested += (_, _) => Hide(); // l'extension ferme sa fenêtre elle-même, une fois son action faite
    }

    // Sous le bouton, bord droit aligné sur lui, sans sortir de l'écran.
    private void Place(double width, double height)
    {
        if (_window is null) return;
        var area = DisplayArea.GetFromWindowId(owner.AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        var w = (int)Math.Min(Math.Ceiling(Math.Clamp(width, MinSize, MaxWidth) * Scale), area.Width - 2 * Gap);
        var h = (int)Math.Min(Math.Ceiling(Math.Clamp(height, MinSize, MaxHeight) * Scale), area.Height - 2 * Gap);
        // La taille demandée est celle du contenu ; les coins et l'ombre restent gérés par le système.
        _window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(w, h));
        var outer = _window.AppWindow.Size;
        var x = (int)Math.Clamp(_anchor.Right - outer.Width, area.X + Gap, Math.Max(area.X + Gap, area.X + area.Width - outer.Width - Gap));
        var y = (int)Math.Clamp(_anchor.Top, area.Y + Gap, Math.Max(area.Y + Gap, area.Y + area.Height - outer.Height - Gap));
        _window.AppWindow.Move(new Windows.Graphics.PointInt32(x, y));
    }

    // Tant que la fenêtre est ouverte, elle suit la taille de son contenu : souvent rapproché au chargement (le contenu
    // arrive par étapes), puis plus espacé (un volet qui se déplie, une liste qui se remplit).
    private async Task FollowSizeAsync(int run)
    {
        (double Width, double Height) shown = default;
        for (var tick = 0; ; tick++)
        {
            await Task.Delay(tick < 25 ? 80 : 350);
            if (run != _run || !IsOpen || _view?.CoreWebView2 is not { } core) return;
            try
            {
                var raw = await core.ExecuteScriptAsync(Measure);
                if (run != _run || !IsOpen) return;
                var size = JsonSerializer.Deserialize<double[]>(JsonSerializer.Deserialize<string>(raw) ?? "[]");
                if (size is not { Length: 2 } || !double.IsFinite(size[0]) || !double.IsFinite(size[1]) || size[0] < 1 || size[1] < 1 || (Math.Abs(size[0] - shown.Width) < 1 && Math.Abs(size[1] - shown.Height) < 1)) continue;
                shown = (size[0], size[1]);
                Place(size[0], size[1]);
            }
            catch (JsonException) { } // page en cours de chargement : rien à mesurer encore
            catch (Exception e) when (e is InvalidOperationException or COMException) { return; }
        }
    }

    public void Hide()
    {
        _run++;
        if (!IsOpen) return;
        IsOpen = false;
        _window?.AppWindow.Hide();
        _view?.CoreWebView2?.Navigate("about:blank"); // la page de l'extension est déchargée : rien ne tourne fenêtre fermée
    }

    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint hwnd, ref Point point);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(nint hwnd, int index, int value);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(nint hwnd, int index);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
