using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace Browser.App;

public sealed partial class PageHost
{
    private sealed class FanRegistration { public string Id = "", Config = ""; public readonly SemaphoreSlim Gate = new(1, 1); }
    private readonly ConditionalWeakTable<CoreWebView2, FanRegistration> _fanRegistrations = new();
    private static readonly string FanSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "inject", "fan.js"));
    private string FanConfig() => JsonSerializer.Serialize(new { enabled = settings.FanEnabled, trigger = settings.FanTrigger });

    private async Task ConfigureFanAsync(CoreWebView2 core)
    {
        var entry = _fanRegistrations.GetValue(core, _ => new FanRegistration());
        await entry.Gate.WaitAsync();
        try
        {
            var config = FanConfig();
            if (entry.Config == config) return;
            var script = FanSource.Replace("__SOL_FAN_KEY__", FanKey).Replace("/*__SOL_FAN_SETTINGS__*/null", config);
            var id = await core.AddScriptToExecuteOnDocumentCreatedAsync(script);
            if (entry.Id.Length > 0) core.RemoveScriptToExecuteOnDocumentCreated(entry.Id);
            (entry.Id, entry.Config) = (id, config);
            PostFanSettings(core);
        }
        finally { entry.Gate.Release(); }
    }

    private void PostFanSettings(CoreWebView2 core) => core.PostWebMessageAsJson(
        "{\"event\":\"fan-settings\",\"fan\":" + FanConfig() + "}");
}
