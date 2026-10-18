using System.Text.Json;
using System.Text.Json.Nodes;

namespace Browser.Core;

public static class ExtensionAction
{
    public const string Prefix = "Sol action ", KeyFile = "sol-action-key.txt";
    public static string? Marker(string folder)
    {
        var path = Path.Combine(folder, KeyFile);
        if (!File.Exists(path)) return null;
        var key = File.ReadAllText(path);
        return Guid.TryParseExact(key, "N", out _) ? Prefix + key : null;
    }

    public static (string Script, bool Changed) Prepare(string folder, JsonObject manifest)
    {
        if (manifest["action"] is not JsonObject && manifest["browser_action"] is not JsonObject
            && manifest["page_action"] is not JsonObject && manifest["side_panel"] is not JsonObject) return ("", false);
        var changed = false;
        if (Marker(folder) is null)
        { File.WriteAllText(Path.Combine(folder, KeyFile), Guid.NewGuid().ToString("N")); changed = true; }
        var permissions = manifest["permissions"] as JsonArray;
        if (permissions is null) manifest["permissions"] = permissions = new JsonArray();
        // UI plumbing only: native context-menu execution grants activeTab for the clicked tab.
        if (!permissions.Any(p => p?.GetValue<string>() == "contextMenus"))
        { permissions.Add("contextMenus"); changed = true; }
        return ("\n" + Script.Replace("__MARKER__", JsonSerializer.Serialize(Marker(folder))), changed);
    }

    private const string Script = """
        (() => {
          const action = chrome.action || chrome.browserAction || chrome.pageAction;
          if (!chrome.contextMenus || chrome.contextMenus.__solAction) return;
          const listeners = new Set(), event = action?.onClicked;
          if (event) {
            const add = event.addListener.bind(event), remove = event.removeListener.bind(event);
            event.addListener = listener => { add(listener); listeners.add(listener); };
            event.removeListener = listener => { remove(listener); listeners.delete(listener); };
          }
          const marker = __MARKER__, id = '__sol_action_' + marker.slice(-32), menus = chrome.contextMenus;
          let selected;
          chrome.tabs.__solSelected = () => selected;
          const intercept = (event, hidden) => {
            const wrappers = new WeakMap(), listeners = new Set();
            const add = event.addListener.bind(event), remove = event.removeListener.bind(event), has = event.hasListener.bind(event);
            event.addListener = listener => {
              if (!wrappers.has(listener)) wrappers.set(listener, (...args) => { if (!hidden(args[0])) return listener(...args); });
              add(wrappers.get(listener)); listeners.add(listener);
            };
            event.removeListener = listener => { remove(wrappers.get(listener) || listener); listeners.delete(listener); };
            event.hasListener = listener => has(wrappers.get(listener) || listener);
            event.hasListeners = () => listeners.size > 0;
            return add;
          };
          const privateQuery = message => message?.__solSelected === marker;
          const addMessage = intercept(chrome.runtime.onMessage, privateQuery);
          addMessage((message, sender, reply) => { if (privateQuery(message) && sender.id === chrome.runtime.id) reply(selected); });
          const ensure = () => menus.create({ id, title: marker, contexts: ['all'] }, () => { void chrome.runtime.lastError; });
          const clear = menus.removeAll.bind(menus);
          menus.removeAll = callback => {
            if (typeof callback !== 'function') return clear().then(result => { ensure(); return result; });
            return clear(() => {
              const failed = !!chrome.runtime.lastError;
              try { callback(); } finally { if (!failed) ensure(); }
            });
          };
          const addContext = intercept(menus.onClicked, info => info.menuItemId === id);
          ensure();
          addContext((info, tab) => {
            if (info.menuItemId !== id || !tab) return;
            selected = tab.id;
            const manifest = chrome.runtime.getManifest();
            if ((manifest.action || manifest.browser_action || {}).default_popup || manifest.side_panel?.default_path) return;
            for (const listener of listeners) {
              try { Promise.resolve(listener(tab)).catch(error => console.error(error)); }
              catch (error) { console.error(error); }
            }
          });
          menus.__solAction = true;
        })();
        """;

    public static string QueryScript(string marker) =>
        "chrome.tabs.__solSelected = () => chrome.runtime.sendMessage({__solSelected:" + JsonSerializer.Serialize(marker) + "});";

    public const string ContextScript = """
        (() => {
          let armed = false;
          const stop = Event.prototype.stopImmediatePropagation;
          for (const type of ['contextmenu', 'mousedown', 'mouseup', 'pointerdown', 'pointerup', 'auxclick'])
            addEventListener(type, event => { if (armed && event.button === 2) stop.call(event); }, true);
          Object.defineProperty(globalThis, '__solNativeContextMenu', { value: value => { armed = value === true; } });
        })();
        """;
}
