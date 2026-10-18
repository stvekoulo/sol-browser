using Microsoft.UI.Xaml.Controls;
using System.Text.Json;

namespace Browser.App;

public sealed partial class PageHost
{
    // Sélection signalée par inject/selection.js : position en pixels CSS de la page, ramenée aux coordonnées de la
    // fenêtre. Le rapport devicePixelRatio / échelle de l'écran tient compte du zoom de la page.
    private void ReportSelection(string tabId, WebView2 view, string json)
    {
        try
        {
            using var data = JsonDocument.Parse(json);
            var root = data.RootElement;
            var text = root.GetProperty("text").GetString() ?? "";
            if (text.Length is 0 or > 50_000 || view.XamlRoot is null) return;
            var ratio = root.GetProperty("ratio").GetDouble() / view.XamlRoot.RasterizationScale;
            if (!double.IsFinite(ratio) || ratio <= 0) ratio = 1;
            var origin = view.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0, 0));
            double At(string name) => root.GetProperty(name).GetDouble() * ratio;
            SelectionChanged?.Invoke(tabId, origin.X + At("x"), origin.Y + At("top"), origin.Y + At("bottom"),
                new(text, false, "", root.GetProperty("language").GetString() ?? "", root.GetProperty("title").GetString() ?? ""));
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or ArgumentException) { }
    }
}
