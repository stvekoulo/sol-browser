using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.Web.WebView2.Core;

namespace Browser.App;

// Lecteur flottant : quand on quitte un onglet où une vidéo joue, sa vue reste affichée en petit, cadrée sur la vidéo,
// sous des commandes dessinées par le navigateur. La vidéo n'est ni copiée ni déplacée : c'est la page elle-même, que
// le moteur affiche recadrée (rien n'y est modifié, à part une marque sur la vidéo suivie).
//
// Mouvements : le lecteur monte du bas de la fenêtre à l'ouverture et y redescend à la fermeture, sa taille change en
// douceur, ses commandes apparaissent en fondu. La vue web n'est animée que par sa place et sa taille (jamais par la
// couche de composition : voir CornerClip). Rien ne bouge si Windows est réglé sans animations.
public sealed partial class PageHost
{
    private sealed record PipState(double X, double Y, double W, double H, int ViewWidth, int ViewHeight,
        string Title, bool Paused, bool Ended, double Time, double Duration, bool Call);

    private const double PipMin = 240, PipMax = 960, PipStep = 1.25, PipEdge = 18;
    private double PipWidth => Math.Clamp(settings.PipWidth, PipMin, Math.Max(PipMin, Math.Min(PipMax, container.ActualWidth - 2 * PipEdge)));

    private string? _pip;
    private CoreWebView2Frame? _pipFrame; // cadre (lecteur intégré d'un autre site) qui contient la vidéo ; null : la page elle-même
    private Grid? _pipChrome;
    private DispatcherTimer? _pipTimer;
    private PipState? _pipState;
    private string _pipRect = "";
    private double _pipAspect = 9.0 / 16, _pipShown; // _pipShown : largeur affichée en ce moment (elle glisse vers PipWidth)
    private double _pipDrop;                          // descente sous sa place, en unités d'affichage (ouverture, fermeture)
    private int _pipMissing, _pipMove;                // _pipMove : numéro du mouvement en cours (un nouveau annule l'ancien)
    private bool _pipClosing;
    private FontIcon? _pipPlay;
    private TextBlock? _pipTitle;
    private Border? _pipProgress;
    private double _dockHeight;

    // Cadres directs de chaque onglet : un lecteur intégré (YouTube dans un article…) vit dans l'un d'eux.
    private readonly Dictionary<string, List<CoreWebView2Frame>> _childFrames = new();

    private static readonly bool Animated = new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;

    /// <summary>Onglet affiché dans le lecteur flottant, ou null.</summary>
    public string? PipTab => _pip;
    /// <summary>L'utilisateur demande à revenir à l'onglet du lecteur flottant.</summary>
    public event Action<string>? PipReturnRequested;

    /// <summary>Hauteur de la carte des téléchargements (0 : masquée) : le lecteur se pose au-dessus au lieu de la recouvrir.</summary>
    public double DockHeight
    {
        get => _dockHeight;
        set { _dockHeight = value; PlacePip(); }
    }

    private void WatchFrames(string id, CoreWebView2 core)
    {
        var frames = _childFrames[id] = new List<CoreWebView2Frame>();
        core.FrameCreated += (_, e) =>
        {
            frames.Add(e.Frame);
            e.Frame.Destroyed += (_, _) => frames.Remove(e.Frame);
        };
    }

    // La plus grande vidéo en cours de lecture du document (ou celle déjà suivie, même en pause) : sa place dans la
    // fenêtre du document, et son état.
    private const string VideoProbe = """
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
            x: r.left, y: r.top, w: r.width, h: r.height, sx: scrollX, sy: scrollY, vw: innerWidth, vh: innerHeight,
            origin: location.origin, title: document.title, paused: video.paused, ended: video.ended,
            time: video.currentTime, duration: isFinite(video.duration) ? video.duration : 0, call: location.hostname === 'meet.google.com',
          };
        })()
        """;

