using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Velopack;
using Velopack.Sources;

namespace Browser.App;

public static class Program
{
    [STAThread]
    private static void Main()
    {
        // Installation, mise à jour, désinstallation : l'installateur relance Sol avec ses propres arguments, traités
        // ici avant toute fenêtre. Une mise à jour déjà téléchargée est aussi appliquée à ce moment.
        VelopackApp.Build().OnBeforeUninstallFastCallback(_ => WindowsBrowser.Unregister()).Run();

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(p =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App();
        });
    }

/// <summary>Mise à jour en cours : la version, l'avancement du téléchargement (0 à 100), et si elle est prête à s'installer.</summary>
    public sealed record UpdateState(string Version, int Percent, bool Ready);

    public static UpdateState? Update { get; private set; }
    /// <summary>L'état a changé. Appelé hors du fil de l'interface.</summary>
    public static event Action? UpdateChanged;

    private static UpdateManager? s_manager;
    private static UpdateInfo? s_update;

    private static void Set(UpdateState state)
    {
        if (state == Update) return;
        Update = state;
        UpdateChanged?.Invoke();
    }

    /// <summary>Où en est la recherche de mise à jour : « idle », « checking », « current » (à jour), « failed »,
    /// ou « portable » (copie non installée, qui ne se met pas à jour).</summary>
    public static string Check { get; private set; } = "idle";

    private static void SetCheck(string status)
    {
        if (status == Check) return;
        Check = status;
        UpdateChanged?.Invoke();
    }

    private static readonly SemaphoreSlim s_checking = new(1, 1);

    /// <summary>Au lancement, puis toutes les six heures tant que Sol reste ouvert : il n'y a pas besoin de le fermer
    /// pour apprendre qu'une version est sortie.</summary>
    public static async Task UpdateInBackgroundAsync()
    {
        while (true)
        {
            await CheckForUpdateAsync();
            if (Update is { Ready: true } || Check == "portable") return;
            await Task.Delay(TimeSpan.FromHours(6));
        }
    }

    /// <summary>Cherche une version plus récente dans les publications GitLab (projet stevencrdkoulo/sol-browser) et la
    /// télécharge. Elle s'installe quand l'utilisateur redémarre Sol depuis l'encart ou les réglages, ou sinon au
    /// prochain lancement. Appelée aussi par le bouton « Vérifier » des réglages. Une copie non installée (archive
    /// zip, poste de développement) ne se met pas à jour.</summary>
    public static async Task CheckForUpdateAsync()
    {
        if (Update is { Ready: true } || !await s_checking.WaitAsync(0)) return; // déjà prête, ou une recherche est en cours
        try
        {
            // Diagnostic : NAVIGATEUR_UPDATE_PREVIEW=1.2.3 joue un téléchargement factice, pour voir l'encart sans publier de version.
            if (Environment.GetEnvironmentVariable("NAVIGATEUR_UPDATE_PREVIEW") is { Length: > 0 } preview)
            {
                SetCheck("checking");
                await Task.Delay(1500);
                for (var percent = 0; percent <= 100; percent += 10) { Set(new UpdateState(preview, percent, false)); await Task.Delay(1500); }
                Set(new UpdateState(preview, 100, true));
                return;
            }
            var manager = new UpdateManager(new GitlabSource("https://gitlab.com/api/v4/projects/87215621", null, false));
            if (!manager.IsInstalled) { SetCheck("portable"); return; }
            // Copie installée : Windows la connaît comme navigateur (« Ouvrir avec », applications par défaut).
            if (Environment.ProcessPath is { } exe) WindowsBrowser.Register(exe);
            SetCheck("checking");
            if (await manager.CheckForUpdatesAsync() is not { } update) { SetCheck("current"); return; }
            var version = update.TargetFullRelease.Version.ToString();
            Set(new UpdateState(version, 0, false));
            await manager.DownloadUpdatesAsync(update, percent => Set(new UpdateState(version, Math.Clamp(percent, 0, 100), false)));
            (s_manager, s_update) = (manager, update);
            Set(new UpdateState(version, 100, true));
        }
        catch (Exception e)
        {
            DebugLog.Write($"mise à jour : {e.Message}");
            if (Update is { Ready: false }) Update = null; // téléchargement interrompu : rien à proposer
            Check = "failed";
            UpdateChanged?.Invoke();
        }
        finally { s_checking.Release(); }
    }

    /// <summary>Installe la mise à jour téléchargée et relance Sol. À appeler une fois la session enregistrée.</summary>
    public static void RestartToUpdate()
    {
        if (s_manager is { } manager && s_update is { } update) manager.ApplyUpdatesAndRestart(update);
    }
}
