using System.Text.Json;

namespace Browser.Core;

public sealed record Command(string Type, string? Id = null, string? Url = null, string? Title = null,
    string? Input = null, string? Query = null, int? Index = null, double? Ratio = null,
    double? X = null, double? Y = null, double? Width = null, double? Height = null);

public static class CommandParser
{
    private static readonly Dictionary<string, string[]> Required = new()
    {
        ["tab.open"] = [], ["tab.close"] = ["id"], ["tab.activate"] = ["id"],
        ["tab.move"] = ["id", "index"], ["tab.reopen"] = [],
        ["nav.go"] = ["id", "input"], ["nav.back"] = ["id"], ["nav.forward"] = ["id"],
        ["nav.reload"] = ["id"], ["nav.stop"] = ["id"],
        ["split.open"] = [], ["split.close"] = [], ["split.resize"] = ["ratio"],
        ["bookmark.add"] = ["url", "title"], ["bookmark.remove"] = ["url"],
        ["suggest"] = ["id", "query", "x", "y", "width"], ["suggest.pick"] = ["index"], ["suggest.select"] = ["index"],
        ["settings.open"] = ["x", "y"], ["settings.set"] = ["id", "index"], ["settings.text"] = ["id", "input"], ["dock.size"] = ["width", "height"],
        ["menu.pick"] = ["index"], ["tab.menu"] = ["id", "x", "y"], ["capture.menu"] = ["x", "y"],
        ["library.open"] = ["x", "y"],
        ["popup.size"] = ["height"], ["popup.close"] = [],
        ["download.cancel"] = ["id"], ["download.open"] = ["id"], ["download.reveal"] = ["id"],
        ["downloads.open"] = ["x", "y"],
        ["window.minimize"] = [], ["window.maximize"] = [], ["window.close"] = [], ["window.drag"] = [],
        ["zoom.in"] = [], ["zoom.out"] = [], ["zoom.reset"] = [],
        ["permission.answer"] = ["index"], ["permissions.clear"] = [], ["permissions.forget"] = ["id"],
        ["group.new"] = ["id"], ["group.create"] = ["id", "title"], ["group.add"] = ["id", "query"], ["group.remove"] = ["id"],
        ["group.toggle"] = ["id"], ["group.menu"] = ["id", "x", "y"], ["group.update"] = ["id", "title"],
        ["profile.menu"] = ["x", "y"],
        ["profile.switch"] = ["id"], ["profile.delete"] = ["id"], ["profile.new"] = [], ["profile.create"] = ["title"],
        ["profile.edit"] = ["id"], ["profile.rename"] = ["id", "title"],
        ["shortcut.menu"] = ["index", "x", "y"], ["shortcut.open"] = ["index"], ["shortcut.update"] = ["id", "title", "input"],
        ["device.set"] = ["id", "query"], ["device.rotate"] = ["id"], ["device.compare"] = ["id", "query"],
        ["network.toggle"] = [], ["network.ready"] = [], ["network.clear"] = [], ["network.detail"] = ["id"],
        ["blocker.options"] = [],
        ["ui.ready"] = [],
    };

    public static Command? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (Str(root, "type") is not { } type || !Required.TryGetValue(type, out var required)) return null;

            var cmd = new Command(type, Str(root, "id"), Str(root, "url"), Str(root, "title"),
                Str(root, "input"), Str(root, "query"), Int(root, "index"), Num(root, "ratio"),
                Num(root, "x"), Num(root, "y"), Num(root, "width"), Num(root, "height"));
            return required.All(field => Has(cmd, field)) ? cmd : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool Has(Command c, string field) => field switch
    {
        "id" => c.Id is not null, "url" => c.Url is not null, "title" => c.Title is not null,
        "input" => c.Input is not null, "query" => c.Query is not null,
        "index" => c.Index is not null, "ratio" => c.Ratio is not null,
        "x" => c.X is not null, "y" => c.Y is not null,
        "width" => c.Width is not null, "height" => c.Height is not null,
        _ => false,
    };

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;

    // Les nombres servent à placer et dimensionner des vues : ni NaN ni infini.
    private static double? Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) && double.IsFinite(d) ? d : null;
}