    // La page et la place de ses cadres : pour situer dans la page une vidéo trouvée dans un cadre.
    private const string FramesProbe = """
        (() => ({
          sx: scrollX, sy: scrollY, vw: innerWidth, vh: innerHeight, title: document.title,
          frames: [...document.querySelectorAll('iframe')].map(f => {
            const r = f.getBoundingClientRect();
            let origin = '';
            try { origin = new URL(f.src, location.href).origin; } catch {}
            return { x: r.left + f.clientLeft, y: r.top + f.clientTop, w: f.clientWidth, h: f.clientHeight, origin };
          }),
        }))()
        """;

    private static async Task<JsonElement?> EvalAsync(Func<string, Windows.Foundation.IAsyncOperation<string>> run, string script)
    {
        try
        {
            using var doc = JsonDocument.Parse(await run(script));
            return doc.RootElement.ValueKind == JsonValueKind.Object ? doc.RootElement.Clone() : null;
        }
        catch (Exception e) when (e is InvalidOperationException or COMException or JsonException or ArgumentException) { return null; }
    }

    private static double Num(JsonElement e, string name) => e.GetProperty(name).GetDouble();

    private static PipState State(JsonElement v, double x, double y, int viewWidth, int viewHeight, string title) => new(
        x, y, Num(v, "w"), Num(v, "h"), viewWidth, viewHeight, title, v.GetProperty("paused").GetBoolean(), v.GetProperty("ended").GetBoolean(),
        Num(v, "time"), Num(v, "duration"), v.GetProperty("call").GetBoolean());

    // Cherche la vidéo : dans la page, sinon dans ses cadres (lecteur intégré venu d'un autre site). Une fois trouvée,
    // on ne regarde plus que là où elle est.
    // « known » : le cadre où la vidéo a déjà été trouvée (null : dans la page). « search » : premier relevé, on cherche partout.
    private async Task<(PipState State, CoreWebView2Frame? Frame)?> ProbePipAsync(string id, CoreWebView2 core, CoreWebView2Frame? known, bool search)
    {
        if (known is null && await EvalAsync(core.ExecuteScriptAsync, VideoProbe) is { } own)
            return (State(own, Num(own, "x") + Num(own, "sx"), Num(own, "y") + Num(own, "sy"), (int)Num(own, "vw"), (int)Num(own, "vh"), own.GetProperty("title").GetString() ?? ""), null);

        var candidates = known is not null ? [known] : search ? _childFrames.GetValueOrDefault(id, []).ToList() : [];
        foreach (var frame in candidates)
        {
            if (await EvalAsync(frame.ExecuteScriptAsync, VideoProbe) is not { } inner) continue;
            if (await EvalAsync(core.ExecuteScriptAsync, FramesProbe) is not { } page) return null;
            // Le cadre dans la page : celui qui a la taille de la fenêtre vue par le cadre, de préférence du même site.
            JsonElement? box = null;
            foreach (var candidate in page.GetProperty("frames").EnumerateArray())
            {
                if (Math.Abs(Num(candidate, "w") - Num(inner, "vw")) > 2 || Math.Abs(Num(candidate, "h") - Num(inner, "vh")) > 2) continue;
                box ??= candidate;
                if (candidate.GetProperty("origin").GetString() == inner.GetProperty("origin").GetString()) { box = candidate; break; }
            }
            if (box is not { } at) continue;
            return (State(inner, Num(at, "x") + Num(page, "sx") + Num(inner, "x"), Num(at, "y") + Num(page, "sy") + Num(inner, "y"),
                (int)Num(page, "vw"), (int)Num(page, "vh"), page.GetProperty("title").GetString() ?? ""), frame);
        }
        return null;
    }

