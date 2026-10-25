using Browser.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using Windows.System;
using Windows.UI;

namespace Browser.App;

public sealed partial class PageHost
{
    private sealed record PreviewSurface(string SourceId, Grid Overlay, WebView2 View, TextBlock Status, ProgressBar Progress)
    { public string Url = ""; public bool Loading; }
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
        var preview = CreatePreview(sourceId); preview.Url = url; _previews[sourceId] = preview;
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
            core.SourceChanged += (_, _) => { if (LinkPreview.Allowed(core.Source)) preview.Url = core.Source; };
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

        var overlay = new Grid { Background = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)) };
        Grid.SetColumnSpan(overlay, 3); Canvas.SetZIndex(overlay, 50);

        var card = new Grid
        {
            CornerRadius = new CornerRadius(18),
            Background = new SolidColorBrush(surfaceBg),
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            Shadow = new ThemeShadow(),
            Translation = new System.Numerics.Vector3(0, 0, 32),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        overlay.Children.Add(card);

        overlay.PointerPressed += (_, e) => { if (ReferenceEquals(e.OriginalSource, overlay)) { ClosePreview(); e.Handled = true; } };
        overlay.KeyDown += (_, e) => { if (e.Key == VirtualKey.Escape) { ClosePreview(); e.Handled = true; } };

        var rail = new StackPanel { Spacing = PreviewActionSpacing, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        overlay.Children.Add(rail); Canvas.SetZIndex(rail, 2);

        rail.Children.Add(PreviewRailButton(Language.T("Fermer l’aperçu (Échap)", "Close preview (Escape)"),
            "M4 4 L16 16 M16 4 L4 16", ClosePreview, "close", ink, buttonBg, hoverBg, borderBrush));
        rail.Children.Add(PreviewRailButton(Language.T("Ouvrir dans un nouvel onglet", "Open in a new tab"),
            "M9 3 H4 Q3 3 3 4 V16 Q3 17 4 17 H16 Q17 17 17 16 V11 M11 3 H17 V9 M17 3 L9 11",
            () => PromotePreview(false), "tab", ink, buttonBg, hoverBg, borderBrush));
        rail.Children.Add(PreviewRailButton(Language.T("Ouvrir à côté", "Open beside"),
            "M4 3 H16 Q17 3 17 4 V16 Q17 17 16 17 H4 Q3 17 3 16 V4 Q3 3 4 3 M10 3 V17",
            () => PromotePreview(true), "split", ink, buttonBg, hoverBg, borderBrush));

        var view = new CursorWebView { DefaultBackgroundColor = surfaceBg, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        card.Children.Add(view);
        view.SizeChanged += (_, _) => CornerClip.Set(view, 18);

        var progress = new ProgressBar { IsIndeterminate = true, Height = 2, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Stretch };
        Canvas.SetZIndex(progress, 2); card.Children.Add(progress);

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
        Canvas.SetZIndex(status, 3); card.Children.Add(status);

        void LayoutSurfaces()
        {
            var w = overlay.ActualWidth;
            var h = overlay.ActualHeight;
            if (w <= 0 || h <= 0) return;
            // Reserve a real rail on wide windows; move it above the page on compact ones.
            var compact = w < 620;
            var margin = w < 360 ? 8 : 16;
            var railWidth = 3 * PreviewActionSize + 2 * PreviewActionSpacing;
            var reserve = PreviewActionSize + PreviewRailGap + margin;
            var cardW = Math.Max(1, Math.Min(1440, compact ? w - margin * 2 : Math.Min(w * .84, w - reserve * 2)));
            var cardH = Math.Max(1, Math.Min(980, compact ? h - PreviewActionSize - PreviewRailGap - 2 * margin : Math.Min(h * .88, h - 40)));
            card.Width = cardW; card.Height = cardH;
            card.Margin = new Thickness(0, compact ? PreviewActionSize : 0, compact ? 0 : PreviewActionSize, 0);
            var cardLeft = (w - cardW - (compact ? 0 : PreviewActionSize)) / 2;
            var cardTop = (h - cardH + (compact ? PreviewActionSize : 0)) / 2;
            rail.Orientation = compact ? Orientation.Horizontal : Orientation.Vertical;
            rail.Margin = new Thickness(compact ? Math.Max(margin, (w - railWidth) / 2) : cardLeft + cardW + PreviewRailGap,
                compact ? Math.Max(4, cardTop - PreviewActionSize - PreviewRailGap) : cardTop, 0, 0);
        }

        overlay.SizeChanged += (_, _) => LayoutSurfaces();
        return new(sourceId, overlay, view, status, progress);
    }
}
