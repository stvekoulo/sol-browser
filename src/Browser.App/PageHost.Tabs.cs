using Browser.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace Browser.App;

public sealed partial class PageHost
{
    public void Remove(string id)
    {
        _spellingCache.Remove(id);
        _spellingSeen.Remove(id);
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

}