    /// <summary>Passe l'onglet en lecteur flottant s'il joue une vidéo. Faux : rien à montrer.</summary>
    public async Task<bool> TryEnterPipAsync(string id)
    {
        if (!_views.TryGetValue(id, out var view) || view.CoreWebView2 is not { } core || _sims.ContainsKey(id) || _pip == id) return false;
        if (await ProbePipAsync(id, core, known: null, search: true) is not var (state, found) || !_views.ContainsKey(id)) return false;
        if (_pip is not null) ExitPip(pause: false);

        (_pip, _pipFrame, _pipState, _pipRect, _pipMissing, _pipClosing) = (id, found, state, "", 0, false);
        _pipAspect = Math.Clamp(state.H / state.W, 0.3, 1.4);
        _pipShown = PipWidth;
        _pipChrome = BuildPipChrome(id, core, state.Call);
        foreach (var part in new FrameworkElement[] { view, _pipChrome })
        {
            (part.HorizontalAlignment, part.VerticalAlignment) = (HorizontalAlignment.Right, VerticalAlignment.Bottom);
            Grid.SetColumn(part, 0);
            Grid.SetColumnSpan(part, 3);
            Canvas.SetZIndex(part, part == view ? 5 : 6);
        }
        // Il monte du bas de la fenêtre : posé d'abord sous le bord, puis glissé à sa place.
        _pipDrop = Animated ? _pipShown * _pipAspect + 40 : 0;
        PlacePip();
        view.Visibility = Visibility.Visible;
        container.Children.Add(_pipChrome);
        await FramePipAsync(core, state);
        _pipTimer ??= CreatePipTimer();
        _pipTimer.Start();
        RuntimeChanged?.Invoke();
        var move = ++_pipMove;
        var from = _pipDrop;
        await TweenAsync(300, t => { _pipDrop = from * (1 - t); PlacePip(); }, move);
        return true;
    }

    /// <summary>
    /// Ferme le lecteur flottant : la vue de l'onglet reprend sa place ordinaire (masquée ou affichée).
    /// <paramref name="slide"/> : il redescend d'abord sous le bord de la fenêtre.
    /// </summary>
    public async void ExitPip(bool pause, bool slide = false)
    {
        if (_pip is not { } id || _pipClosing) return;
        if (pause) EvalInPip("(v => v && v.pause())(document.querySelector('video[data-nav-pip]'))");
        if (slide && Animated)
        {
            _pipClosing = true;
            var from = _pipDrop;
            var to = _pipShown * _pipAspect + 40;
            await TweenAsync(220, t => { _pipDrop = from + (to - from) * t * t; PlacePip(); }, ++_pipMove); // il accélère en partant
            if (_pip != id) return;
        }
        EvalInPip("(v => v && v.removeAttribute('data-nav-pip'))(document.querySelector('video[data-nav-pip]'))");
        (_pip, _pipFrame, _pipState, _pipClosing, _pipDrop) = (null, null, null, false, 0);
        _pipMove++;
        _pipTimer?.Stop();
        if (_pipChrome is not null) container.Children.Remove(_pipChrome);
        _pipChrome = null;
        if (_views.TryGetValue(id, out var view))
        {
            (view.Width, view.Height, view.HorizontalAlignment, view.VerticalAlignment, view.Margin) =
                (double.NaN, double.NaN, HorizontalAlignment.Stretch, VerticalAlignment.Stretch, new Thickness(0));
            Grid.SetColumnSpan(view, 1);
            Canvas.SetZIndex(view, 0);
            CornerClip.Set(view, 0);
            if (view.CoreWebView2 is { } core) _ = RunPipAsync(core, "Emulation.clearDeviceMetricsOverride", "{}");
        }
        if (slide) RuntimeChanged?.Invoke(); // la vue redevient celle d'un onglet masqué
    }

    /// <summary>Agrandit (facteur &gt; 1) ou réduit le lecteur flottant, en douceur ; la taille choisie est retenue.</summary>
    public async void ResizePip(double factor)
    {
        if (_pip is not { } id || _pipClosing) return;
        settings.PipWidth = Math.Round(PipWidth * factor);
        settings.PipWidth = PipWidth; // ramenée dans les bornes, et dans la place disponible
        Browser.Core.JsonFileStore.Save(Browser.Core.AppPaths.Settings, settings);
        double from = _pipShown, to = PipWidth;
        var move = ++_pipMove;
        _pipDrop = 0;
        await TweenAsync(200, t => { _pipShown = from + (to - from) * t; PlacePip(); }, move);
        // La vidéo est recadrée à la nouvelle échelle une fois la taille atteinte (pendant le mouvement, l'image suit en gros).
        if (_pip == id && _pipState is { } state && Core(id) is { } core) { _pipRect = ""; await FramePipAsync(core, state); }
    }

