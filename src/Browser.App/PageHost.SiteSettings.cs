using Browser.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace Browser.App;

public sealed partial class PageHost
{

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
        foreach (var mirror in _sims.Values.SelectMany(sims => sims.Skip(1)).Select(sim => sim.View.CoreWebView2).OfType<CoreWebView2>().ToArray())
        {
            try { await ConfigureFanAsync(mirror); }
            catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException) { }
        }
    }

    private async Task ApplySettingsAsync(string id, CoreWebView2 core)
    {
        await ConfigureFanAsync(core);
        StartSpelling();
        if (!settings.MemorySaver) Wake(id, core);
        else if (_hiddenSince.ContainsKey(id) && !core.IsSuspended) core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
        if (!settings.MemorySaver && _hiddenSince.ContainsKey(id)) _hiddenSince[id] = DateTime.UtcNow;
        var iCloud = (await core.Profile.GetBrowserExtensionsAsync()).Any(e => e.IsEnabled && e.Id is "pejdijmoenmkgeppbflobdenhhabjlaj" or "mfbcdcnpokpoajjciilocoachedjkima");
        core.Settings.IsPasswordAutosaveEnabled = settings.SavePasswords && !IsPrivate(id) && !iCloud;
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
    private async Task Harden(CoreWebView2 core,bool withFan=true)
    {
        var s = core.Settings;
        s.AreHostObjectsAllowed = false;
        NativeDownloadUi.Suppress(core, () => NativeDownloadsRequested?.Invoke(), disableAccelerators: false);
        var downloadSignal = System.Text.Json.JsonSerializer.Serialize("sol.downloads:" + FanKey);
        await core.AddScriptToExecuteOnDocumentCreatedAsync("(() => { const post=chrome.webview.postMessage.bind(chrome.webview); addEventListener('keydown',e=>{if(e.isTrusted&&e.ctrlKey&&!e.altKey&&!e.shiftKey&&e.code==='KeyJ'){e.preventDefault();e.stopImmediatePropagation();post(" + downloadSignal + ");}},true); })()");
        core.WebMessageReceived += (_, e) => { if (e.Source == core.Source && e.WebMessageAsJson == downloadSignal) NativeDownloadsRequested?.Invoke(); };
        s.IsWebMessageEnabled = true;
        // Mots de passe : gestionnaire du moteur (proposition d'enregistrement, stockage chiffré, remplissage).
        s.IsPasswordAutosaveEnabled = settings.SavePasswords && !AppPaths.Private && !PrivateBrowsing.IsProfile(core.Profile.ProfileName);
        s.IsSwipeNavigationEnabled = true;
        s.IsZoomControlEnabled = false; // le zoom est géré par le navigateur (Ctrl +, Ctrl -, Ctrl molette), pas par le moteur // balayage horizontal : page précédente ou suivante
        s.IsGeneralAutofillEnabled = false;
        s.HiddenPdfToolbarItems = CoreWebView2PdfToolbarItems.None; // recherche, pages, rotation, impression et enregistrement natifs
        _ = core.AddScriptToExecuteOnDocumentCreatedAsync(SidewaysScript);
        _ = core.AddScriptToExecuteOnDocumentCreatedAsync("document.addEventListener('pointerdown',e=>{if(e.isTrusted&&window.chrome?.webview)window.chrome.webview.postMessage('sol.page.pointer')},true)");
        if(withFan){await ConfigureFanAsync(core);core.DOMContentLoaded += (_, _) => PostFanSettings(core);}
        _ = core.AddScriptToExecuteOnDocumentCreatedAsync(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "inject", "video-cursor.js")));
        // Mémorise la sélection au clic droit pour Sol AI ; sans effet tant que l'utilisateur n'agit pas.
        _ = core.AddScriptToExecuteOnDocumentCreatedAsync(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "inject", "ai.js")));
        _ = core.AddScriptToExecuteOnDocumentCreatedAsync(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "inject", "streaming-fix.js")));
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
            await NativePermissions.ResetAsync(core.Profile, origin);
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

}
