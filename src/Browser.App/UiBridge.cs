using System.Text.Json;
using Browser.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace Browser.App;

// Vue d'interface : synchronise la barre, la sidebar et adapte la couleur de coque à la page active.
public sealed class UiBridge(WebView2 view, string page = "index.html", WebView2? sidebar = null, ColumnDefinition? sidebarColumn = null, PageHost? pages = null)
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
          const ctx = document.createElement('canvas').getContext('2d');
          const resolve = css => {
            if (!css || !ctx) return null;
            ctx.fillStyle = '#000'; ctx.fillStyle = css;
            const v = ctx.fillStyle;
            if (v.startsWith('#')) return [parseInt(v.slice(1, 3), 16), parseInt(v.slice(3, 5), 16), parseInt(v.slice(5, 7), 16), 1];
            return parse(v);
          };
          const lum = ([r, g, b]) => (0.299 * r + 0.587 * g + 0.114 * b) / 255;
          const sat = ([r, g, b]) => { const max = Math.max(r, g, b), min = Math.min(r, g, b); return max === 0 ? 0 : (max - min) / max; };

          const els = [];
          for (const x of [window.innerWidth * 0.5, window.innerWidth * 0.25, 24]) {
            let el = document.elementFromPoint(x, 12);
            while (el) { els.push(el); el = el.parentElement; }
          }
          els.push(document.querySelector('ytd-app'), document.querySelector('main'), document.body, document.documentElement);
          let bg = [255, 255, 255];
          for (const el of els) {
            if (!el) continue;
            const c = parse(getComputedStyle(el).backgroundColor);
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
              vote(parse(style.backgroundColor), weight * Math.min(3, (box.width * box.height) / 4000 + 0.5));
              vote(parse(style.color), weight * 0.4);
              if (el instanceof SVGElement) vote(parse(style.fill), weight * 0.6);
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
        if (s_lastProbe is not var (bg, dark, accent)) return;
        var on = Settings;
        s_lastThemeMsg = JsonSerializer.Serialize(new
        {
            @event = "theme", bg, dark, accent, mode = on?.Theme ?? "page", systemDark = IsSystemDark(), lang = Language.Current,
            font = on?.Font ?? "default",
            tint = new { topbar = on?.TintTopbar ?? true, sidebar = on?.TintSidebar ?? true, menus = on?.TintMenus ?? true, background = on?.TintBackground ?? true },
        });
        view.CoreWebView2?.PostWebMessageAsJson(s_lastThemeMsg);
        sidebar?.CoreWebView2?.PostWebMessageAsJson(s_lastThemeMsg);
        foreach (var floating in _floating) floating.Post(s_lastThemeMsg);
    }
    private string? _activeId;
    private readonly List<UiBridge> _floating = new(); // vues flottantes qui suivent la couleur de la page : menus, encart

    public void AttachPopup(UiBridge floatingBridge) => _floating.Add(floatingBridge);

    /// <summary>Couleur de fond de la page active (rouge, vert, bleu).</summary>
    public event Action<byte, byte, byte>? PageColorChanged;

    public async Task InitAsync(string uiFolder, Action<Command> onCommand)
    {
        await InitViewAsync(view, page, uiFolder, onCommand);
        if (sidebar is not null)
            await InitViewAsync(sidebar, "index.html#sidebar", uiFolder, onCommand);

        if (pages is not null)
        {
            pages.Loaded += _ => ProbeActiveThemeAsync(withDelays: true);
            pages.PageChanged += (_, _, _) => ProbeActiveThemeAsync(withDelays: false);
        }
    }

    private async Task InitViewAsync(WebView2 target, string targetPage, string uiFolder, Action<Command> onCommand)
    {
        await target.EnsureCoreWebView2Async(await AdBlock.EnvironmentAsync());
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

    private async Task SampleOnceAsync()
    {
        if (pages is null || _activeId is null || pages.CoreOf(_activeId) is not { } core) return;
        try
        {
            var raw = await core.ExecuteScriptAsync(ThemeProbeScript);
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
