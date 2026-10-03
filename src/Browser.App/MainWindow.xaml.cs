using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace Browser.App;

public sealed partial class MainWindow : Window
{
    private BrowserController? _controller;

    public MainWindow()
    {
        InitializeComponent();
        Title = "Navigateur";
        // Pas de barre de titre système : l'interface dessine la sienne. La bordure reste, pour le redimensionnement.
        if (AppWindow.Presenter is OverlappedPresenter presenter) presenter.SetBorderAndTitleBar(true, false);
        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        var pages = new PageHost(Pages, PrimaryColumn, SecondaryColumn);
        var bridge = new UiBridge(Ui);
        _controller = new BrowserController(this, pages, bridge);
        await bridge.InitAsync(Path.Combine(AppContext.BaseDirectory, "ui"), _controller.Execute);
    }
}
