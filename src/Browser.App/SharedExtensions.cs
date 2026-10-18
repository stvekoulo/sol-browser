using Browser.Core;
using Microsoft.Web.WebView2.Core;

namespace Browser.App;

// Installation et état communs au profil Sol ; stockage et cookies restent propres aux sessions WebView2.
internal static class SharedExtensions
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static Dictionary<string, bool?>? _state;
    private static string StateFile => Path.Combine(AppPaths.DataDir, "extension-state.json");
    private static Dictionary<string, bool?> State => _state!;
    private static Dictionary<string, bool?> LoadState()
    {
        var state = JsonFileStore.Load<Dictionary<string, bool?>>(StateFile);
        var sessions = new TabSessions(Path.Combine(AppPaths.DataDir, "tab-sessions.json"));
        var root = Path.Combine(Environment.GetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER") ?? AppPaths.WebData, "EBWebView");
        var profiles = sessions.Items.Select(s => Path.Combine(root, "WV2Profile_sol-" + s.Id)).Prepend(Path.Combine(root, "Default"));
        foreach (var (id, enabled) in ExtensionMigration.Read(profiles))
            if (!state.ContainsKey(id) && id != PageZoom.ExtensionId && ExtensionCatalog.Read(Folder(id), id) is not null)
                state[id] = enabled;
        JsonFileStore.Save(StateFile, state);
        return state;
    }
    private static string Folder(string id)
    {
        var local = Path.Combine(AppPaths.DataDir, "extensions", id);
        return Directory.Exists(local) ? local : Path.Combine(AppPaths.BaseDir, "extensions", id);
    }

    public static async Task SetAsync(string id, bool? enabled)
    {
        if (AppPaths.Private || !ExtensionCatalog.IsId(id) || id == PageZoom.ExtensionId) return;
        await Gate.WaitAsync();
        try
        {
            _state ??= await Task.Run(LoadState);
            State[id] = enabled; // null conserve une suppression, même lorsqu'une ancienne session est rouverte.
            JsonFileStore.Save(StateFile, State);
        }
        finally { Gate.Release(); }
    }

    public static async Task<IReadOnlyDictionary<string, bool?>> SnapshotAsync()
    {
        await Gate.WaitAsync();
        try { _state ??= await Task.Run(LoadState); return new Dictionary<string, bool?>(State); }
        finally { Gate.Release(); }
    }

    public static async Task<bool> SyncAsync(CoreWebView2Profile profile, string? reloadId = null)
    {
        if (AppPaths.Private || PrivateBrowsing.IsProfile(profile.ProfileName)) return true;
        await Gate.WaitAsync();
        try
        {
            _state ??= await Task.Run(LoadState);
            var installed = (await profile.GetBrowserExtensionsAsync()).ToDictionary(e => e.Id);
            var changed = false;
            // Migration des installations existantes, sans ressusciter celles supprimées depuis la migration.
            foreach (var extension in installed.Values)
                if (extension.Id != PageZoom.ExtensionId && !State.ContainsKey(extension.Id)
                    && ExtensionCatalog.Read(Folder(extension.Id), extension.Id) is not null)
                { State[extension.Id] = extension.IsEnabled; changed = true; }
            if (changed) JsonFileStore.Save(StateFile, State);
            var success = true;
            foreach (var (id, enabled) in State)
            {
                try
                {
                    if (!ExtensionCatalog.IsId(id) || id == PageZoom.ExtensionId) continue;
                    var extension = installed.GetValueOrDefault(id);
                    if (enabled is null)
                    {
                        if (extension is not null) await extension.RemoveAsync();
                        continue;
                    }
                    // Ne pas lancer le service worker d'une extension désactivée dans une nouvelle session.
                    if (extension is null && !enabled.Value) continue;
                    var folder = Folder(id);
                    if (ExtensionCatalog.Read(folder, id) is null) continue;
                    var prepared = enabled.Value && ExtensionShim.Apply(folder);
                    if (extension is null || prepared || id == reloadId && enabled.Value)
                    {
                        extension = await profile.AddBrowserExtensionAsync(folder);
                        if (extension.Id != id)
                        {
                            await extension.RemoveAsync();
                            throw new InvalidOperationException("L’identité de l’extension a changé.");
                        }
                    }
                    if (extension.IsEnabled != enabled.Value) await extension.EnableAsync(enabled.Value);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException
                    or System.Runtime.InteropServices.COMException or ArgumentException)
                {
                    success = false;
                    DebugLog.Write($"extension {id} : synchronisation {profile.ProfileName} : {error.Message}");
                }
            }
            return success;
        }
        finally { Gate.Release(); }
    }
}
