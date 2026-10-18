using Browser.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace Browser.App;

public sealed partial class PageHost
{

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
        await (_privateCleanup ??= DeleteAbandonedPrivateProfilesAsync(profile));
        RestoreGitLabSession(profile);
        if (_sessionCookies.Remove(profile.ProfileName, out var cookies))
            foreach (var cookie in cookies) profile.CookieManager.AddOrUpdateCookie(cookie);
    }
    private async Task PrepareProfileAsync(CoreWebView2Profile profile)
    {
        await NativePermissions.SyncAsync(profile);
        await AdBlock.RemoveBundledAsync(profile);
        await PageZoom.InstallAsync(profile, settings.PageZoom);
        await SharedExtensions.SyncAsync(profile);
        ExtensionsChanged?.Invoke();
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
        if (tab.Private) _privateTabs.Add(tab.Id);
        _views[tab.Id] = view;
        view.GotFocus += (_, _) => { if (_visible.Contains(tab.Id) && _views.GetValueOrDefault(tab.Id) == view) PageFocused?.Invoke(tab.Id); };
        container.Children.Add(view);
        try
        {
            await StartEngineAsync(view, tab.Private ? PrivateProfileName : tab.SessionId.Length == 0 ? null : "sol-" + tab.SessionId);
            (_bubbles ??= new NativeBubbles(container)).Watch(view.CoreWebView2.BrowserProcessId);
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
        await core.AddScriptToExecuteOnDocumentCreatedAsync(ExtensionAction.ContextScript);
        core.DOMContentLoaded += (_, _) =>
        {
            if(!core.Source.StartsWith("https://start.sol/",StringComparison.Ordinal)) { view.Opacity=1;ContentPainted?.Invoke(); }
        };
        Harden(core);
        core.SetVirtualHostNameToFolderMapping("start.sol", Path.Combine(AppContext.BaseDirectory, "ui"), CoreWebView2HostResourceAccessKind.Deny);
        core.WebMessageReceived += (_, e) =>
        {
            if(e.Source is Settings.DefaultHomePage or QuickNotes.PageUrl && e.WebMessageAsJson.StartsWith('"') && e.TryGetWebMessageAsString()=="\"ui.painted\"")
            { view.Opacity=1;ContentPainted?.Invoke();return; }
            if (e.Source == core.Source && e.WebMessageAsJson == "\"sol.page.pointer\"") { PagePressed?.Invoke(); return; }
            if (e.Source == core.Source && e.WebMessageAsJson == "\"sol.selection.none\"") { SelectionCleared?.Invoke(tab.Id); return; }
            if (e.Source == core.Source && e.WebMessageAsJson.StartsWith("\"{\\\"sol\\\":\\\"selection\\\"", StringComparison.Ordinal)) { ReportSelection(tab.Id, view, e.TryGetWebMessageAsString()); return; }
            // During hash history navigation, WebView2 may still report the previous internal fragment.
            if (e.Source is Settings.DefaultHomePage or QuickNotes.PageUrl && core.Source is Settings.DefaultHomePage or QuickNotes.PageUrl
                && CommandParser.Parse(e.TryGetWebMessageAsString()) is { } ready)
            {
                if (ready.Type == "notes.ready") { NotesRequested?.Invoke(tab.Id, ready); return; }
                if (ready.Type == "start.ready") { StartRequested?.Invoke(tab.Id, ready); return; }
            }
            if (e.Source == QuickNotes.PageUrl && core.Source == QuickNotes.PageUrl)
            {
                if (CommandParser.Parse(e.TryGetWebMessageAsString()) is { } notes && notes.Type is
                    "notes.ready" or "chat.toggle" or "chat.ask" or "notes.open" or "notes.draft" or "notes.save" or "notes.edit" or "notes.discard" or "notes.delete" or "notes.export" or "notes.visit" or "notes.link" or
                    "notes.autosave" or "notes.meta" or "notes.action" or "notes.version" or "notes.tag" or "notes.comment" or "notes.import" or "notes.attach" or "notes.file" or "notes.detach" or "notes.order" or "notes.dates" or "notes.archive-policy")
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
            if (CommandParser.Parse(e.TryGetWebMessageAsString()) is { } command && command.Type is "start.ready" or "start.navigate" or "start.group" or "start.favorites"
                or "start.open" or "start.split" or "start.copy" or "start.topic" or "start.groupedit")
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
                    if (IsPdf(tab.Id))
                    {
                        if (IsPrivate(tab.Id)) _ = PageZoom.TabAsync(core, 1);
                        else PageZoom.KeepPlain(e.Request.Uri);
                    }
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
            if (HandleExtensionAction(tab.Id, e)) return;
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
        if (navigate) Navigate(tab.Id, _target.GetValueOrDefault(tab.Id, tab.Url));
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
}
