using Browser.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace Browser.App;

/// <summary>Une WebView2 par onglet. Aucun canal vers l'hôte n'est ouvert sur ces vues.</summary>
public sealed class PageHost(Grid container, ColumnDefinition primary, ColumnDefinition divider, ColumnDefinition secondary, string blurScript, Settings settings)
{
    private readonly Dictionary<string, WebView2> _views = new();
    private readonly Dictionary<string, string> _blurScriptIds = new(); // vues où le flou est installé
    private readonly HashSet<string> _loading = new();

    public event Action<string, string?, string?>? PageChanged; // id, url, titre
    public event Action? RuntimeChanged;                         // chargement, historique
    public event Action<string>? Loaded;                         // id, page chargée avec succès
    public event Action<string>? NewTabRequested;
    public event Action<double, double, CoreWebView2ContextMenuRequestedEventArgs>? ContextMenuRequested; // x, y dans la fenêtre
    public event Action<string, CoreWebView2DownloadStartingEventArgs>? DownloadStarting; // onglet d'origine
    public event Action<CoreWebView2PermissionRequestedEventArgs>? PermissionRequested;
    public event Action<string, string>? StatusChanged;         // id, adresse du lien survolé (vide = aucun)
    public event Action<string, string?>? FaviconChanged;        // id, icône en data: (ou null)

    public bool IsLoading(string id) => _loading.Contains(id);
    public bool CanGoBack(string id) => Core(id)?.CanGoBack ?? false;
    public bool CanGoForward(string id) => Core(id)?.CanGoForward ?? false;

    public async void Add(Tab tab)
    {
        var view = new WebView2 { Visibility = Visibility.Collapsed };
        _views[tab.Id] = view;
        container.Children.Add(view);
        await view.EnsureCoreWebView2Async();
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
            if (e.IsSuccess) { if (!_errorUrl.ContainsKey(tab.Id)) Loaded?.Invoke(tab.Id); }
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
            ContextMenuRequested?.Invoke(point.X, point.Y, e);
        };
        core.DownloadStarting += (_, e) => { _downloading.Add(tab.Id); DownloadStarting?.Invoke(tab.Id, e); };
        // Adresse du lien survolé : la bulle du moteur est coupée, le navigateur l'affiche dans son propre design.
        core.Settings.IsStatusBarEnabled = false;
        core.StatusBarTextChanged += (_, _) => StatusChanged?.Invoke(tab.Id, core.StatusBarText);
        core.FaviconChanged += async (_, _) => FaviconChanged?.Invoke(tab.Id, await ReadFaviconAsync(core));
        Navigate(tab.Id, tab.Url);
    }

    // Pages d'erreur du navigateur. _target : adresse de la navigation en cours ; _errorUrl : adresse dont la page
    // d'erreur est affichée ; _showingError : notre page d'erreur est en train de se charger.
    private readonly Dictionary<string, string> _target = new(), _errorUrl = new();
    private readonly HashSet<string> _showingError = new(), _downloading = new();

    private void ShowError(string id, CoreWebView2 core, string url, string kind)
    {
        _errorUrl[id] = url;
        _showingError.Add(id);
        try { core.NavigateToString(ErrorPages.Html(url, kind)); }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException) { _showingError.Remove(id); }
        PageChanged?.Invoke(id, url, ErrorPages.Describe(kind).Title);
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
        core.Settings.IsPasswordAutosaveEnabled = settings.SavePasswords;
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
        if (settings.BlurEnabled && !installed)
        {
            _blurScriptIds[id] = await core.AddScriptToExecuteOnDocumentCreatedAsync(blurScript);
            await core.ExecuteScriptAsync(blurScript); // la page déjà affichée
        }
        else if (!settings.BlurEnabled && installed)
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
        s.IsPasswordAutosaveEnabled = settings.SavePasswords;
        s.IsSwipeNavigationEnabled = true;
        s.IsZoomControlEnabled = false; // le zoom est géré par le navigateur (Ctrl +, Ctrl -, Ctrl molette), pas par le moteur // balayage horizontal : page précédente ou suivante
        s.IsGeneralAutofillEnabled = false;
        s.IsReputationCheckingRequired = true; // SmartScreen
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
    }
    public void Stop(string id) => Core(id)?.Stop();

    public void ApplyLayout(string? activeId, string? secondaryId, double ratio)
    {
        var split = secondaryId is not null && _views.ContainsKey(secondaryId);
        primary.Width = new GridLength(split ? ratio : 1, GridUnitType.Star);
        divider.Width = new GridLength(split ? 8 : 0);
        secondary.Width = split ? new GridLength(1 - ratio, GridUnitType.Star) : new GridLength(0);
        foreach (var (id, view) in _views)
        {
            var column = id == activeId ? 0 : split && id == secondaryId ? 2 : -1;
            view.Visibility = column < 0 ? Visibility.Collapsed : Visibility.Visible;
            if (column >= 0) Grid.SetColumn(view, column);
        }
    }

    // Zoom d'une page. Ce type de vue n'expose pas le zoom du moteur : il est appliqué à la racine du document,
    // maintenant et à chaque nouvelle page de l'onglet.
    private readonly Dictionary<string, (double Zoom, string? ScriptId)> _zoom = new();

    public double ZoomOf(string id) => _zoom.TryGetValue(id, out var z) ? z.Zoom : 1;

    public async void SetZoom(string id, double zoom)
    {
        if (Core(id) is not { } core) return;
        if (_zoom.TryGetValue(id, out var old) && old.ScriptId is not null) core.RemoveScriptToExecuteOnDocumentCreated(old.ScriptId);
        var value = zoom == 1 ? "" : zoom.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        _zoom[id] = (zoom, null);
        RuntimeChanged?.Invoke();
        try
        {
            await core.ExecuteScriptAsync($"document.documentElement.style.zoom = '{value}'");
            if (zoom == 1) return;
            var script = $"(() => {{ const z = () => document.documentElement.style.zoom = '{value}'; document.documentElement ? z() : document.addEventListener('DOMContentLoaded', z, {{ once: true }}); }})()";
            var scriptId = await core.AddScriptToExecuteOnDocumentCreatedAsync(script);
            if (_zoom.TryGetValue(id, out var now) && now.Zoom == zoom) _zoom[id] = (zoom, scriptId);
            else core.RemoveScriptToExecuteOnDocumentCreated(scriptId); // le zoom a encore changé entre-temps
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException) { /* vue fermée entre-temps */ }
    }

    public CoreWebView2? CoreOf(string id) => Core(id);

    private CoreWebView2? Core(string id) =>
        _views.TryGetValue(id, out var view) ? view.CoreWebView2 : null;
}
