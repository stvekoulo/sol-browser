using System;
using System.IO;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;

namespace Proto;

public sealed partial class MainWindow : Window
{
    private const double BarHeight = 88;
    private string? _blurScriptId;

    public MainWindow()
    {
        InitializeComponent();
        _ = InitAsync();
    }

    private async System.Threading.Tasks.Task InitAsync()
    {
        // Sans effet en WinUI 3 (microsoft-ui-xaml#6527) : le fond reste celui du thème. Voir les résultats du prototype.
        Ui.DefaultBackgroundColor = Colors.Transparent;
        await Ui.EnsureCoreWebView2Async();
        await Page.EnsureCoreWebView2Async();

        Ui.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "ui.local", Path.Combine(AppContext.BaseDirectory, "ui"),
            CoreWebView2HostResourceAccessKind.Allow);
        Ui.CoreWebView2.WebMessageReceived += OnUiMessage;
        Ui.Source = new Uri("https://ui.local/index.html");
        Page.Source = new Uri("https://www.wikipedia.org/");
    }

    private async void OnUiMessage(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var inject = Path.Combine(AppContext.BaseDirectory, "inject");
        switch (e.TryGetWebMessageAsString())
        {
            case "menu:open":
                Ui.Height = double.NaN;
                Ui.VerticalAlignment = VerticalAlignment.Stretch;
                break;
            case "menu:close":
                Ui.VerticalAlignment = VerticalAlignment.Top;
                Ui.Height = BarHeight;
                break;
            case "blur:on":
                _blurScriptId = await Page.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                    File.ReadAllText(Path.Combine(inject, "blur.js")));
                Page.CoreWebView2.Reload();
                break;
            case "blur:off":
                if (_blurScriptId is not null) Page.CoreWebView2.RemoveScriptToExecuteOnDocumentCreated(_blurScriptId);
                _blurScriptId = null;
                Page.CoreWebView2.Reload();
                break;
            case "bench":
                var expr = System.Text.Json.JsonSerializer.Serialize(new
                {
                    expression = File.ReadAllText(Path.Combine(inject, "bench.js")),
                    awaitPromise = true,
                    returnByValue = true,
                });
                var json = await Page.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate", expr);
                using (var doc = System.Text.Json.JsonDocument.Parse(json))
                    Ui.CoreWebView2.PostWebMessageAsString(
                        doc.RootElement.GetProperty("result").TryGetProperty("value", out var v)
                            ? v.GetString() ?? "échec de la mesure" : "échec de la mesure");
                break;
        }
    }
}
