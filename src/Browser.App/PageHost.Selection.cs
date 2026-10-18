using Microsoft.UI.Xaml.Controls;
using System.Text.Json;

namespace Browser.App;

public sealed partial class PageHost
{
    // Clé écrite dans inject/fan.js : un message d'éventail sans elle vient de la page, pas d'un clic sur l'éventail.
    private static readonly string FanKey = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));

    private void ReportFan(string tabId, WebView2 view, string json)
    {
        try
        {
            using var data = JsonDocument.Parse(json);
            var root = data.RootElement;
            if (!settings.FanEnabled || root.GetProperty("key").GetString() != FanKey || view.XamlRoot is null) return;
            var kind = root.GetProperty("kind").GetString() ?? "";
            var action = root.GetProperty("action").GetString() ?? "";
            var value = root.GetProperty("value").GetString() ?? "";
            if (kind is not ("selection" or "link" or "image" or "code") || value.Length is 0 or > 50_000) return;
            var ratio = root.GetProperty("ratio").GetDouble() / view.XamlRoot.RasterizationScale;
            if (!double.IsFinite(ratio) || ratio <= 0) ratio = 1;
            var origin = view.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0, 0));
            FanAction?.Invoke(tabId, kind, action, origin.X + root.GetProperty("x").GetDouble() * ratio, origin.Y + root.GetProperty("y").GetDouble() * ratio,
                new(value, false, "", root.GetProperty("language").GetString() ?? "", root.GetProperty("title").GetString() ?? ""));
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or ArgumentException) { }
    }
}