    // Pose la vue et ses commandes : taille affichée, place choisie par l'utilisateur (gardée dans la zone des pages),
    // au-dessus de la carte des téléchargements si elle est là, et descente en cours.
    private void PlacePip()
    {
        if (_pip is not { } id || _pipChrome is null || !_views.TryGetValue(id, out var view)) return;
        double width = Math.Round(_pipShown), height = Math.Round(_pipShown * _pipAspect);
        var right = Math.Clamp(settings.PipRight, PipEdge, Math.Max(PipEdge, container.ActualWidth - width - PipEdge));
        var bottom = Math.Clamp(settings.PipBottom, PipEdge, Math.Max(PipEdge, container.ActualHeight - height - PipEdge));
        // La carte des téléchargements occupe le coin bas droit : le lecteur qui s'y trouve passe au-dessus.
        if (_dockHeight > 0 && right < 380 && bottom < _dockHeight + 2 * PipEdge) bottom = _dockHeight + 2 * PipEdge;
        foreach (var part in new FrameworkElement[] { view, _pipChrome })
            (part.Width, part.Height, part.Margin) = (width, height, new Thickness(0, 0, right, bottom - _pipDrop));
        view.UpdateLayout();
        CornerClip.Set(view, 16);
    }

    // Mouvement de 0 à 1, ralenti à l'arrivée. Un mouvement plus récent l'interrompt.
    private async Task TweenAsync(int milliseconds, Action<double> apply, int move)
    {
        if (!Animated) { apply(1); return; }
        var start = Environment.TickCount64;
        while (move == _pipMove)
        {
            var t = Math.Min(1, (Environment.TickCount64 - start) / (double)milliseconds);
            apply(1 - Math.Pow(1 - t, 4));
            if (t >= 1) return;
            await Task.Delay(12);
        }
    }

