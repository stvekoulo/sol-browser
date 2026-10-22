using Browser.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace Browser.App;

public sealed partial class PageHost
{
    // Simulateur d'appareil : la vue de l'onglet prend la place de la page sur l'écran de l'appareil (sous sa barre
    // d'état), dans sa coque, le tout à l'échelle de la fenêtre. Le site reçoit l'agent de l'appareil, un écran tactile,
    // sa taille d'affichage et sa densité de pixels. Rien n'est injecté dans la page.
    // Comparaison d'écrans : la vue de l'onglet et des vues « miroirs » de la même page, une par appareil, côte à côte.
    private sealed class Sim(WebView2 view, Device device)
    {
        public WebView2 View { get; } = view;
        public Device Device { get; } = device;
        public bool Landscape;
        public FrameworkElement? Below, Above;
        public (double Scale, bool Landscape) Drawn; // ce que la coque dessinée représente : redessinée seulement si cela change
        public double Inset => Device.Inset(Landscape);
        public (int Width, int Height) Screen => Landscape ? (Device.Height, Device.Width) : (Device.Width, Device.Height);
        /// <summary>Ce que le site voit : l'écran moins la barre d'état.</summary>
        public (int Width, int Height) Viewport => (Screen.Width, Screen.Height - (int)Inset);
        public (double Width, double Height) Shell => Landscape ? (Device.ShellHeight, Device.ShellWidth) : (Device.ShellWidth, Device.ShellHeight);
    }

    public const string Comparison = "compare";
    private NativeBubbles? _bubbles; // bulles du moteur, recentrées sur la zone des pages
    private readonly Dictionary<string, List<Sim>> _sims = new(); // le premier est la vue de l'onglet, les suivants ses miroirs
    private readonly Dictionary<string, string> _mode = new();    // nom de l'appareil, ou « compare »
    private readonly Dictionary<string, (double Width, double Height, double Scale)> _fit = new();
    private readonly Dictionary<string, (string Name, bool Landscape)> _wanted = new(); // onglets endormis : appareil à leur réveil
    private readonly HashSet<WebView2> _following = new(); // miroirs en train de suivre leur onglet
    private Windows.UI.Color _pageColor = Windows.UI.Color.FromArgb(255, 255, 255, 255);
    // Fond et encre des barres d'état : partagés par toutes les coques, ils changent de couleur sans rien redessiner.
    private readonly Microsoft.UI.Xaml.Media.SolidColorBrush _pageBrush = new(Windows.UI.Color.FromArgb(255, 255, 255, 255));
    private readonly Microsoft.UI.Xaml.Media.SolidColorBrush _inkBrush = new(Windows.UI.Color.FromArgb(255, 0, 0, 0));
    private string? _desktopUserAgent;
    private bool _watchingLayout;

    /// <summary>Écrans de la comparaison, dans l'ordre d'affichage.</summary>
    public IReadOnlyList<string> ComparisonDevices { get; set; } = Device.DefaultComparison;

    public string? DeviceOf(string id) => _mode.GetValueOrDefault(id);

    /// <summary>Appareil d'un onglet et son orientation, y compris pour un onglet encore endormi. Null : vue ordinaire.</summary>
    public (string Name, bool Landscape)? DeviceState(string id) =>
        _mode.TryGetValue(id, out var name) ? (name, _sims.GetValueOrDefault(id)?.FirstOrDefault()?.Landscape ?? false)
        : _wanted.TryGetValue(id, out var wanted) ? wanted : null;

    /// <summary>Appareil à appliquer quand l'onglet (endormi) sera affiché.</summary>
    public void WantDevice(string id, string name, bool landscape) => _wanted[id] = (name, landscape);

