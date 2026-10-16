using System.Runtime.InteropServices;
using System.Text.Json;
using Browser.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace Browser.App;

public sealed partial class BrowserController
{
    private bool ExecuteCommands1(Command c)
    {
        var id = c.Id ?? "";
        switch (c.Type)
        {
            case "settings.set":
                if (id == "aiThinking")
                {
                    if (c.Index is not (0 or 1)) return true;
                    _settings.SolAiThinking = c.Index == 1; JsonFileStore.Save(AppPaths.Settings, _settings);
                    if (_settingsOpen) _popup.Post(SettingsJson());
                    PublishAi(); return true;
                }
                if (id == "aiKeepMinutes")
                {
                    if (c.Index is not (>= 0 and <= 10080)) return true;
                    _settings.SolAiKeepMinutes = c.Index.Value; _ai.SetKeepMinutes(c.Index.Value);
                    JsonFileStore.Save(AppPaths.Settings, _settings);
                    if (_settingsOpen) _popup.Post(SettingsJson());
                    return true;
                }
                if (id is "aiTone" or "aiLength" or "aiAdaptation" or "aiReset")
                {
                    if (c.Index is not (0 or 1 or 2)) return true;
                    if (id == "aiTone") _settings.SolAiTone = c.Index.Value;
                    else if (id == "aiLength") _settings.SolAiLength = c.Index.Value;
                    else if (id == "aiAdaptation") _settings.SolAiAdaptation = c.Index == 1;
                    else { _settings.SolAiTone = 0; _settings.SolAiLength = 0; _settings.SolAiAdaptation = false; }
                    JsonFileStore.Save(AppPaths.Settings, _settings);
                    if (_settingsOpen) _popup.Post(SettingsJson());
                    return true;
                }
                if (id == "spelling")
                {
                    if (c.Index is not (0 or 1 or 2 or 3)) return true;
                    _settings.Spelling = c.Index.Value;
                    JsonFileStore.Save(AppPaths.Settings, _settings);
                    _pages.ApplySettings();
                    if (_settingsOpen) _popup.Post(SettingsJson());
                    return true;
                }
                if (id == "uiScale")
                {
                    if (c.Index is not (0 or 1 or 2)) return true;
                    _settings.UiScale = new[] { 1d, 0.9, 0.8 }[c.Index.Value];
                    JsonFileStore.Save(AppPaths.Settings, _settings);
                    InterfaceScaleChanged?.Invoke();
                    _bridge.BroadcastTheme();
                    _popup.Rescale();
                    if (_paletteItems is not null) _popup.Post(PaletteJson());
                    if (_settingsOpen) _popup.Post(SettingsJson());
                    return true;
                }
                if (id == "theme") SetTheme(c.Index!.Value);
                else if (id == "language") SetLanguage(c.Index!.Value);
                else if (id == "font") SetFont(c.Index!.Value);
                else if (id == "wallpaper") SetWallpaper(c.Index!.Value);
                else if (id == "wallpaperDaily") SetWallpaperDaily(c.Index != 0);
                else if (id == "engine") SetEngine(c.Index!.Value);
                else SetSetting(id, c.Index != 0);
                return true;
            case "settings.text": if (id == "home") SetHomePage(c.Input!); return true;
            case "dock.ready": _dock.Ready(); return true;
            case "dock.size":
                if (!_dockShown && (_updateDismissed || Program.Update is null)) return true;
                _dock.SetSize(c.Width!.Value, c.Height!.Value);
                return true;
            case "shortcut.menu": OpenShortcutMenu(c.Index!.Value, c.X!.Value, c.Y!.Value); return true;
            case "shortcut.update": UpdateShortcut(id, c.Title!, c.Input!); break;
            case "shortcut.open": OpenShortcut(c.Index!.Value); break;
            case "network.toggle": ToggleNetwork(); return true;
            case "devtools.open": OpenDevTools(); return true;
            case "network.pause" or "network.preserve":
                if (_network.Shown == id && c.Index is 0 or 1) _network.Configure(id, c.Type == "network.pause" ? "pause" : "preserve", c.Index == 1);
                return true;
            case "network.export": if (_network.Shown == id) _ = ExportNetworkAsync(id); return true;
            // Le panneau vient d'être créé : il reçoit les couleurs de la page (envoyées avant qu'il n'écoute), puis sa liste.
            case "network.ready": _bridge.BroadcastTheme(); _network.Post(); return true;
            case "network.clear": if (_network.Shown is { } listed) _network.Clear(listed); return true;
            case "network.replay": if (_network.Shown == id) _ = _network.ReplayAsync(id, c.Input!); return true;
            case "network.cancel": if (_network.Shown == id) _network.CancelReplay(id); return true;
            case "network.mock" or "network.unmock":
                if (_network.Shown == id && _pages.CoreOf(id) is { } mockCore) _ = _network.MockAsync(id, mockCore, c.Type == "network.mock" ? c.Input : null);
                return true;
            case "network.detail":
                if (_network.Shown is { } inspected && _pages.CoreOf(inspected) is { } inspectedCore) _ = _network.PostDetailAsync(inspected, inspectedCore, id);
                return true;
            case "welcome.open": HidePopup(); _ = _welcome.ShowAsync(); return true;
            case "welcome.ready": PostWelcome(); return true;
            case "welcome.name":
                _settings.ProfileName = new string(c.Title!.Where(ch => !char.IsControl(ch)).ToArray()).Trim() is { Length: <= 40 } name ? name : "";
                JsonFileStore.Save(AppPaths.Settings, _settings);
                Refresh();
                return true;
            case "welcome.done":
                _settings.Onboarded = true;
                JsonFileStore.Save(AppPaths.Settings, _settings);
                _welcome.Hide();
                Refresh();
                return true;
            case "import.run": _ = ImportAsync(id, c.Query!); return true;
            case "import.csv": _ = ImportCsvAsync(fromSettings: c.Id == "settings"); return true;
            case "extensions.open": _ = OpenExtensionsAsync(); return true;
            case "extension.add": _ = AddExtensionAsync(id); return true;
            case "extension.folder": _ = AddExtensionFolderAsync(); return true;
            case "extension.store":
                HidePopup(); Open("https://chromewebstore.google.com/"); break;
            case "extension.current":
                if (_tabs.ActiveId is { } storeTab && _pages.CoreOf(storeTab) is { } storeCore)
                    _ = StoreExtension(storeCore.Source) is { } shownId && _installedExtensions.Contains(shownId) ? OpenExtensionsAsync() : InstallStoreExtensionAsync(storeCore.Source);
                return true;
            case "extensions.update": _ = UpdateExtensionsAsync(manual: true); return true;
            case "extension.update": if (ExtensionCatalog.IsId(id) && _extensionUpdates.ContainsKey(id)) _ = OfferStoreExtensionAsync(id); return true;
            case "extension.install": _ = InstallStoreExtensionAsync(c.Title!); return true;
            case "extension.confirm": _ = ConfirmStoreExtensionAsync(id); return true;
            case "extension.pin": _ = PinExtensionAsync(id); return true;
            case "extension.reload": _ = ReloadExtensionAsync(id); return true;
            case "extension.options":
                if (ExtensionCatalog.IsId(id) && ExtensionCatalog.Read(ExtensionFolder(id), id)?.Options is { } options)
                { HidePopup(); Open($"chrome-extension://{id}/{options.TrimStart('/')}"); } break;
            case "extension.toggle": _ = ChangeExtensionAsync(id, remove: false); return true;
            case "extension.remove": _ = ChangeExtensionAsync(id, remove: true); return true;
            case "extensions.menu": _ = OpenExtensionsMenuAsync(c.X!.Value, c.Y!.Value); return true;
            case "extension.show":
                if (ExtensionCatalog.IsId(id) && ExtensionCatalog.Read(ExtensionFolder(id), id) is { } shownExtension)
                {
                    HidePopup();
                    ShowExtension(shownExtension, c.X ?? (_window.Content is FrameworkElement root ? root.ActualWidth - 14 : 1200), c.Y ?? Scaled(58));
                    break;
                }
                return true;
            case "update.later": _updateDismissed = true; UpdateDock(); return true;
            case "update.check": _updateDismissed = false; _ = Program.CheckForUpdateAsync(); return true;
            case "update.restart":
                if (Program.Update is not { Ready: true }) return true;
                SaveSession();
                JsonFileStore.Save(AppPaths.Settings, _settings);
                Program.RestartToUpdate();
                return true;
            case "backup.export": AskBackupKey(export: true); return true;
            case "backup.export.key": _ = ExportBackupAsync(c.Title!); return true;
            case "backup.import": _ = PickBackupAsync(); return true;
            case "backup.restore": RestoreBackup(c.Title!); return true;
            case "device.compare": SetComparison(id, c.Query!); return true;
            case "device.rotate": _pages.RotateDevice(id); return true;
            case "device.set": _pages.SetDevice(id, c.Query!.Length == 0 ? null : c.Query); HidePopup(); return true;
            case "tab.menu": OpenTabMenu(id, c.X!.Value, c.Y!.Value); return true;
            case "session.menu": OpenSessionMenu(id, c.X!.Value, c.Y!.Value); return true;
            case "session.set": ChangeSession(id, c.Query!); return true;
            case "session.new": AskName(T("Nouvelle session", "New session"), T("Cookies, connexions et stockage de site séparés. Historique, favoris et notes restent ceux du profil Sol.", "Separate cookies, sign-ins and site storage. History, bookmarks and notes belong to the Sol profile."), T("Nom de la session", "Session name"), T("Créer", "Create"), "", "session.create", id); return true;
            case "session.create":
                if (_sessions.Create(c.Title!) is { } created && (id.Length > 0 ? id : _tabs.ActiveId) is { } current) ChangeSession(current, created);
                else Notice(T("Session non créée", "Session not created"), T("Utilisez un nom et au maximum 30 sessions.", "Use a name and at most 30 sessions."));
                return true;
            case "session.rename":
                _sessions.Rename(id, c.Title!); HidePopup(); Refresh(); return true;
            case "session.delete": _ = DeleteSessionAsync(id); return true;
            case "permission.answer": AnswerPermission(c.Index != 0); return true;
            case "permissions.forget": _ = ForgetPermissionsAsync(id); return true;
            case "permissions.clear": _ = ForgetPermissionsAsync(null); return true;
            case "site-data.clear": if (_settingsOpen) _ = ClearSiteDataAsync(); return true;
            case "site.reset": _ = ResetSiteAsync(); return true;
            case "profile.menu": OpenProfileMenu(c.X!.Value, c.Y!.Value); return true;
            case "profile.new": AskProfileName(); return true;
            case "profile.create": SwitchProfile(Profiles.Create(AppPaths.BaseDir, c.Title!)); return true;
            case "profile.edit":
                if (id != Profiles.Default && Profiles.List(AppPaths.BaseDir).Contains(id))
                    AskName(T("Renommer le profil", "Rename profile"), "", T("Nom du profil", "Profile name"), T("Renommer", "Rename"), Profiles.NameOf(AppPaths.BaseDir, id), "profile.rename", id);
                return true;
            default: return false;
        }
        HidePopup(); Refresh();
        return true;
    }
}
