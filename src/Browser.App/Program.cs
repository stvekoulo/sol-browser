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
        VelopackApp.Build().Run();

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(p =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App();
        });
    }

    /// <summary>Cherche une version plus récente dans les publications GitHub et la télécharge ; elle s'installe au
    /// prochain lancement. Sans effet pour une copie non installée (archive zip, poste de développement).</summary>
    public static async Task UpdateInBackgroundAsync()
    {
        try
        {
            var manager = new UpdateManager(new GithubSource("https://github.com/stvekoulo/sol-browser", null, false));
            if (!manager.IsInstalled) return;
            if (await manager.CheckForUpdatesAsync() is { } update) await manager.DownloadUpdatesAsync(update);
        }
        catch (Exception e) { DebugLog.Write($"mise à jour : {e.Message}"); }
    }
}
