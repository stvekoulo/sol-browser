using Browser.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Browser.App;

/// <summary>Une WebView2 par onglet. Aucun canal vers l'hôte n'est ouvert sur ces vues.</summary>
public sealed class PageHost(Grid container, ColumnDefinition primary, ColumnDefinition secondary)
{
    private readonly Dictionary<string, WebView2> _views = new();
    private readonly HashSet<string> _loading = new();

    public event Action<string, string?, string?>? PageChanged; // id, url, titre
    public event Action? RuntimeChanged;                         // chargement, historique
    public event Action<string>? NewTabRequested;

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
        core.SourceChanged += (_, _) => PageChanged?.Invoke(tab.Id, core.Source, null);
        core.DocumentTitleChanged += (_, _) => PageChanged?.Invoke(tab.Id, null, core.DocumentTitle);
        core.HistoryChanged += (_, _) => RuntimeChanged?.Invoke();
        core.NavigationStarting += (_, _) => { _loading.Add(tab.Id); RuntimeChanged?.Invoke(); };
        core.NavigationCompleted += (_, _) => { _loading.Remove(tab.Id); RuntimeChanged?.Invoke(); };
        core.NewWindowRequested += (_, e) => { e.Handled = true; NewTabRequested?.Invoke(e.Uri); };
        Navigate(tab.Id, tab.Url);
    }

    public void Remove(string id)
    {
        if (!_views.Remove(id, out var view)) return;
        _loading.Remove(id);
        container.Children.Remove(view);
        view.Close();
    }

    public void Navigate(string id, string url)
    {
        if (Core(id) is { } core && Uri.TryCreate(url, UriKind.Absolute, out _)) core.Navigate(url);
    }

    public void Back(string id) => Core(id)?.GoBack();
    public void Forward(string id) => Core(id)?.GoForward();
    public void Reload(string id) => Core(id)?.Reload();
    public void Stop(string id) => Core(id)?.Stop();

    public void ApplyLayout(string? activeId, string? secondaryId, double ratio)
    {
        var split = secondaryId is not null && _views.ContainsKey(secondaryId);
        primary.Width = new GridLength(split ? ratio : 1, GridUnitType.Star);
        secondary.Width = split ? new GridLength(1 - ratio, GridUnitType.Star) : new GridLength(0);
        foreach (var (id, view) in _views)
        {
            var column = id == activeId ? 0 : split && id == secondaryId ? 1 : -1;
            view.Visibility = column < 0 ? Visibility.Collapsed : Visibility.Visible;
            if (column >= 0) Grid.SetColumn(view, column);
        }
    }

    private Microsoft.Web.WebView2.Core.CoreWebView2? Core(string id) =>
        _views.TryGetValue(id, out var view) ? view.CoreWebView2 : null;
}
