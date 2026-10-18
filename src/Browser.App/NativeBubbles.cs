using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI.Xaml;

namespace Browser.App;

/// <summary>
/// Bulles du moteur (« Enregistrer votre mot de passe ? », « Mettre à jour le mot de passe ? ») : de petites fenêtres à
/// lui, qu'il accroche au coin haut droit de la page. Selon l'écran et son échelle, elles peuvent s'y retrouver hors de
/// vue. Elles restent celles du moteur ; Sol les replace seulement en haut de la zone des pages, au centre.
/// </summary>
internal sealed class NativeBubbles(FrameworkElement area)
{
    private WinEventProc? _proc; // gardé en vie : Windows l'appelle tant que la surveillance dure
    private nint _hook;

    /// <summary>Surveille les fenêtres du processus du moteur. Les vues d'un même dossier de données le partagent.</summary>
    public void Watch(uint browserProcessId)
    {
        if (_hook != 0) return;
        _proc = Moved;
        _hook = SetWinEventHook(ObjectShow, ObjectLocationChange, 0, _proc, browserProcessId, 0, 0);
        if (_hook != 0) area.Unloaded += (_, _) => { UnhookWinEvent(_hook); _hook = 0; };
    }

    private void Moved(nint hook, uint change, nint hwnd, int item, int child, uint thread, uint time)
    {
        if (change is not (ObjectShow or ObjectLocationChange) || item != 0 || child != 0 || area.XamlRoot is not { } root) return;
        // Une bulle : fenêtre du moteur rattachée à une page, avec un titre. Les listes déroulantes et les suggestions
        // n'ont pas de titre ; la fenêtre vidéo flottante reste au premier plan ; une info-bulle est plus petite.
        if (GetWindow(hwnd, Owner) == 0 || GetWindowTextLength(hwnd) == 0 || !IsWindowVisible(hwnd)
            || (GetWindowLong(hwnd, ExStyle) & TopMost) != 0) return;
        var name = new StringBuilder(32);
        if (GetClassName(hwnd, name, name.Capacity) == 0 || name.ToString() != "Chrome_WidgetWin_1" || !GetWindowRect(hwnd, out var box)) return;
        var scale = root.RasterizationScale;
        int width = box.Right - box.Left, height = box.Bottom - box.Top;
        if (width < 240 * scale || height < 120 * scale) return;

        var origin = new Point();
        var window = Microsoft.UI.Win32Interop.GetWindowFromWindowId(root.ContentIslandEnvironment.AppWindowId);
        if (!ClientToScreen(window, ref origin)) return;
        var pages = area.TransformToVisual(null).TransformBounds(new(0, 0, area.ActualWidth, area.ActualHeight));
        var x = origin.X + (int)Math.Round((pages.X + (pages.Width - width / scale) / 2) * scale);
        var y = origin.Y + (int)Math.Round((pages.Y + 8) * scale);
        // Déjà en place : le déplacement lui-même repasse par ici.
        if (Math.Abs(box.Left - x) <= 1 && Math.Abs(box.Top - y) <= 1) return;
        SetWindowPos(hwnd, 0, x, y, 0, 0, 0x0015); // sans taille, sans ordre, sans activation
    }

    private const uint ObjectShow = 0x8002, ObjectLocationChange = 0x800B, Owner = 4;
    private const int ExStyle = -20;
    private const int TopMost = 8;
    private delegate void WinEventProc(nint hook, uint change, nint hwnd, int item, int child, uint thread, uint time);
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern nint SetWinEventHook(uint first, uint last, nint module, WinEventProc proc, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint hwnd, uint relation);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint hwnd, StringBuilder name, int count);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(nint hwnd, int index);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint hwnd, ref Point point);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
}
