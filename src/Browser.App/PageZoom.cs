using System.Text.Json;
using Browser.Core;
using Microsoft.Web.WebView2.Core;

namespace Browser.App;

/// <summary>
/// Zoom des pages : le vrai zoom du moteur. Ce type de vue ne l'expose pas au navigateur, mais le moteur le donne à
/// ses extensions : Sol charge donc dans chaque profil une extension minuscule, à lui, qui applique le zoom du profil
/// à chaque onglet. (Avant, la page était mise en page plus grande ou plus petite puis rendue à l'échelle par
/// émulation d'écran ; le moteur dirigeait alors mal les clics dans certains cadres intégrés aux pages, comme la
/// liste de suggestions d'un gestionnaire de mots de passe.)
///
/// Le navigateur lui parle de deux façons :
/// - le fichier zoom.json de son dossier, que lui seul écrit : le zoom du profil, et les adresses à laisser à 100 %
///   (documents PDF, qui ont leur propre zoom) ;
/// - pour un onglet précis (appareil simulé, lecteur flottant : leur page doit rester à 100 %), un événement lancé
///   dans la page, que le script de contenu de l'extension relaie. Une page qui l'imiterait ne changerait que son
///   propre zoom.
/// </summary>
public static class PageZoom
{
    /// <summary>Identité fixée par la clé du manifeste : la même dans tous les profils.</summary>
    public const string ExtensionId = "joongffhgagcehjmlaeopoemilgmiabe";
    private const string Key = "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAsPyc9JB2w9NlGrB9/prLVZv/I26hQ07QLSFpciR+mGFCnU4kyvjNnmEQC2u5waUcFC5m8SwVdbecI4Dq0NuE0KR8mthWmlCeMCe9vfN2JCSzK0c0AYeaJHG4DsyE+TSUuPLRFxqwwCaHhbqPsf5lRPduOCkr4qhZG6Ti+jLYFJNgaJux6Tt3BKkyakyir43oaRLCwzWVGIZ9HLnNjPc7LaLXRo7I38aiQsVrEzkhg89LIILMBdXkExuTOvBpeepMrVHMlmXG0F34oeKpx4VifhIzdJbyiA3rrKqLFogi8QErQHBivLTetN2yThM6schiGFWD6tFLE70ZxEOkAAvztQIDAQAB";
    private const string Event = "sol-page-zoom";

    private static string Folder => Path.Combine(AppPaths.DataDir, "zoom");

    private static string Manifest => JsonSerializer.Serialize(new
    {
        manifest_version = 3, name = "Sol", version = "1.0", key = Key,
        description = "Zoom des pages de Sol.",
        permissions = new[] { "tabs", "storage" },
        background = new { service_worker = "worker.js" },
        content_scripts = new[] { new { matches = new[] { "<all_urls>" }, js = new[] { "content.js" }, run_at = "document_start" } },
    });

    // Dans la page : relaie la demande du navigateur pour cet onglet.
    private const string Content = $$"""
        addEventListener('{{Event}}', e => { try { chrome.runtime.sendMessage({ zoom: e.detail }); } catch {} });
        """;

