using System.Runtime.InteropServices;
using Browser.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Browser.App;

public sealed partial class MainWindow
{
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
                settings.SidebarWidth = _sidebar!.Width / settings.UiFactor;
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
            _sidebar!.Resize(SidebarHost.DefaultWidth * settings.UiFactor);
            settings.SidebarWidth = _sidebar.Width / settings.UiFactor;
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

            var scale = Root.XamlRoot.RasterizationScale;
            var sideWidth = SidebarColumn.ActualWidth * scale;
            var pagesWidth = rect.Right - sideWidth - 8 * scale;
            if (pagesWidth <= 0) return;
            var ratio = (point.X - sideWidth - 4 * scale) / pagesWidth;
            if (Math.Abs(ratio - _dividerRatio) < 0.002) return;
            _dividerRatio = ratio;
            _controller?.Execute(new Command("split.resize", Ratio: ratio));
        };
        return timer;
    }

}
