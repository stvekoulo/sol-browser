using Browser.Core;
using Microsoft.UI.Xaml;

namespace Browser.App;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        // Données des sites (cookies, cache) dans le dossier de l'application, sauf si l'environnement de dev l'a déjà fixé.
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER")))
            Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", Path.Combine(AppPaths.DataDir, "webview2"));

        // Options du moteur, ajoutées à celles déjà demandées par l'environnement :
        // - barres de défilement fines qui n'apparaissent qu'au défilement (quand le réglage les affiche) ;
        // - retour et avance par balayage horizontal à deux doigts sur le pavé tactile.
        const string features = "--enable-features=OverlayScrollbar,msOverlayScrollbarWinStyle,msOverlayScrollbarWinStyleAnimation,OverscrollHistoryNavigation";
        var existing = Environment.GetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS");
        Environment.SetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS", $"{existing} {features}".Trim());

        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