    private DispatcherTimer CreatePipTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += async (_, _) =>
        {
            if (_pipClosing) return;
            if (_pip is not { } id || Core(id) is not { } core) { ExitPip(pause: false); return; }
            var state = (await ProbePipAsync(id, core, _pipFrame, search: false))?.State;
            if (_pip != id || _pipClosing) return;
            // Vidéo disparue (page changée, appel terminé) ou lecture finie : le lecteur se referme de lui-même.
            if (state is null || state.Ended)
            {
                if (++_pipMissing >= 5) ExitPip(pause: false, slide: true);
                return;
            }
            _pipMissing = 0;
            await FramePipAsync(core, state);
        };
        return timer;
    }

    // Cadre le rendu de la page sur la vidéo, et met à jour les commandes. La page ne voit aucun changement de taille.
    private async Task FramePipAsync(CoreWebView2 core, PipState s)
    {
        _pipState = s;
        if (_pipPlay is not null) _pipPlay.Glyph = s.Paused ? "" : "";
        if (_pipTitle is not null) _pipTitle.Text = s.Title;
        if (_pipProgress is not null) _pipProgress.Width = s.Duration > 0 ? Math.Clamp(s.Time / s.Duration, 0, 1) * _pipShown : 0;
        var rect = $"{s.X:0},{s.Y:0},{s.W:0},{s.H:0},{PipWidth:0}";
        if (rect == _pipRect) return;
        _pipRect = rect;
        await RunPipAsync(core, "Emulation.setDeviceMetricsOverride", JsonSerializer.Serialize(new
        {
            width = s.ViewWidth, height = s.ViewHeight, deviceScaleFactor = 0, mobile = false,
            viewport = new { x = s.X, y = s.Y, width = s.W, height = s.H, scale = PipWidth / s.W },
        }));
    }

    // Agit sur la vidéo suivie, là où elle est : dans la page, ou dans le cadre du lecteur intégré.
    private async void EvalInPip(string script)
    {
        try
        {
            if (_pipFrame is { } frame) await frame.ExecuteScriptAsync(script);
            else if (_pip is { } id && Core(id) is { } core) await core.ExecuteScriptAsync(script);
        }
        catch (Exception e) { DebugLog.Write($"lecteur flottant : script refusé ({e.GetType().Name} {e.Message})"); } // vue ou cadre fermé entre-temps
    }

    private static async Task RunPipAsync(CoreWebView2 core, string method, string parameters)
    {
        try { await core.CallDevToolsProtocolMethodAsync(method, parameters); }
        catch (Exception e) when (e is InvalidOperationException or COMException or ArgumentException) { /* vue fermée entre-temps */ }
    }

    // Commandes du lecteur, par-dessus la vidéo : elles apparaissent en fondu au survol. Dessinées par le navigateur
    // (pilules sombres arrondies), elles agissent sur la vidéo de la page ; celles d'un appel envoient les raccourcis du
    // site. Le fond se tire à la souris pour déplacer le lecteur ; la molette change sa taille.
    private Grid BuildPipChrome(string id, CoreWebView2 core, bool call)
    {
        static SolidColorBrush Tone(byte a, byte v) => new(Windows.UI.Color.FromArgb(a, v, v, v));
        const string video = "document.querySelector('video[data-nav-pip]')";
        static string T(string fr, string en) => Browser.Core.Language.T(fr, en);

        Border Pill(string glyph, string tip, Action run, double size = 34)
        {
            var pill = new Border
            {
                Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = Tone(150, 20),
                BorderBrush = Tone(40, 255), BorderThickness = new Thickness(1),
                Child = new FontIcon { Glyph = glyph, FontSize = size * 0.4, Foreground = Tone(255, 255) },
                BackgroundTransition = new BrushTransition { Duration = TimeSpan.FromMilliseconds(120) },
            };
            ToolTipService.SetToolTip(pill, tip);
            pill.PointerEntered += (_, _) => pill.Background = Tone(215, 70);
            pill.PointerExited += (_, _) => pill.Background = Tone(150, 20);
            pill.PointerPressed += (_, e) => { e.Handled = true; pill.Background = Tone(235, 110); run(); };
            pill.PointerReleased += (_, _) => pill.Background = Tone(215, 70);
            return pill;
        }
        Border Jump(string text, string tip, int seconds) // saut dans la vidéo : le nombre de secondes en clair
        {
            var pill = Pill("", tip, () => EvalInPip($"{video}.currentTime += {seconds}"));
            pill.Child = new TextBlock { Text = text, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = Tone(255, 255), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            return pill;
        }
        void Key(string key, int code) // raccourci du site, avec Ctrl
        {
            foreach (var type in new[] { "keyDown", "keyUp" })
                _ = RunPipAsync(core, "Input.dispatchKeyEvent", JsonSerializer.Serialize(new { type, modifiers = 2, key, code = "Key" + key.ToUpperInvariant(), windowsVirtualKeyCode = code }));
        }

        var chrome = new Grid { Background = new SolidColorBrush(Colors.Transparent), CornerRadius = new CornerRadius(16) };
        var shade = new Grid { Opacity = 0, Background = Tone(96, 0), CornerRadius = new CornerRadius(16) };
        chrome.Children.Add(shade);
        // Fondu des commandes : un élément d'interface ordinaire, animé par le cadre d'interface.
        void Fade(double to)
        {
            if (!Animated) { shade.Opacity = to; return; }
            var fade = new DoubleAnimation { To = to, Duration = TimeSpan.FromMilliseconds(to > 0 ? 160 : 240), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(fade, shade);
            Storyboard.SetTargetProperty(fade, "Opacity");
            var board = new Storyboard();
            board.Children.Add(fade);
            board.Begin();
        }
        chrome.PointerEntered += (_, _) => Fade(1);
        chrome.PointerExited += (_, _) => Fade(0);
        chrome.PointerWheelChanged += (_, e) =>
        {
            e.Handled = true;
            ResizePip(e.GetCurrentPoint(chrome).Properties.MouseWheelDelta > 0 ? PipStep : 1 / PipStep);
        };
        chrome.PointerPressed += (_, e) => { if (e.GetCurrentPoint(chrome).Properties.IsLeftButtonPressed) DragPip(); };

        shade.Children.Add(_pipTitle = new TextBlock
        {
            FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = Tone(255, 255), TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(14, 13, 156, 0), VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false,
        });
        var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 8, 8, 0) };
        top.Children.Add(Pill("", T("Réduire le lecteur", "Make the player smaller"), () => ResizePip(1 / PipStep), 30));
        top.Children.Add(Pill("", T("Agrandir le lecteur", "Make the player larger"), () => ResizePip(PipStep), 30));
        top.Children.Add(Pill("", T("Revenir à l'onglet", "Back to the tab"), () => PipReturnRequested?.Invoke(id), 30));
        top.Children.Add(Pill("", call ? T("Fermer le lecteur", "Close the player") : T("Fermer et mettre en pause", "Close and pause"), () => ExitPip(pause: !call, slide: true), 30));
        shade.Children.Add(top);

        var middle = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _pipPlay = null;
        if (call)
        {
            middle.Children.Add(Pill("", T("Couper ou rétablir le micro", "Mute or unmute the microphone"), () => Key("d", 68), 42));
            middle.Children.Add(Pill("", T("Couper ou rétablir la caméra", "Turn the camera off or on"), () => Key("e", 69), 42));
        }
        else
        {
            middle.Children.Add(Jump("−10", T("Reculer de 10 secondes", "Back 10 seconds"), -10));
            var play = Pill("", T("Lecture ou pause", "Play or pause"), () =>
            {
                EvalInPip($"(v => v.paused ? v.play() : v.pause())({video})");
                if (_pipPlay is not null) _pipPlay.Glyph = _pipPlay.Glyph == "" ? "" : ""; // réponse immédiate, confirmée au prochain relevé
            }, 46);
            _pipPlay = (FontIcon)play.Child;
            middle.Children.Add(play);
            middle.Children.Add(Jump("+10", T("Avancer de 10 secondes", "Forward 10 seconds"), 10));
        }
        shade.Children.Add(middle);

        // Avancement de la lecture : un trait fin en bas, toujours visible.
        chrome.Children.Add(_pipProgress = new Border
        {
            Height = 3, Width = 0, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
            Background = Tone(230, 255), CornerRadius = new CornerRadius(1.5), IsHitTestVisible = false,
        });
        return chrome;
    }

    // Déplacement du lecteur : le fond des commandes est tiré à la souris. La souris est suivie directement tant que
    // le bouton est enfoncé (comme pour le séparateur de la vue scindée) ; la place est retenue au relâchement.
    private DispatcherTimer? _pipDrag;

    private void DragPip()
    {
        if (_pip is null || _pipClosing || !GetCursorPos(out var origin)) return;
        double right = settings.PipRight, bottom = settings.PipBottom;
        _pipMove++; // arrête un mouvement en cours
        _pipDrop = 0;
        _pipDrag?.Stop();
        _pipDrag = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _pipDrag.Tick += (_, _) =>
        {
            var button = GetSystemMetrics(23 /* SM_SWAPBUTTON */) != 0 ? 0x02 : 0x01;
            if (_pip is null || (GetAsyncKeyState(button) & 0x8000) == 0)
            {
                _pipDrag?.Stop();
                (settings.PipRight, settings.PipBottom) = (_pipChrome?.Margin.Right ?? PipEdge, _pipChrome?.Margin.Bottom ?? PipEdge);
                Browser.Core.JsonFileStore.Save(Browser.Core.AppPaths.Settings, settings);
                return;
            }
            if (!GetCursorPos(out var point)) return;
            var scale = container.XamlRoot?.RasterizationScale ?? 1;
            (settings.PipRight, settings.PipBottom) = (right - (point.X - origin.X) / scale, bottom - (point.Y - origin.Y) / scale);
            PlacePip();
        };
        _pipDrag.Start();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint { public int X, Y; }

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out CursorPoint point);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
}
