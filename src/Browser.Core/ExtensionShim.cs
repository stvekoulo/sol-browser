using System.Text.Json.Nodes;

namespace Browser.Core;

/// <summary>
/// Ce qu'une extension voit des onglets. Le moteur de Sol présente chaque vue comme un onglet « actif », seul dans sa
/// fenêtre, y compris la fenêtre de l'extension elle-même ; et son idée de la « fenêtre courante » ne suit pas l'onglet
/// affiché. Une extension écrite pour Chrome demande pourtant « l'onglet actif de la fenêtre courante » pour savoir où
/// agir (remplir un mot de passe, lire l'adresse du site) : elle recevait le mauvais onglet, ou sa propre fenêtre.
/// Ce script redonne la vision de Chrome : une fenêtre, tous les onglets de Sol dedans, un seul actif — celui que
/// le moteur a affiché en dernier — et la fenêtre de l'extension n'en fait pas partie.
/// Il est chargé avant le code de l'extension : dans sa fenêtre par le navigateur, dans son script de fond par un
/// script d'amorce que <see cref="Apply"/> pose dans la copie de l'extension gardée par Sol.
/// </summary>
public static class ExtensionShim
{
    public const string TabsFile = "sol-tabs.js", WorkerFile = "sol-worker.js";

    public const string Script = """
        (() => {
          const tabs = globalThis.chrome && chrome.tabs;
          if (!tabs || !tabs.query || tabs.__sol) return;
          const query = tabs.query.bind(tabs);
          const manifest = chrome.runtime.getManifest();
          const popup = ((manifest.action || manifest.browser_action || {}).default_popup || '').replace(/^\//, '');
          const popupUrl = popup ? chrome.runtime.getURL(popup).split(/[?#]/)[0] : null;
          // Les vues qui affichent la fenêtre de l'extension : pour le moteur, des onglets comme les autres.
          const ownWindows = async () => {
            if (!popupUrl || !chrome.runtime.getContexts) return [];
            try {
              return (await chrome.runtime.getContexts({ contextTypes: ['TAB'] }))
                .filter(c => (c.documentUrl || '').split(/[?#]/)[0] === popupUrl).map(c => c.tabId);
            } catch { return []; }
          };
          const resolve = async info => {
            info = { ...(info || {}) };
            const active = info.active;
            for (const key of ['active', 'highlighted', 'currentWindow', 'lastFocusedWindow']) delete info[key];
            if (info.windowId === -2) delete info.windowId; // WINDOW_ID_CURRENT
            const own = await ownWindows();
            const pages = list => list.filter(t => !own.includes(t.id));
            // L'onglet affiché : celui que le moteur a montré en dernier.
            const shown = pages(await query({})).reduce((a, b) => !a || (b.lastAccessed || 0) > (a.lastAccessed || 0) ? b : a, null);
            return pages(await query(info))
              .map(t => { t.active = t.highlighted = !!shown && t.id === shown.id; return t; })
              .filter(t => active === undefined || t.active === active);
          };
          tabs.query = (info, callback) => {
            const result = resolve(info);
            if (typeof callback !== 'function') return result;
            result.then(callback, () => callback([]));
          };
          tabs.__sol = true;
        })();
        """;

    /// <summary>
    /// Pose le script dans le dossier d'une extension et fait démarrer son script de fond par une amorce qui le charge
    /// d'abord. Sans effet sur une extension sans script de fond. Vrai si le dossier a changé (l'extension est à recharger).
    /// </summary>
    public static bool Apply(string folder)
    {
        var manifestPath = Path.Combine(folder, "manifest.json");
        if (JsonNode.Parse(File.ReadAllText(manifestPath), null, ExtensionPackage.Lenient) is not JsonObject manifest
            || manifest["background"] is not JsonObject background
            || background["service_worker"]?.GetValue<string>() is not { Length: > 0 } worker) return false;

        var changed = Write(Path.Combine(folder, TabsFile), Script);
        var path = worker.Replace('\\', '/').TrimStart('/');
        if (Path.GetFileName(path) == WorkerFile) return changed; // déjà amorcé : seul le script pouvait être à mettre à jour

        // L'amorce est rangée à côté du script d'origine : ce qu'il charge par un chemin relatif garde le même point de départ.
        var directory = path.Contains('/') ? path[..(path.LastIndexOf('/') + 1)] : "";
        var original = path[directory.Length..];
        var module = background["type"]?.GetValue<string>() == "module";
        var root = Path.GetFullPath(folder) + Path.DirectorySeparatorChar;
        var bootstrap = Path.GetFullPath(Path.Combine(folder, directory, WorkerFile));
        if (!bootstrap.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return changed; // chemin qui sort du dossier : on n'y touche pas
        var name = System.Text.Json.JsonSerializer.Serialize(module ? "./" + original : original);
        File.WriteAllText(bootstrap, module
            ? $"import '/{TabsFile}';\nimport {name};\n"
            : $"importScripts('/{TabsFile}', {name});\n");
        background["service_worker"] = directory + WorkerFile;
        File.WriteAllText(manifestPath, manifest.ToJsonString());
        return true;
    }

    private static bool Write(string file, string content)
    {
        if (File.Exists(file) && File.ReadAllText(file) == content) return false;
        File.WriteAllText(file, content);
        return true;
    }
}
