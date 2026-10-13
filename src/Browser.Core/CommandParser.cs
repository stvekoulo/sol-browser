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
        ["session.menu"] = ["id", "x", "y"], ["session.set"] = ["id", "query"],
        ["session.new"] = [], ["session.create"] = ["title"], ["session.rename"] = ["id", "title"],
        ["session.delete"] = ["id"], ["site.reset"] = [],
        ["tab.move"] = ["id", "index"], ["tab.pin"] = ["id", "index"], ["tab.reopen"] = [],
        ["sidebar.menu"] = ["x", "y"],
        ["nav.go"] = ["id", "input"], ["nav.back"] = ["id"], ["nav.forward"] = ["id"],
        ["nav.reload"] = ["id"], ["nav.stop"] = ["id"],
        ["split.open"] = [], ["split.close"] = [], ["split.resize"] = ["ratio"], ["split.menu"] = ["x", "y"],
        ["bookmark.add"] = ["url", "title"], ["bookmark.remove"] = ["url"],
        ["suggest"] = ["id", "query", "x", "y", "width"], ["suggest.pick"] = ["index"], ["suggest.select"] = ["index"],
        ["settings.open"] = ["x", "y"], ["settings.set"] = ["id", "index"], ["settings.text"] = ["id", "input"], ["dock.size"] = ["width", "height"], ["dock.ready"] = [], ["notes.link"] = ["url"],
        ["menu.pick"] = ["index"], ["tab.menu"] = ["id", "x", "y"], ["capture.menu"] = [],
        ["library.open"] = ["x", "y"], ["history.open"] = ["x", "y"],
        ["library.search"] = ["query", "index"],
        ["translation.open"] = ["id", "x", "y"], ["translation.run"] = ["id", "query"],
        ["translation.original"] = ["id"],
        ["reader.toggle"] = ["id"],
        ["design.toggle"] = ["id"],
        ["reader.listen"] = ["id"], ["reader.stop"] = ["id"], ["bookmark.toggle"] = ["id"],
        ["palette.open"] = [], ["palette.pick"] = ["id"],
        ["spelling.languages"] = [],
        ["ai.catalog"] = [], ["ai.enable"] = ["index"], ["ai.select"] = ["id"],
        ["ai.download"] = ["id"], ["ai.remove"] = ["id"], ["ai.check"] = ["id"], ["ai.optimize"] = ["id"], ["ai.cancel"] = [],
        ["ai.open"] = ["id"], ["ai.notes"] = ["index", "width", "height"], ["ai.run"] = ["query", "title"],
        ["ai.apply"] = [], ["ai.copy"] = [], ["ai.note"] = [], ["ai.settings"] = [],
        ["notes.open"] = [], ["notes.draft"] = ["id", "title", "input"],
        ["notes.save"] = [], ["notes.delete"] = ["id"], ["notes.edit"] = ["id"], ["notes.export"] = [],
        ["notes.select"] = ["id"],
        ["notes.discard"] = [],
        ["notes.manage"] = [], ["notes.ready"] = [], ["notes.visit"] = ["id"],
        ["start.ready"] = [], ["start.navigate"] = ["input"], ["start.group"] = ["id"], ["start.favorites"] = [],
        ["popup.size"] = ["height"], ["popup.close"] = [],
        ["download.cancel"] = ["id"], ["download.open"] = ["id"], ["download.reveal"] = ["id"],
        ["downloads.open"] = ["x", "y"],
        ["window.minimize"] = [], ["window.maximize"] = [], ["window.close"] = [], ["window.drag"] = [],
        ["zoom.in"] = [], ["zoom.out"] = [], ["zoom.reset"] = [],
        ["permission.answer"] = ["index"], ["permissions.clear"] = [], ["permissions.forget"] = ["id"], ["site-data.clear"] = [],
        ["group.new"] = ["id"], ["group.create"] = ["id", "title"], ["group.add"] = ["id", "query"], ["group.remove"] = ["id"],
        ["group.toggle"] = ["id"], ["group.menu"] = ["id", "x", "y"], ["group.update"] = ["id", "title"],
        ["group.move"] = ["id", "index"], ["group.open"] = ["id"], ["group.edit"] = ["id"],
        ["profile.menu"] = ["x", "y"],
        ["profile.switch"] = ["id"], ["profile.delete"] = ["id"], ["profile.new"] = [], ["profile.create"] = ["title"], ["profile.isolated"] = [], ["window.private"] = [],
        ["profile.edit"] = ["id"], ["profile.rename"] = ["id", "title"],
        ["shortcut.menu"] = ["index", "x", "y"], ["shortcut.open"] = ["index"], ["shortcut.update"] = ["id", "title", "input"],
        ["shortcut.move"] = ["id", "index"],
        ["device.set"] = ["id", "query"], ["device.rotate"] = ["id"], ["device.compare"] = ["id", "query"],
        ["network.toggle"] = [], ["network.ready"] = [], ["network.clear"] = [], ["network.detail"] = ["id"],
        ["network.replay"] = ["id", "input"], ["network.cancel"] = ["id"], ["network.mock"] = ["id", "input"], ["network.unmock"] = ["id"],
        ["network.pause"] = ["id", "index"], ["network.preserve"] = ["id", "index"], ["network.export"] = ["id"], ["devtools.open"] = [],
        ["welcome.ready"] = [], ["welcome.open"] = [], ["welcome.done"] = [], ["welcome.name"] = ["title"],
        ["import.run"] = ["id", "query"], ["import.csv"] = [], ["update.restart"] = [], ["update.later"] = [], ["update.check"] = [],
        ["extensions.open"] = [], ["extensions.menu"] = ["x", "y"], ["extension.add"] = ["id"], ["extension.folder"] = [], ["extension.toggle"] = ["id"], ["extension.remove"] = ["id"], ["extension.show"] = ["id"],
        ["extension.store"] = [], ["extension.install"] = ["title"], ["extension.options"] = ["id"], ["extension.reload"] = ["id"],
        ["extension.confirm"] = ["id"], ["extension.current"] = [],
        ["extension.pin"] = ["id"], ["extensions.update"] = [], ["extension.update"] = ["id"],
        ["capture.size"] = ["height"], ["capture.close"] = [], ["capture.visibility"] = ["index"],
        ["capture.run"] = ["query"], ["capture.region"] = ["x", "y", "width", "height"],
        ["capture.selection"] = ["index"], ["capture.save"] = [],
        ["backup.export"] = [], ["backup.export.key"] = ["title"], ["backup.import"] = [], ["backup.restore"] = ["title"],
        ["ui.ready"] = [], ["ui.tooltip"] = [],
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
