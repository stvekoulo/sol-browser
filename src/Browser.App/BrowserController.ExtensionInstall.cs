using System.Runtime.InteropServices;
using System.Text.Json;
using Browser.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace Browser.App;

public sealed partial class BrowserController
{

    private async Task ReloadExtensionAsync(string id)
    {
        if (AppPaths.Private || EngineProfile is null || !ExtensionCatalog.IsId(id) || id == AdBlock.ExtensionId) return;
        await _extensionChanges.WaitAsync();
        try
        {
            var state = await SharedExtensions.SnapshotAsync();
            if (!state.TryGetValue(id, out var enabled) || enabled is null) return;
            ExtensionPopup?.Hide();
            await _pages.SyncExtensionsAsync(reloadId: id);
            await RefreshExtensionsAsync();
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or COMException)
        { Notice(T("Extension indisponible", "Extension unavailable"), e.Message); }
        finally { _extensionChanges.Release(); }
    }

    // Copie le dossier dans celui de Sol, sous l'identifiant de l'extension, puis l'installe dans le profil.
    /// <param name="quiet">Mise à jour en arrière-plan : un échec est noté au journal, sans déranger.</param>
    private async Task<bool> InstallExtensionAsync(string source, string? knownId, bool quiet = false)
    {
        if (AppPaths.Private || EngineProfile is not { } profile) return false;
        await _extensionChanges.WaitAsync();
        string? backup = null, destination = null;
        var wasEnabled = true;
        try
        {
            var id = knownId;
            if (id is null)
            {
                // Dossier désigné à la main : l'identifiant n'est connu qu'une fois l'extension chargée.
                var trial = ExtensionCatalog.CopyTo(source, Path.Combine(ExtensionsDir, "ajout"));
                ExtensionCatalog.EnsureKey(trial); // l’identité ne dépend plus du déplacement du dossier
                var loaded = await profile.AddBrowserExtensionAsync(trial);
                id = loaded.Id;
                await loaded.RemoveAsync();
                source = trial;
            }
            var folder = Path.Combine(ExtensionsDir, id);
            destination = folder;
            wasEnabled = (await profile.GetBrowserExtensionsAsync()).FirstOrDefault(e => e.Id == id)?.IsEnabled ?? true;
            if (Directory.Exists(folder) && !string.Equals(Path.GetFullPath(source), Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase))
            {
                backup = folder + "-previous-" + Guid.NewGuid().ToString("N");
                ExtensionCatalog.CopyTo(folder, backup);
            }
            if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase)) ExtensionCatalog.CopyTo(source, folder);
            ExtensionShim.Apply(folder); // la vision des onglets d'un navigateur à une fenêtre, avant le code de l'extension
            var installed = await profile.AddBrowserExtensionAsync(folder);
            if (installed.Id != id)
            {
                await installed.RemoveAsync();
                throw new InvalidOperationException("L’identité de l’extension a changé. Installez sa version signée depuis le store pour préserver son accès natif.");
            }
            await installed.EnableAsync(wasEnabled);
            await SharedExtensions.SetAsync(id, wasEnabled);
            await _pages.SyncExtensionsAsync(reloadId: id);
            _pages.ApplySettings();
            await RefreshExtensionsAsync();
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or JsonException or System.Runtime.InteropServices.COMException)
        {
            if (backup is not null && destination is not null)
            {
                try
                {
                    ExtensionCatalog.CopyTo(backup, destination);
                    await (await profile.AddBrowserExtensionAsync(destination)).EnableAsync(wasEnabled);
                }
                catch (Exception restore) when (restore is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or COMException)
                { DebugLog.Write("extension : restauration incomplète, copie conservée : " + restore.Message); backup = null; }
            }
            if (quiet) DebugLog.Write("extension : mise à jour refusée : " + e.Message);
            else Notice(T("Extension refusée", "Extension rejected"), T($"Le moteur n'a pas pu charger cette extension : {e.Message}", $"The engine could not load this extension: {e.Message}"));
            return false;
        }
        finally
        {
            try { if (backup is not null && Directory.Exists(backup)) Directory.Delete(backup, true); }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { DebugLog.Write("extension ancienne copie : " + cleanup.Message); }
            _extensionChanges.Release();
        }
    }

    private Task AddExtensionAsync(string id) =>
        _extensionOffers.TryGetValue(id, out var offer) ? InstallExtensionAsync(offer.Folder, offer.Id) : Task.CompletedTask;

    private async Task AddExtensionFolderAsync()
    {
        var picker = new Windows.Storage.Pickers.FolderPicker { SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Downloads };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(_window));
        if (await picker.PickSingleFolderAsync() is not { } folder) return;
        if (ExtensionCatalog.Read(folder.Path, "") is null)
        {
            Notice(T("Ce n'est pas une extension", "Not an extension"), T("Ce dossier ne contient pas de fichier manifest.json d'extension.", "This folder has no extension manifest.json file."));
            return;
        }
        await InstallExtensionAsync(folder.Path, knownId: null);
    }

    private async Task ChangeExtensionAsync(string id, bool remove)
    {
        if (AppPaths.Private || EngineProfile is not { } profile || !ExtensionCatalog.IsId(id) || id == AdBlock.ExtensionId) return;
        await _extensionChanges.WaitAsync();
        try
        {
            var state = await SharedExtensions.SnapshotAsync();
            if (!state.TryGetValue(id, out var enabled) || enabled is null) return;
            await SharedExtensions.SetAsync(id, remove ? null : !enabled.Value);
            await _pages.SyncExtensionsAsync(reloadId: !remove && !enabled.Value ? id : null);
            ExtensionPopup?.Hide();
            _pages.ApplySettings();
            await RefreshExtensionsAsync();
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException or ArgumentException)
        { Notice(T("Extension indisponible", "Extension unavailable"), e.Message); }
        finally { _extensionChanges.Release(); }
    }

    // Profils : chacun a ses données. Le panneau en change, en crée ou en supprime.
    private bool _profilesOpen;

    private string ProfilesJson() => JsonSerializer.Serialize(new
    {
        popup = "profiles",
        // Les sessions isolées des autres fenêtres ne sont pas des profils où l'on revient.
        items = Profiles.List(AppPaths.BaseDir).Where(id => id == AppPaths.Profile || !Profiles.IsTemporary(AppPaths.BaseDir, id)).Select(id => new
        {
            id,
            name = ProfileName(id),
            current = id == AppPaths.Profile,
            // Ni le profil par défaut, ni celui de cette fenêtre, ni un profil ouvert dans une autre fenêtre.
            renamable = id != Profiles.Default && !Profiles.IsTemporary(AppPaths.BaseDir, id),
            deletable = id != Profiles.Default && id != AppPaths.Profile && !IsProfileOpen(id),
        }),
    });

    private void OpenProfileMenu(double x, double y)
    {
        if (ClosesPanel("profile")) return;
        HidePopup();
        _profilesOpen = true;
        _popup.Show(x - Scaled(300), y, 300, ProfilesJson(), focus: true);
    }

    private void AskProfileName() =>
        AskName(T("Nouveau profil", "New profile"), T("Ses onglets, son historique, ses favoris et ses mots de passe restent séparés des autres profils.", "Its tabs, history, bookmarks and passwords stay separate from other profiles."),
            T("Nom du profil", "Profile name"), T("Créer et ouvrir", "Create and open"), "", "profile.create");

    private void DeleteProfile(string id)
    {
        if (id == AppPaths.Profile || IsProfileOpen(id)) return;
        try { Profiles.Delete(AppPaths.BaseDir, id); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* fichiers encore utilisés : le profil reste */ }
        if (_profilesOpen) _popup.Post(ProfilesJson());
    }

    private static bool IsProfileOpen(string id) => App.IsProfileOpen(id);

    // Changer de profil : une fenêtre du nouveau profil prend la place de celle-ci, au même endroit.
    // (Les données d'un profil sont fixées au démarrage du moteur : on ne peut pas en changer en cours de route.)
    private void SwitchProfile(string id) => OpenProfile(id, replace: true);

    // replace : la nouvelle fenêtre prend la place de celle-ci. Sinon elle s'ouvre à côté, légèrement décalée.
    private void OpenProfile(string id, bool replace, bool isPrivate = false)
    {
        if (id == AppPaths.Profile || !Profiles.List(AppPaths.BaseDir).Contains(id)) return;
        if (Environment.ProcessPath is not { } exe) return;
        var app = _window.AppWindow;
        var shift = replace ? 0 : 36;
        var start = new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = false };
        start.ArgumentList.Add($"--profile={id}");
        if (isPrivate) start.ArgumentList.Add("--private");
        start.ArgumentList.Add($"--bounds={app.Position.X + shift},{app.Position.Y + shift},{app.Size.Width},{app.Size.Height}");
        if (replace && Presenter?.State == OverlappedPresenterState.Maximized) start.ArgumentList.Add("--maximized");
        if (replace && IsFullScreen) start.ArgumentList.Add("--fullscreen");
        // La nouvelle fenêtre repart de l'environnement d'origine, pas de celui que ce profil s'est réglé.
        foreach (var (variable, value) in App.LaunchEnvironment)
        {
            if (value is null) start.Environment.Remove(variable);
            else start.Environment[variable] = value;
        }
        try { System.Diagnostics.Process.Start(start); }
        catch (System.ComponentModel.Win32Exception) { return; } // rien ne s'est ouvert : on garde cette fenêtre
        if (replace) _ = CloseWithSessionAsync();
    }

    // Bouton de capture d'écran de la barre d'outils.
    private void OpenCaptureMenu()
    {
        if (_capture.Busy) return;
        if (CapturePanel?.IsOpen == true) { CapturePanel.Hide(); return; }
        HidePopup();
        CapturePanel?.ShowBottom(640, JsonSerializer.Serialize(new { @event = "capture-open", mode = "visible", device = _tabs.ActiveId is { } shown && _pages.DeviceOf(shown) is not null }));
    }

    private void Capture(string mode, bool browser = false)
    {
        var tab = _tabs.Tabs.FirstOrDefault(t => t.Id == _tabs.ActiveId);
        if (browser) _ = _capture.RunWindowAsync(null, mode, T("du navigateur", "of browser"));
        // Appareil simulé : la page seule n'a pas sa coque. La capture prend ce que la fenêtre en affiche.
        else if (tab is not null && mode != "full" && _pages.DeviceArea(tab.Id) is { } devices) _ = _capture.RunWindowAsync(devices, mode, tab.Title);
        else if (tab is not null && _pages.CoreOf(tab.Id) is { } core) _ = _capture.RunAsync(core, mode, tab.Title);
    }

    // Clic droit dans une page : le moteur fournit ses commandes (copier, coller, inspecter…), on les affiche dans notre vue.
    private static readonly HashSet<string> NativeCommands =
    [
        "back", "forward", "reload", "cut", "copy", "paste", "selectAll",
        "copyLinkLocation", "saveLinkAs", "copyImage", "copyImageLocation", "saveImageAs",
        "saveAs", "print", "viewPageSource", "inspectElement", "spellCheck",
    ];

    private static string DeviceLabel(string name) => name switch
    {
        "iPhone Duo cover" => T("iPhone Duo (plié)", "iPhone Duo (folded)"),
        "Desktop" => T("Ordinateur", "Desktop"),
        PageHost.Comparison => T("Comparaison", "Comparison"),
        _ => name,
    };

    // Comparaison d'écrans : de deux à quatre appareils, choisis par l'utilisateur et retenus dans les réglages.
    private const int MinCompared = 2, MaxCompared = 4;

    private void ApplyComparison()
    {
        var known = Device.All.Select(d => d.Name).Where(_settings.CompareDevices.Contains).ToList(); // dans l'ordre du catalogue
        _pages.ComparisonDevices = known.Count is >= MinCompared and <= MaxCompared ? known : Device.DefaultComparison;
    }

    private void AskComparison(string tabId)
    {
        HidePopup();
        _popup.ShowCentered(340, JsonSerializer.Serialize(new
        {
            popup = "compare-pick", id = tabId, min = MinCompared, max = MaxCompared,
            devices = Device.All.Select(d => new { name = d.Name, label = DeviceLabel(d.Name), size = $"{d.Width} × {d.Height}", on = _pages.ComparisonDevices.Contains(d.Name) }),
        }), top: 64);
    }
}
