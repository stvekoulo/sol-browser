using System.Text.Json;

namespace Browser.Core;

public sealed record Command(string Type, string? Id = null, string? Url = null, string? Title = null,
    string? Input = null, string? Query = null, int? Index = null, double? Ratio = null);

public static class CommandParser
{
    private static readonly Dictionary<string, string[]> Required = new()
    {
        ["tab.open"] = [], ["tab.close"] = ["id"], ["tab.activate"] = ["id"],
        ["tab.move"] = ["id", "index"], ["tab.reopen"] = [],
        ["nav.go"] = ["id", "input"], ["nav.back"] = ["id"], ["nav.forward"] = ["id"],
        ["nav.reload"] = ["id"], ["nav.stop"] = ["id"],
        ["split.open"] = ["id"], ["split.close"] = [], ["split.resize"] = ["ratio"],
        ["bookmark.add"] = ["url", "title"], ["bookmark.remove"] = ["url"],
        ["history.search"] = ["query"],
        ["download.cancel"] = ["id"], ["download.open"] = ["id"],
        ["window.minimize"] = [], ["window.maximize"] = [], ["window.close"] = [],
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
                Str(root, "input"), Str(root, "query"), Int(root, "index"), Num(root, "ratio"));
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
        _ => false,
    };

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;

    private static double? Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
}
