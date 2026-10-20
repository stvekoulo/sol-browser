using System.Runtime.InteropServices;
using System.Text.Json;
using Browser.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace Browser.App;

public sealed partial class BrowserController
{

    // Clic droit sur l'en-tête d'un groupe.
    // Fenêtre d'édition d'un groupe : nom, couleur libre, icône. Sert aussi à en créer un autour d'un onglet.
    private (string? TabId, TabGroup? Group)? _groupEditing;
    private void EditGroup(string? tabId, TabGroup? group)
    {
        var members = group is null ? GroupingTabs(tabId ?? "").Select(t => t.Id).ToArray() : [];
        HidePopup();
        _groupCreationTabs = members;
        _groupEditing = (tabId, group); _groupSuggestion = null;
        _popup.ShowCentered(340, GroupEditJson(), top: 64);
    }
    private string GroupEditJson()
    {
        var (tabId, group) = _groupEditing ?? (null, null);
        return JsonSerializer.Serialize(new
        {
            popup = "group-edit",
            create = group is null,
            count = group is null ? _groupCreationTabs.Length : _tabs.Tabs.Count(t => t.GroupId == group.Id),
            id = group?.Id ?? tabId,
            name = group?.Name ?? "",
            color = group?.Color ?? TabManager.GroupColors[_tabs.Groups.Count % TabManager.GroupColors.Count],
            icon = group?.Icon ?? "",
            presets = TabManager.GroupColors,
            // Sol AI propose un nom, une couleur et une icône d'après les onglets du groupe existant.
            ai = group is not null && AiReady,
            suggesting = group is not null && _groupSuggesting == group.Id,
            suggestion = _groupSuggestion is { } s ? new { name = s.Name, color = s.Color, icon = s.Icon } : null,
        });
    }
    private void PublishGroupEditor() { if (_groupEditing is not null && !_closingWindow) _popup.Post(GroupEditJson()); }

    // Action à lancer une fois le menu refermé (ouvrir une saisie depuis un menu, par exemple).
    private Action? _afterMenu;

    // Fenêtre de saisie d'un nom, au milieu de la zone des pages. La réponse revient sous la forme d'une commande.
    private void AskName(string title, string hint, string placeholder, string submit, string value, string command, string? id = null, bool secret = false)
    {
        HidePopup();
        _popup.ShowCentered(340, JsonSerializer.Serialize(new { popup = "prompt", title, hint, placeholder, submit, value, command, id, secret }));
    }

    private void Notice(string title, string hint)
    {
        HidePopup();
        _popup.ShowCentered(340, JsonSerializer.Serialize(new { popup = "notice", title, hint }));
    }

