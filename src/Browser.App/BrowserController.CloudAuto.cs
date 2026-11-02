using System.Text.Json;
using Browser.Core;
using Microsoft.UI.Dispatching;

namespace Browser.App;

// Sauvegarde automatique (une fois par 24 h, au démarrage puis tant que Sol reste ouvert) et restauration complète depuis
// l'accueil de première ouverture.
public sealed partial class BrowserController
{
    private static readonly TimeSpan CloudAutoEvery = TimeSpan.FromHours(24);
    private DispatcherQueueTimer? _cloudAutoTimer;

    private void StartCloudAuto()
    {
        if (AppPaths.Private) return;
        _cloudAutoTimer = _window.DispatcherQueue.CreateTimer();
        // Premier passage peu après le démarrage, une fois la fenêtre et ses onglets prêts ; ensuite toutes les 30 minutes.
        _cloudAutoTimer.Interval = TimeSpan.FromMinutes(2);
        _cloudAutoTimer.Tick += (_, _) =>
        {
            _cloudAutoTimer.Interval = TimeSpan.FromMinutes(30);
            LoadCloud();
            if (_closingWindow || _cloudBusy || _cloud is not { AutoKey: { } key } session) return;
            if (session.LastBackup is { } last && DateTimeOffset.UtcNow - last < CloudAutoEvery) return;
            DebugLog.Write("cloud.auto.start");
            _ = UploadCloudAsync(key, quiet: true);
        };
        _cloudAutoTimer.Start();
    }

    private void CloudAutoCommand(Command c)
    {
        switch (c.Type)
        {
            case "cloud.auto" when c.Index == 0:
                if (_cloud is { } session) { _cloud = session with { AutoKey = null }; SaveCloud(); }
                PostCloud();
                break;
            case "cloud.auto":
                if (_cloud is null || _cloudBusy) break;
                AskName(T("Sauvegarde automatique", "Automatic backup"),
                    T($"Votre clé de sauvegarde, au moins {CloudKeyLength} caractères et différente du mot de passe du compte. Sol la garde, chiffrée par Windows pour votre compte, pour sauvegarder seul toutes les 24 h.",
                      $"Your backup key, at least {CloudKeyLength} characters and different from your account password. Sol keeps it, encrypted by Windows for your account, to back up on its own every 24 h."),
                    T("Clé de sauvegarde", "Backup key"), T("Activer", "Turn on"), "", "cloud.auto.key", secret: true);
                break;
            case "cloud.auto.key":
                if (_cloud is not { } current) break;
                if (CloudKeyProblem(c.Title!) is { } problem)
                {
                    // L'accueil affiche l'erreur dans sa page ; ailleurs, une note la signale.
                    if (_welcome.IsOpen) { _cloudError = "key-rule"; PostCloud(); } else Notice(T("Clé refusée", "Key rejected"), problem);
                    break;
                }
                _cloud = current with { AutoKey = c.Title };
                SaveCloud();
                _ = UploadCloudAsync(c.Title!, quiet: _welcome.IsOpen);
                break;
            case "cloud.restore.key":
                _ = CloudRunAsync(() => RestoreFromCloudAsync(c.Id!, c.Title!));
                break;
        }
    }

    // Accueil : la sauvegarde retrouvée remplace tout le profil, puis Sol redémarre pour l'appliquer avant d'ouvrir ses fichiers.
    private async Task RestoreFromCloudAsync(string id, string key)
    {
        var bytes = await Cloud.DownloadAsync(_cloud!.Cookie, id);
        var files = await Task.Run(() => Backup.Open(bytes, key));
        if (files is null) { _cloudError = "key"; return; }
        Backup.Stage(AppPaths.DataDir, files);
        if (files.TryGetValue(Backup.Passwords, out var json) && JsonSerializer.Deserialize<List<ImportedLogin>>(json) is { } logins)
            PasswordImport.Queue(AppPaths.PendingPasswords, logins);
        // La même clé continue les sauvegardes automatiques sur ce PC.
        _cloud = _cloud with { AutoKey = key, LastBackup = DateTimeOffset.UtcNow };
        SaveCloud();
        _settings.Onboarded = true;
        JsonFileStore.Save(AppPaths.Settings, _settings);
        DebugLog.Write("cloud.restore.staged");
        _cloudNotice = "restored";
        _window.DispatcherQueue.TryEnqueue(Relaunch);
    }
}
