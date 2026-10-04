using Browser.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace Browser.App;

/// <summary>Une WebView2 par onglet. Aucun canal vers l'hôte n'est ouvert sur ces vues.</summary>
public sealed partial class PageHost(Grid container, ColumnDefinition primary, ColumnDefinition divider, ColumnDefinition secondary, string blurScript, Settings settings)
{
    private readonly Dictionary<string, WebView2> _views = new();
    private readonly Dictionary<string, string> _blurScriptIds = new(); // vues où le flou est installé
    private readonly HashSet<string> _loading = new();

    public event Action<string, string?, string?>? PageChanged; // id, url, titre
    public event Action? RuntimeChanged;                         // chargement, historique
    public event Action<string>? Loaded;                         // id, page chargée avec succès
    public event Action<string>? NewTabRequested;
    public event Action<string, double, double, CoreWebView2ContextMenuRequestedEventArgs>? ContextMenuRequested; // id, x, y dans la fenêtre
    public event Action<string, double, double>? DeviceAreaMenuRequested; // id, x, y : clic droit à côté d'un appareil simulé
    public event Action<string, CoreWebView2DownloadStartingEventArgs>? DownloadStarting; // onglet d'origine
    public event Action<CoreWebView2PermissionRequestedEventArgs>? PermissionRequested;
    public event Action<string, string>? StatusChanged;         // id, adresse du lien survolé (vide = aucun)
    public event Action<string, string?>? FaviconChanged;        // id, icône en data: (ou null)
    public event Action<bool>? FullScreenChanged;                // une page (vidéo…) entre en plein écran ou en sort

    public bool IsLoading(string id) => _loading.Contains(id);
    public bool CanGoBack(string id) => Core(id)?.CanGoBack ?? false;
    public bool CanGoForward(string id) => Core(id)?.CanGoForward ?? false;

    // Onglets endormis (rouverts d'une session) : aucune vue, aucune page chargée tant qu'ils ne sont pas affichés.
    private readonly Dictionary<string, Tab> _asleep = new();

    public void AddAsleep(Tab tab) => _asleep[tab.Id] = tab;

    // Toutes les vues partagent le même moteur. Une fenêtre privée aussi : elle a son propre dossier de données, effacé
    // à sa fermeture, ce qui laisse le bloqueur de publicités y fonctionner (le mode privé du moteur l'en empêcherait).
    private static async Task StartEngineAsync(WebView2 view) => await view.EnsureCoreWebView2Async(await AdBlock.EnvironmentAsync());

