using System.Security.Cryptography;
using System.Text.Json;
using Browser.Core;

namespace Browser.App;

// Compte Sol et sauvegarde en ligne. Le profil est scellé sur l'appareil (voir Backup) avant de partir : le service ne
// reçoit que du chiffré. La session et l'empreinte du mot de passe du compte restent protégées pour l'utilisateur Windows.
public sealed partial class BrowserController
{
    // Adresses publiques du projet Neon : l'accès aux données dépend du compte, jamais d'un secret livré dans Sol.
    private static readonly CloudAccount Cloud = new(new HttpClient { Timeout = TimeSpan.FromMinutes(10) },
        Environment.GetEnvironmentVariable("NAVIGATEUR_CLOUD_AUTH") ?? "https://ep-plain-firefly-b84mlktb.neonauth.c-14.us-east-1.aws.neon.tech/neondb/auth",
        Environment.GetEnvironmentVariable("NAVIGATEUR_CLOUD_API") ?? "https://br-wispy-night-b89mzp5p-backup.compute.c-14.us-east-1.aws.neon.tech");
    private const int CloudKeyLength = 12; // le fichier est hors de la machine : la clé doit résister à une attaque hors ligne
    // AutoKey : la clé gardée pour la sauvegarde automatique, protégée comme le reste de ce fichier par Windows.
    private sealed record CloudSession(string Email, string Cookie, byte[] Salt, byte[] PasswordHash, string? AutoKey = null, DateTimeOffset? LastBackup = null);
    private CloudSession? _cloud;
    private bool _cloudLoaded, _cloudBusy, _cloudUpload;
    private string? _cloudError, _cloudNotice;
    private IReadOnlyList<CloudBackup> _cloudBackups = [];
    private static string CloudFile => Path.Combine(AppPaths.DataDir, "cloud-session.bin");

    private object CloudJson()
    {
        LoadCloud();
        return new
        {
            email = _cloud?.Email, busy = _cloudBusy, error = _cloudError, notice = _cloudNotice, auto = _cloud?.AutoKey is not null,
            last = _cloud?.LastBackup?.LocalDateTime.ToString("g", System.Globalization.CultureInfo.GetCultureInfo(Language.Current)),
            backups = _cloudBackups.Select(b => new { id = b.Id, device = b.Device, bytes = b.Bytes, date = b.CreatedAt.LocalDateTime.ToString("g", System.Globalization.CultureInfo.GetCultureInfo(Language.Current)) }),
        };
    }

