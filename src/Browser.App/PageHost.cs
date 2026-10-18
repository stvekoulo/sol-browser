using Browser.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace Browser.App;

/// <summary>Une WebView2 par onglet. Aucun canal vers l'hôte n'est ouvert sur ces vues.</summary>
public sealed partial class PageHost(Grid container, ColumnDefinition primary, ColumnDefinition divider, ColumnDefinition secondary, string blurScript, Settings settings)
{
    private readonly Dictionary<string, WebView2> _views = new();
    private readonly Dictionary<string, string> _blurScriptIds = new(); // vues où le flou est installé
    private readonly HashSet<string> _loading = new();
    private readonly HashSet<string> _ready = new();
    private readonly HashSet<string> _visible = new();
    private readonly HashSet<string> _pdf = new();
    private readonly Dictionary<string, Task> _profileReady = new();
    // Le moteur perd les cookies de session à la fermeture de la dernière vue du profil.
    // Les conserver uniquement en mémoire permet de changer d'onglet/session sans déconnecter le compte.
    private readonly Dictionary<string, IReadOnlyList<CoreWebView2Cookie>> _sessionCookies = new();
    private readonly Dictionary<string, Task> _closingProfiles = new();
    public event Action<string>? SessionFailed;
    public event Action? PagePressed;
    public bool IsPdf(string id) => _pdf.Contains(id);
    private bool _watchLayout;
    private (string? Primary, string? Secondary, double Ratio) _layout;

    public event Action<string, string?, string?>? PageChanged; // id, url, titre
    public event Action? RuntimeChanged;                         // chargement, historique
    public event Action? ContentPainted;
    public event Action? NativeDownloadsRequested;
    public event Action<string>? Loaded;                         // id, page chargée avec succès
    public event Action<string>? PageFocused;
    public event Action<string, Command>? StartRequested;
    public event Action<string, Command>? NotesRequested;
    public event Action<string>? StoreInstallRequested;
    public event Action? StoreManageRequested; // bouton d'une extension déjà installée : ouvrir leur gestion

    public static bool IsStoreDetail(string url)
    {
        try { ExtensionPackage.StoreId(url); return true; }
        catch (ArgumentException) { return false; }
    }

    private static bool IsStore(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var parsed) && parsed.Scheme == "https" && parsed.Host == "chromewebstore.google.com";

    // Le store est une application à page unique : le script est posé sur toutes ses pages et suit lui-même l'adresse.
    // Sur une fiche, le bouton d'installation (repéré au mot « Chrome », quelle que soit la langue) devient celui de Sol.
    private const string StoreScript = """
        (() => {
          if (window.__solStore) return; window.__solStore = true;
          let installed = [];
          const id = () => (location.pathname.match(/^\/detail\/(?:[^/]+\/)?([a-p]{32})\/?$/) || [])[1];
          const french = () => (document.documentElement.lang || navigator.language).startsWith('fr');
          const label = () => installed.includes(id())
            ? (french() ? 'Installée dans Sol' : 'Installed in Sol') : (french() ? 'Ajouter à Sol' : 'Add to Sol');
          const ours = b => b.dataset.sol === '1' || (/chrome/i.test(b.textContent || '') && b.textContent.trim().length < 48);
          const update = () => {
            if (!id()) return;
            document.querySelectorAll('button').forEach(b => {
              if (!ours(b)) return;
              b.dataset.sol = '1';
              if (b.disabled) b.disabled = false;
              b.removeAttribute('aria-disabled');
              if (b.textContent.trim() !== label()) b.textContent = label();
            });
          };
          window.__solStoreInstalled = ids => { installed = ids; update(); };
          document.addEventListener('click', e => {
            const b = e.target.closest?.('button'); if (!b || !id() || !ours(b)) return;
            e.preventDefault(); e.stopImmediatePropagation();
            window.chrome.webview.postMessage(installed.includes(id()) ? 'sol.store.manage' : 'sol.store.install');
          }, true);
          new MutationObserver(update).observe(document.documentElement, {childList:true, subtree:true, characterData:true, attributes:true, attributeFilter:['disabled','aria-disabled']});
          update();
        })()
        """;

    private async Task PrepareStoreAsync(CoreWebView2 core)
    {
        if (!IsStore(core.Source)) return;
        try { await core.ExecuteScriptAsync(StoreScript + $";window.__solStoreInstalled({System.Text.Json.JsonSerializer.Serialize((await core.Profile.GetBrowserExtensionsAsync()).Select(e => e.Id))})"); }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException) { }
    }

    /// <summary>Une extension vient d'être installée ou retirée : les fiches du store ouvertes le montrent.</summary>
    public void RefreshStore()
    {
        foreach (var view in _views.Values)
            if (view.CoreWebView2 is { } core) _ = PrepareStoreAsync(core);
    }
    public void PostStart(string id, string json)
    {
        if (Core(id) is { } core && core.Source is Settings.DefaultHomePage or QuickNotes.PageUrl) core.PostWebMessageAsJson(json);
    }
    public event Action<CoreWebView2>? Navigating;
    public Func<string, string, Tab?>? CreatePopupTab;
    public event Action<string>? PopupClosed;
    public event Action<string, double, double, CoreWebView2ContextMenuRequestedEventArgs>? ContextMenuRequested; // id, x, y dans la fenêtre
    public event Action<string, string, string, double, double, PageAiText>? FanAction; // id, élément, action, x, y dans la fenêtre, valeur
    public event Action<string, double, double>? DeviceAreaMenuRequested; // id, x, y : clic droit à côté d'un appareil simulé
    public event Action<string, CoreWebView2DownloadStartingEventArgs>? DownloadStarting; // onglet d'origine
    public event Action<CoreWebView2, CoreWebView2PermissionRequestedEventArgs>? PermissionRequested;
    public event Action<string, string>? StatusChanged;         // id, adresse du lien survolé (vide = aucun)
    public event Action<string, string?>? FaviconChanged;        // id, icône en data: (ou null)
    public event Action<bool>? FullScreenChanged;                // une page (vidéo…) entre en plein écran ou en sort

    public bool IsLoading(string id) => _loading.Contains(id);
    public bool CanGoBack(string id) => Core(id)?.CanGoBack ?? false;
    public bool CanGoForward(string id) => Core(id)?.CanGoForward ?? false;

    // Onglets endormis (rouverts d'une session) : aucune vue, aucune page chargée tant qu'ils ne sont pas affichés.
    private readonly Dictionary<string, Tab> _asleep = new();

    public void AddAsleep(Tab tab) => _asleep[tab.Id] = tab;
    /// <summary>L'onglet est déchargé : il se rechargera quand on y reviendra.</summary>
    public bool IsAsleep(string id) => _asleep.ContainsKey(id);
}
