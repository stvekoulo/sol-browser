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

        // Lancé par une fenêtre privée qui se ferme (« --cleanup=profil ») : attend que cette fenêtre et son moteur aient
        // rendu les fichiers, efface ses données, puis s'arrête sans rien afficher.
        const string cleanup = "--cleanup=";
        if (Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith(cleanup, StringComparison.Ordinal))?[cleanup.Length..] is { } closing)
        {
            if (Profiles.IsValid(closing))
            {
                var dir = Profiles.DataDir(AppPaths.BaseDir, closing);
                for (var attempt = 0; attempt < 120 && Directory.Exists(dir); attempt++)
                {
                    if (!IsProfileOpen(closing)) Profiles.DeleteClosedTemporary(AppPaths.BaseDir, IsProfileOpen);
                    if (Directory.Exists(dir)) Thread.Sleep(500);
                }
            }
            Environment.Exit(0);
        }

        // Une seule fenêtre par profil : deux fenêtres sur les mêmes fichiers se marcheraient dessus.
        s_profileLock = new Mutex(initiallyOwned: true, $"Navigateur.Profil.{AppPaths.Profile}", out var first);
        if (!first) Environment.Exit(0);

        // Données des sites (cookies, cache, mots de passe) : un dossier par profil. Seul le profil par défaut
        // accepte un dossier imposé par l'environnement de développement.
        if (AppPaths.Profile != Profiles.Default || string.IsNullOrEmpty(LaunchEnvironment[WebDataVariable]))
            Environment.SetEnvironmentVariable(WebDataVariable, AppPaths.WebData);

        // Sauvegarde restaurée pendant la session précédente : ses fichiers prennent la place de ceux du profil.
        if (!AppPaths.Private) Backup.ApplyStaged(AppPaths.DataDir);

        // Mots de passe repris d'un autre navigateur pendant la session précédente : écrits avant que le moteur ne s'ouvre.
        if (!AppPaths.Private)
            PasswordImport.ApplyPending(AppPaths.PendingPasswords, Path.Combine(Environment.GetEnvironmentVariable(WebDataVariable)!, "EBWebView"));

        // Options du moteur, ajoutées à celles déjà demandées par l'environnement :
        // - barres de défilement fines qui n'apparaissent qu'au défilement (quand le réglage les affiche) ;
        // - retour et avance par balayage horizontal à deux doigts sur le pavé tactile.
        // - langue : celle de l'interface, que les sites reçoivent aussi comme langue préférée.
        Language.Current = Language.Resolve(JsonFileStore.Load<Settings>(AppPaths.Settings).Language, System.Globalization.CultureInfo.CurrentUICulture.Name);
        // - un seul processus de rendu par site : les vues de l'interface (barre, carte, menus) en partagent un au lieu
        //   d'en occuper un chacune, et les onglets d'un même site aussi. L'isolation entre sites différents reste entière.
        const string features = "--process-per-site " + "--enable-features=OverlayScrollbar,msOverlayScrollbarWinStyle,msOverlayScrollbarWinStyleAnimation,OverscrollHistoryNavigation";
        Environment.SetEnvironmentVariable(WebArgsVariable, $"{LaunchEnvironment[WebArgsVariable]} {features} --lang={Language.Current}".Trim());

        InitializeComponent();
    }

    public static bool IsProfileOpen(string id)
    {
        if (!Mutex.TryOpenExisting($"Navigateur.Profil.{id}", out var held)) return false;
        held.Dispose();
        return true;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
        if (!AppPaths.Private) _ = Program.UpdateInBackgroundAsync();
    }
}