    /// <summary>
    /// Affiche l'onglet comme sur l'appareil donné, ou sur plusieurs écrans côte à côte (« compare ») ; null revient à
    /// la vue ordinateur. La page est rechargée.
    /// </summary>
    public async void SetDevice(string id, string? name, bool landscape = false)
    {
        if (!_views.TryGetValue(id, out var view) || view.CoreWebView2 is not { } core) return;
        IReadOnlyList<Device>? devices = name switch
        {
            null => [],
            Comparison => ComparisonDevices.Select(Device.Find).OfType<Device>().ToList(),
            _ => Device.Find(name) is { } found ? [found] : null,
        };
        if (devices is null || (name is not null && devices.Count == 0)) return;
        _desktopUserAgent ??= core.Settings.UserAgent;
        ClearSims(id, view);

        try
        {
            if (devices.Count == 0)
            {
                (view.Width, view.Height, view.HorizontalAlignment, view.VerticalAlignment, view.Margin) =
                    (double.NaN, double.NaN, HorizontalAlignment.Stretch, VerticalAlignment.Stretch, new Thickness(0));
                ClipToDevice(view);
                RuntimeChanged?.Invoke();
                core.Settings.UserAgent = _desktopUserAgent;
                await core.CallDevToolsProtocolMethodAsync("Emulation.setTouchEmulationEnabled", """{"enabled":false,"maxTouchPoints":1}""");
                await core.CallDevToolsProtocolMethodAsync("Emulation.setEmitTouchEventsForMouse", """{"enabled":false}""");
                await core.CallDevToolsProtocolMethodAsync("Emulation.clearDeviceMetricsOverride", "{}");
                await ApplyZoomAsync(id);
                await ApplySettingsAsync(id, core); // le flou du bas des pages revient
                core.Reload();
                return;
            }

            // La place disponible change (fenêtre, vue scindée, carte latérale) : les appareils se remettent à l'échelle.
            if (!_watchingLayout)
            {
                _watchingLayout = true;
                container.LayoutUpdated += (_, _) => FitDevices();
                // Clic droit autour des appareils (zone vide, coque) : le menu du simulateur. Le fond doit exister
                // pour recevoir le clic ; transparent, il ne change rien à l'affichage.
                container.Background ??= new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
                container.RightTapped += (_, e) =>
                {
                    var column = e.GetPosition(container).X > primary.ActualWidth ? 2 : 0;
                    var shown = _sims.Keys.FirstOrDefault(tab => _views.TryGetValue(tab, out var main) && main.Visibility == Visibility.Visible && Grid.GetColumn(main) == column);
                    if (shown is null) return;
                    e.Handled = true;
                    var point = e.GetPosition(null);
                    DeviceAreaMenuRequested?.Invoke(shown, point.X, point.Y);
                };
            }
            _mode[id] = name!;
            var sims = _sims[id] = [new Sim(view, devices[0]) { Landscape = landscape && !devices[0].Computer }];
            // Comparaison : chaque écran prend sa place tout de suite, à l'échelle finale. Rien ne se redispose à
            // l'arrivée des miroirs, dont les moteurs démarrent ensemble pendant que l'onglet se recharge.
            foreach (var device in devices.Skip(1))
            {
                var mirror = new WebView2 { Visibility = view.Visibility, DefaultBackgroundColor = UiBridge.InitialBackground };
                Grid.SetColumn(mirror, Grid.GetColumn(view));
                container.Children.Add(mirror);
                sims.Add(new Sim(mirror, device) { Landscape = landscape && !device.Computer });
            }
            LayoutSims(id);
            RuntimeChanged?.Invoke();
            await ApplySettingsAsync(id, core); // retire le flou du bas des pages
            await PageZoom.TabAsync(core, 1);   // l'appareil a ses dimensions exactes : sa page n'est pas zoomée
            await EmulateAsync(id, sims[0]);
            await WireDeviceSyncAsync(id,core);
            core.Reload();
            await Task.WhenAll(sims.Skip(1).Select(async sim =>
            {
                // Entre-temps, l'onglet a pu être fermé ou changer de mode : ses miroirs ont été fermés avec lui.
                if (!await StartMirrorAsync(id, view, sim.View) || !_sims.TryGetValue(id, out var current) || current != sims) return;
                await EmulateAsync(id, sim);
                Follow(sim.View, core.Source);
            }));
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException) { /* vue fermée entre-temps */ }
    }

    /// <summary>Tourne l'appareil d'un quart de tour (portrait ↔ paysage). Un écran d'ordinateur ne tourne pas.</summary>
    public async void RotateDevice(string id)
    {
        if (!_sims.TryGetValue(id, out var sims)) return;
        foreach (var sim in sims.Where(s => !s.Device.Computer)) sim.Landscape = !sim.Landscape;
        LayoutSims(id);
        await Task.WhenAll(sims.ToList().Select(sim => EmulateAsync(id, sim, metricsOnly: true)));
    }

    /// <summary>Couleur de fond de la page affichée : celle de la barre d'état des appareils simulés.</summary>
    public void SetPageColor(byte r, byte g, byte b)
    {
        var color = Windows.UI.Color.FromArgb(255, r, g, b);
        if (color == _pageColor) return;
        _pageColor = color;
        _pageBrush.Color = color;
        var dark = 0.299 * r + 0.587 * g + 0.114 * b < 128;
        _inkBrush.Color = dark ? Windows.UI.Color.FromArgb(255, 255, 255, 255) : Windows.UI.Color.FromArgb(255, 0, 0, 0);
    }

