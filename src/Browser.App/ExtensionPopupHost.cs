using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Browser.App;

/// <summary>
/// Fenêtre d'une extension (la page qu'ouvre son bouton), affichée dans un encart flottant sous la barre du haut au
/// lieu d'un onglet. La vue partage le moteur et le profil des pages : l'extension y est chez elle. L'encart prend la
/// taille de son contenu et se referme quand on clique ailleurs.
/// Coins arrondis par CornerClip, comme les menus (jamais d'API de composition sur un cadre flottant).
/// </summary>
public sealed class ExtensionPopupHost(Grid frame, WebView2 view)
{
    private const double Edge = 10, MinWidth = 260, MaxWidth = 780, MinHeight = 120, MaxHeight = 620;
    private bool _started;
    private double _anchorRight, _top;
    private DateTime _shownAt;
    private int _run;

    /// <summary>Un lien de l'extension demande un nouvel onglet.</summary>
    public event Action<string>? TabRequested;

    public bool IsOpen => frame.Visibility == Visibility.Visible;

    /// <param name="right">Bord droit de l'encart (celui du bouton), dans la fenêtre.</param>
    public async Task ShowAsync(string url, double right, double top)
    {
        if (!_started)
        {
            _started = true;
            await view.EnsureCoreWebView2Async(await AdBlock.EnvironmentAsync());
            var core = view.CoreWebView2;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.NewWindowRequested += (_, e) => { e.Handled = true; Hide(); TabRequested?.Invoke(e.Uri); };
            core.WindowCloseRequested += (_, _) => Hide(); // l'extension ferme sa fenêtre elle-même, une fois son action faite
            core.NavigationCompleted += (_, _) => _ = FitSoonAsync(_run);
            // Clic ailleurs : l'encart se referme. Le tout premier changement de curseur, à l'ouverture, ne compte pas.
            view.LostFocus += (_, _) => { if ((DateTime.UtcNow - _shownAt).TotalMilliseconds > 500) Hide(); };
        }
        _run++;
        (_anchorRight, _top, _shownAt) = (right, top, DateTime.UtcNow);
        Place(380, MinHeight); // au plus court : la mesure du contenu ne peut que l'agrandir, jamais le réduire
        frame.Visibility = Visibility.Visible;
        frame.UpdateLayout();
        CornerClip.Set(view, 16);
        view.CoreWebView2.Navigate(url);
        await Task.Delay(180);
        if (IsOpen) view.Focus(FocusState.Programmatic);
    }

    private void Place(double width, double height)
    {
        var window = (FrameworkElement)frame.Parent;
        frame.Width = Math.Clamp(width, MinWidth, Math.Min(MaxWidth, window.ActualWidth - 2 * Edge));
        frame.Height = Math.Clamp(height, MinHeight, Math.Min(MaxHeight, window.ActualHeight - _top - Edge));
        frame.Margin = new Thickness(Math.Max(Edge, Math.Min(_anchorRight, window.ActualWidth - Edge) - frame.Width), _top, 0, 0);
    }

    // Une fenêtre d'extension se dimensionne à son contenu, qui arrive souvent après le chargement : on mesure plusieurs fois.
    private async Task FitSoonAsync(int run)
    {
        foreach (var wait in new[] { 60, 250, 700, 1500 })
        {
            await Task.Delay(wait);
            if (run != _run || !IsOpen) return;
            try
            {
                var raw = await view.CoreWebView2.ExecuteScriptAsync("JSON.stringify([Math.max(document.body ? document.body.scrollWidth : 0, document.documentElement.scrollWidth), Math.max(document.body ? document.body.scrollHeight : 0, document.documentElement.scrollHeight)])");
                var size = JsonSerializer.Deserialize<double[]>(JsonSerializer.Deserialize<string>(raw) ?? "[]");
                if (size is not { Length: 2 } || size[0] < 1 || size[1] < 1) continue;
                Place(size[0], size[1]);
                frame.UpdateLayout();
                CornerClip.Set(view, 16);
            }
            catch (Exception e) when (e is InvalidOperationException or JsonException or System.Runtime.InteropServices.COMException) { return; }
        }
    }

    public void Hide()
    {
        if (!IsOpen) return;
        _run++;
        frame.Visibility = Visibility.Collapsed;
        view.CoreWebView2?.Navigate("about:blank"); // la page de l'extension est déchargée : rien ne tourne encart fermé
    }
}
