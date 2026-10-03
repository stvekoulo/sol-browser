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
    private const string ThemeProbeScript = """
        (() => {
          if (location.href === 'about:blank') {
            document.documentElement.style.background = '#eef0e2';
            return { bg: 'rgb(238, 240, 226)', dark: false };
          }
          const parse = s => {
            const m = s && s.match(/rgba?\((\d+),\s*(\d+),\s*(\d+)(?:,\s*([\d.]+))?\)/);
            return m ? [+m[1], +m[2], +m[3], m[4] === undefined ? 1 : +m[4]] : null;
          };
          const els = [];
          for (const x of [window.innerWidth * 0.5, window.innerWidth * 0.25, 24]) {
            let el = document.elementFromPoint(x, 12);
            while (el) { els.push(el); el = el.parentElement; }
          }
          els.push(document.querySelector('ytd-app'), document.querySelector('main'), document.body, document.documentElement);
          for (const el of els) {
            if (!el) continue;
            const c = parse(getComputedStyle(el).backgroundColor);
            if (c && c[3] > 0.15) {
              const [r, g, b] = c;
              const lum = (0.299 * r + 0.587 * g + 0.114 * b) / 255;
              return { bg: `rgb(${r}, ${g}, ${b})`, dark: lum < 0.42 };
            }
          }
          return { bg: 'rgb(255, 255, 255)', dark: false };
        })()
        """;

    private static string? s_lastThemeMsg;
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
        await target.EnsureCoreWebView2Async();
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
            s_lastThemeMsg = JsonSerializer.Serialize(new { @event = "theme", bg, dark });
            view.CoreWebView2?.PostWebMessageAsJson(s_lastThemeMsg);
            sidebar?.CoreWebView2?.PostWebMessageAsJson(s_lastThemeMsg);
            foreach (var floating in _floating) floating.Post(s_lastThemeMsg);
        }
        catch (Exception) { }
    }
}
