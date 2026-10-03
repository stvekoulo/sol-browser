using System.Text.Json;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;

namespace Browser.App;

// Lecteur flottant : quand on quitte un onglet où une vidéo joue, sa vue reste affichée en petit, en bas à droite,
// cadrée sur la vidéo, sous des commandes dessinées par le navigateur. La vidéo n'est ni copiée ni déplacée : c'est
// la page elle-même, que le moteur affiche recadrée (rien n'y est modifié, à part une marque sur la vidéo suivie).
public sealed partial class PageHost
{
    private const double PipMin = 240, PipMax = 960, PipStep = 1.25;
    private double PipWidth => Math.Clamp(settings.PipWidth, PipMin, Math.Max(PipMin, Math.Min(PipMax, container.ActualWidth - 36)));
    private double _pipAspect = 9.0 / 16;
    private JsonElement? _pipState;
    private string? _pip;
    private Grid? _pipChrome;
    private DispatcherTimer? _pipTimer;
    private string _pipRect = "";
    private int _pipMissing;
    private FontIcon? _pipPlay;
    private TextBlock? _pipTitle;
    private Border? _pipProgress;

    /// <summary>Onglet affiché dans le lecteur flottant, ou null.</summary>
    public string? PipTab => _pip;
    /// <summary>L'utilisateur demande à revenir à l'onglet du lecteur flottant.</summary>
    public event Action<string>? PipReturnRequested;

    // La plus grande vidéo en cours de lecture de la page (ou celle déjà suivie, même en pause) : sa place dans la page,
    // et son état. Les vidéos d'un cadre venu d'un autre site ne sont pas visibles d'ici.
    private const string PipProbe = """
        (() => {
          const tracked = document.querySelector('video[data-nav-pip]');
          const playing = [...document.querySelectorAll('video')].filter(v => !v.paused && v.readyState > 2 && v.videoWidth > 0);
          const area = v => { const r = v.getBoundingClientRect(); return r.width * r.height; };
          const video = tracked && tracked.isConnected && area(tracked) > 0 ? tracked : playing.sort((a, b) => area(b) - area(a))[0];
          if (!video) return null;
          const r = video.getBoundingClientRect();
          if (r.width < 120 || r.height < 60) return null;
          video.setAttribute('data-nav-pip', '');
          return {
            x: Math.round(r.left + scrollX), y: Math.round(r.top + scrollY), w: Math.round(r.width), h: Math.round(r.height),
            vw: innerWidth, vh: innerHeight, title: document.title, paused: video.paused, ended: video.ended,
            time: video.currentTime, duration: isFinite(video.duration) ? video.duration : 0, call: location.hostname === 'meet.google.com',
          };
        })()
        """;

    /// <summary>Passe l'onglet en lecteur flottant s'il joue une vidéo. Faux : rien à montrer.</summary>
    public async Task<bool> TryEnterPipAsync(string id)
    {
        if (!_views.TryGetValue(id, out var view) || view.CoreWebView2 is not { } core || _sims.ContainsKey(id)) return false;
        if (await ProbePipAsync(core) is not { } state) return false;
        if (!_views.ContainsKey(id)) return false;
        if (_pip is not null) ExitPip(pause: false);

        _pip = id;
        _pipRect = "";
        _pipMissing = 0;
        _pipAspect = Math.Clamp(state.GetProperty("h").GetDouble() / state.GetProperty("w").GetDouble(), 0.3, 1.4);
        var call = state.GetProperty("call").GetBoolean();
        foreach (var part in new FrameworkElement[] { view, _pipChrome = BuildPipChrome(id, core, call) })
        {
            (part.Width, part.Height, part.HorizontalAlignment, part.VerticalAlignment, part.Margin) =
                (PipWidth, Math.Round(PipWidth * _pipAspect), HorizontalAlignment.Right, VerticalAlignment.Bottom, new Thickness(0, 0, 18, 18));
            Grid.SetColumn(part, 0);
            Grid.SetColumnSpan(part, 3);
            Canvas.SetZIndex(part, part == view ? 5 : 6);
        }
        view.Visibility = Visibility.Visible;
        container.Children.Add(_pipChrome);
        view.UpdateLayout();
        CornerClip.Set(view, 16);
        await FramePipAsync(core, state);

        _pipTimer ??= CreatePipTimer();
        _pipTimer.Start();
        RuntimeChanged?.Invoke();
        return true;
    }

    /// <summary>Agrandit (facteur &gt; 1) ou réduit le lecteur flottant ; la taille choisie est retenue.</summary>
    public async void ResizePip(double factor)
    {
        if (_pip is not { } id || !_views.TryGetValue(id, out var view) || _pipChrome is null) return;
        settings.PipWidth = Math.Round(PipWidth * factor);
        settings.PipWidth = PipWidth; // ramenée dans les bornes, et dans la place disponible
        Browser.Core.JsonFileStore.Save(Browser.Core.AppPaths.Settings, settings);
        foreach (var part in new FrameworkElement[] { view, _pipChrome })
            (part.Width, part.Height) = (PipWidth, Math.Round(PipWidth * _pipAspect));
        view.UpdateLayout();
        CornerClip.Set(view, 16);
        _pipRect = ""; // l'échelle a changé : la vidéo est recadrée à la nouvelle taille
        if (_pipState is { } state && view.CoreWebView2 is { } core) await FramePipAsync(core, state);
    }

