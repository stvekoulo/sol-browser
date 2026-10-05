using System.Text.Json;
using Browser.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace Browser.App;

// Vue d'interface : synchronise la barre, la sidebar et adapte la couleur de coque à la page active.
public sealed partial class UiBridge(WebView2 view, string page = "index.html", WebView2? sidebar = null, ColumnDefinition? sidebarColumn = null, PageHost? pages = null)
{
    private const string Origin = "https://ui.navigateur/";
    // Lit les couleurs de la page active : son fond, et sa couleur d'accent (celle qui fait son identité).
    // L'accent vient, dans l'ordre : de la balise theme-color du site, puis de la couleur vive la plus présente
    // parmi son en-tête, ses boutons et ses liens. Un site sans couleur vive n'a pas d'accent.
    private const string ThemeProbeScript = """
        (() => {
          // Onglet vide : fond du navigateur. Une page d'erreur du navigateur (même adresse) garde ses propres couleurs.
          if (location.href === 'about:blank' && !document.documentElement.hasAttribute('data-nav-error')) {
            document.documentElement.style.background = '#eef0e2';
            return { bg: 'rgb(238, 240, 226)', dark: false, accent: null };
          }
          const parse = s => {
            const m = s && s.match(/rgba?\((\d+),\s*(\d+),\s*(\d+)(?:,\s*([\d.]+))?\)/);
            return m ? [+m[1], +m[2], +m[3], m[4] === undefined ? 1 : +m[4]] : null;
          };
          // Résout n'importe quelle couleur CSS (nom, hex, hsl…) sans rien ajouter à la page.
          const canvas = document.createElement('canvas');
          canvas.width = canvas.height = 1;
          const ctx = canvas.getContext('2d', { willReadFrequently: true });
          const resolve = css => {
            if (!css || !ctx || !CSS.supports('color', css)) return null;
            const rgb = parse(css);
            if (rgb) return rgb;
            // Les sites modernes renvoient oklch(), color(), lab()… même pour leurs fonds noirs.
            // Le moteur convertit la couleur en sRGB ; la méthode de sélection du fond reste identique.
            ctx.clearRect(0, 0, 1, 1);
            ctx.fillStyle = css; ctx.fillRect(0, 0, 1, 1);
            const [r, g, b, a] = ctx.getImageData(0, 0, 1, 1).data;
            return [r, g, b, a / 255];
          };
          const lum = ([r, g, b]) => (0.299 * r + 0.587 * g + 0.114 * b) / 255;
          const sat = ([r, g, b]) => { const max = Math.max(r, g, b), min = Math.min(r, g, b); return max === 0 ? 0 : (max - min) / max; };

          if (document.readyState === 'loading') return null;
          const els = [];
          for (const x of [window.innerWidth * 0.5, window.innerWidth * 0.25, 24]) {
            let el = document.elementFromPoint(x, 12);
            while (el) { els.push(el); el = el.parentElement; }
          }
          els.push(document.querySelector('ytd-app'), document.querySelector('main'), document.body, document.documentElement);
          let bg = [255, 255, 255];
          for (const el of els) {
            if (!el) continue;
            const c = resolve(getComputedStyle(el).backgroundColor);
            if (c && c[3] > 0.15) { bg = c.slice(0, 3); break; }
          }

          // Couleur d'accent : chaque couleur vive rencontrée reçoit un poids, la plus lourde l'emporte.
          const votes = new Map();
          const vote = (c, weight) => {
            if (!c || c[3] < 0.5 || sat(c) < 0.35 || lum(c) < 0.12 || lum(c) > 0.92) return;
            const key = c.slice(0, 3).map(v => Math.round(v / 24) * 24).join(',');
            const entry = votes.get(key) || { c: c.slice(0, 3), w: 0 };
            entry.w += weight * (0.5 + sat(c));
            votes.set(key, entry);
          };
          const meta = document.querySelector('meta[name="theme-color"]');
          if (meta) vote(resolve(meta.content), 12);
          if (sat(bg) >= 0.35) vote([...bg, 1], 10); // une page déjà colorée est sa propre couleur
          const sample = (selector, weight, limit) => {
            let n = 0;
            for (const el of document.querySelectorAll(selector)) {
              if (n >= limit) break;
              const box = el.getBoundingClientRect();
              if (box.width < 8 || box.height < 8 || box.bottom < 0 || box.top > innerHeight) continue;
              n++;
              const style = getComputedStyle(el);
              vote(resolve(style.backgroundColor), weight * Math.min(3, (box.width * box.height) / 4000 + 0.5));
              vote(resolve(style.color), weight * 0.4);
              if (el instanceof SVGElement) vote(resolve(style.fill), weight * 0.6);
            }
          };
          sample('header, nav, [role="banner"]', 3, 6);
          sample('button, [role="button"], input[type="submit"], a[class*="btn"], a[class*="button"]', 2, 24);
          sample('header svg, nav svg, [class*="logo"] svg, [class*="logo"]', 2, 8);
          sample('a[href]', 1, 30);
          let best = null;
          for (const entry of votes.values()) if (!best || entry.w > best.w) best = entry;

          const rgb = c => `rgb(${c[0]}, ${c[1]}, ${c[2]})`;
          return { bg: rgb(bg), dark: lum(bg) < 0.42, accent: best ? rgb(best.c) : null };
        })()
        """;

