using System.Runtime.InteropServices;
using System.Text.Json;
using Browser.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace Browser.App;

public sealed partial class BrowserController
{

    // La fenêtre de l'extension s'ouvre dans l'encart, sous la barre ; une extension qui n'a que des réglages les ouvre en onglet.
    private async void ShowExtension(ExtensionInfo extension, double right, double top)
    {
        try
        {
            if (AppPaths.Private || EngineProfile is not { } profile || !(await profile.GetBrowserExtensionsAsync()).Any(e => e.Id == extension.Id && e.IsEnabled)) return;
            if (extension.Popup is { } popup && ExtensionPopup is { } host) await host.ShowAsync($"chrome-extension://{extension.Id}/{popup.TrimStart('/')}", right, top, profile.ProfileName);
            else if ((extension.Popup ?? extension.Options) is { } page) { Open($"chrome-extension://{extension.Id}/{page.TrimStart('/')}"); Refresh(); }
            // Ni fenêtre ni réglages : elle agit au clic sur son bouton, ou dans un panneau latéral. Le moteur ne transmet pas ce clic.
            else Notice(extension.Name, T("Cette extension n’a pas de fenêtre à ouvrir : elle agit au clic sur son bouton ou dans un panneau latéral, ce que le moteur de Sol ne sait pas encore faire. Ses autres fonctions (dans les pages, par clic droit) restent actives.", "This extension has no window to open: it acts when its button is clicked or in a side panel, which Sol's engine cannot do yet. Its other features (in pages, by right-click) remain active."));
        }
        catch (Exception e) when (e is InvalidOperationException or COMException or ArgumentException)
        { Notice(T("Extension indisponible", "Extension unavailable"), e.Message); }
    }

    // Bouton de la barre : les extensions actives qui ont une fenêtre, puis l'accès à leur gestion.
    private async Task OpenExtensionsMenuAsync(double x, double y)
    {
        if (AppPaths.Private) return;
        if (ClosesPanel("extensions")) return;
        var entries = new List<(string Label, Action? Run)>();
        if (EngineProfile is { } profile)
            foreach (var installed in await profile.GetBrowserExtensionsAsync())
                if (installed.IsEnabled && installed.Id != AdBlock.ExtensionId && ExtensionCatalog.Read(ExtensionFolder(installed.Id), installed.Id) is { } info && (info.Popup ?? info.Options) is not null)
                    entries.Add((info.Name, () => _afterMenu = () => ShowExtension(info, x, y)));
        if (entries.Count == 0) { await OpenExtensionsAsync(); return; }
        entries.Add(("", null));
        entries.Add((T("Gérer les extensions…", "Manage extensions…"), () => _afterMenu = () => _ = OpenExtensionsAsync()));
        ShowMenu(x - Scaled(260), y, entries);
    }
    private Dictionary<string, ExtensionInfo> _extensionOffers = new();
    private List<object> _pinnedExtensions = [];
    private bool _pinsInitialized;
    private string? _pinProfileName;
    private static string ExtensionsDir => Path.Combine(AppPaths.DataDir, "extensions");
    private static string ExtensionFolder(string id)
    {
        var folder = Path.Combine(ExtensionsDir, id);
        return Directory.Exists(folder) ? folder : Path.Combine(AppPaths.BaseDir, "extensions", id); // anciennes installations partagées
    }
    private Microsoft.Web.WebView2.Core.CoreWebView2Profile? EngineProfile => _tabs.ActiveId is { } active ? _pages.CoreOf(active)?.Profile : null;

