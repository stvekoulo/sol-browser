using Browser.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.Web.WebView2.Core;
using Windows.System;
using Windows.UI;

namespace Browser.App;

public sealed partial class PageHost
{
    private sealed record PreviewSurface(string SourceId, Grid Overlay, WebView2 View, TextBlock Status, ProgressBar Progress,
        TextBlock Title, TextBlock Host, Button Back, Button Forward)
    {
        public string Url = ""; public bool Loading;
        public void Show(string url, string title)
        {
            var host = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : url;
            Host.Text = host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
            Title.Text = title.Length > 0 ? title : Host.Text;
        }
    }
    private readonly Dictionary<string, PreviewSurface> _previews = [];
    private PreviewSurface? _preview;
    private string? _previewTabId;
    private readonly string _previewEscape = "sol.preview.close:" + Guid.NewGuid().ToString("N");
    public string? PreviewSourceId => _preview?.SourceId;
    public event Action<string, string, bool>? PreviewPromoted;

    private bool Retained(PreviewSurface preview) => _previews.GetValueOrDefault(preview.SourceId) == preview;
    public void SetPreviewTab(string? activeId)
    {
        _previewTabId = activeId;
        _preview = activeId is null ? null : _previews.GetValueOrDefault(activeId);
        foreach (var preview in _previews.Values)
            preview.Overlay.Visibility = preview == _preview ? Visibility.Visible : Visibility.Collapsed;
    }
    public void ClosePreview() { if (_preview is { } current) ClosePreviewFor(current.SourceId, focus: true); }
    public void ClosePreviewFor(string sourceId, bool focus = false)
    {
        if (!_previews.Remove(sourceId, out var preview)) return;
        if (_preview == preview) _preview = null;
        container.Children.Remove(preview.Overlay); preview.View.Close();
        try { if (focus && _views.TryGetValue(sourceId, out var owner)) owner.Focus(FocusState.Programmatic); }
        catch (InvalidOperationException) { }
    }
    public void CloseAllPreviews()
    {
        foreach (var id in _previews.Keys.ToArray()) ClosePreviewFor(id);
    }
    public void PromotePreview(bool split)
    {
        if (_preview is not { } preview || !LinkPreview.Allowed(preview.Url)) return;
        var id = preview.SourceId; var url = preview.Url; ClosePreview(); PreviewPromoted?.Invoke(id, url, split);
    }
    public void NavigatePreview(string action)
    {
        if (_preview?.View.CoreWebView2 is not { } core) return;
        if (action == "back" && core.CanGoBack) core.GoBack();
        else if (action == "forward" && core.CanGoForward) core.GoForward();
        else if (action == "reload") { if (_preview.Loading) core.Stop(); else core.Reload(); }
    }
    public async Task OpenPreviewAsync(string sourceId, string url)
    {
        if (!LinkPreview.Allowed(url) || Core(sourceId) is not { } source) return;
        ClosePreviewFor(sourceId);
        var preview = CreatePreview(sourceId); preview.Url = url; preview.Show(url, ""); _previews[sourceId] = preview;
        SetPreviewTab(_previewTabId ?? sourceId);
        container.Children.Add(preview.Overlay);
        try
        {
            await StartEngineAsync(preview.View, source.Profile.ProfileName);
            if (!Retained(preview)) return;
            var core = preview.View.CoreWebView2;
            await Harden(core, withFan: false);
            if (!Retained(preview)) return;
            core.Settings.AreDefaultContextMenusEnabled = true;
            core.Settings.IsStatusBarEnabled = true;
            var signal = System.Text.Json.JsonSerializer.Serialize(_previewEscape);
            await core.AddScriptToExecuteOnDocumentCreatedAsync("addEventListener('keydown',e=>{if(e.isTrusted&&e.key==='Escape'){e.preventDefault();e.stopImmediatePropagation();chrome.webview.postMessage(" + signal + ")}},true)");
            core.WebMessageReceived += (_, e) => { if (_preview == preview && e.Source == core.Source && e.WebMessageAsJson == signal) ClosePreview(); };
            core.FrameCreated += (_, e) => e.Frame.WebMessageReceived += (_, message) => { if (_preview == preview && message.WebMessageAsJson == signal) ClosePreview(); };
            core.NavigationStarting += (_, e) =>
            {
                if (!LinkPreview.Allowed(e.Uri)) { e.Cancel = true; return; }
                preview.Loading = true; preview.Progress.Visibility = Visibility.Visible; preview.Status.Visibility = Visibility.Collapsed;
            };
            core.SourceChanged += (_, _) => { if (LinkPreview.Allowed(core.Source)) { preview.Url = core.Source; preview.Show(core.Source, core.DocumentTitle); } };
            core.DocumentTitleChanged += (_, _) => preview.Show(preview.Url, core.DocumentTitle);
            core.HistoryChanged += (_, _) => { preview.Back.IsEnabled = core.CanGoBack; preview.Forward.IsEnabled = core.CanGoForward; };
            core.NavigationCompleted += (_, e) =>
            {
                preview.Loading = false; preview.Progress.Visibility = Visibility.Collapsed;
                if (!e.IsSuccess && e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
                {
                    preview.Status.Text = Language.T("La page n’a pas pu être chargée. Réessayez.", "The page could not be loaded. Retry.");
                    preview.Status.Visibility = Visibility.Visible;
                }
            };
            core.NewWindowRequested += (_, e) => { e.Handled = true; if (LinkPreview.Allowed(e.Uri)) core.Navigate(e.Uri); };
            core.DownloadStarting += (_, e) =>
            {
                e.Cancel = true;
                preview.Status.Text = Language.T("Ouvrez cet aperçu dans un onglet pour télécharger.", "Open this preview in a tab to download.");
                preview.Status.Visibility = Visibility.Visible;
            };
            core.ProcessFailed += (_, _) =>
            {
                preview.Status.Text = Language.T("Cet aperçu s’est interrompu. Rechargez la page.", "This preview stopped. Reload the page.");
                preview.Status.Visibility = Visibility.Visible;
            };
            core.Navigate(url); if (_preview == preview) preview.View.Focus(FocusState.Programmatic);
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException or IOException)
        {
            if (Retained(preview))
            {
                preview.Progress.Visibility = Visibility.Collapsed;
                preview.Status.Text = Language.T("Impossible d’ouvrir cet aperçu.", "Unable to open this preview.");
                preview.Status.Visibility = Visibility.Visible;
            }
        }
    }

    private PreviewSurface CreatePreview(string sourceId)
    {
        var isDark = (0.299 * _pageColor.R + 0.587 * _pageColor.G + 0.114 * _pageColor.B < 160) || settings.Theme == "dark" || (settings.Theme != "light" && UiBridge.InitialBackground.R < 128);
        var surfaceBg = isDark ? Color.FromArgb(255, 18, 18, 20) : Color.FromArgb(255, 255, 255, 255);
        var buttonBg = new SolidColorBrush(isDark ? Color.FromArgb(225, 28, 28, 32) : Color.FromArgb(235, 255, 255, 255));
        var hoverBg = new SolidColorBrush(isDark ? Color.FromArgb(240, 48, 48, 54) : Color.FromArgb(245, 240, 240, 245));
        var borderBrush = new SolidColorBrush(isDark ? Color.FromArgb(38, 255, 255, 255) : Color.FromArgb(25, 0, 0, 0));
        var ink = new SolidColorBrush(isDark ? Color.FromArgb(255, 245, 245, 245) : Color.FromArgb(255, 24, 24, 27));
        var muted = new SolidColorBrush(isDark ? Color.FromArgb(150, 245, 245, 245) : Color.FromArgb(150, 24, 24, 27));
        var clear = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

        var overlay = new Grid { Background = new SolidColorBrush(Color.FromArgb(140, 0, 0, 0)) };
        Grid.SetColumnSpan(overlay, 3); Canvas.SetZIndex(overlay, 50);

        var slide = new TranslateTransform();
        var card = new Grid
        {
            CornerRadius = new CornerRadius(16),
            Background = new SolidColorBrush(surfaceBg),
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            Shadow = new ThemeShadow(),
            Translation = new System.Numerics.Vector3(0, 0, 32),
            RenderTransform = slide,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        card.RowDefinitions.Add(new RowDefinition { Height = new GridLength(PreviewHeaderHeight) });
        card.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        overlay.Children.Add(card);

        overlay.PointerPressed += (_, e) => { if (ReferenceEquals(e.OriginalSource, overlay)) { ClosePreview(); e.Handled = true; } };
        overlay.KeyDown += (_, e) => { if (e.Key == VirtualKey.Escape) { ClosePreview(); e.Handled = true; } };

        // En-tête : l'historique propre à l'aperçu, puis le titre et le domaine de la page affichée.
        var header = new Grid { Padding = new Thickness(8, 0, 16, 0), BorderBrush = borderBrush, BorderThickness = new Thickness(0, 0, 0, 1) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Button Nav(string label, string kind) => PreviewButton(label, kind, () => NavigatePreview(kind), ink, clear, hoverBg, clear, 32, 16, 8);
        var back = Nav(Language.T("Page précédente", "Back"), "back");
        var forward = Nav(Language.T("Page suivante", "Forward"), "forward");
        back.IsEnabled = forward.IsEnabled = false;
        var nav = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        nav.Children.Add(back); nav.Children.Add(forward); nav.Children.Add(Nav(Language.T("Recharger", "Reload"), "reload"));
        header.Children.Add(nav);
        var title = new TextBlock { Foreground = ink, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap };
        var host = new TextBlock { Foreground = muted, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap };
        var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), Spacing = 1 };
        label.Children.Add(title); label.Children.Add(host);
        Grid.SetColumn(label, 1); header.Children.Add(label);
        card.Children.Add(header);

        var rail = new StackPanel { Spacing = PreviewActionSpacing, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        overlay.Children.Add(rail); Canvas.SetZIndex(rail, 2);
        rail.Children.Add(PreviewButton(Language.T("Fermer l’aperçu (Échap)", "Close preview (Escape)"), "close", ClosePreview, ink, buttonBg, hoverBg, borderBrush));
        rail.Children.Add(PreviewButton(Language.T("Ouvrir dans un nouvel onglet", "Open in a new tab"), "tab", () => PromotePreview(false), ink, buttonBg, hoverBg, borderBrush));
        rail.Children.Add(PreviewButton(Language.T("Ouvrir à côté", "Open beside"), "split", () => PromotePreview(true), ink, buttonBg, hoverBg, borderBrush));

        var view = new CursorWebView { DefaultBackgroundColor = surfaceBg, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        Grid.SetRow(view, 1); card.Children.Add(view);
        view.SizeChanged += (_, _) => CornerClip.Set(view, 16, squareTop: true);

        var progress = new ProgressBar { IsIndeterminate = true, Height = 2, MinHeight = 2, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Stretch };
        Grid.SetRow(progress, 1); Canvas.SetZIndex(progress, 2); card.Children.Add(progress);

        var status = new TextBlock
        {
            Foreground = ink,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(16, 0, 16, 12),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Visibility = Visibility.Collapsed
        };
        Grid.SetRow(status, 1); Canvas.SetZIndex(status, 3); card.Children.Add(status);

        void LayoutSurfaces()
        {
            var w = overlay.ActualWidth;
            var h = overlay.ActualHeight;
            if (w <= 0 || h <= 0) return;
            // La carte occupe presque toute la page, commandes à sa droite ; sur une zone étroite elles passent au-dessus.
            var compact = w < 620;
            var margin = compact ? 8 : w < 900 ? 16 : 24;
            var railLength = 3 * PreviewActionSize + 2 * PreviewActionSpacing;
            var side = compact ? 0 : PreviewActionSize + PreviewRailGap;
            var above = compact ? PreviewActionSize + PreviewRailGap : 0;
            var cardW = Math.Max(1, Math.Min(1680, w - 2 * margin - side));
            var cardH = Math.Max(1, h - 2 * margin - above);
            var left = Math.Max(margin, (w - cardW - side) / 2);
            card.Width = cardW; card.Height = cardH;
            card.Margin = new Thickness(left, margin + above, 0, 0);
            rail.Orientation = compact ? Orientation.Horizontal : Orientation.Vertical;
            rail.Margin = compact ? new Thickness(Math.Max(margin, (w - railLength) / 2), margin, 0, 0)
                : new Thickness(left + cardW + PreviewRailGap, margin, 0, 0);
        }

        overlay.SizeChanged += (_, _) => LayoutSurfaces();
        overlay.Loaded += (_, _) =>
        {
            if (!PreviewMotion.AnimationsEnabled) return;
            var story = new Storyboard();
            foreach (var (target, property, from, to, ms) in new (DependencyObject, string, double, double, double)[] { (overlay, "Opacity", 0, 1, 160), (slide, "Y", 18, 0, 260) })
            {
                var animation = new DoubleAnimation { From = from, To = to, Duration = TimeSpan.FromMilliseconds(ms), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                Storyboard.SetTarget(animation, target); Storyboard.SetTargetProperty(animation, property);
                story.Children.Add(animation);
            }
            story.Begin();
        };
        return new(sourceId, overlay, view, status, progress, title, host, back, forward);
    }
}