    private void LoadCloud()
    {
        if (_cloudLoaded || AppPaths.Private) return;
        _cloudLoaded = true;
        try { if (File.Exists(CloudFile)) _cloud = JsonSerializer.Deserialize<CloudSession>(ProtectedData.Unprotect(File.ReadAllBytes(CloudFile), null, DataProtectionScope.CurrentUser)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException or JsonException) { DebugLog.Write($"cloud.session {e.GetType().Name}"); }
    }

    private void SaveCloud()
    {
        try
        {
            if (_cloud is null) File.Delete(CloudFile);
            else
            {
                File.WriteAllBytes(CloudFile + ".tmp", ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(_cloud), null, DataProtectionScope.CurrentUser));
                File.Move(CloudFile + ".tmp", CloudFile, true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException) { DebugLog.Write($"cloud.save {e.GetType().Name}"); }
    }

    private bool CloudCommand(Command c)
    {
        if (!c.Type.StartsWith("cloud.", StringComparison.Ordinal)) return false;
        if (AppPaths.Private) return true;
        LoadCloud();
        switch (c.Type)
        {
            case "cloud.sign": _ = CloudRunAsync(() => SignInCloudAsync(c.Query!.Trim(), c.Input!, c.Id == "up")); break;
            case "cloud.signout": _cloud = null; _cloudBackups = []; _cloudError = _cloudNotice = null; SaveCloud(); PostCloud(); break;
            case "cloud.refresh": if (_cloud is not null) _ = CloudRunAsync(RefreshCloudAsync); else PostCloud(); break;
            case "cloud.backup":
                if (_cloud is null || _cloudBusy) break;
                _cloudUpload = true;
                AskName(T("Clé de sauvegarde", "Backup key"),
                    T($"Au moins {CloudKeyLength} caractères, différente du mot de passe du compte. Personne ne peut la retrouver : sans elle, la sauvegarde est perdue.", $"At least {CloudKeyLength} characters, different from your account password. Nobody can recover it: without it, the backup is lost."),
                    T("Clé de sauvegarde", "Backup key"), T("Sauvegarder", "Back up"), "", "backup.export.key", secret: true);
                break;
            case "cloud.restore": _ = CloudRunAsync(() => DownloadCloudAsync(c.Id!)); break;
            case "cloud.auto" or "cloud.auto.key" or "cloud.restore.key": CloudAutoCommand(c); break;
            case "cloud.delete": _ = CloudRunAsync(async () => { await Cloud.DeleteAsync(_cloud!.Cookie, c.Id!); await RefreshCloudAsync(); }); break;
        }
        return true;
    }

    private void PostCloud()
    {
        if (_settingsOpen) _popup.Post(SettingsJson());
        if (_welcome.IsOpen) _welcome.Post(JsonSerializer.Serialize(new { welcome = "cloud", cloud = CloudJson() }));
    }

    private async Task CloudRunAsync(Func<Task> work)
    {
        if (_cloudBusy) return;
        _cloudBusy = true; _cloudError = _cloudNotice = null; PostCloud();
        try { await work(); }
        catch (CloudException e)
        {
            DebugLog.Write($"cloud.failed {e.Code}");
            _cloudError = e.Code;
            if (e.Code == "signed-out") { _cloud = null; _cloudBackups = []; SaveCloud(); }
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or CryptographicException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        { DebugLog.Write($"cloud.failed {e.GetType().Name} {e.Message}"); _cloudError = "server"; }
        finally { _cloudBusy = false; PostCloud(); }
    }

    private static byte[] PasswordHash(string password, byte[] salt) => Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);

    private async Task SignInCloudAsync(string email, string password, bool create)
    {
        var cookie = await Cloud.SignInAsync(email, password, create);
        var salt = RandomNumberGenerator.GetBytes(16);
        _cloud = new(email, cookie, salt, PasswordHash(password, salt));
        SaveCloud();
        await RefreshCloudAsync();
    }

    private async Task RefreshCloudAsync() { if (_cloud is { } session) _cloudBackups = await Cloud.ListAsync(session.Cookie); }

    // Le service voit passer le mot de passe du compte : le réutiliser comme clé annulerait le chiffrement.
    private string? CloudKeyProblem(string key)
    {
        if (key.Length < CloudKeyLength) return T($"La clé doit contenir au moins {CloudKeyLength} caractères.", $"The key needs at least {CloudKeyLength} characters.");
        if (_cloud is { } s && CryptographicOperations.FixedTimeEquals(PasswordHash(key, s.Salt), s.PasswordHash))
            return T("La clé doit être différente du mot de passe du compte.", "The key must differ from your account password.");
        return null;
    }

    /// <param name="quiet">Sauvegarde automatique : aucun message si tout va bien.</param>
    private async Task UploadCloudAsync(string key, bool quiet = false)
    {
        if (CloudKeyProblem(key) is { } problem) { Notice(T("Clé refusée", "Key rejected"), problem); return; }
        if (!quiet) HidePopup();
        PasswordBackupDocument? passwords = null;
        await CloudRunAsync(async () =>
        {
            var result = await SealProfileAsync(key);
            var sealedFile = result.File;
            passwords = result.Passwords;
            await Cloud.UploadAsync(_cloud!.Cookie, sealedFile, Environment.MachineName);
            if (_cloud is { } session) { _cloud = session with { LastBackup = DateTimeOffset.UtcNow }; SaveCloud(); }
            await RefreshCloudAsync();
            _cloudNotice = "saved";
        });
        if (quiet && passwords is { Unreadable: > 0 } && _cloudNotice == "saved") Notice(T("Sauvegarde automatique incomplète", "Incomplete automatic backup"), PasswordBackupSummary(passwords));
        if (quiet) { if (_cloudError is "signed-out") Notice(T("Sauvegarde automatique arrêtée", "Automatic backup stopped"), CloudErrorText("signed-out")); return; }
        if (_cloudNotice == "saved") Notice(T("Sauvegarde envoyée", "Backup sent"), T("Réglages, onglets, favoris, historique, notes, mots de passe et extensions, chiffrés sur cet appareil avec votre clé.", "Settings, tabs, bookmarks, history, notes, passwords and extensions, encrypted on this device with your key.") + " " + PasswordBackupSummary(passwords!));
        else if (_cloudError is { } error) Notice(T("Sauvegarde impossible", "Backup failed"), CloudErrorText(error));
    }

    private async Task DownloadCloudAsync(string id)
    {
        var bytes = await Cloud.DownloadAsync(_cloud!.Cookie, id);
        var folder = Path.Combine(AppPaths.DataDir, "import");
        Directory.CreateDirectory(folder);
        _restoreFile = Path.Combine(folder, $"cloud-{Guid.NewGuid():N}.solbackup");
        await File.WriteAllBytesAsync(_restoreFile, bytes);
        AskBackupKey(export: false);
    }

    private static string CloudErrorText(string code) => code switch
    {
        "signed-out" => T("Votre session a expiré. Reconnectez-vous.", "Your session expired. Sign in again."),
        "network" => T("Connexion au service impossible.", "Could not reach the service."),
        "too-large" => T("La sauvegarde dépasse 256 Mo.", "The backup exceeds 256 MB."),
        _ => T("Le service a refusé la demande. Réessayez.", "The service rejected the request. Try again."),
    };

    private static string PasswordBackupSummary(PasswordBackupDocument passwords) =>
        T($"Mots de passe exportés : {passwords.Exported}. Illisibles et omis : {passwords.Unreadable}.",
          $"Passwords exported: {passwords.Exported}. Unreadable and omitted: {passwords.Unreadable}.")
        + (passwords.Unreadable > 0 ? T(" Exportez les entrées manquantes en CSV depuis leur navigateur puis importez-les dans Sol.", " Export the missing entries as CSV from their browser, then import them into Sol.") : "");

    private async Task<(byte[] File, PasswordBackupDocument Passwords)> SealProfileAsync(string key)
    {
        SaveSession();
        JsonFileStore.Save(AppPaths.Settings, _settings);
        var temp = Path.Combine(AppPaths.DataDir, "import");
        Directory.CreateDirectory(temp);
        var copy = Path.Combine(temp, $"backup-{Guid.NewGuid():N}.db");
        _db.BackupTo(copy);
        return await Task.Run(() =>
        {
            try
            {
                var files = Backup.Collect(AppPaths.DataDir);
                files["browser.db"] = File.ReadAllBytes(copy);
                var sessions = files.TryGetValue("tab-sessions.json", out var sessionJson)
                    ? JsonSerializer.Deserialize<List<TabSession>>(sessionJson) ?? [] : [];
                var passwords = PasswordBackup.Collect(EngineData, sessions, temp, AppPaths.PendingPasswords);
                files[Backup.Passwords] = JsonSerializer.SerializeToUtf8Bytes(passwords);
                return (Backup.Seal(files, key), passwords);
            }
            finally { File.Delete(copy); }
        });
    }
}
