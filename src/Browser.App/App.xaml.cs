using Browser.Core;
using Microsoft.UI.Xaml;

namespace Browser.App;

public partial class App : Application
{
    private const string WebDataVariable = "WEBVIEW2_USER_DATA_FOLDER", WebArgsVariable = "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS";

    /// <summary>Environnement tel que reçu au lancement, avant nos réglages : celui qu'on transmet à une autre fenêtre.</summary>
    public static IReadOnlyDictionary<string, string?> LaunchEnvironment { get; private set; } = new Dictionary<string, string?>();

    private static Mutex? s_profileLock;
    private Window? _window;

    public App()
    {
        LaunchEnvironment = new Dictionary<string, string?>
        {
            [WebDataVariable] = Environment.GetEnvironmentVariable(WebDataVariable),
            [WebArgsVariable] = Environment.GetEnvironmentVariable(WebArgsVariable),
        };

        // Une seule fenêtre par profil : deux fenêtres sur les mêmes fichiers se marcheraient dessus.
        s_profileLock = new Mutex(initiallyOwned: true, $"Navigateur.Profil.{AppPaths.Profile}", out var first);
        if (!first) Environment.Exit(0);

        // Données des sites (cookies, cache, mots de passe) : un dossier par profil. Seul le profil par défaut
        // accepte un dossier imposé par l'environnement de développement.
        if (AppPaths.Profile != Profiles.Default || string.IsNullOrEmpty(LaunchEnvironment[WebDataVariable]))
            Environment.SetEnvironmentVariable(WebDataVariable, AppPaths.WebData);

        // Options du moteur, ajoutées à celles déjà demandées par l'environnement :
        // - barres de défilement fines qui n'apparaissent qu'au défilement (quand le réglage les affiche) ;
        // - retour et avance par balayage horizontal à deux doigts sur le pavé tactile.
        const string features = "--enable-features=OverlayScrollbar,msOverlayScrollbarWinStyle,msOverlayScrollbarWinStyleAnimation,OverscrollHistoryNavigation";
        Environment.SetEnvironmentVariable(WebArgsVariable, $"{LaunchEnvironment[WebArgsVariable]} {features}".Trim());

        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
