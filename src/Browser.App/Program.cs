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

    /// <summary>Cherche une version plus récente dans les publications GitLab (projet stevencrdkoulo/sol-browser) et la
    /// télécharge. Elle s'installe quand l'utilisateur redémarre Sol depuis l'encart ou les réglages, ou sinon au
    /// prochain lancement. Sans effet pour une copie non installée (archive zip, poste de développement).</summary>
    public static async Task UpdateInBackgroundAsync()
    {
        // Diagnostic : NAVIGATEUR_UPDATE_PREVIEW=1.2.3 joue un téléchargement factice, pour voir l'encart sans publier de version.
        if (Environment.GetEnvironmentVariable("NAVIGATEUR_UPDATE_PREVIEW") is { Length: > 0 } preview)
        {
            for (var percent = 0; percent <= 100; percent += 10) { Set(new UpdateState(preview, percent, false)); await Task.Delay(1500); }
            Set(new UpdateState(preview, 100, true));
            return;
        }
        try
        {
            var manager = new UpdateManager(new GitlabSource("https://gitlab.com/api/v4/projects/87215621", null, false));
            if (!manager.IsInstalled) return;
            // Copie installée : Windows la connaît comme navigateur (« Ouvrir avec », applications par défaut).
            if (Environment.ProcessPath is { } exe) WindowsBrowser.Register(exe);
            if (await manager.CheckForUpdatesAsync() is not { } update) return;
            var version = update.TargetFullRelease.Version.ToString();
            Set(new UpdateState(version, 0, false));
            await manager.DownloadUpdatesAsync(update, percent => Set(new UpdateState(version, Math.Clamp(percent, 0, 100), false)));
            (s_manager, s_update) = (manager, update);
            Set(new UpdateState(version, 100, true));
        }
        catch (Exception e)
        {
            DebugLog.Write($"mise à jour : {e.Message}");
            if (Update is { Ready: false }) { Update = null; UpdateChanged?.Invoke(); } // téléchargement interrompu : rien à proposer
        }
    }

    /// <summary>Installe la mise à jour téléchargée et relance Sol. À appeler une fois la session enregistrée.</summary>
    public static void RestartToUpdate()
    {
        if (s_manager is { } manager && s_update is { } update) manager.ApplyUpdatesAndRestart(update);
    }
}