    private static string? s_lastThemeMsg;
    private static (string Bg, bool Dark, string? Accent)? s_lastProbe;

    /// <summary>Réglages lus pour savoir où la couleur du site doit teinter l'interface.</summary>
    public static Settings? Settings { get; set; }

    public static Windows.UI.Color InitialBackground => Settings?.Theme == "dark" || (Settings?.Theme == "system" && IsSystemDark())
        ? Windows.UI.Color.FromArgb(255, 28, 28, 30) : Windows.UI.Color.FromArgb(255, 238, 240, 226);

    // Thème de Windows, lu par l'hôte : une vue d'interface masquée ne le suit pas de façon fiable par elle-même.
    private static readonly Windows.UI.ViewManagement.UISettings s_system = new();

    private static bool IsSystemDark()
    {
        var c = s_system.GetColorValue(Windows.UI.ViewManagement.UIColorType.Background);
        return c.R + c.G + c.B < 384;
    }

    /// <summary>Prévient quand Windows change de thème ou de couleurs (appelé hors du fil de l'interface).</summary>
    public static event Action? SystemColorsChanged;

    static UiBridge() => s_system.ColorValuesChanged += (_, _) => SystemColorsChanged?.Invoke();

    /// <summary>Envoie les couleurs de la page active à toutes les vues d'interface, avec les réglages de teinte.</summary>
    public void BroadcastTheme()
    {
        var on = Settings;
        if (pages is not null && _activeId is not null && pages.CoreOf(_activeId) is { Source: Settings.DefaultHomePage })
        {
            var wall = on?.StartWallpaper == true;
            var darkStart = wall || on?.Theme == "dark" || (on?.Theme == "system" && IsSystemDark());
            s_lastProbe = (wall ? "rgb(40, 68, 105)" : darkStart ? "rgb(9, 9, 11)" : "rgb(238, 240, 226)", darkStart, null);
        }
        if (s_lastProbe is not var (bg, dark, accent)) return;
        accent = AmbientAccent ?? accent; // ce que la page montre à l'instant, sinon la couleur annoncée par le site
        s_lastThemeMsg = JsonSerializer.Serialize(new
        {
            @event = "theme", bg, dark, accent, mode = on?.Theme ?? "page", systemDark = IsSystemDark(), lang = Language.Current,
            privateMode = AppPaths.Private,
            font = on?.Font ?? "default",
            uiScale = on?.UiFactor ?? 0.92,
            tint = new { topbar = on?.TintTopbar ?? true, sidebar = on?.TintSidebar ?? true, menus = on?.TintMenus ?? true, background = on?.TintBackground ?? true },
        });
        view.CoreWebView2?.PostWebMessageAsJson(s_lastThemeMsg);
        sidebar?.CoreWebView2?.PostWebMessageAsJson(s_lastThemeMsg);
        foreach (var floating in _floating) floating.Post(s_lastThemeMsg);
        ThemePublished?.Invoke(s_lastThemeMsg);
    }
    public event Action<string>? ThemePublished;
    private string? _activeId;
    private readonly List<UiBridge> _floating = new(); // vues flottantes qui suivent la couleur de la page : menus, encart

    public void AttachPopup(UiBridge floatingBridge) => _floating.Add(floatingBridge);

    /// <summary>Couleur de fond de la page active (rouge, vert, bleu).</summary>
    public event Action<byte, byte, byte>? PageColorChanged;

    public async Task InitAsync(string uiFolder, Action<Command> onCommand)
    {
        var initial = InitialBackground;
        s_lastProbe ??= ($"rgb({initial.R}, {initial.G}, {initial.B})", initial.R < 128, null);
        BroadcastTheme();
        await InitViewAsync(view, page, uiFolder, onCommand);
        if (sidebar is not null)
            await InitViewAsync(sidebar, "index.html#sidebar", uiFolder, onCommand);

        if (pages is not null)
        {
            pages.Loaded += _ => ProbeActiveThemeAsync(withDelays: true);
            pages.PageChanged += (_, _, _) => ProbeActiveThemeAsync(withDelays: false);
            StartAmbient();
        }
    }