    private const string Worker = """
        const START = 'https://start.sol/';
        let fallback = 1, plain = [];
        // Le fichier du navigateur est relu à chaque fois : il n'a pas d'autre moyen de prévenir.
        const settings = async () => {
          try {
            const read = await (await fetch(chrome.runtime.getURL('zoom.json'), { cache: 'no-store' })).json();
            if (read.zoom >= 0.25 && read.zoom <= 5) fallback = read.zoom;
            plain = Array.isArray(read.plain) ? read.plain : [];
          } catch {}
        };
        // Zoom propre à un onglet : une clé par onglet, pour que deux demandes simultanées ne s'écrasent pas.
        const own = async id => (await chrome.storage.session.get('t' + id))['t' + id];
        const isDocument = url => /\.pdf$/i.test(url.split(/[?#]/)[0]) || plain.includes(url);
        // navigated : l'onglet change d'adresse. Un document n'est remis à 100 % qu'à ce moment : ensuite son zoom est à l'utilisateur.
        async function apply(tab, navigated) {
          try {
            const url = tab.url || tab.pendingUrl || '';
            const mine = await own(tab.id);
            await settings();
            let zoom;
            if (mine != null) zoom = mine;
            else if (!url) return;
            else if (url.startsWith(START) || url.startsWith('chrome-extension://')) zoom = 1;
            else if (isDocument(url)) { if (!navigated) return; zoom = 1; }
            else zoom = fallback;
            if (Math.abs(await chrome.tabs.getZoom(tab.id) - zoom) > 0.001) await chrome.tabs.setZoom(tab.id, zoom);
          } catch {}
        }
        // Extension tout juste chargée : les onglets déjà ouverts n'ont déclenché aucun événement pour elle.
        chrome.runtime.onInstalled.addListener(async () => { for (const tab of await chrome.tabs.query({})) await apply(tab, true); });
        chrome.tabs.onCreated.addListener(tab => apply(tab, true));
        chrome.tabs.onUpdated.addListener((id, change, tab) => { if (change.url || change.status) apply(tab, true); });
        chrome.tabs.onRemoved.addListener(id => chrome.storage.session.remove('t' + id));
        chrome.runtime.onMessage.addListener((message, sender) => {
          if (!sender.tab) return;
          (async () => {
            if (message.zoom === 'refresh') { for (const tab of await chrome.tabs.query({})) await apply(tab, false); return; }
            const key = 't' + sender.tab.id;
            if (typeof message.zoom === 'number' && message.zoom >= 0.25 && message.zoom <= 5) await chrome.storage.session.set({ [key]: message.zoom });
            else await chrome.storage.session.remove(key);
            await apply(sender.tab, false);
          })();
        });
        """;

    private static readonly HashSet<string> s_plain = new();
    private static double s_zoom = 1;

    /// <summary>Zoom du profil. Appliqué aux onglets à leur prochaine navigation, ou tout de suite après <see cref="RefreshAsync"/>.</summary>
    public static void Set(double zoom)
    {
        s_zoom = zoom;
        WriteSettings();
    }

    /// <summary>Adresse d'un document (PDF) : affichée à 100 %, le zoom y est celui du lecteur.</summary>
    public static void KeepPlain(string url)
    {
        if (s_plain.Count > 200) s_plain.Clear(); // les plus anciens n'ont plus d'onglet depuis longtemps
        if (s_plain.Add(url)) WriteSettings();
    }

    private static void WriteSettings()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Path.Combine(Folder, "zoom.json"), JsonSerializer.Serialize(new { zoom = s_zoom, plain = s_plain }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { DebugLog.Write("zoom : réglage non écrit : " + e.Message); }
    }

    /// <summary>Pose l'extension dans le dossier du profil et la charge dans le moteur, avant la première page.</summary>
    public static async Task InstallAsync(CoreWebView2Profile profile, double zoom)
    {
        try
        {
            Set(zoom);
            var changed = false;
            foreach (var (name, content) in new[] { ("manifest.json", Manifest), ("worker.js", Worker), ("content.js", Content) })
            {
                var file = Path.Combine(Folder, name);
                if (File.Exists(file) && File.ReadAllText(file) == content) continue;
                File.WriteAllText(file, content);
                changed = true;
            }
            var installed = (await profile.GetBrowserExtensionsAsync()).FirstOrDefault(e => e.Id == ExtensionId);
            if (changed || installed is null) installed = await profile.AddBrowserExtensionAsync(Folder);
            if (!installed.IsEnabled) await installed.EnableAsync(true);
            if (installed.Id != ExtensionId) DebugLog.Write($"zoom : identité inattendue ({installed.Id})");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException)
        { DebugLog.Write("zoom : extension non chargée, les pages restent à 100 % : " + e.Message); }
    }

    /// <summary>Zoom propre à cet onglet (1 pour un appareil simulé ou le lecteur flottant) ; null : celui du profil.</summary>
    public static Task TabAsync(CoreWebView2 core, double? zoom) => SendAsync(core, zoom is { } z ? z.ToString(System.Globalization.CultureInfo.InvariantCulture) : "null");

    /// <summary>Le réglage vient de changer : tous les onglets le prennent maintenant. La page donnée sert de relais.</summary>
    public static Task RefreshAsync(CoreWebView2 core) => SendAsync(core, "'refresh'");

    private static async Task SendAsync(CoreWebView2 core, string detail)
    {
        try { await core.ExecuteScriptAsync($"dispatchEvent(new CustomEvent('{Event}', {{ detail: {detail} }}))"); }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException) { /* vue fermée entre-temps */ }
    }
}