    /// <summary>Ferme le lecteur flottant : la vue de l'onglet reprend sa place ordinaire (masquée ou affichée).</summary>
    public void ExitPip(bool pause)
    {
        if (_pip is not { } id) return;
        DebugLog.Write($"lecteur flottant fermé ({new System.Diagnostics.StackTrace().GetFrame(1)?.GetMethod()?.Name})");
        _pip = null;
        _pipTimer?.Stop();
        if (_pipChrome is not null) container.Children.Remove(_pipChrome);
        _pipChrome = null;
        if (!_views.TryGetValue(id, out var view)) return;
        (view.Width, view.Height, view.HorizontalAlignment, view.VerticalAlignment, view.Margin) =
            (double.NaN, double.NaN, HorizontalAlignment.Stretch, VerticalAlignment.Stretch, new Thickness(0));
        Grid.SetColumnSpan(view, 1);
        Canvas.SetZIndex(view, 0);
        CornerClip.Set(view, 0);
        if (view.CoreWebView2 is not { } core) return;
        _ = RunPipAsync(core, "Emulation.clearDeviceMetricsOverride", "{}");
        _ = EvalPipAsync(core, $"(() => {{ const v = document.querySelector('video[data-nav-pip]'); if (v) {{ {(pause ? "v.pause();" : "")} v.removeAttribute('data-nav-pip'); }} }})()");
    }

