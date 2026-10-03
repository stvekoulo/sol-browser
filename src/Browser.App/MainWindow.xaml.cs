using Browser.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;

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
        var settings = JsonFileStore.Load<Settings>(AppPaths.Settings);
        var blur = settings.BlurEnabled
            ? File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "inject", "blur.js"))
            : null;

        var pages = new PageHost(Pages, PrimaryColumn, SecondaryColumn, blur);
        var bridge = new UiBridge(Ui);
        _controller = new BrowserController(this, pages, bridge, settings);
        AddShortcuts();
        await bridge.InitAsync(Path.Combine(AppContext.BaseDirectory, "ui"), _controller.Execute);
    }

    private void AddShortcuts()
    {
        const VirtualKeyModifiers ctrl = VirtualKeyModifiers.Control, shift = VirtualKeyModifiers.Shift, alt = VirtualKeyModifiers.Menu;
        Shortcut(VirtualKey.T, ctrl, "tab.open");
        Shortcut(VirtualKey.W, ctrl, "tab.close");
        Shortcut(VirtualKey.T, ctrl | shift, "tab.reopen");
        Shortcut(VirtualKey.L, ctrl, "address.focus");
        Shortcut(VirtualKey.R, ctrl, "nav.reload");
        Shortcut(VirtualKey.F5, VirtualKeyModifiers.None, "nav.reload");
        Shortcut(VirtualKey.Tab, ctrl, "tab.next");
        Shortcut(VirtualKey.Tab, ctrl | shift, "tab.previous");
        Shortcut(VirtualKey.Left, alt, "nav.back");
        Shortcut(VirtualKey.Right, alt, "nav.forward");
    }

    private void Shortcut(VirtualKey key, VirtualKeyModifiers modifiers, string action)
    {
        var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
        accelerator.Invoked += (_, e) =>
        {
            e.Handled = true;
            if (action == "address.focus") Ui.Focus(FocusState.Programmatic);
            _controller?.Shortcut(action);
        };
        Root.KeyboardAccelerators.Add(accelerator);
    }
}
