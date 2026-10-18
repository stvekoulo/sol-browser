using Browser.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace Browser.App;

public sealed partial class PageHost
{
    private (double Width, double Height) Room(WebView2 main) =>
        ((Grid.GetColumn(main) == 2 ? secondary : primary).ActualWidth, container.ActualHeight);

    private void FitDevices()
    {
        if (_sims.Count == 0) return;
        foreach (var (id, sims) in _sims.ToList())
        {
            if (!_views.TryGetValue(id, out var main) || main.Visibility != Visibility.Visible) continue;
            var room = Room(main);
            if (_fit.TryGetValue(id, out var fit) && Math.Abs(fit.Width - room.Width) < 1 && Math.Abs(fit.Height - room.Height) < 1) continue;
            if (LayoutSims(id))
                foreach (var sim in sims) _ = EmulateAsync(id, sim, metricsOnly: true); // seule l'échelle a changé
        }
    }

    // Range les appareils d'un onglet côte à côte, au centre, à l'échelle où ils tiennent tous. Vrai si l'échelle a changé.
    private bool LayoutSims(string id)
    {
        if (!_sims.TryGetValue(id, out var sims) || !_views.TryGetValue(id, out var main)) return false;
        const double gap = 36;
        var room = Room(main);
        var total = sims.Sum(s => s.Shell.Width) + gap * (sims.Count - 1);
        var scale = Math.Round(Math.Clamp(Math.Min((room.Width - 36) / total, (room.Height - 36) / sims.Max(s => s.Shell.Height)), 0.1, 1), 2);
        var changed = !_fit.TryGetValue(id, out var fit) || fit.Scale != scale;
        _fit[id] = (room.Width, room.Height, scale);

        var x = Math.Max(0, (room.Width - total * scale) / 2);
        foreach (var sim in sims)
        {
            // La coque n'est redessinée que si son échelle ou son orientation change ; sinon elle est seulement replacée.
            var redraw = sim.Below is null || sim.Above is null || sim.Drawn != (scale, sim.Landscape);
            if (redraw)
            {
                if (sim.Below is not null) container.Children.Remove(sim.Below);
                if (sim.Above is not null) container.Children.Remove(sim.Above);
                (sim.Below, sim.Above) = DeviceFrame.Build(sim.Device, scale, sim.Landscape, _pageBrush, _inkBrush);
                sim.Drawn = (scale, sim.Landscape);
            }
            var screenLeft = x + (sim.Shell.Width - sim.Screen.Width) / 2 * scale;
            (sim.View.Width, sim.View.Height) = (Math.Round(sim.Viewport.Width * scale), Math.Round(sim.Viewport.Height * scale));
            // La page s'affiche sous la barre d'état : décalée vers le bas de sa hauteur. Sous l'écran, la coque d'un
            // ordinateur continue (base, pied) : la marge du bas garde la page à la hauteur de l'écran.
            var foot = sim.Device.Foot * scale;
            foreach (var (part, left, top, bottom) in new[] { (sim.Below, x, 0.0, 0.0), (sim.View, screenLeft, sim.Inset * scale, foot), (sim.Above, screenLeft, 0.0, foot) })
            {
                (part.HorizontalAlignment, part.VerticalAlignment, part.Margin) = (HorizontalAlignment.Left, VerticalAlignment.Center, new Thickness(left, top, 0, bottom));
                Grid.SetColumn(part, Grid.GetColumn(main));
                part.Visibility = main.Visibility;
            }
            if (redraw)
            {
                container.Children.Insert(container.Children.IndexOf(sim.View), sim.Below);     // coque et écran : sous la vue
                container.Children.Insert(container.Children.IndexOf(sim.View) + 1, sim.Above); // îlot : par-dessus
            }
            ClipToDevice(sim.View);
            x += (sim.Shell.Width + gap) * scale;
        }
        return changed;
    }

    // Coins de la page : ceux de l'écran de l'appareil (ceux du bas seulement sous une barre d'état) ; aucun pour une vue ordinaire.
    private void ClipToDevice(WebView2 view)
    {
        foreach (var (id, sims) in _sims)
            if (sims.FirstOrDefault(s => s.View == view) is { } sim)
            {
                CornerClip.Set(view, (float)(sim.Device.PageRadius * _fit.GetValueOrDefault(id, (0, 0, 1)).Scale), squareTop: sim.Inset > 0);
                return;
            }
        CornerClip.Set(view, 0);
    }

    /// <param name="metricsOnly">L'appareil est déjà émulé : seules sa taille à l'écran ou son orientation ont changé.</param>
    private async Task EmulateAsync(string id, Sim sim, bool metricsOnly = false)
    {
        if (sim.View.CoreWebView2 is not { } core) return;
        try
        {
            var device = sim.Device;
            var touch = !device.Computer;
            if (!metricsOnly)
            {
                core.Settings.UserAgent = device.Agent(_desktopUserAgent);
                await core.CallDevToolsProtocolMethodAsync("Emulation.setTouchEmulationEnabled", System.Text.Json.JsonSerializer.Serialize(new { enabled = touch, maxTouchPoints = touch ? 5 : 1 }));
                await core.CallDevToolsProtocolMethodAsync("Emulation.setEmitTouchEventsForMouse", System.Text.Json.JsonSerializer.Serialize(new { enabled = touch, configuration = "mobile" }));
            }
            var wide = sim.Screen.Width > sim.Screen.Height;
            await core.CallDevToolsProtocolMethodAsync("Emulation.setDeviceMetricsOverride", System.Text.Json.JsonSerializer.Serialize(new
            {
                width = sim.Viewport.Width, height = sim.Viewport.Height, deviceScaleFactor = device.PixelRatio, mobile = touch,
                scale = _fit.GetValueOrDefault(id, (0, 0, 1)).Scale,
                screenWidth = sim.Screen.Width, screenHeight = sim.Screen.Height,
                screenOrientation = new { type = wide ? "landscapePrimary" : "portraitPrimary", angle = sim.Landscape ? 90 : 0 },
            }));
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException) { /* vue fermée entre-temps */ }
    }

    public async Task RestoreCaptureViewportAsync(CoreWebView2 core)
    {
        var id = _views.FirstOrDefault(pair => pair.Value.CoreWebView2 == core).Key;
        if (id is null) return;
        if (_sims.TryGetValue(id, out var sims) && sims.FirstOrDefault(sim => sim.View.CoreWebView2 == core) is { } sim)
            await EmulateAsync(id, sim); // métriques, densité et tactile de l'appareil, sans recharger sa page
        else await core.CallDevToolsProtocolMethodAsync("Emulation.clearDeviceMetricsOverride", "{}");
    }

}