    public async void Add(Tab tab)
    {
        var view = new WebView2 { Visibility = Visibility.Collapsed };
        view.SizeChanged += (_, _) => { if (_zoom.ContainsKey(tab.Id)) _ = ApplyZoomAsync(tab.Id); };
        _views[tab.Id] = view;
        container.Children.Add(view);
        await StartEngineAsync(view);
        if (!_views.ContainsKey(tab.Id)) return; // fermé pendant l'initialisation

        var core = view.CoreWebView2;
        Harden(core);
        await ApplySettingsAsync(tab.Id, core);
        if (!_views.ContainsKey(tab.Id)) return;

        // Pendant qu'une page d'erreur est affichée, l'onglet garde l'adresse demandée, pas celle de la page d'erreur.
        core.SourceChanged += (_, _) => PageChanged?.Invoke(tab.Id, _errorUrl.GetValueOrDefault(tab.Id, core.Source), null);
        core.DocumentTitleChanged += (_, _) => PageChanged?.Invoke(tab.Id, null, core.DocumentTitle);
        core.HistoryChanged += (_, _) => RuntimeChanged?.Invoke();
        core.Settings.IsBuiltInErrorPageEnabled = false; // les erreurs de chargement sont affichées par le navigateur
        core.NavigationStarting += (_, e) =>
        {
            // Toute navigation autre que l'affichage de notre page d'erreur remet l'onglet dans son état normal.
            if (!_showingError.Remove(tab.Id)) _errorUrl.Remove(tab.Id);
            _target[tab.Id] = _errorUrl.GetValueOrDefault(tab.Id, e.Uri);
            _loading.Add(tab.Id);
            RuntimeChanged?.Invoke();
        };
        core.NavigationCompleted += (_, e) =>
        {
            _loading.Remove(tab.Id);
            RuntimeChanged?.Invoke();
            // Une réponse d'erreur du serveur (403, 404, 500…) est quand même sa page à lui : on la laisse affichée. Les
            // vérifications anti-robots, par exemple, répondent 403 puis rechargent la vraie page.
            if (e.IsSuccess || e.HttpStatusCode >= 400) { if (!_errorUrl.ContainsKey(tab.Id)) Loaded?.Invoke(tab.Id); }
            // Ni une navigation annulée (arrêt, remplacement), ni un lien devenu téléchargement ne sont des erreurs.
            else if (e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled && !_downloading.Remove(tab.Id))
                ShowError(tab.Id, core, _target.GetValueOrDefault(tab.Id, core.Source), ErrorKind(e.WebErrorStatus));
        };
        core.ProcessFailed += (_, e) =>
        {
            if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessExited && _views.ContainsKey(tab.Id))
                ShowError(tab.Id, core, _errorUrl.GetValueOrDefault(tab.Id, core.Source), "crash");
        };
        core.NewWindowRequested += (_, e) => { e.Handled = true; NewTabRequested?.Invoke(e.Uri); };
        core.ContextMenuRequested += (_, e) =>
        {
            // Position du clic dans la fenêtre, pour y poser notre menu.
            var point = view.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(e.Location.X, e.Location.Y));
            ContextMenuRequested?.Invoke(tab.Id, point.X, point.Y, e);
        };
        view.SizeChanged += (_, _) => { if (_pip != tab.Id) ClipToDevice(view); };
        core.SourceChanged += (_, _) => SyncMirrors(tab.Id, core.Source);
        WatchFrames(tab.Id, core);
        core.DownloadStarting += (_, e) => { _downloading.Add(tab.Id); DownloadStarting?.Invoke(tab.Id, e); };
        // Adresse du lien survolé : la bulle du moteur est coupée, le navigateur l'affiche dans son propre design.
        core.Settings.IsStatusBarEnabled = false;
        core.StatusBarTextChanged += (_, _) => StatusChanged?.Invoke(tab.Id, core.StatusBarText);
        core.FaviconChanged += async (_, _) => FaviconChanged?.Invoke(tab.Id, await ReadFaviconAsync(core));
        Navigate(tab.Id, tab.Url);
        if (_wanted.Remove(tab.Id, out var wanted)) SetDevice(tab.Id, wanted.Name, wanted.Landscape);
    }

    // Pages d'erreur du navigateur. _target : adresse de la navigation en cours ; _errorUrl : adresse dont la page
    // d'erreur est affichée ; _showingError : notre page d'erreur est en train de se charger.
    private readonly Dictionary<string, string> _target = new(), _errorUrl = new();
    private readonly HashSet<string> _showingError = new(), _downloading = new();

    private void ShowError(string id, CoreWebView2 core, string url, string kind)
    {
        _errorUrl[id] = url;
        _showingError.Add(id);
        try { core.NavigateToString(ErrorPages.Html(url, kind, Language.Current)); }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException) { _showingError.Remove(id); }
        PageChanged?.Invoke(id, url, ErrorPages.Describe(kind, Language.Current).Title);
    }

    private static string ErrorKind(CoreWebView2WebErrorStatus status) => status switch
    {
        CoreWebView2WebErrorStatus.HostNameNotResolved => "dns",
        CoreWebView2WebErrorStatus.Timeout => "timeout",
        CoreWebView2WebErrorStatus.Disconnected or CoreWebView2WebErrorStatus.CannotConnect or CoreWebView2WebErrorStatus.ConnectionAborted
            or CoreWebView2WebErrorStatus.ConnectionReset or CoreWebView2WebErrorStatus.ServerUnreachable => "offline",
        CoreWebView2WebErrorStatus.CertificateCommonNameIsIncorrect or CoreWebView2WebErrorStatus.CertificateExpired
            or CoreWebView2WebErrorStatus.CertificateIsInvalid or CoreWebView2WebErrorStatus.CertificateRevoked
            or CoreWebView2WebErrorStatus.ClientCertificateContainsErrors => "certificate",
        _ => "other",
    };

    /// <summary>Applique les réglages aux pages déjà ouvertes, sans les recharger.</summary>
    public async void ApplySettings()
    {
        foreach (var (id, view) in _views.ToList())
        {
            if (view.CoreWebView2 is not { } core) continue;
            try { await ApplySettingsAsync(id, core); }
            catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException) { /* vue fermée entre-temps */ }
        }
    }

    private async Task ApplySettingsAsync(string id, CoreWebView2 core)
    {
        core.Settings.IsPasswordAutosaveEnabled = settings.SavePasswords && !AppPaths.Private;
        // Thème clair ou sombre imposé : les sites qui proposent les deux versions suivent le même choix.
        core.Profile.PreferredColorScheme = settings.Theme switch
        {
            "light" => CoreWebView2PreferredColorScheme.Light,
            "dark" => CoreWebView2PreferredColorScheme.Dark,
            _ => CoreWebView2PreferredColorScheme.Auto,
        };

        // Barres de défilement : réglage du moteur, pas une feuille de style injectée, donc valable sur toutes les pages
        // quelle que soit leur politique de sécurité. Affichées, elles sont fines et n'apparaissent qu'au défilement.
        await core.CallDevToolsProtocolMethodAsync("Emulation.setScrollbarsHidden", settings.HideScrollbars ? """{"hidden":true}""" : """{"hidden":false}""");

        var installed = _blurScriptIds.TryGetValue(id, out var scriptId);
        var blur = settings.BlurEnabled && !_mode.ContainsKey(id); // pas de flou dans un écran simulé
        if (blur && !installed)
        {
            _blurScriptIds[id] = await core.AddScriptToExecuteOnDocumentCreatedAsync(blurScript);
            await core.ExecuteScriptAsync(blurScript); // la page déjà affichée
        }
        else if (!blur && installed)
        {
            core.RemoveScriptToExecuteOnDocumentCreated(scriptId);
            _blurScriptIds.Remove(id);
            await core.ExecuteScriptAsync("window.__navBlurHost && window.__navBlurHost.remove(); window.__navBlurHost = undefined; window.__navBlur = false;");
        }
    }

    // L'icône est lue par l'hôte et transmise en data: à l'interface, qui ne fait ainsi aucune requête réseau.
    private static async Task<string?> ReadFaviconAsync(CoreWebView2 core)
    {
        try
        {
            if (string.IsNullOrEmpty(core.FaviconUri)) return null;
            using var stream = (await core.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png)).AsStreamForRead();
            using var bytes = new MemoryStream();
            await stream.CopyToAsync(bytes);
            return bytes.Length is > 0 and <= 64 * 1024 ? "data:image/png;base64," + Convert.ToBase64String(bytes.ToArray()) : null;
        }
        catch (Exception) // page fermée ou icône illisible : pas d'icône, rien d'autre à faire
        {
            return null;
        }
    }

    // Une page web est du contenu non fiable : aucun pont vers l'hôte, aucune permission accordée d'office.
    private void Harden(CoreWebView2 core)
    {
        var s = core.Settings;
        s.AreHostObjectsAllowed = false;
        s.IsWebMessageEnabled = false;
        // Mots de passe : gestionnaire du moteur (proposition d'enregistrement, stockage chiffré, remplissage).
        s.IsPasswordAutosaveEnabled = settings.SavePasswords && !AppPaths.Private; // rien à retenir d'une fenêtre privée
        s.IsSwipeNavigationEnabled = true;
        s.IsZoomControlEnabled = false; // le zoom est géré par le navigateur (Ctrl +, Ctrl -, Ctrl molette), pas par le moteur // balayage horizontal : page précédente ou suivante
        s.IsGeneralAutofillEnabled = false;
        s.IsReputationCheckingRequired = true; // SmartScreen
        // Serveur de développement en https avec un certificat fait maison : accepté pour cet ordinateur seulement
        // (localhost, 127.0.0.1, ::1). Partout ailleurs, une erreur de certificat bloque la page.
        core.ServerCertificateErrorDetected += (_, e) =>
        {
            if (Uri.TryCreate(e.RequestUri, UriKind.Absolute, out var uri) && uri.IsLoopback) e.Action = CoreWebView2ServerCertificateErrorAction.AlwaysAllow;
        };
        core.ContainsFullScreenElementChanged += (_, _) => FullScreenChanged?.Invoke(core.ContainsFullScreenElement);
        // Accès sensibles (micro, caméra, position, notifications…) : jamais accordés d'office, le navigateur
        // demande à l'utilisateur. Sans personne pour répondre, la demande est refusée.
        core.PermissionRequested += (_, e) =>
        {
            if (PermissionRequested is null) e.State = CoreWebView2PermissionState.Deny;
            else PermissionRequested(e);
        };
    }

    public void Remove(string id)
    {
        _asleep.Remove(id);
        _hiddenSince.Remove(id);
        if (_pip == id) ExitPip(pause: false);
        _childFrames.Remove(id);
        if (_views.TryGetValue(id, out var closing)) ClearSims(id, closing);
        if (!_views.Remove(id, out var view)) return;
        _loading.Remove(id);
        _blurScriptIds.Remove(id);
        _zoom.Remove(id);
        _target.Remove(id); _errorUrl.Remove(id); _showingError.Remove(id); _downloading.Remove(id);
        container.Children.Remove(view);
        view.Close();
    }

    public void Navigate(string id, string url)
    {
        if (Core(id) is { } core && Uri.TryCreate(url, UriKind.Absolute, out _)) core.Navigate(url);
    }

    public void Back(string id) => Core(id)?.GoBack();
    public void Forward(string id) => Core(id)?.GoForward();
    // Recharger une page d'erreur, c'est retenter l'adresse qui a échoué.
    public void Reload(string id)
    {
        if (_errorUrl.TryGetValue(id, out var failed)) Navigate(id, failed);
        else Core(id)?.Reload();
        foreach (var mirror in _sims.GetValueOrDefault(id, []).Skip(1))
        {
            _following.Add(mirror.View);
            mirror.View.CoreWebView2?.Reload();
        }
    }
    public void Stop(string id) => Core(id)?.Stop();

    public void ApplyLayout(string? activeId, string? secondaryId, double ratio)
    {
        // Un onglet endormi qu'on affiche se réveille : sa vue est créée et sa page chargée maintenant.
        foreach (var id in new[] { activeId, secondaryId })
            if (id is not null && _asleep.Remove(id, out var tab)) Add(tab);
        var split = secondaryId is not null && _views.ContainsKey(secondaryId);
        primary.Width = new GridLength(split ? ratio : 1, GridUnitType.Star);
        divider.Width = new GridLength(split ? 8 : 0);
        secondary.Width = split ? new GridLength(1 - ratio, GridUnitType.Star) : new GridLength(0);
        // L'onglet du lecteur flottant redevient l'onglet affiché : il reprend sa place.
        if (_pip is not null && (_pip == activeId || _pip == secondaryId)) ExitPip(pause: false);
        foreach (var (id, view) in _views)
        {
            if (id == _pip) continue; // lecteur flottant : sa vue reste affichée, là où il l'a posée
            var column = id == activeId ? 0 : split && id == secondaryId ? 2 : -1;
            view.Visibility = column < 0 ? Visibility.Collapsed : Visibility.Visible;
            if (column >= 0) Grid.SetColumn(view, column);
            Rest(id, view, hidden: column < 0);
            // Coques et vues miroirs d'un onglet simulé : mêmes colonne et visibilité que sa vue.
            foreach (var sim in _sims.GetValueOrDefault(id, []))
                foreach (var part in new FrameworkElement?[] { sim.View, sim.Below, sim.Above })
                {
                    if (part is null) continue;
                    part.Visibility = view.Visibility;
                    if (column >= 0) Grid.SetColumn(part, column);
                }
        }
    }

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
        public double Inset => Device.Inset(Landscape);
        public (int Width, int Height) Screen => Landscape ? (Device.Height, Device.Width) : (Device.Width, Device.Height);
        /// <summary>Ce que le site voit : l'écran moins la barre d'état.</summary>
        public (int Width, int Height) Viewport => (Screen.Width, Screen.Height - (int)Inset);
        public (double Width, double Height) Shell => Landscape ? (Device.ShellHeight, Device.ShellWidth) : (Device.ShellWidth, Device.ShellHeight);
    }

    public const string Comparison = "compare";
    private readonly Dictionary<string, List<Sim>> _sims = new(); // le premier est la vue de l'onglet, les suivants ses miroirs
    private readonly Dictionary<string, string> _mode = new();    // nom de l'appareil, ou « compare »
    private readonly Dictionary<string, (double Width, double Height, double Scale)> _fit = new();
    private readonly Dictionary<string, (string Name, bool Landscape)> _wanted = new(); // onglets endormis : appareil à leur réveil
    private readonly HashSet<WebView2> _following = new(); // miroirs en train de suivre leur onglet
    private Windows.UI.Color _pageColor = Windows.UI.Color.FromArgb(255, 255, 255, 255);
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
            var sims = _sims[id] = [new Sim(view, devices[0]) { Landscape = landscape && !devices[0].Desktop }];
            LayoutSims(id);
            RuntimeChanged?.Invoke();
            await ApplySettingsAsync(id, core); // retire le flou du bas des pages
            await EmulateAsync(id, sims[0]);
            core.Reload();

            foreach (var device in devices.Skip(1))
            {
                var mirror = await AddMirrorAsync(id, view);
                // Entre-temps, l'onglet a pu être fermé ou changer de mode : ce miroir n'a plus sa place.
                if (!_sims.TryGetValue(id, out var current) || current != sims)
                {
                    container.Children.Remove(mirror);
                    mirror.Close();
                    return;
                }
                sims.Add(new Sim(mirror, device) { Landscape = landscape && !device.Desktop });
                LayoutSims(id);
                foreach (var each in sims) await EmulateAsync(id, each); // l'échelle a changé pour tous
                Follow(mirror, core.Source);
            }
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException) { /* vue fermée entre-temps */ }
    }

    /// <summary>Tourne l'appareil d'un quart de tour (portrait ↔ paysage). Un écran d'ordinateur ne tourne pas.</summary>
    public async void RotateDevice(string id)
    {
        if (!_sims.TryGetValue(id, out var sims)) return;
        foreach (var sim in sims.Where(s => !s.Device.Desktop)) sim.Landscape = !sim.Landscape;
        LayoutSims(id);
        foreach (var sim in sims.ToList()) await EmulateAsync(id, sim);
    }

    /// <summary>Couleur de fond de la page affichée : celle de la barre d'état des appareils simulés.</summary>
    public void SetPageColor(byte r, byte g, byte b)
    {
        var color = Windows.UI.Color.FromArgb(255, r, g, b);
        if (color == _pageColor) return;
        _pageColor = color;
        foreach (var id in _sims.Keys.ToList())
            if (_views.TryGetValue(id, out var main) && main.Visibility == Visibility.Visible) LayoutSims(id);
    }

    // Vue miroir : la même page que l'onglet, sur un autre appareil. Elle n'a pas d'existence propre : ce qu'on y fait
    // (clic sur un lien, changement de page d'une application) fait naviguer l'onglet, qui entraîne ses miroirs.
    private async Task<WebView2> AddMirrorAsync(string id, WebView2 main)
    {
        var view = new WebView2 { Visibility = main.Visibility };
        Grid.SetColumn(view, Grid.GetColumn(main));
        container.Children.Add(view);
        await StartEngineAsync(view);
        var core = view.CoreWebView2;
        Harden(core);
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.NewWindowRequested += (_, e) => { e.Handled = true; NewTabRequested?.Invoke(e.Uri); };
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
        await core.CallDevToolsProtocolMethodAsync("Emulation.setScrollbarsHidden", """{"hidden":true}""");
        return view;
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
            container.Children.Remove(sim.View);
            sim.View.Close();
        }
    }

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
                foreach (var sim in sims) _ = EmulateAsync(id, sim);
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
            if (sim.Below is not null) container.Children.Remove(sim.Below);
            if (sim.Above is not null) container.Children.Remove(sim.Above);
            (sim.Below, sim.Above) = DeviceFrame.Build(sim.Device, scale, sim.Landscape, _pageColor);
            var screenLeft = x + (sim.Shell.Width - sim.Screen.Width) / 2 * scale;
            (sim.View.Width, sim.View.Height) = (Math.Round(sim.Viewport.Width * scale), Math.Round(sim.Viewport.Height * scale));
            // La page s'affiche sous la barre d'état : décalée vers le bas de sa hauteur.
            foreach (var (part, left, top) in new[] { (sim.Below, x, 0.0), (sim.View, screenLeft, sim.Inset * scale), (sim.Above, screenLeft, 0.0) })
            {
                (part.HorizontalAlignment, part.VerticalAlignment, part.Margin) = (HorizontalAlignment.Left, VerticalAlignment.Center, new Thickness(left, top, 0, 0));
                Grid.SetColumn(part, Grid.GetColumn(main));
                part.Visibility = main.Visibility;
            }
            container.Children.Insert(container.Children.IndexOf(sim.View), sim.Below);     // coque et écran : sous la vue
            container.Children.Insert(container.Children.IndexOf(sim.View) + 1, sim.Above); // îlot : par-dessus
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
                CornerClip.Set(view, (float)(sim.Device.Radius * _fit.GetValueOrDefault(id, (0, 0, 1)).Scale), squareTop: sim.Inset > 0);
                return;
            }
        CornerClip.Set(view, 0);
    }

    private async Task EmulateAsync(string id, Sim sim)
    {
        if (sim.View.CoreWebView2 is not { } core) return;
        try
        {
            var device = sim.Device;
            core.Settings.UserAgent = device.UserAgent ?? _desktopUserAgent;
            var touch = !device.Desktop;
            await core.CallDevToolsProtocolMethodAsync("Emulation.setTouchEmulationEnabled", System.Text.Json.JsonSerializer.Serialize(new { enabled = touch, maxTouchPoints = touch ? 5 : 1 }));
            await core.CallDevToolsProtocolMethodAsync("Emulation.setEmitTouchEventsForMouse", System.Text.Json.JsonSerializer.Serialize(new { enabled = touch, configuration = "mobile" }));
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

    // Économie de mémoire. Un onglet masqué demande d'abord au moteur d'occuper moins de mémoire ; après quelques
    // minutes il est mis en veille (scripts à l'arrêt, page gardée telle quelle) ; s'il reste masqué longtemps, sa vue
    // est fermée et il redevient un onglet endormi, rechargé quand on y revient. Un onglet qui joue du son, ou que le
    // navigateur garde affiché ailleurs (vue scindée, appareil simulé), n'est jamais touché.
    // Diagnostic : NAVIGATEUR_SAVER_TEST=1 ramène les délais à quelques secondes pour pouvoir observer le mécanisme.
    private static readonly bool SaverTest = Environment.GetEnvironmentVariable("NAVIGATEUR_SAVER_TEST") == "1";
    private static readonly TimeSpan SuspendAfter = SaverTest ? TimeSpan.FromSeconds(4) : TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DiscardAfter = SaverTest ? TimeSpan.FromSeconds(18) : TimeSpan.FromMinutes(30);
    private readonly Dictionary<string, DateTime> _hiddenSince = new();
    private DispatcherTimer? _saver;

    /// <summary>Onglets à ne jamais décharger (donné par le navigateur : vue scindée…).</summary>
    public Func<string, bool> KeepAwake { get; set; } = _ => false;
    public event Action<string>? Discarded; // id : la vue a été fermée, l'onglet se rechargera
    public int AsleepCount => _asleep.Count;

    private void Rest(string id, WebView2 view, bool hidden)
    {
        if (view.CoreWebView2 is not { } core) return;
        try
        {
            if (hidden && _hiddenSince.TryAdd(id, DateTime.UtcNow)) core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
            else if (!hidden && _hiddenSince.Remove(id))
            {
                core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal;
                if (core.IsSuspended) core.Resume();
            }
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException) { /* vue fermée entre-temps */ }
        if (_saver is null)
        {
            _saver = new DispatcherTimer { Interval = SaverTest ? TimeSpan.FromSeconds(2) : TimeSpan.FromMinutes(1) };
            _saver.Tick += (_, _) => SaveMemory(DateTime.UtcNow);
            _saver.Start();
        }
    }

    /// <summary>Passe en revue les onglets masqués ; <paramref name="now"/> est donné pour pouvoir avancer l'horloge en test.</summary>
    public void SaveMemory(DateTime now)
    {
        if (!settings.MemorySaver) return;
        foreach (var (id, since) in _hiddenSince.ToList())
        {
            if (!_views.TryGetValue(id, out var view) || view.CoreWebView2 is not { } core) { _hiddenSince.Remove(id); continue; }
            try
            {
                if (core.IsDocumentPlayingAudio) { _hiddenSince[id] = now; continue; } // musique, appel : il reste éveillé
                var busy = KeepAwake(id) || _sims.ContainsKey(id) || _loading.Contains(id);
                if (!busy && now - since >= DiscardAfter && core.Source.StartsWith("http", StringComparison.Ordinal) && !_errorUrl.ContainsKey(id)) Discard(id, core);
                else if (now - since >= SuspendAfter && !core.IsSuspended) _ = core.TrySuspendAsync();
            }
            catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException) { /* vue fermée entre-temps */ }
        }
    }

    private void Discard(string id, CoreWebView2 core)
    {
        var tab = new Tab(id, core.Source, core.DocumentTitle);
        Remove(id);
        _hiddenSince.Remove(id);
        _asleep[id] = tab;
        Discarded?.Invoke(id);
    }

    // Zoom d'une page. Ce type de vue n'expose pas le zoom du moteur : la page est donc mise en page dans une fenêtre
    // plus petite (ou plus grande), puis rendue à l'échelle sur toute la vue, avec la densité de pixels qu'il faut pour
    // rester nette. Pour la page, c'est un vrai zoom de navigateur : largeur, hauteur et unités « vh » suivent, et un
    // élément en plein écran couvre tout. Le réglage tient d'une page à l'autre de l'onglet et suit la taille de la vue.
    private readonly Dictionary<string, double> _zoom = new();

    public double ZoomOf(string id) => _zoom.GetValueOrDefault(id, 1);

    public void SetZoom(string id, double zoom)
    {
        if (Core(id) is null) return;
        if (zoom == 1) _zoom.Remove(id); else _zoom[id] = zoom;
        RuntimeChanged?.Invoke();
        _ = ApplyZoomAsync(id);
    }

    private async Task ApplyZoomAsync(string id)
    {
        // Un écran simulé et le lecteur flottant règlent eux-mêmes ces métriques : le zoom reprend quand ils se terminent.
        if (!_views.TryGetValue(id, out var view) || view.CoreWebView2 is not { } core || _mode.ContainsKey(id) || _pip == id) return;
        var zoom = ZoomOf(id);
        try
        {
            if (zoom == 1 || view.ActualWidth < 1 || view.ActualHeight < 1)
            {
                await core.CallDevToolsProtocolMethodAsync("Emulation.clearDeviceMetricsOverride", "{}");
                return;
            }
            await core.CallDevToolsProtocolMethodAsync("Emulation.setDeviceMetricsOverride", System.Text.Json.JsonSerializer.Serialize(new
            {
                width = (int)Math.Round(view.ActualWidth / zoom), height = (int)Math.Round(view.ActualHeight / zoom),
                deviceScaleFactor = (view.XamlRoot?.RasterizationScale ?? 1) * zoom, mobile = false, scale = zoom,
            }));
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException or ArgumentException) { /* vue fermée entre-temps */ }
    }

    public CoreWebView2? CoreOf(string id) => Core(id);

    private CoreWebView2? Core(string id) =>
        _views.TryGetValue(id, out var view) ? view.CoreWebView2 : null;
}
