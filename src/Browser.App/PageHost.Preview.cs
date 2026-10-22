using Browser.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using Windows.UI;
using Windows.System;

namespace Browser.App;

public sealed partial class PageHost
{
    private sealed record PreviewSurface(string SourceId, Grid Overlay, WebView2 View, TextBlock Address, TextBlock Status,
        Button Back, Button Forward, Button Reload, ProgressBar Progress)
    { public string Url = ""; public bool Loading; }
    private PreviewSurface? _preview;
    private readonly string _previewEscape = "sol.preview.close:" + Guid.NewGuid().ToString("N");
    public string? PreviewSourceId => _preview?.SourceId;
    public event Action<string, string, bool>? PreviewPromoted;

    public void ClosePreview()
    {
        if (_preview is not { } preview) return;
        _preview = null; container.Children.Remove(preview.Overlay);
        preview.View.Close();
        try { if (_views.TryGetValue(preview.SourceId, out var owner)) owner.Focus(FocusState.Programmatic); }
        catch (InvalidOperationException) { }
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
        ClosePreview();
        var preview = CreatePreview(sourceId); preview.Url = url; preview.Address.Text = url; _preview = preview;
        container.Children.Add(preview.Overlay);
        try
        {
            await StartEngineAsync(preview.View, source.Profile.ProfileName);
            if (_preview != preview) return;
            var core = preview.View.CoreWebView2;
            await Harden(core, withFan: false);
            if (_preview != preview) return;
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
                preview.Reload.Content = "×";
            };
            core.SourceChanged += (_, _) => { if (LinkPreview.Allowed(core.Source)) { preview.Url = core.Source; preview.Address.Text = core.Source; ToolTipService.SetToolTip(preview.Address, core.Source); } };
            core.HistoryChanged += (_, _) => { preview.Back.IsEnabled = core.CanGoBack; preview.Forward.IsEnabled = core.CanGoForward; };
            core.NavigationCompleted += (_, e) =>
            {
                preview.Loading = false; preview.Reload.Content = "↻"; preview.Progress.Visibility = Visibility.Collapsed;
                if (!e.IsSuccess && e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
                {
                    preview.Status.Text = Language.T("La page n’a pas pu être chargée. Réessayez avec ↻.", "The page could not be loaded. Retry with ↻.");
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
            core.Navigate(url); preview.View.Focus(FocusState.Programmatic);
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException or IOException)
        {
            if (_preview == preview)
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
        var buttonBg = isDark ? Color.FromArgb(220, 26, 26, 30) : Color.FromArgb(235, 255, 255, 255);
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

        var rail = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(buttonBg),
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            Shadow = new ThemeShadow(),
            Translation = new System.Numerics.Vector3(0, 0, 16),
            Padding = new Thickness(3)
        };
        var navigation = new StackPanel { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed, Spacing = 3 };
        overlay.Children.Add(toolbar); overlay.Children.Add(rail); Canvas.SetZIndex(toolbar, 2); Canvas.SetZIndex(rail, 2);

        Button Action(string label, string glyph, StackPanel parent, Action run, int size = 36, CornerRadius? radius = null)
        {
            var button = new Button
            {
                Content = glyph,
                Width = size,
                Height = size,
                MinWidth = 28,
                MinHeight = 28,
                CornerRadius = radius ?? new CornerRadius(12),
                Padding = new Thickness(0),
                Foreground = ink,
                Background = new SolidColorBrush(buttonBg),
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1),
                Shadow = new ThemeShadow(),
                Translation = new System.Numerics.Vector3(0, 0, 16),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                FontSize = 13.5
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label);
            ToolTipService.SetToolTip(button, label);
            button.Click += (_, _) => run();
            parent.Children.Add(button);
            return button;
        }

        Action(Language.T("Navigation de l’aperçu", "Preview navigation"), "☰", toolbar,
            () => navigation.Visibility = navigation.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible, 30, new CornerRadius(9));
        toolbar.Children.Add(navigation);
        var back = Action(Language.T("Précédent", "Back"), "‹", navigation, () => NavigatePreview("back"), 30, new CornerRadius(9)); back.IsEnabled = false;
        var forward = Action(Language.T("Suivant", "Forward"), "›", navigation, () => NavigatePreview("forward"), 30, new CornerRadius(9)); forward.IsEnabled = false;
        var reload = Action(Language.T("Recharger / arrêter", "Reload / stop"), "↻", navigation, () => NavigatePreview("reload"), 30, new CornerRadius(9));
        var address = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 240, VerticalAlignment = VerticalAlignment.Center, Foreground = ink, FontSize = 12, Margin = new Thickness(6, 0, 8, 0) };
        navigation.Children.Add(address);

        Action(Language.T("Fermer l’aperçu (Échap)", "Close preview (Escape)"), "✕", rail, ClosePreview, 38);
        Action(Language.T("Ouvrir dans un nouvel onglet", "Open in a new tab"), "⤢", rail, () => PromotePreview(false), 38);
        Action(Language.T("Ouvrir à côté", "Open beside"), "◫", rail, () => PromotePreview(true), 38);

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
            var cardW = Math.Clamp(w * 0.84, 600, 1440);
            if (cardW > w - 100) cardW = Math.Max(320, w - 100);
            var cardH = Math.Clamp(h * 0.88, 440, 980);
            if (cardH > h - 60) cardH = Math.Max(260, h - 60);

            card.Width = cardW;
            card.Height = cardH;

            var cardLeft = (w - cardW) / 2;
            var cardTop = (h - cardH) / 2;

            var railX = cardLeft + cardW + 12;
            if (railX + 48 > w) railX = cardLeft + cardW - 50;
            rail.Margin = new Thickness(railX, cardTop, 0, 0);
            toolbar.Margin = new Thickness(cardLeft + 14, cardTop + 14, 0, 0);
            address.MaxWidth = Math.Max(80, Math.Min(340, cardW - 200));
        }

        overlay.SizeChanged += (_, _) => LayoutSurfaces();
        return new(sourceId, overlay, view, address, status, back, forward, reload, progress);
    }
}