    private async Task<string> ExtensionsJsonAsync()
    {
        var installed = new List<object>();
        var have = new HashSet<string>();
        if (EngineProfile is { } profile)
            foreach (var extension in await profile.GetBrowserExtensionsAsync())
            {
                // Le bloqueur intégré a son propre réglage ; seules les extensions rangées dans le dossier de Sol sont listées.
                if (ExtensionCatalog.Read(ExtensionFolder(extension.Id), extension.Id) is not { } info || extension.Id == AdBlock.ExtensionId) continue;
                have.Add(extension.Id);
                installed.Add(new { id = extension.Id, name = info.Name, version = info.Version, enabled = extension.IsEnabled, page = (info.Popup ?? info.Options) is not null, options = info.Options is not null,
                    pinned = _settings.PinnedExtensions.Contains(extension.Id), icon = ExtensionCatalog.Icon(info.Folder),
                    store = info.Store, update = _extensionUpdates.GetValueOrDefault(extension.Id) });
            }
        // Les extensions des autres navigateurs : beaucoup de petits fichiers à lire, hors du fil de l'interface.
        _extensionOffers = await Task.Run(() => BrowserImport.Detect(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData))
            .SelectMany(source => ExtensionCatalog.Scan(source.UserData, source.Name))
            // Les composants internes du navigateur Comet ne sont pas des extensions transférables.
            .Where(e => !have.Contains(e.Id) && !e.Name.StartsWith("Comet", StringComparison.OrdinalIgnoreCase))
            .GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.First()));
        return JsonSerializer.Serialize(new
        {
            popup = "extensions",
            session = _tabs.Tabs.FirstOrDefault(t => t.Id == _tabs.ActiveId)?.SessionId is { Length: > 0 } session ? _sessions.Name(session) : T("Principale", "Main"),
            updates = _extensionCheck,
            installed,
            available = _extensionOffers.Values.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).Select(e => new { id = e.Id, name = e.Name, version = e.Version, from = e.From }),
        });
    }

    private async Task OpenExtensionsAsync()
    {
        if (AppPaths.Private || EngineProfile is not { } profile) return;
        HidePopup();
        var json = await ExtensionsJsonAsync();
        if (_closingWindow || EngineProfile?.ProfileName != profile.ProfileName) return;
        _extensionsOpen = true;
        _popup.ShowCentered(560, json, top: 80);
    }

    private async Task RefreshExtensionsAsync()
    {
        await RefreshPinnedExtensionsAsync();
        if (_extensionsOpen) _popup.Post(await ExtensionsJsonAsync());
    }

    private async Task RefreshPinnedExtensionsAsync()
    {
        if (AppPaths.Private || EngineProfile is not { } profile) return;
        _pinsInitialized = true;
        _pinProfileName = profile.ProfileName;
        var extensions = await profile.GetBrowserExtensionsAsync();
        if (EngineProfile?.ProfileName != profile.ProfileName) return;
        _installedExtensions = extensions.Where(e => e.Id != AdBlock.ExtensionId && e.Id != PageZoom.ExtensionId).Select(e => e.Id).ToHashSet();
        foreach (var gone in _extensionUpdates.Keys.Where(id => !_installedExtensions.Contains(id)).ToList()) _extensionUpdates.Remove(gone);
        _pages.RefreshStore();
        _pinnedExtensions = _settings.PinnedExtensions.Distinct().Where(ExtensionCatalog.IsId)
            .Select(id => extensions.FirstOrDefault(e => e.Id == id && e.IsEnabled))
            .Where(e => e is not null).Select(e => ExtensionCatalog.Read(ExtensionFolder(e!.Id), e.Id))
            .Where(info => info is not null).Select(info => (object)new { id = info!.Id, name = info.Name, icon = ExtensionCatalog.Icon(info.Folder) }).ToList();
        Refresh();
    }

    private async Task PinExtensionAsync(string id)
    {
        if (AppPaths.Private || !ExtensionCatalog.IsId(id) || EngineProfile is not { } profile
            || !(await profile.GetBrowserExtensionsAsync()).Any(e => e.Id == id)) return;
        if (!_settings.PinnedExtensions.Remove(id)) _settings.PinnedExtensions.Add(id);
        JsonFileStore.Save(AppPaths.Settings, _settings);
        await RefreshExtensionsAsync();
    }

    private bool _installingExtension;
    private ExtensionInfo? _pendingExtension;
    private readonly SemaphoreSlim _extensionChanges = new(1, 1);
    private HashSet<string> _installedExtensions = [];
    // Mises à jour trouvées qui demandent de nouveaux accès : identifiant → version. Elles attendent un accord.
    private readonly Dictionary<string, string> _extensionUpdates = new();
    private string _extensionCheck = ""; // "", "checking", "current", "updated", "failed"

    private static string? StoreExtension(string url)
    {
        try { return ExtensionPackage.StoreId(url); }
        catch (ArgumentException) { return null; }
    }

    // Récupère le paquet signé d'une extension du store et le déballe dans un dossier provisoire, que l'appelant supprime.
    private static async Task<ExtensionInfo> DownloadStoreExtensionAsync(string id, string staging)
    {
        var version = (await AdBlock.EnvironmentAsync()).BrowserVersionString.Split(' ')[0];
        using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var response = await client.GetAsync(ExtensionPackage.DownloadUri(id, version), System.Net.Http.HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        // Extension retirée du store, payante, ou réservée à un autre navigateur : le store répond sans paquet.
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            throw new InvalidDataException(T("Le Chrome Web Store ne fournit pas cette extension (retirée, payante ou réservée à Chrome).", "The Chrome Web Store does not provide this extension (removed, paid or Chrome-only)."));
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync();
        using var package = new MemoryStream();
        var buffer = new byte[65536]; int count;
        while ((count = await stream.ReadAsync(buffer, timeout.Token)) != 0)
        {
            if (package.Length + count > 32 * 1024 * 1024) throw new InvalidDataException(T("Extension trop volumineuse (maximum 32 Mo).", "Extension too large (32 MB at most)."));
            package.Write(buffer, 0, count);
        }
        ExtensionPackage.Extract(package.ToArray(), id, staging);
        return ExtensionCatalog.Read(staging, id)!;
    }

    private Task InstallStoreExtensionAsync(string input)
    {
        if (StoreExtension(input) is { } id) return OfferStoreExtensionAsync(id);
        Notice(T("Installation impossible", "Installation failed"), T("Ouvrez la fiche d’une extension sur le Chrome Web Store.", "Open an extension page on the Chrome Web Store."));
        return Task.CompletedTask;
    }

    // Télécharge l'extension, puis demande l'accord de l'utilisateur en lui montrant les accès qu'elle demande.
    // Sert aussi à une mise à jour qui demande de nouveaux accès.
    private async Task OfferStoreExtensionAsync(string id)
    {
        if (_installingExtension || AppPaths.Private || EngineProfile is not { } profile) return;
        _installingExtension = true;
        var staging = Path.Combine(ExtensionsDir, "store-" + Guid.NewGuid().ToString("N"));
        try
        {
            Notice(T("Téléchargement de l’extension", "Downloading extension"), T("Le paquet signé est récupéré depuis le Chrome Web Store…", "Fetching the signed package from the Chrome Web Store…"));
            var offered = await DownloadStoreExtensionAsync(id, staging);
            if (_closingWindow || EngineProfile?.ProfileName != profile.ProfileName) return;
            HidePopup();
            _pendingExtension = offered;
            _popup.ShowCentered(440, JsonSerializer.Serialize(new
            {
                popup = "extension-confirm", id, name = offered.Name, version = offered.Version, icon = ExtensionCatalog.Icon(staging),
                update = _installedExtensions.Contains(id),
                access = ExtensionPermissions.Describe(offered.Permissions, Language.Current == "fr"),
            }));
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or System.Net.Http.HttpRequestException or TaskCanceledException
            or System.Security.Cryptography.CryptographicException or JsonException or InvalidOperationException or OverflowException or OperationCanceledException)
        { Notice(T("Installation impossible", "Installation failed"), e is System.Net.Http.HttpRequestException or OperationCanceledException ? T("Le Chrome Web Store ne répond pas. Vérifiez la connexion, puis réessayez.", "The Chrome Web Store is not responding. Check the connection, then try again.") : e.Message); }
        finally
        {
            _installingExtension = false;
            try { if (_pendingExtension?.Folder != staging && Directory.Exists(staging)) Directory.Delete(staging, true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { DebugLog.Write("extension temporaire : " + e.Message); }
        }
    }

    private async Task ConfirmStoreExtensionAsync(string id)
    {
        if (_pendingExtension is not { } pending || pending.Id != id) return;
        _pendingExtension = null;
        try
        {
            if (!await InstallExtensionAsync(pending.Folder, id)) return;
            _extensionUpdates.Remove(id);
            await OpenExtensionsAsync();
        }
        finally
        {
            try { if (Directory.Exists(pending.Folder)) Directory.Delete(pending.Folder, true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { DebugLog.Write("extension temporaire : " + e.Message); }
        }
    }

    // « 4.9 » et « 4.9.0.0 » sont la même version.
    private static Version Full(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));

    // Au démarrage, une fois les pages lancées : au plus une recherche par demi-journée.
    private async Task UpdateExtensionsLaterAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(40));
        if (!_closingWindow && DateTime.UtcNow - _settings.ExtensionsChecked > TimeSpan.FromHours(12)) await UpdateExtensionsAsync(manual: false);
    }

    // Mises à jour des extensions venues du store : Sol demande leur dernière version, télécharge le paquet signé et
    // remplace l'extension si elle ne demande aucun accès de plus. Sinon elle attend l'accord de l'utilisateur.
    private async Task UpdateExtensionsAsync(bool manual)
    {
        if (AppPaths.Private || _extensionCheck == "checking" || EngineProfile is not { } profile) return;
        async Task ShowAsync(string state) { _extensionCheck = state; if (_extensionsOpen) _popup.Post(await ExtensionsJsonAsync()); }
        await ShowAsync("checking");
        var updated = 0;
        try
        {
            var mine = (await profile.GetBrowserExtensionsAsync()).Where(e => e.Id != AdBlock.ExtensionId)
                .Select(e => ExtensionCatalog.Read(ExtensionFolder(e.Id), e.Id)).OfType<ExtensionInfo>().Where(e => e.Store).ToList();
            if (mine.Count > 0)
            {
                var engine = (await AdBlock.EnvironmentAsync()).BrowserVersionString.Split(' ')[0];
                using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                var latest = ExtensionPackage.ParseUpdates(await client.GetStringAsync(ExtensionPackage.UpdateCheckUri(mine.Select(e => e.Id), engine)));
                foreach (var current in mine)
                {
                    if (!latest.TryGetValue(current.Id, out var version) || !Version.TryParse(current.Version, out var have) || Full(version) <= Full(have)) continue;
                    var staging = Path.Combine(ExtensionsDir, "store-" + Guid.NewGuid().ToString("N"));
                    try
                    {
                        var next = await DownloadStoreExtensionAsync(current.Id, staging);
                        if (_closingWindow || EngineProfile?.ProfileName != profile.ProfileName) { await ShowAsync(""); return; }
                        var before = ExtensionPermissions.Describe(current.Permissions, french: true);
                        if (ExtensionPermissions.Describe(next.Permissions, french: true).Except(before).Any()) _extensionUpdates[current.Id] = next.Version;
                        else if (await InstallExtensionAsync(staging, current.Id, quiet: true)) { _extensionUpdates.Remove(current.Id); updated++; }
                    }
                    catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or System.Net.Http.HttpRequestException
                        or System.Security.Cryptography.CryptographicException or JsonException or InvalidOperationException or OverflowException or OperationCanceledException)
                    { DebugLog.Write($"extension {current.Id} : mise à jour impossible : {e.Message}"); }
                    finally
                    {
                        try { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
                        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { DebugLog.Write("extension temporaire : " + e.Message); }
                    }
                }
            }
            _settings.ExtensionsChecked = DateTime.UtcNow;
            JsonFileStore.Save(AppPaths.Settings, _settings);
            await ShowAsync(updated > 0 ? "updated" : "current");
        }
        catch (Exception e) when (e is System.Net.Http.HttpRequestException or OperationCanceledException or ArgumentException or InvalidOperationException or COMException)
        {
            DebugLog.Write("extensions : recherche de mises à jour impossible : " + e.Message);
            await ShowAsync(manual ? "failed" : "");
        }
    }
}