    private DispatcherTimer CreatePipTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += async (_, _) =>
        {
            if (_pip is not { } id || Core(id) is not { } core) { ExitPip(pause: false); return; }
            var state = await ProbePipAsync(core);
            if (_pip != id) return;
            // Vidéo disparue (page changée, appel terminé) ou lecture finie : le lecteur se referme de lui-même.
            if (state is not { } s || s.GetProperty("ended").GetBoolean())
            {
                if (++_pipMissing >= 5) { ExitPip(pause: false); RuntimeChanged?.Invoke(); }
                return;
            }
            _pipMissing = 0;
            await FramePipAsync(core, s);
        };
        return timer;
    }

    // Cadre le rendu de la page sur la vidéo, et met à jour les commandes. La page ne voit aucun changement de taille.
    private async Task FramePipAsync(CoreWebView2 core, JsonElement s)
    {
        _pipState = s;
        if (_pipPlay is not null) _pipPlay.Glyph = s.GetProperty("paused").GetBoolean() ? "" : "";
        if (_pipTitle is not null) _pipTitle.Text = s.GetProperty("title").GetString() ?? "";
        if (_pipProgress is not null)
        {
            var duration = s.GetProperty("duration").GetDouble();
            _pipProgress.Width = duration > 0 ? Math.Clamp(s.GetProperty("time").GetDouble() / duration, 0, 1) * PipWidth : 0;
        }
        double x = s.GetProperty("x").GetDouble(), y = s.GetProperty("y").GetDouble(), w = s.GetProperty("w").GetDouble(), h = s.GetProperty("h").GetDouble();
        var rect = $"{x},{y},{w},{h},{PipWidth}";
        if (rect == _pipRect) return;
        _pipRect = rect;
        await RunPipAsync(core, "Emulation.setDeviceMetricsOverride", JsonSerializer.Serialize(new
        {
            width = s.GetProperty("vw").GetInt32(), height = s.GetProperty("vh").GetInt32(), deviceScaleFactor = 0, mobile = false,
            viewport = new { x, y, width = w, height = h, scale = PipWidth / w },
        }));
    }

    private static async Task<JsonElement?> ProbePipAsync(CoreWebView2 core)
    {
        try
        {
            var raw = await core.ExecuteScriptAsync(PipProbe);
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) DebugLog.Write($"lecteur flottant : sonde vide ({raw})");
            return doc.RootElement.ValueKind == JsonValueKind.Object ? doc.RootElement.Clone() : null;
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException or JsonException) { DebugLog.Write($"lecteur flottant : sonde en échec ({e.Message})"); return null; }
    }

    private static async Task EvalPipAsync(CoreWebView2 core, string script)
    {
        try { await core.ExecuteScriptAsync(script); }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException) { /* vue fermée entre-temps */ }
    }

    private static async Task RunPipAsync(CoreWebView2 core, string method, string parameters)
    {
        try { await core.CallDevToolsProtocolMethodAsync(method, parameters); }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException or ArgumentException) { /* vue fermée entre-temps */ }
    }

    // Commandes du lecteur, par-dessus la vidéo : elles apparaissent au survol. Dessinées par le navigateur (pilules
    // sombres arrondies), elles agissent sur la vidéo de la page ; celles d'un appel envoient les raccourcis du site.
    private Grid BuildPipChrome(string id, CoreWebView2 core, bool call)
    {
        static SolidColorBrush Tone(byte a, byte v) => new(Windows.UI.Color.FromArgb(a, v, v, v));
        const string video = "document.querySelector('video[data-nav-pip]')";

        Border Pill(string glyph, string tip, Action run, double size = 34)
        {
            var pill = new Border
            {
                Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = Tone(150, 20),
                BorderBrush = Tone(40, 255), BorderThickness = new Thickness(1),
                Child = new FontIcon { Glyph = glyph, FontSize = size * 0.4, Foreground = Tone(255, 255) },
            };
            ToolTipService.SetToolTip(pill, tip);
            pill.PointerEntered += (_, _) => pill.Background = Tone(210, 60);
            pill.PointerExited += (_, _) => pill.Background = Tone(150, 20);
            pill.PointerPressed += (_, e) => { e.Handled = true; run(); };
            return pill;
        }
        Border Jump(string text, string tip, int seconds) // saut dans la vidéo : le nombre de secondes en clair
        {
            var pill = Pill("", tip, () => _ = EvalPipAsync(core, $"{video}.currentTime += {seconds}"));
            pill.Child = new TextBlock { Text = text, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = Tone(255, 255), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            return pill;
        }
        void Key(string key, int code) // raccourci du site, avec Ctrl
        {
            foreach (var type in new[] { "keyDown", "keyUp" })
                _ = RunPipAsync(core, "Input.dispatchKeyEvent", JsonSerializer.Serialize(new { type, modifiers = 2, key, code = "Key" + key.ToUpperInvariant(), windowsVirtualKeyCode = code }));
        }

        var chrome = new Grid { Background = new SolidColorBrush(Colors.Transparent), CornerRadius = new CornerRadius(16) };
        var shade = new Grid { Opacity = 0, Background = Tone(90, 0), CornerRadius = new CornerRadius(16) };
        chrome.Children.Add(shade);
        chrome.PointerEntered += (_, _) => shade.Opacity = 1;
        chrome.PointerExited += (_, _) => shade.Opacity = 0;
        // La molette, sur le lecteur, l'agrandit ou le réduit.
        chrome.PointerWheelChanged += (_, e) =>
        {
            e.Handled = true;
            ResizePip(e.GetCurrentPoint(chrome).Properties.MouseWheelDelta > 0 ? PipStep : 1 / PipStep);
        };

        shade.Children.Add(_pipTitle = new TextBlock
        {
            FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = Tone(255, 255), TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(14, 12, 168, 0), VerticalAlignment = VerticalAlignment.Top,
        });
        var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 8, 8, 0) };
        top.Children.Add(Pill("", Browser.Core.Language.T("Réduire le lecteur", "Make the player smaller"), () => ResizePip(1 / PipStep), 30));
        top.Children.Add(Pill("", Browser.Core.Language.T("Agrandir le lecteur", "Make the player larger"), () => ResizePip(PipStep), 30));
        top.Children.Add(Pill("", Browser.Core.Language.T("Revenir à l'onglet", "Back to the tab"), () => PipReturnRequested?.Invoke(id), 30));
        top.Children.Add(Pill("", Browser.Core.Language.T("Fermer et mettre en pause", "Close and pause"), () => { ExitPip(pause: !call); RuntimeChanged?.Invoke(); }, 30));
        shade.Children.Add(top);

        var middle = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        if (call)
        {
            middle.Children.Add(Pill("", Browser.Core.Language.T("Couper ou rétablir le micro", "Mute or unmute the microphone"), () => Key("d", 68), 42));
            middle.Children.Add(Pill("", Browser.Core.Language.T("Couper ou rétablir la caméra", "Turn the camera off or on"), () => Key("e", 69), 42));
        }
        else
        {
            middle.Children.Add(Jump("−10", Browser.Core.Language.T("Reculer de 10 secondes", "Back 10 seconds"), -10));
            var play = Pill("", Browser.Core.Language.T("Lecture ou pause", "Play or pause"), () => _ = EvalPipAsync(core, $"(v => v.paused ? v.play() : v.pause())({video})"), 46);
            _pipPlay = (FontIcon)play.Child;
            middle.Children.Add(play);
            middle.Children.Add(Jump("+10", Browser.Core.Language.T("Avancer de 10 secondes", "Forward 10 seconds"), 10));
        }
        if (call) _pipPlay = null;
        shade.Children.Add(middle);

        // Avancement de la lecture : un trait fin en bas, toujours visible.
        chrome.Children.Add(_pipProgress = new Border
        {
            Height = 3, Width = 0, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
            Background = Tone(230, 255), CornerRadius = new CornerRadius(1.5), Margin = new Thickness(0, 0, 0, 0),
        });
        return chrome;
    }
}
