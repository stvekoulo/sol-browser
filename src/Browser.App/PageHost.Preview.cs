using Browser.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.Web.WebView2.Core;
using Windows.System;
using Windows.UI;

namespace Browser.App;

public sealed partial class PageHost
{
    private sealed record PreviewSurface(string SourceId, Grid Overlay, WebView2 View, TextBlock Status, ProgressBar Progress)
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
        var preview = CreatePreview(sourceId); preview.Url = url; _preview = preview;
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

        var rail = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        overlay.Children.Add(rail); Canvas.SetZIndex(rail, 2);

        void AnimateProp(DependencyObject target, string prop, double to, int ms = 160)
        {
            var anim = new DoubleAnimation { To = to, Duration = TimeSpan.FromMilliseconds(ms), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(anim, target);
            Storyboard.SetTargetProperty(anim, prop);
            var sb = new Storyboard();
            sb.Children.Add(anim);
            sb.Begin();
        }

        Button CreateRailButton(string label, string pathData, Action run, Action<TransformGroup, bool> onHover)
        {
            var pathIcon = (PathIcon)XamlReader.Load($"<PathIcon xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Data='{pathData}' />");
            pathIcon.Foreground = ink;
            var transforms = new TransformGroup();
            var scale = new ScaleTransform { ScaleX = 1, ScaleY = 1, CenterX = 10, CenterY = 10 };
            var rotate = new RotateTransform { Angle = 0, CenterX = 10, CenterY = 10 };
            var translate = new TranslateTransform { X = 0, Y = 0 };
            transforms.Children.Add(scale);
            transforms.Children.Add(rotate);
            transforms.Children.Add(translate);
            pathIcon.RenderTransform = transforms;

            var btnScale = new ScaleTransform { ScaleX = 1, ScaleY = 1, CenterX = 19, CenterY = 19 };
            var button = new Button
            {
                Content = pathIcon,
                Width = 38,
                Height = 38,
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(0),
                Background = buttonBg,
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1),
                Shadow = new ThemeShadow(),
                Translation = new System.Numerics.Vector3(0, 0, 16),
                RenderTransform = btnScale
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label);
            ToolTipService.SetToolTip(button, label);
            button.Click += (_, _) => run();
            button.PointerEntered += (_, _) => { button.Background = hoverBg; onHover(transforms, true); };
            button.PointerExited += (_, _) => { button.Background = buttonBg; onHover(transforms, false); };
            button.PointerPressed += (_, _) => { AnimateProp(btnScale, "ScaleX", 0.92, 80); AnimateProp(btnScale, "ScaleY", 0.92, 80); };
            button.PointerReleased += (_, _) => { AnimateProp(btnScale, "ScaleX", 1.0, 120); AnimateProp(btnScale, "ScaleY", 1.0, 120); };
            button.PointerCaptureLost += (_, _) => { AnimateProp(btnScale, "ScaleX", 1.0, 120); AnimateProp(btnScale, "ScaleY", 1.0, 120); };
            rail.Children.Add(button);
            return button;
        }

        CreateRailButton(Language.T("Fermer l’aperçu (Échap)", "Close preview (Escape)"),
            "M 4 4 L 16 16 M 16 4 L 4 16", ClosePreview,
            (t, hover) => { AnimateProp(t.Children[1], "Angle", hover ? 90 : 0, 200); AnimateProp(t.Children[0], "ScaleX", hover ? 1.12 : 1, 160); AnimateProp(t.Children[0], "ScaleY", hover ? 1.12 : 1, 160); });

        CreateRailButton(Language.T("Ouvrir dans un nouvel onglet", "Open in a new tab"),
            "M 9 3 H 4 C 3.4 3 3 3.4 3 4 v 10 c 0 .6 .4 1 1 1 h 10 c .6 0 1 -.4 1 -1 V 9 M 11 3 h 6 v 6 M 17 3 L 8 12", () => PromotePreview(false),
            (t, hover) => { AnimateProp(t.Children[2], "X", hover ? 1.5 : 0, 160); AnimateProp(t.Children[2], "Y", hover ? -1.5 : 0, 160); AnimateProp(t.Children[0], "ScaleX", hover ? 1.08 : 1, 160); AnimateProp(t.Children[0], "ScaleY", hover ? 1.08 : 1, 160); });

        CreateRailButton(Language.T("Ouvrir à côté", "Open beside"),
            "M 3 3 h 14 v 14 H 3 Z M 10 3 v 14", () => PromotePreview(true),
            (t, hover) => { AnimateProp(t.Children[0], "ScaleX", hover ? 1.18 : 1, 160); AnimateProp(t.Children[0], "ScaleY", hover ? 1.05 : 1, 160); });

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
        }

        overlay.SizeChanged += (_, _) => LayoutSurfaces();
        return new(sourceId, overlay, view, status, progress);
    }
}