    private async Task ToggleDesignAsync(string id)
    {
        if (_pages.CoreOf(id) is not { } core || !IsWebAddress(core.Source) || core.Source.StartsWith("https://start.sol/") || _pages.IsPdf(id)) return;
        HidePopup();
        var source = core.Source;
        try
        {
            _designScript ??= File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "inject", "design.js"));
            var result = await core.ExecuteScriptAsync("(() => { const designOptions=" + JsonSerializer.Serialize(new { en = Language.Current == "en" }) + ";return (\n" + _designScript + "\n);})()");
            if (core.Source != source) return;
            if (result == "true") { _designTabs.Add(id); await UpdateDesignThemeAsync(id); }
            else _designTabs.Remove(id);
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or COMException)
        { Notice(T("Inspection indisponible", "Inspection unavailable"), T("Réessayez après le chargement de la page.", "Try again after the page has loaded.")); }
    }

    private async Task UpdateDesignThemeAsync(string id)
    {
        if (_readerTheme is null || _pages.CoreOf(id) is not { } core) return;
        try
        {
            await core.ExecuteScriptAsync("document.getElementById('sol-design-view')?.solTheme?.(" + _readerTheme + "," + JsonSerializer.Serialize(_settings.UiFactor / _settings.PageZoom) + ")");
        }
        catch (Exception e) when (e is InvalidOperationException or COMException) { /* document replaced */ }
    }

    private async Task EndDesignOnNavigationAsync(string id)
    {
        if (_pages.CoreOf(id) is not { } core) return;
        try { await core.ExecuteScriptAsync("(() => {const h=document.getElementById('sol-design-view');if(h?.solSource!==location.href)h?.solClose?.();})()"); }
        catch (Exception e) when (e is InvalidOperationException or COMException) { /* document replaced */ }
    }

    private async Task ToggleReaderAsync(string id)
    {
        if (!_readerBusy.Add(id)) return;
        try
        {
            if (_pages.CoreOf(id) is not { } core || !IsWebAddress(core.Source) || core.Source.StartsWith("https://start.sol/") || _pages.IsPdf(id)) return;
            var source = core.Source;
            _readerScript ??= File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "inject", "Readability.js"))
                + "\nreturn (\n" + File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "inject", "reader.js")) + "\n);";
            var options = JsonSerializer.Serialize(new { en = Language.Current == "en" });
            var result = await core.ExecuteScriptAsync("(() => { const readerOptions = " + options + ";" + _readerScript + "; })()");
            if (core.Source != source) return;
            if (result == "true") _reading.Add(id);
            else _reading.Remove(id);
            if (result is not ("true" or "false")) Notice(T("Mode lecteur indisponible", "Reader mode unavailable"), T("Cette page ne contient pas d’article lisible, ou son contenu est trop volumineux.", "This page has no readable article, or its content is too large."));
            Refresh();
            if (result == "true") await UpdateReaderThemeAsync(id);
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or COMException)
        { Notice(T("Lecture impossible", "Could not open reader"), T("Réessayez après le chargement de la page.", "Try again after the page has loaded.")); }
        finally { _readerBusy.Remove(id); }
    }

    private async Task UpdateReaderThemeAsync(string id)
    {
        if (_readerTheme is null || _pages.CoreOf(id) is not { } core) return;
        try
        {
            await core.ExecuteScriptAsync("(() => { const t=" + _readerTheme + "; const h=document.getElementById('sol-reader-view'); if(!h)return; const dark=t.mode==='dark'||(t.mode==='system'?t.systemDark:t.mode==='light'?false:t.dark); h.style.setProperty('--bg',dark?'#121214':'#eef0e2');h.style.setProperty('--fg',dark?'#ececef':'#171719');if(t.accent)h.style.setProperty('--accent',t.accent);h.style.colorScheme=dark?'dark':'light'; })()");
        }
        catch (Exception e) when (e is InvalidOperationException or COMException) { /* navigation replaced the document */ }
    }

    private async Task ReaderSpeechAsync(string id, bool stop)
    {
        if (_pages.CoreOf(id) is not { } core) return;
        var source = core.Source;
        if (!_reading.Contains(id) && !stop) await ToggleReaderAsync(id);
        if (!_reading.Contains(id) || core.Source != source) return;
        HidePopup();
        try
        {
            await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", JsonSerializer.Serialize(new
            {
                expression = $"document.getElementById('sol-reader-view')?.{(stop ? "solStop" : "solListen")}?.()",
                userGesture = !stop, awaitPromise = true,
            }));
        }
        catch (Exception e) when (e is InvalidOperationException or COMException)
        { Notice(T("Lecture indisponible", "Reading unavailable"), T("Réessayez après le chargement de la page.", "Try again after the page has loaded.")); }
    }

    private async Task EndReaderOnNavigationAsync(string id)
    {
        if (_pages.CoreOf(id) is not { } core) return;
        try { await core.ExecuteScriptAsync("(() => {const h=document.getElementById('sol-reader-view');if(h?.solArticle && h.solArticle.url!==location.href){h.solRestore();h.remove();}})()"); }
        catch (Exception e) when (e is InvalidOperationException or COMException) { /* the old document is gone */ }
    }

    private static bool IsWebAddress(string url) =>
        url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase);

    // Dossier du moteur pour ce profil : sa base de mots de passe et la clé qui les chiffre.
    private static string EngineData => Path.Combine(Environment.GetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER") ?? AppPaths.WebData, "EBWebView");

    private async Task<string?> PickFileAsync(string label, string extension)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker { SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(extension);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(_window));
        return (await picker.PickSingleFileAsync())?.Path;
    }

    // Mots de passe exportés en CSV par un autre navigateur : pour ceux qu'il garde pour lui seul sur le disque.
    private async Task ImportCsvAsync(bool fromSettings)
    {
        if (fromSettings) HidePopup();
        if (AppPaths.Private) return;
        IReadOnlyList<ImportedLogin> logins = [];
        string? error = null;
        try
        {
            if (await PickFileAsync("CSV", ".csv") is not { } file) return;
            logins = PasswordImport.ReadCsv(await File.ReadAllTextAsync(file));
            PasswordImport.Queue(AppPaths.PendingPasswords, logins);
            if (logins.Count == 0) error = T("Aucun mot de passe trouvé. Exportez les mots de passe en CSV avec les colonnes URL, identifiant et mot de passe.", "No passwords found. Export passwords as CSV with URL, username and password columns.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or COMException)
        { logins = []; error = T("Le fichier n’a pas pu être importé. Vérifiez son accès et réessayez.", "The file could not be imported. Check access to the file and retry."); }
        _welcome.Post(JsonSerializer.Serialize(new { welcome = "csv", passwords = logins.Count, error }));
        // Depuis les réglages, l'accueil n'est pas à l'écran : le résultat s'affiche dans une fenêtre de message.
        if (fromSettings)
            Notice(T("Mots de passe", "Passwords"), error ?? (logins.Count > 0
                ? T($"{logins.Count} mot{(logins.Count > 1 ? "s" : "")} de passe importé{(logins.Count > 1 ? "s" : "")}. Ils seront disponibles au prochain démarrage de Sol.", $"{logins.Count} password{(logins.Count == 1 ? "" : "s")} imported. They will be available the next time Sol starts.")
                : T("Aucun mot de passe trouvé dans ce fichier.", "No password found in this file.")));
    }

    // Sauvegarde du profil dans un fichier chiffré par une clé que l'utilisateur choisit et garde : Sol ne la retient pas.
    private string? _restoreFile;

    private void AskBackupKey(bool export, bool tooShort = false)
    {
        if (AppPaths.Private) return;
        if (export)
            AskName(T("Clé de sauvegarde", "Backup key"),
                tooShort ? T($"Au moins {Backup.MinKeyLength} caractères.", $"At least {Backup.MinKeyLength} characters.")
                         : T("Choisissez une clé et gardez-la : sans elle, la sauvegarde est illisible, et Sol ne peut pas la retrouver.", "Choose a key and keep it: without it the backup cannot be read, and Sol cannot recover it."),
                T("Clé de sauvegarde", "Backup key"), T("Exporter", "Export"), "", "backup.export.key", secret: true);
        else
            AskName(T("Clé de sauvegarde", "Backup key"), T("La clé choisie au moment de l'export.", "The key chosen when the backup was exported."),
                T("Clé de sauvegarde", "Backup key"), T("Restaurer", "Restore"), "", "backup.restore", secret: true);
    }

    private async Task ExportBackupAsync(string key)
    {
        if (AppPaths.Private) return;
        if (key.Length < Backup.MinKeyLength) { AskBackupKey(export: true, tooShort: true); return; }
        HidePopup();
        var picker = new Windows.Storage.Pickers.FileSavePicker
        {
            SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary,
            SuggestedFileName = $"Sol {DateTime.Now:yyyy-MM-dd}",
        };
        picker.FileTypeChoices.Add(T("Sauvegarde Sol", "Sol backup"), [".solbackup"]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(_window));
        if (await picker.PickSaveFileAsync() is not { } target) return;
        try
        {
            SaveSession();
            JsonFileStore.Save(AppPaths.Settings, _settings);
            var temp = Path.Combine(AppPaths.DataDir, "import");
            Directory.CreateDirectory(temp);
            var copy = Path.Combine(temp, $"backup-{Guid.NewGuid():N}.db");
            _db.BackupTo(copy);
            var (sealedFile, passwords) = await Task.Run(() =>
            {
                var files = new Dictionary<string, byte[]> { ["browser.db"] = File.ReadAllBytes(copy) };
                File.Delete(copy);
                foreach (var name in Backup.ProfileFiles)
                    if (name != "browser.db" && File.Exists(Path.Combine(AppPaths.DataDir, name))) files[name] = File.ReadAllBytes(Path.Combine(AppPaths.DataDir, name));
                var logins = PasswordImport.Read(EngineData, Path.Combine(EngineData, "Default"), temp).Logins;
                files[Backup.Passwords] = JsonSerializer.SerializeToUtf8Bytes(logins);
                return (Backup.Seal(files, key), logins.Count);
            });
            await File.WriteAllBytesAsync(target.Path, sealedFile);
            Notice(T("Sauvegarde exportée", "Backup exported"),
                T($"Réglages, onglets, favoris, historique et {passwords} mot{(passwords > 1 ? "s" : "")} de passe. Gardez le fichier et sa clé.", $"Settings, tabs, bookmarks, history and {passwords} password{(passwords == 1 ? "" : "s")}. Keep the file and its key."));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            Notice(T("Export impossible", "Export failed"), e.Message);
        }
    }

    private async Task PickBackupAsync()
    {
        if (AppPaths.Private) return;
        HidePopup();
        if (await PickFileAsync(T("Sauvegarde Sol", "Sol backup"), ".solbackup") is not { } file) return;
        _restoreFile = file;
        AskBackupKey(export: false);
    }

    private void RestoreBackup(string key)
    {
        if (AppPaths.Private || _restoreFile is not { } file) return;
        Dictionary<string, byte[]>? files = null;
        try { files = Backup.Open(File.ReadAllBytes(file), key); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        if (files is null)
        {
            Notice(T("Restauration impossible", "Restore failed"), T("La clé est incorrecte, ou ce fichier n'est pas une sauvegarde de Sol.", "The key is wrong, or this file is not a Sol backup."));
            return;
        }
        _restoreFile = null;
        try
        {
            Backup.Stage(AppPaths.DataDir, files);
            if (files.TryGetValue(Backup.Passwords, out var json) && JsonSerializer.Deserialize<List<ImportedLogin>>(json) is { } logins)
                PasswordImport.Queue(AppPaths.PendingPasswords, logins);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            Notice(T("Restauration impossible", "Restore failed"), e.Message);
            return;
        }
        Notice(T("Sauvegarde prête", "Backup ready"), T("Fermez puis rouvrez Sol : la sauvegarde remplacera les réglages, onglets, favoris et historique actuels, et ajoutera ses mots de passe.", "Close and reopen Sol: the backup will replace the current settings, tabs, bookmarks and history, and add its passwords."));
    }

    // Extensions. Le moteur les charge depuis un dossier : Sol reprend celles des autres navigateurs de l'ordinateur,
    // ou un dossier désigné, et en garde sa copie dans son dossier de données. Chaque profil de Sol a les siennes.
    private bool _extensionsOpen;

    private ExtensionPopupHost? _extensionPopup;
    /// <summary>Encart flottant où s'ouvre la fenêtre d'une extension.</summary>
    public ExtensionPopupHost? ExtensionPopup
    {
        get => _extensionPopup;
        init { _extensionPopup = value; if (value is not null) value.TabRequested += url => { Open(url); Refresh(); }; }
    }
}
