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
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
