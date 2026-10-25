using Browser.Core;
using Microsoft.Web.WebView2.Core;

namespace Browser.App;

// Shared WebView2 environment for pages and user-installed extensions.
public static class AdBlock
{
    private static readonly StartupTask<CoreWebView2Environment> Engine = new(CreateEnvironmentAsync);
    public static Task<CoreWebView2Environment> EnvironmentAsync() => Engine.GetAsync();
    private static async Task<CoreWebView2Environment> CreateEnvironmentAsync() =>
        await CoreWebView2Environment.CreateWithOptionsAsync(null, Environment.GetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER"),
            new CoreWebView2EnvironmentOptions { AreBrowserExtensionsEnabled = true });
    // Compatibility with extension management: no built-in blocker is installed anymore.
    public static string? ExtensionId => null;
    public static async Task RemoveBundledAsync(CoreWebView2Profile profile)
    {
        if (!File.Exists(Path.Combine(AppPaths.DataDir, "ublock-path.txt"))) return;
        foreach (var extension in await profile.GetBrowserExtensionsAsync())
            if (extension.Name.StartsWith("uBlock Origin Lite", StringComparison.OrdinalIgnoreCase)
                && !Directory.Exists(Path.Combine(AppPaths.DataDir, "extensions", extension.Id))
                && !Directory.Exists(Path.Combine(AppPaths.BaseDir, "extensions", extension.Id)))
            {
                await extension.EnableAsync(false);
                try { await extension.RemoveAsync(); }
                catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.COMException)
                { DebugLog.Write($"blocker.remove.failed {error.GetType().Name}"); }
            }
    }
}
