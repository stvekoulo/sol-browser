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
    private readonly HashSet<string> _ready = new();
    private readonly HashSet<string> _skipSiteRoute = new();
    private readonly HashSet<string> _visible = new();
    private readonly HashSet<string> _pdf = new();
    private readonly Dictionary<string, Task> _profileReady = new();
    // Le moteur perd les cookies de session à la fermeture de la dernière vue du profil.
    // Les conserver uniquement en mémoire permet de changer d'onglet/session sans déconnecter le compte.
    private readonly Dictionary<string, IReadOnlyList<CoreWebView2Cookie>> _sessionCookies = new();
    private readonly Dictionary<string, Task> _closingProfiles = new();
    public event Action<string>? SessionFailed;
    public event Action? PagePressed;
    public Func<string, string, string?, bool, bool>? RouteNavigation { get; set; }
    public event Action<string>? NavigationSettled;
    public bool IsPdf(string id) => _pdf.Contains(id);
    private bool _watchLayout;
    private (string? Primary, string? Secondary, double Ratio) _layout;

    public event Action<string, string?, string?>? PageChanged; // id, url, titre
    public event Action? RuntimeChanged;                         // chargement, historique
    public event Action<string>? Loaded;                         // id, page chargée avec succès
    public event Action<string>? PageFocused;
    public event Action<string, Command>? StartRequested;
    public event Action<string, Command>? NotesRequested;
    public event Action<string>? StoreInstallRequested;
    public event Action? StoreManageRequested; // bouton d'une extension déjà installée : ouvrir leur gestion

    public static bool IsStoreDetail(string url)
    {
        try { ExtensionPackage.StoreId(url); return true; }
        catch (ArgumentException) { return false; }
    }

    private static bool IsStore(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var parsed) && parsed.Scheme == "https" && parsed.Host == "chromewebstore.google.com";

    // Le store est une application à page unique : le script est posé sur toutes ses pages et suit lui-même l'adresse.
    // Sur une fiche, le bouton d'installation (repéré au mot « Chrome », quelle que soit la langue) devient celui de Sol.
    private const string StoreScript = """
        (() => {
          if (window.__solStore) return; window.__solStore = true;
          let installed = [];
          const id = () => (location.pathname.match(/^\/detail\/(?:[^/]+\/)?([a-p]{32})\/?$/) || [])[1];
          const french = () => (document.documentElement.lang || navigator.language).startsWith('fr');
          const label = () => installed.includes(id())
            ? (french() ? 'Installée dans Sol' : 'Installed in Sol') : (french() ? 'Ajouter à Sol' : 'Add to Sol');
          const ours = b => b.dataset.sol === '1' || (/chrome/i.test(b.textContent || '') && b.textContent.trim().length < 48);
          const update = () => {
            if (!id()) return;
            document.querySelectorAll('button').forEach(b => {
              if (!ours(b)) return;
              b.dataset.sol = '1';
              if (b.disabled) b.disabled = false;
              b.removeAttribute('aria-disabled');
              if (b.textContent.trim() !== label()) b.textContent = label();
            });
          };
          window.__solStoreInstalled = ids => { installed = ids; update(); };
          document.addEventListener('click', e => {
            const b = e.target.closest?.('button'); if (!b || !id() || !ours(b)) return;
            e.preventDefault(); e.stopImmediatePropagation();
            window.chrome.webview.postMessage(installed.includes(id()) ? 'sol.store.manage' : 'sol.store.install');
          }, true);
          new MutationObserver(update).observe(document.documentElement, {childList:true, subtree:true, characterData:true, attributes:true, attributeFilter:['disabled','aria-disabled']});
          update();
        })()
        """;

    private async Task PrepareStoreAsync(CoreWebView2 core)
    {
        if (!IsStore(core.Source)) return;
        try { await core.ExecuteScriptAsync(StoreScript + $";window.__solStoreInstalled({System.Text.Json.JsonSerializer.Serialize((await core.Profile.GetBrowserExtensionsAsync()).Select(e => e.Id))})"); }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException) { }
    }

    /// <summary>Une extension vient d'être installée ou retirée : les fiches du store ouvertes le montrent.</summary>
    public void RefreshStore()
    {
        foreach (var view in _views.Values)
            if (view.CoreWebView2 is { } core) _ = PrepareStoreAsync(core);
    }
    public void PostStart(string id, string json)
    {
        if (Core(id) is { } core && core.Source is Settings.DefaultHomePage or QuickNotes.PageUrl) core.PostWebMessageAsJson(json);
    }
    public event Action<CoreWebView2>? Navigating;
    public async Task<string?> PreviewUrlAsync(string url, CancellationToken cancellation)
    {
        var preview = new WebView2 { Width = 1024, Height = 640, Opacity = 0, IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        container.Children.Insert(0, preview);
        try
        {
            await StartEngineAsync(preview);
            var core = preview.CoreWebView2;
            Harden(core);
            core.PermissionRequested += (_, e) => { e.State = CoreWebView2PermissionState.Deny; e.Handled = true; };
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.LaunchingExternalUriScheme += (_, e) => e.Cancel = true;
            core.DownloadStarting += (_, e) => e.Cancel = true;
            var loaded = new TaskCompletionSource<bool>();
            core.DOMContentLoaded += (_, _) => loaded.TrySetResult(true);
            core.NavigationCompleted += (_, e) => { if (!e.IsSuccess) loaded.TrySetResult(false); };
            core.Navigate(url);
            if (!await loaded.Task.WaitAsync(TimeSpan.FromSeconds(18), cancellation)) return null;
            await Task.Delay(300, cancellation);
            var json = await core.CallDevToolsProtocolMethodAsync("Page.captureScreenshot", "{\"format\":\"png\",\"captureBeyondViewport\":false}");
            using var result = System.Text.Json.JsonDocument.Parse(json);
            return "data:image/png;base64," + result.RootElement.GetProperty("data").GetString();
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException
            or TimeoutException or OperationCanceledException or System.Text.Json.JsonException) { return null; }
        finally { container.Children.Remove(preview); CloseView(preview); }
    }
    public Func<string, string, Tab?>? CreatePopupTab;
    public event Action<string>? PopupClosed;
    public event Action<string, double, double, CoreWebView2ContextMenuRequestedEventArgs>? ContextMenuRequested; // id, x, y dans la fenêtre
    public event Action<string, double, double>? DeviceAreaMenuRequested; // id, x, y : clic droit à côté d'un appareil simulé
    public event Action<string, CoreWebView2DownloadStartingEventArgs>? DownloadStarting; // onglet d'origine
    public event Action<CoreWebView2, CoreWebView2PermissionRequestedEventArgs>? PermissionRequested;
    public event Action<string, string>? StatusChanged;         // id, adresse du lien survolé (vide = aucun)
    public event Action<string, string?>? FaviconChanged;        // id, icône en data: (ou null)
    public event Action<bool>? FullScreenChanged;                // une page (vidéo…) entre en plein écran ou en sort

    public bool IsLoading(string id) => _loading.Contains(id);
    public bool CanGoBack(string id) => Core(id)?.CanGoBack ?? false;
    public bool CanGoForward(string id) => Core(id)?.CanGoForward ?? false;

    // Onglets endormis (rouverts d'une session) : aucune vue, aucune page chargée tant qu'ils ne sont pas affichés.
    private readonly Dictionary<string, Tab> _asleep = new();

    public void AddAsleep(Tab tab) => _asleep[tab.Id] = tab;
    /// <summary>L'onglet est déchargé : il se rechargera quand on y reviendra.</summary>
    public bool IsAsleep(string id) => _asleep.ContainsKey(id);

    // Toutes les vues partagent le même moteur. Une fenêtre privée aussi : elle a son propre dossier de données, effacé
    // à sa fermeture, ce qui laisse le bloqueur de publicités y fonctionner (le mode privé du moteur l'en empêcherait).
    private async Task StartEngineAsync(WebView2 view, string? profileName = null)
    {
        var name = profileName ?? "Default";
        if (_closingProfiles.TryGetValue(name, out var closing))
        { await closing; if (_closingProfiles.GetValueOrDefault(name) == closing) _closingProfiles.Remove(name); }
        var environment = await AdBlock.EnvironmentAsync();
        if (profileName is null) await view.EnsureCoreWebView2Async(environment);
        else
        {
            var options = environment.CreateCoreWebView2ControllerOptions();
            options.ProfileName = profileName;
            await view.EnsureCoreWebView2Async(environment, options);
        }
        var profile = view.CoreWebView2.Profile;
        if (_sessionCookies.Remove(profile.ProfileName, out var cookies))
            foreach (var cookie in cookies) profile.CookieManager.AddOrUpdateCookie(cookie);
    }
    private async Task PrepareProfileAsync(CoreWebView2Profile profile)
    {
        await AdBlock.RemoveBundledAsync(profile);
        await PageZoom.InstallAsync(profile, settings.PageZoom);
    }
    public async Task<bool> DeleteSessionAsync(string sessionId)
    {
        if (!TabSessions.ValidId(sessionId)) return false;
        var view = new WebView2 { Opacity = 0, IsHitTestVisible = false, Width = 1, Height = 1 };
        container.Children.Add(view);
        try
        {
            await StartEngineAsync(view, "sol-" + sessionId);
            // L'API moteur marque le profil pour suppression après fermeture de ses vues.
            view.CoreWebView2.Profile.Delete();
            _profileReady.Remove("sol-" + sessionId);
            _sessionCookies.Remove("sol-" + sessionId);
            return true;
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException)
        { return false; }
        finally { container.Children.Remove(view); view.Close(); }
    }

    public async void Add(Tab tab) => await AddAsync(tab);

    private async Task<CoreWebView2?> AddAsync(Tab tab, bool navigate = true)
    {
        if (!_watchLayout)
        {
            _watchLayout = true;
            container.SizeChanged += (_, _) => ApplyLayout(_layout.Primary, _layout.Secondary, _layout.Ratio);
        }
        var view = new CursorWebView { Visibility = Visibility.Collapsed, Opacity = 0, DefaultBackgroundColor = UiBridge.InitialBackground };
        _views[tab.Id] = view;
        view.GotFocus += (_, _) => { if (_visible.Contains(tab.Id) && _views.GetValueOrDefault(tab.Id) == view) PageFocused?.Invoke(tab.Id); };
        container.Children.Add(view);
        try
        {
            await StartEngineAsync(view, tab.SessionId.Length == 0 ? null : "sol-" + tab.SessionId);
            // Préparer les extensions du profil avant sa première navigation.
            var profile = view.CoreWebView2.Profile;
            if (!_profileReady.TryGetValue(profile.ProfileName, out var ready))
                _profileReady[profile.ProfileName] = ready = PrepareProfileAsync(profile);
            await ready;
        }
        catch (Exception e) when ((e is InvalidOperationException or System.Runtime.InteropServices.COMException)
            && _views.GetValueOrDefault(tab.Id) != view) { return null; } // fermé pendant le démarrage
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            DebugLog.Write($"session.start.failed {e.GetType().Name} {e.HResult}");
            Remove(tab.Id); AddAsleep(tab); SessionFailed?.Invoke(tab.Id); return null;
        }
        if (!_views.TryGetValue(tab.Id, out var initializing) || initializing != view) return null; // fermé ou rouvert pendant l'initialisation

        var core = view.CoreWebView2;
        var documentUrl = tab.Url;
        var navigationUrl = tab.Url;
        var navigationSource = tab.Url;
        var redirected = false;
        var routeSite = true;
        // NavigationStarting peut déjà laisser partir un GET. Intercepter le document avant le réseau
        // empêche d'envoyer les cookies du compte précédent au site de destination.
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.Document, CoreWebView2WebResourceRequestSourceKinds.Document);
        core.WebResourceRequested += (_, e) =>
        {
            if (_views.GetValueOrDefault(tab.Id) != view || routeSite && e.Request.Method == "GET" && e.Request.Uri == navigationUrl.Split('#', 2)[0]
                && !redirected && RouteNavigation?.Invoke(tab.Id, navigationUrl, navigationSource, true) == true)
            {
                e.Response = core.Environment.CreateWebResourceResponse(null!, 204, "No Content", "");
            }
        };
        core.DOMContentLoaded += (_, _) => view.Opacity = 1;
        Harden(core);
        core.SetVirtualHostNameToFolderMapping("start.sol", Path.Combine(AppContext.BaseDirectory, "ui"), CoreWebView2HostResourceAccessKind.Deny);
        core.WebMessageReceived += (_, e) =>
        {
            if (e.Source == core.Source && e.WebMessageAsJson == "\"sol.page.pointer\"") { PagePressed?.Invoke(); return; }
            if (e.Source == QuickNotes.PageUrl && core.Source == QuickNotes.PageUrl)
            {
                if (CommandParser.Parse(e.TryGetWebMessageAsString()) is { } notes && notes.Type is
                    "notes.ready" or "notes.open" or "notes.draft" or "notes.save" or "notes.edit" or "notes.discard" or "notes.delete" or "notes.export" or "notes.visit" or "notes.link" or "ai.notes")
                    NotesRequested?.Invoke(tab.Id, notes);
                return;
            }
            if (IsStore(e.Source) && IsStoreDetail(core.Source))
            {
                if (e.WebMessageAsJson == "\"sol.store.install\"") StoreInstallRequested?.Invoke(core.Source);
                else if (e.WebMessageAsJson == "\"sol.store.manage\"") StoreManageRequested?.Invoke();
                return;
            }
            if (e.Source != Settings.DefaultHomePage || core.Source != Settings.DefaultHomePage) return;
            if (CommandParser.Parse(e.TryGetWebMessageAsString()) is { } command && command.Type is "start.ready" or "start.navigate" or "start.group")
                StartRequested?.Invoke(tab.Id, command);
        };
        await ApplySettingsAsync(tab.Id, core);
        if (!_views.TryGetValue(tab.Id, out initializing) || initializing != view) return null;

        // Pendant qu'une page d'erreur est affichée, l'onglet garde l'adresse demandée, pas celle de la page d'erreur.
        core.SourceChanged += (_, _) =>
        {
            PageChanged?.Invoke(tab.Id, _errorUrl.GetValueOrDefault(tab.Id, core.Source), null);
        };
        core.DocumentTitleChanged += (_, _) => PageChanged?.Invoke(tab.Id, null, core.DocumentTitle);
        core.HistoryChanged += (_, _) => RuntimeChanged?.Invoke();
        core.Settings.IsBuiltInErrorPageEnabled = false; // les erreurs de chargement sont affichées par le navigateur
        core.NavigationStarting += (_, e) =>
        {
            redirected = e.IsRedirected;
            routeSite = !_skipSiteRoute.Remove(tab.Id) && e.IsUserInitiated
                && !TabSessions.IsSignIn(e.Uri);
            if (!redirected) navigationSource = documentUrl;
            navigationUrl = e.Uri;
            _pdf.Remove(tab.Id);
            if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var document) && document.AbsolutePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) _pdf.Add(tab.Id);
            core.Settings.IsZoomControlEnabled = IsPdf(tab.Id);
            core.Settings.IsWebMessageEnabled = true; // handler validates origin and permits only dismissal outside internal pages/stores
            Navigating?.Invoke(core);
            DebugLog.Write($"navigation.start {tab.Id} #{e.NavigationId} redirect={e.IsRedirected}");
            if (_navigation.GetValueOrDefault(tab.Id) != e.NavigationId) _downloading.Remove(tab.Id);
            _navigation[tab.Id] = e.NavigationId;
            // Toute navigation autre que l'affichage de notre page d'erreur remet l'onglet dans son état normal.
            if (!_showingError.Remove(tab.Id)) _errorUrl.Remove(tab.Id);
            _target[tab.Id] = _errorUrl.GetValueOrDefault(tab.Id, e.Uri);
            _loading.Add(tab.Id);
            RuntimeChanged?.Invoke();
        };
        core.DOMContentLoaded += (_, _) => _ = PrepareStoreAsync(core);
        core.WebResourceResponseReceived += (sender, e) =>
        {
            if (e.Request.Uri != _target.GetValueOrDefault(tab.Id)) return;
            try
            {
                if (e.Response.Headers.Contains("Content-Type"))
                {
                    if (e.Response.Headers.GetHeader("Content-Type").Split(';')[0].Trim().Equals("application/pdf", StringComparison.OrdinalIgnoreCase)) _pdf.Add(tab.Id);
                    else _pdf.Remove(tab.Id);
                    core.Settings.IsZoomControlEnabled = IsPdf(tab.Id);
                    if (IsPdf(tab.Id)) PageZoom.KeepPlain(e.Request.Uri); // un document reste à 100 % : son lecteur a son propre zoom
                    RuntimeChanged?.Invoke();
                }
            }
            catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.COMException) { }
        };
        core.NavigationCompleted += (sender, e) =>
        {
            DebugLog.Write($"navigation.end {tab.Id} #{e.NavigationId} current={_navigation.GetValueOrDefault(tab.Id)} success={e.IsSuccess} status={e.WebErrorStatus} http={e.HttpStatusCode}");
            // Une navigation remplacée ne peut ni arrêter le nouvel indicateur ni remplacer sa page.
            if (!_views.TryGetValue(tab.Id, out var current) || current != view
                || _navigation.GetValueOrDefault(tab.Id) != e.NavigationId) return;
            NavigationSettled?.Invoke(tab.Id);
            if (e.IsSuccess) documentUrl = core.Source;
            _loading.Remove(tab.Id);
            var downloading = _downloading.Remove(tab.Id);
            RuntimeChanged?.Invoke();
            // Une réponse d'erreur du serveur (403, 404, 500…) est quand même sa page à lui : on la laisse affichée. Les
            // vérifications anti-robots, par exemple, répondent 403 puis rechargent la vraie page.
            if (e.IsSuccess || (e.HttpStatusCode >= 400 && e.WebErrorStatus is not
                (CoreWebView2WebErrorStatus.ValidProxyAuthenticationRequired or CoreWebView2WebErrorStatus.ValidAuthenticationCredentialsRequired)))
            { if (!_errorUrl.ContainsKey(tab.Id)) Loaded?.Invoke(tab.Id); }
            // Ni une navigation annulée (arrêt, remplacement), ni un lien devenu téléchargement ne sont des erreurs.
            else if (!downloading && !_errorUrl.ContainsKey(tab.Id)
                && e.WebErrorStatus is not (CoreWebView2WebErrorStatus.OperationCanceled or CoreWebView2WebErrorStatus.ConnectionAborted
                    or CoreWebView2WebErrorStatus.ValidAuthenticationCredentialsRequired))
                ShowError(tab.Id, core, _target.GetValueOrDefault(tab.Id, core.Source), ErrorKind(e.WebErrorStatus), e.WebErrorStatus.ToString());
        };
        core.ProcessFailed += (_, e) =>
        {
            if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessExited && _views.GetValueOrDefault(tab.Id) == view)
                ShowError(tab.Id, core, _errorUrl.GetValueOrDefault(tab.Id, core.Source), "crash");
        };
        core.NewWindowRequested += (sender, e) => _ = OpenWindowAsync(tab.Id, e);
        core.ContextMenuRequested += (_, e) =>
        {
            // Position du clic dans la fenêtre, pour y poser notre menu.
            var point = view.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(e.Location.X, e.Location.Y));
            ContextMenuRequested?.Invoke(tab.Id, point.X, point.Y, e);
        };
        view.SizeChanged += (_, _) => ClipToDevice(view);
        core.SourceChanged += (_, _) => SyncMirrors(tab.Id, core.Source);
        core.DownloadStarting += (_, e) =>
        {
            DebugLog.Write($"download.start {tab.Id} current={_navigation.GetValueOrDefault(tab.Id)}");
            _downloading.Add(tab.Id);
            DownloadStarting?.Invoke(tab.Id, e);
        };
        // Adresse du lien survolé : la bulle du moteur est coupée, le navigateur l'affiche dans son propre design.
        core.Settings.IsStatusBarEnabled = false;
        core.StatusBarTextChanged += (_, _) => StatusChanged?.Invoke(tab.Id, core.StatusBarText);
        core.FaviconChanged += async (_, _) => FaviconChanged?.Invoke(tab.Id, await ReadFaviconAsync(core));
        core.DOMContentLoaded += (_, _) => { if (_mode.ContainsKey(tab.Id)) _ = PageZoom.TabAsync(core, 1); };
        if (!_views.TryGetValue(tab.Id, out initializing) || initializing != view) return null;
        _ready.Add(tab.Id);
        if (navigate) Navigate(tab.Id, _target.GetValueOrDefault(tab.Id, tab.Url), route: false);
        if (_wanted.Remove(tab.Id, out var wanted)) SetDevice(tab.Id, wanted.Name, wanted.Landscape);
        return core;
    }

    private async Task OpenWindowAsync(string source, CoreWebView2NewWindowRequestedEventArgs request)
    {
        using var deferral = request.GetDeferral();
        request.Handled = true;
        Tab? tab = null;
        try
        {
            tab = CreatePopupTab?.Invoke(source, request.Uri);
            if (tab is null) return;
            var core = await AddAsync(tab, navigate: false);
            if (core is null) { PopupClosed?.Invoke(tab.Id); return; }
            core.WindowCloseRequested += (_, _) => PopupClosed?.Invoke(tab.Id);
            request.NewWindow = core;
            RuntimeChanged?.Invoke();
        }
        catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            DebugLog.Write($"popup.failed {error.GetType().Name}");
            if (tab is not null) PopupClosed?.Invoke(tab.Id);
        }
        finally { deferral.Complete(); }
    }

    // Pages d'erreur du navigateur. _target : adresse de la navigation en cours ; _errorUrl : adresse dont la page
    // d'erreur est affichée ; _showingError : notre page d'erreur est en train de se charger.
    private readonly Dictionary<string, string> _target = new(), _errorUrl = new();
    private readonly Dictionary<string, ulong> _navigation = new();
    private readonly HashSet<string> _showingError = new(), _downloading = new();

    private void ShowError(string id, CoreWebView2 core, string url, string kind, string? status = null)
    {
        _errorUrl[id] = url;
        _showingError.Add(id);
        DebugLog.Write($"navigation {id} : {status ?? kind}");
        try { core.NavigateToString(ErrorPages.Html(url, kind, Language.Current, status)); }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException) { _showingError.Remove(id); }
        PageChanged?.Invoke(id, url, ErrorPages.Describe(kind, Language.Current).Title);
    }

    private static string ErrorKind(CoreWebView2WebErrorStatus status) => status switch
    {
        CoreWebView2WebErrorStatus.HostNameNotResolved => "dns",
        CoreWebView2WebErrorStatus.Timeout => "timeout",
        CoreWebView2WebErrorStatus.Disconnected => "offline",
        CoreWebView2WebErrorStatus.CannotConnect or CoreWebView2WebErrorStatus.ServerUnreachable => "connect",
        CoreWebView2WebErrorStatus.ConnectionReset => "reset",
        CoreWebView2WebErrorStatus.ErrorHttpInvalidServerResponse => "response",
        CoreWebView2WebErrorStatus.RedirectFailed => "redirect",
        CoreWebView2WebErrorStatus.ValidProxyAuthenticationRequired => "proxy",
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
        StartSpelling();
        if (!settings.MemorySaver && core.IsSuspended) core.Resume();
        if (!settings.MemorySaver && _hiddenSince.ContainsKey(id)) _hiddenSince[id] = DateTime.UtcNow;
        var iCloud = (await core.Profile.GetBrowserExtensionsAsync()).Any(e => e.IsEnabled && e.Id is "pejdijmoenmkgeppbflobdenhhabjlaj" or "mfbcdcnpokpoajjciilocoachedjkima");
        core.Settings.IsPasswordAutosaveEnabled = settings.SavePasswords && !AppPaths.Private && !iCloud;
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

    // Balayage horizontal : la page note le moment où elle s'en est servie elle-même — un carrousel ou une zone qui
    // défile de côté, ou un geste que son script a pris en charge. Le navigateur consulte cette note avant de traiter
    // le balayage comme « page précédente / suivante » (voir MainWindow). La page n'a aucun canal vers l'hôte : c'est
    // l'hôte qui vient lire.
    private const string SidewaysScript = """
        (() => {
          if (window.__solSideways) return;
          const state = window.__solSideways = { at: -1e9 };
          addEventListener('wheel', (e) => {
            if (Math.abs(e.deltaX) <= Math.abs(e.deltaY)) return;
            let used = false;
            for (let el = e.target; el && el.nodeType === 1 && !used; el = el.parentElement) {
              const room = el.scrollWidth - el.clientWidth;
              if (room <= 1) continue;
              const flow = getComputedStyle(el).overflowX;
              if (flow !== 'auto' && flow !== 'scroll' && el !== document.scrollingElement) continue;
              used = e.deltaX < 0 ? el.scrollLeft > 0 : el.scrollLeft < room - 1;
            }
            if (used) state.at = performance.now();
            else setTimeout(() => { if (e.defaultPrevented) state.at = performance.now(); }, 0); // geste pris par le script de la page
          }, { passive: true, capture: true });
        })();
        """;

    /// <summary>La page a-t-elle utilisé elle-même un balayage horizontal dans les dernières millisecondes données ?</summary>
    public async Task<bool> UsedSidewaysAsync(string id, double withinMs)
    {
        if (Core(id) is not { } core) return false;
        try
        {
            var raw = await core.ExecuteScriptAsync("window.__solSideways ? performance.now() - window.__solSideways.at : 1e9");
            return double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ago) && ago < withinMs;
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException) { return false; }
    }

    // Une page web est non fiable : aucun objet hôte, aucune permission ; seuls les messages filtrés sont acceptés.
    private void Harden(CoreWebView2 core)
    {
        var s = core.Settings;
        s.AreHostObjectsAllowed = false;
        s.IsWebMessageEnabled = true;
        // Mots de passe : gestionnaire du moteur (proposition d'enregistrement, stockage chiffré, remplissage).
        s.IsPasswordAutosaveEnabled = settings.SavePasswords && !AppPaths.Private; // rien à retenir d'une fenêtre privée
        s.IsSwipeNavigationEnabled = true;
        s.IsZoomControlEnabled = false; // le zoom est géré par le navigateur (Ctrl +, Ctrl -, Ctrl molette), pas par le moteur // balayage horizontal : page précédente ou suivante
        s.IsGeneralAutofillEnabled = false;
        s.HiddenPdfToolbarItems = CoreWebView2PdfToolbarItems.None; // recherche, pages, rotation, impression et enregistrement natifs
        _ = core.AddScriptToExecuteOnDocumentCreatedAsync(SidewaysScript);
        _ = core.AddScriptToExecuteOnDocumentCreatedAsync("document.addEventListener('pointerdown',e=>{if(e.isTrusted)window.chrome.webview.postMessage('sol.page.pointer')},true)");
        _ = core.AddScriptToExecuteOnDocumentCreatedAsync(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "inject", "video-cursor.js")));
        if (settings.SolAiEnabled)
            _ = core.AddScriptToExecuteOnDocumentCreatedAsync(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "inject", "ai.js")));
        s.IsReputationCheckingRequired = true; // SmartScreen
        // Aucun certificat invalide n'est accepté silencieusement, même sur localhost.
        core.ServerCertificateErrorDetected += (_, e) =>
        {
            e.Action = CoreWebView2ServerCertificateErrorAction.Cancel;
        };
        core.ContainsFullScreenElementChanged += (_, _) => FullScreenChanged?.Invoke(core.ContainsFullScreenElement);
        // Accès sensibles (micro, caméra, position, notifications…) : jamais accordés d'office, le navigateur
        // demande à l'utilisateur. Sans personne pour répondre, la demande est refusée.
        core.PermissionRequested += (_, e) =>
        {
            if (PermissionRequested is null) e.State = CoreWebView2PermissionState.Deny;
            else PermissionRequested(core, e);
        };
    }

    public IEnumerable<CoreWebView2Profile> Profiles => _views.Values.Select(v => v.CoreWebView2?.Profile).OfType<CoreWebView2Profile>().DistinctBy(p => p.ProfileName);
    public async Task<bool> ClearSiteDataAsync(string id)
    {
        if (Core(id) is not { } core) return false;
        try
        {
            await core.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.Cookies |
                CoreWebView2BrowsingDataKinds.AllDomStorage | CoreWebView2BrowsingDataKinds.DiskCache);
            return true;
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException)
        { DebugLog.Write($"site-data.clear {e.GetType().Name}"); return false; }
    }

    public async Task<bool> ResetSiteAsync(string id, string origin)
    {
        if (Core(id) is not { } core || PermissionStore.OriginOf(core.Source) != origin) return false;
        var name = core.Profile.ProfileName;
        var host = new Uri(origin).IdnHost;
        bool Matches(CoreWebView2Cookie cookie)
        {
            var domain = cookie.Domain.TrimStart('.');
            return host.Equals(domain, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);
        }
        var pages = _views.Values.Select(v => v.CoreWebView2).OfType<CoreWebView2>()
            .Where(p => p.Profile.ProfileName == name && PermissionStore.OriginOf(p.Source) == origin).ToArray();
        try
        {
            foreach (var page in pages)
            {
                page.Stop();
                await page.ExecuteScriptAsync("sessionStorage.clear()");
            }
            await core.CallDevToolsProtocolMethodAsync("Storage.clearDataForOrigin",
                System.Text.Json.JsonSerializer.Serialize(new { origin, storageTypes = "all" }));
            foreach (var cookie in await core.Profile.CookieManager.GetCookiesAsync(""))
                if (Matches(cookie)) core.Profile.CookieManager.DeleteCookie(cookie);
            // Les cookies gardés en mémoire ne doivent pas revenir au prochain changement de session.
            if (_sessionCookies.TryGetValue(name, out var saved)) _sessionCookies[name] = saved.Where(c => !Matches(c)).ToArray();
            // ponytail: le moteur ne propose pas de purge HTTP par origine ; seul ce cache est partagé dans la session.
            await core.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.DiskCache);
            foreach (var page in pages)
                if (PermissionStore.OriginOf(page.Source) == origin) page.Reload();
            return true;
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException)
        { DebugLog.Write($"site.reset {e.GetType().Name}"); return false; }
    }

    public void Remove(string id)
    {
        _spellingCache.Remove(id);
        _asleep.Remove(id);
        _pdf.Remove(id);
        _ready.Remove(id);
        _skipSiteRoute.Remove(id);
        _hiddenSince.Remove(id);
        if (_pip == id) ExitPip();
        if (_views.TryGetValue(id, out var closing)) ClearSims(id, closing);
        if (!_views.Remove(id, out var view)) return;
        _loading.Remove(id);
        _blurScriptIds.Remove(id);
        _navigation.Remove(id);
        _target.Remove(id); _errorUrl.Remove(id); _showingError.Remove(id); _downloading.Remove(id);
        container.Children.Remove(view);
        CloseView(view);
    }
    private void CloseView(WebView2 view)
    {
        var core = view.CoreWebView2;
        if (core is null || _views.Values.Any(v => v.CoreWebView2?.Profile.ProfileName == core.Profile.ProfileName))
        { view.Close(); return; }
        var name = core.Profile.ProfileName;
        // Fermer pendant l'installation d'une extension peut laisser son attente liée à une vue détruite.
        _profileReady.Remove(name);
        _closingProfiles[name] = PreserveSessionAsync(view, core, name);
    }
    private async Task PreserveSessionAsync(WebView2 view, CoreWebView2 core, string name)
    {
        try { _sessionCookies[name] = (await core.Profile.CookieManager.GetCookiesAsync("")).Where(c => c.IsSession).Select(c => core.Profile.CookieManager.CopyCookie(c)).ToArray(); }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException)
        { DebugLog.Write($"session.cookies.failed {e.GetType().Name} {e.HResult}"); }
        finally { view.Close(); }
    }

    public void Navigate(string id, string url, bool route = true)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out _)) return;
        if (route && RouteNavigation?.Invoke(id, url, null, false) == true) return;
        if (!route) _skipSiteRoute.Add(id);
        if (_ready.Contains(id) && Core(id) is { } core)
        {
            // Le moteur termine parfois l'ancien chargement avant d'annoncer le suivant (ConnectionAborted).
            // Il est déjà remplacé dès que l'utilisateur demande une autre adresse.
            _navigation.Remove(id);
            core.Navigate(url);
        }
        else if (_views.ContainsKey(id))
        {
            // Saisie pendant le démarrage du moteur : la dernière adresse remplace la page initiale.
            _target[id] = url;
            PageChanged?.Invoke(id, url, null);
        }
        else if (_asleep.TryGetValue(id, out var tab))
        {
            _asleep[id] = tab with { Url = url };
            PageChanged?.Invoke(id, url, null);
        }
    }

    public void Back(string id)
    {
        if (Core(id) is not { CanGoBack: true } core) return;
        _navigation.Remove(id);
        core.GoBack();
    }
    public void Forward(string id)
    {
        if (Core(id) is not { CanGoForward: true } core) return;
        _navigation.Remove(id);
        core.GoForward();
    }
    // Recharger une page d'erreur, c'est retenter l'adresse qui a échoué.
    public void Reload(string id)
    {
        if (_errorUrl.TryGetValue(id, out var failed)) Navigate(id, failed);
        else if (Core(id) is { } core)
        {
            _navigation.Remove(id);
            core.Reload();
        }
        foreach (var mirror in _sims.GetValueOrDefault(id, []).Skip(1))
        {
            _following.Add(mirror.View);
            mirror.View.CoreWebView2?.Reload();
        }
    }
    public void Stop(string id)
    {
        _navigation.Remove(id);
        Core(id)?.Stop();
        _loading.Remove(id);
        RuntimeChanged?.Invoke();
    }

    public void ApplyLayout(string? activeId, string? secondaryId, double ratio)
    {
        _layout = (activeId, secondaryId, ratio);
        _visible.Clear();
        if (activeId is not null) _visible.Add(activeId);
        if (secondaryId is not null) _visible.Add(secondaryId);
        // Un onglet endormi qu'on affiche se réveille : sa vue est créée et sa page chargée maintenant.
        foreach (var id in new[] { activeId, secondaryId })
            if (id is not null && _asleep.Remove(id, out var tab)) Add(tab);
        var split = secondaryId is not null && _views.ContainsKey(secondaryId);
        ratio = SplitLayout.FitRatio(ratio, container.ActualWidth - 8);
        primary.Width = new GridLength(split ? ratio : 1, GridUnitType.Star);
        divider.Width = new GridLength(split ? 8 : 0);
        secondary.Width = split ? new GridLength(1 - ratio, GridUnitType.Star) : new GridLength(0);
        // L'onglet du lecteur flottant redevient l'onglet affiché : il reprend sa place.
        if (_pip is not null && (_pip == activeId || _pip == secondaryId)) ExitPip();
        foreach (var (id, view) in _views)
        {
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
            var sims = _sims[id] = [new Sim(view, devices[0]) { Landscape = landscape && !devices[0].Computer }];
            LayoutSims(id);
            RuntimeChanged?.Invoke();
            await ApplySettingsAsync(id, core); // retire le flou du bas des pages
            await PageZoom.TabAsync(core, 1);   // l'appareil a ses dimensions exactes : sa page n'est pas zoomée
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
                sims.Add(new Sim(mirror, device) { Landscape = landscape && !device.Computer });
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
        foreach (var sim in sims.Where(s => !s.Device.Computer)) sim.Landscape = !sim.Landscape;
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
        await StartEngineAsync(view, main.CoreWebView2.Profile.ProfileName);
        var core = view.CoreWebView2;
        Harden(core);
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
            // La page s'affiche sous la barre d'état : décalée vers le bas de sa hauteur. Sous l'écran, la coque d'un
            // ordinateur continue (base, pied) : la marge du bas garde la page à la hauteur de l'écran.
            var foot = sim.Device.Foot * scale;
            foreach (var (part, left, top, bottom) in new[] { (sim.Below, x, 0.0, 0.0), (sim.View, screenLeft, sim.Inset * scale, foot), (sim.Above, screenLeft, 0.0, foot) })
            {
                (part.HorizontalAlignment, part.VerticalAlignment, part.Margin) = (HorizontalAlignment.Left, VerticalAlignment.Center, new Thickness(left, top, 0, bottom));
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
                CornerClip.Set(view, (float)(sim.Device.PageRadius * _fit.GetValueOrDefault(id, (0, 0, 1)).Scale), squareTop: sim.Inset > 0);
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
            core.Settings.UserAgent = device.Agent(_desktopUserAgent);
            var touch = !device.Computer;
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
                var busy = KeepAwake(id) || _pip == id || _sims.ContainsKey(id) || _loading.Contains(id);
                if (busy) continue;
                if (now - since >= DiscardAfter && core.Source.StartsWith("http", StringComparison.Ordinal) && !_errorUrl.ContainsKey(id)) _ = DiscardIfSafeAsync(id, core);
                else if (now - since >= SuspendAfter && !core.IsSuspended) _ = SuspendAsync(id, core);
            }
            catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException) { /* vue fermée entre-temps */ }
        }
    }

    private readonly HashSet<string> _saving = new();
    private async Task SuspendAsync(string id, CoreWebView2 core)
    {
        if (!_saving.Add(id)) return;
        try
        {
            await core.TrySuspendAsync();
            if (!settings.MemorySaver || !_hiddenSince.ContainsKey(id)) core.Resume();
        }
        catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.COMException) { }
        finally { _saving.Remove(id); }
    }

    private async Task DiscardIfSafeAsync(string id, CoreWebView2 core)
    {
        if (!_saving.Add(id)) return;
        try
        {
            // Keep live state and embedded sign-in/media frames; suspension retains their browser context.
            if (TabSessions.IsSignIn(core.Source)) return;
            if (core.IsSuspended) core.Resume();
            var safe = await core.ExecuteScriptAsync(@"(() => { try {
                return sessionStorage.length === 0 && !document.querySelector('iframe')
                    && ![...document.querySelectorAll('input,textarea')].some(e => e.value !== e.defaultValue)
                    && ![...document.querySelectorAll('video')].some(e => !e.paused);
                } catch { return false; } })()");
            if (safe == "true" && settings.MemorySaver && _hiddenSince.ContainsKey(id)
                && CoreOf(id) == core && !_loading.Contains(id) && !KeepAwake(id)) Discard(id, core);
            else if (settings.MemorySaver && _hiddenSince.ContainsKey(id) && CoreOf(id) == core)
            {
                await core.TrySuspendAsync();
                if (!settings.MemorySaver || !_hiddenSince.ContainsKey(id)) core.Resume();
            }
        }
        catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.COMException) { }
        finally { _saving.Remove(id); }
    }

    private void Discard(string id, CoreWebView2 core)
    {
        var name = core.Profile.ProfileName;
        var tab = new Tab(id, core.Source, core.DocumentTitle, SessionId: name.StartsWith("sol-") ? name[4..] : "");
        Remove(id);
        _hiddenSince.Remove(id);
        _asleep[id] = tab;
        Discarded?.Invoke(id);
    }

    // Zoom des pages : celui du moteur, appliqué par l'extension de Sol (voir PageZoom). Le réglage du profil vaut pour
    // tous les onglets ; la page de démarrage, les documents, les appareils simulés et le lecteur flottant restent à 100 %.
    public void SetZoom(double zoom)
    {
        settings.PageZoom = zoom;
        PageZoom.Set(zoom);
        RuntimeChanged?.Invoke();
        // Une page affichée relaie la demande : tous les onglets prennent le nouveau zoom tout de suite.
        foreach (var core in _views.Values.Select(v => v.CoreWebView2).OfType<CoreWebView2>().DistinctBy(c => c.Profile.ProfileName))
            _ = PageZoom.RefreshAsync(core);
    }

    // À la sortie d'un appareil simulé : la page retrouve les métriques de sa vue et le zoom du profil.
    private async Task ApplyZoomAsync(string id)
    {
        if (!_views.TryGetValue(id, out var view) || view.CoreWebView2 is not { } core || _mode.ContainsKey(id)) return;
        try { await core.CallDevToolsProtocolMethodAsync("Emulation.clearDeviceMetricsOverride", "{}"); }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException or ArgumentException) { return; /* vue fermée entre-temps */ }
        await PageZoom.TabAsync(core, null);
    }

    public CoreWebView2? CoreOf(string id) => Core(id);


    private CoreWebView2? Core(string id) =>
        _views.TryGetValue(id, out var view) ? view.CoreWebView2 : null;
}