    // Vue miroir : la même page que l'onglet, sur un autre appareil. Elle n'a pas d'existence propre : ce qu'on y fait
    // (clic sur un lien, changement de page d'une application) fait naviguer l'onglet, qui entraîne ses miroirs.
    // Faux : la vue a été fermée pendant le démarrage de son moteur.
    private async Task<bool> StartMirrorAsync(string id, WebView2 main, WebView2 view)
    {
        if (main.CoreWebView2 is not { } source) return false;
        await StartEngineAsync(view, source.Profile.ProfileName);
        if (view.CoreWebView2 is not { } core) return false;
        await Harden(core);
        await WireDeviceSyncAsync(id,core);
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.NewWindowRequested += (sender, e) => _ = OpenWindowAsync(id, e);
        core.DownloadStarting += (_, e) => e.Cancel = true; // les téléchargements passent par l'onglet
        core.NavigationStarting += (_, e) =>
        {
            // Une navigation demandée par le navigateur lui-même (suivre l'onglet) compte aussi comme « de l'utilisateur ».
            if (_following.Remove(view) || !e.IsUserInitiated || e.IsRedirected) return;
            e.Cancel = true;
            Navigate(id, e.Uri);
        };
        // Changement d'adresse sans nouveau document (application à page unique) : l'onglet suit. Un nouveau document
        // n'est jamais remonté : deux écrans redirigés vers deux adresses (site mobile, site ordinateur) se renverraient la balle.
        core.SourceChanged += (_, e) =>
        {
            if (!e.IsNewDocument && main.CoreWebView2 is { } tab && tab.Source != core.Source) Navigate(id, core.Source);
        };
        view.SizeChanged += (_, _) => ClipToDevice(view);
        core.DOMContentLoaded += (_, _) => _ = PageZoom.TabAsync(core, 1); // comme la vue de l'onglet : page d'appareil, à 100 %
        await core.CallDevToolsProtocolMethodAsync("Emulation.setScrollbarsHidden", """{"hidden":true}""");
        return true;
    }

    private void SyncMirrors(string id, string url)
    {
        foreach (var mirror in _sims.GetValueOrDefault(id, []).Skip(1)) Follow(mirror.View, url);
    }

    private void Follow(WebView2 mirror, string url)
    {
        if (mirror.CoreWebView2 is not { } core || core.Source == url || !url.StartsWith("http", StringComparison.Ordinal)) return;
        _following.Add(mirror);
        core.Navigate(url);
    }

    // Retire les coques et ferme les miroirs ; la vue de l'onglet reste.
    private void ClearSims(string id, WebView2 main)
    {
        _mode.Remove(id);
        _fit.Remove(id);
        _wanted.Remove(id);
        if (!_sims.Remove(id, out var sims)) return;
        foreach (var sim in sims)
        {
            if (sim.Below is not null) container.Children.Remove(sim.Below);
            if (sim.Above is not null) container.Children.Remove(sim.Above);
            if (sim.View == main) continue;
            _following.Remove(sim.View);
            if(sim.View.CoreWebView2 is {} closedCore)_deviceSyncWired.Remove(closedCore);
            container.Children.Remove(sim.View);
            sim.View.Close();
        }
    }

    /// <summary>Place des appareils simulés d'un onglet affiché, dans la fenêtre : leurs coques et une marge. Null : vue ordinaire.</summary>
    public Windows.Foundation.Rect? DeviceArea(string id)
    {
        if (!_sims.TryGetValue(id, out var sims) || !_views.TryGetValue(id, out var main) || main.Visibility != Visibility.Visible) return null;
        var shells = sims.Select(s => s.Below).OfType<FrameworkElement>().Where(s => s.ActualWidth > 0)
            .Select(s => s.TransformToVisual(null).TransformBounds(new(0, 0, s.ActualWidth, s.ActualHeight))).ToList();
        if (shells.Count == 0) return null;
        const double margin = 24;
        var room = Room(main);
        var column = container.TransformToVisual(null).TransformBounds(new(Grid.GetColumn(main) == 2 ? container.ActualWidth - room.Width : 0, 0, room.Width, room.Height));
        double left = Math.Max(column.Left, shells.Min(s => s.Left) - margin), top = Math.Max(column.Top, shells.Min(s => s.Top) - margin);
        double right = Math.Min(column.Right, shells.Max(s => s.Right) + margin), bottom = Math.Min(column.Bottom, shells.Max(s => s.Bottom) + margin);
        return right > left && bottom > top ? new(left, top, right - left, bottom - top) : null;
    }

}