    private async Task InitViewAsync(WebView2 target, string targetPage, string uiFolder, Action<Command> onCommand)
    {
        // Fond transparent tant que la vue n'a rien dessiné (apparition, changement de taille) : on voit le fond de
        // la fenêtre, aux couleurs de la page, au lieu d'un rectangle noir ou blanc.
        target.Opacity = 0; // aucune surface du moteur avant la première peinture React
        target.DefaultBackgroundColor = Microsoft.UI.Colors.Transparent;
        var environment = await AdBlock.EnvironmentAsync();
        var options = environment.CreateCoreWebView2ControllerOptions();
        options.ProfileName = "SolInterface"; // aucune extension de page ne peut injecter du code dans l’interface privilégiée
        await target.EnsureCoreWebView2Async(environment, options);
        var core = target.CoreWebView2;
        core.SetVirtualHostNameToFolderMapping("ui.navigateur", uiFolder, CoreWebView2HostResourceAccessKind.Deny);
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;

        // L'interface ne quitte jamais ses fichiers locaux.
        core.NavigationStarting += (_, e) => { if (!e.Uri.StartsWith(Origin, StringComparison.Ordinal)) e.Cancel = true; };
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.NavigationCompleted += (_, _) => { if (s_lastThemeMsg is not null) core.PostWebMessageAsJson(s_lastThemeMsg); };

        core.WebMessageReceived += (_, e) =>
        {
            string json;
            try { json = e.TryGetWebMessageAsString(); }
            catch (ArgumentException) { return; }
            if (!e.Source.StartsWith(Origin, StringComparison.Ordinal)) return;

            if (json == "\"ui.painted\"") { target.Opacity = 1; return; }

            if (SidebarHost is { } host && json.Contains("\"sidebar.toggle\"", StringComparison.Ordinal))
            {
                var open = host.Toggle();
                view.CoreWebView2?.PostWebMessageAsJson($$"""{"event":"sidebar","open":{{(open ? "true" : "false")}}}""");
                return;
            }
            if (CommandParser.Parse(json) is { } command)
                onCommand(command);
        };

        core.Navigate(Origin + targetPage);
    }

    public void FocusAddress()
    {
        view.Focus(FocusState.Programmatic);
        view.CoreWebView2?.PostWebMessageAsJson("""{"event":"focus-address"}""");
    }

    /// <summary>Carte latérale pilotée par le bouton de la barre.</summary>
    public SidebarHost? SidebarHost { get; set; }

    public void Post(string json)
    {
        view.CoreWebView2?.PostWebMessageAsJson(json);
        sidebar?.CoreWebView2?.PostWebMessageAsJson(json);

        // Synchronise la couleur de la coque avec l'onglet actif.
        if (json.Contains("\"activeId\":", StringComparison.Ordinal))
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("activeId", out var prop) && prop.GetString() is { } id)
                {
                    var changed = id != _activeId;
                    _activeId = id;
                    if (changed) s_ambient = null;
                    ProbeActiveThemeAsync(withDelays: changed);
                }
            }
            catch (JsonException) { }
        }
    }

    private async void ProbeActiveThemeAsync(bool withDelays)
    {
        await SampleOnceAsync();
        if (!withDelays) return;
        await Task.Delay(350);
        await SampleOnceAsync();
        await Task.Delay(800);
        await SampleOnceAsync();
    }

    private int _themeRequest;
    private async Task SampleOnceAsync()
    {
        if (pages is null || _activeId is null || pages.CoreOf(_activeId) is not { } core) return;
        var id = _activeId;
        var source = core.Source;
        if (source == Settings.DefaultHomePage)
        {
            BroadcastTheme();
            if (s_lastProbe is var (startBg, _, _) && System.Text.RegularExpressions.Regex.Match(startBg, @"rgb\((\d+), (\d+), (\d+)\)") is { Success: true } sm)
                PageColorChanged?.Invoke(byte.Parse(sm.Groups[1].Value), byte.Parse(sm.Groups[2].Value), byte.Parse(sm.Groups[3].Value));
            return;
        }
        var request = ++_themeRequest;
        try
        {
            var raw = await core.ExecuteScriptAsync(ThemeProbeScript);
            if (request != _themeRequest || _activeId != id || core.Source != source || pages.CoreOf(id) != core) return;
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return;
            var bg = doc.RootElement.GetProperty("bg").GetString() ?? "rgb(245, 246, 238)";
            var dark = doc.RootElement.GetProperty("dark").GetBoolean();
            // Le fond de la fenêtre suit aussi la page : pas d'éclair sombre derrière une vue en cours de chargement.
            if (System.Text.RegularExpressions.Regex.Match(bg, @"rgb\((\d+), (\d+), (\d+)\)") is { Success: true } m)
                PageColorChanged?.Invoke(byte.Parse(m.Groups[1].Value), byte.Parse(m.Groups[2].Value), byte.Parse(m.Groups[3].Value));
            var accent = doc.RootElement.TryGetProperty("accent", out var accentValue) && accentValue.ValueKind == JsonValueKind.String ? accentValue.GetString() : null;
            s_lastProbe = (bg, dark, accent);
            BroadcastTheme();
        }
        catch (Exception) { }
    }
}
