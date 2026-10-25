using Browser.Core;
using Microsoft.Web.WebView2.Core;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Browser.App;

public sealed partial class BrowserController
{
    private static string FormFields(string arguments)
    {
        using var doc = JsonDocument.Parse(arguments);
        if (!doc.RootElement.TryGetProperty("fields", out var fields) || fields.ValueKind != JsonValueKind.Array
            || fields.GetArrayLength() is < 1 or > 30) throw new ArgumentException("provide 1 to 30 fields");
        foreach (var field in fields.EnumerateArray())
        {
            if (field.ValueKind != JsonValueKind.Object || !field.TryGetProperty("ref", out var reference) || reference.ValueKind != JsonValueKind.String
                || reference.GetString() is not { Length: > 0 and <= 80 }
                || !field.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String || kind.GetString() is not ("text" or "select" or "checked")
                || !field.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.String || value.GetString()!.Length > 50_000)
                throw new ArgumentException("invalid form field");
        }
        return fields.GetRawText();
    }
    private static string FormDescription(string arguments)
    {
        try
        {
            using var doc = JsonDocument.Parse(FormFields(arguments));
            return string.Join("\n", doc.RootElement.EnumerateArray().Select(field =>
            {
                var value = field.GetProperty("value").GetString()!;
                return field.GetProperty("ref").GetString() + " : " + value[..Math.Min(value.Length, 200)];
            }));
        }
        catch (Exception error) when (error is JsonException or ArgumentException) { return T("Champs invalides", "Invalid fields"); }
    }
    private static string CheckedArgument(string arguments)
    {
        using var doc = JsonDocument.Parse(arguments);
        return doc.RootElement.TryGetProperty("checked", out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetRawText() : throw new ArgumentException("checked must be a boolean");
    }

    private async Task<string> PageInputToolAsync(CoreWebView2 core, AgentCall call, CancellationToken ct)
    {
        string A(string key) => SolAiAgent.Argument(call.Arguments, key);
        var key = A("key");
        var keys = new Dictionary<string, (string Code, int VirtualKey, string Text)>
        {
            ["Enter"] = ("Enter", 13, "\r"), ["Tab"] = ("Tab", 9, ""), ["Shift+Tab"] = ("Tab", 9, ""),
            ["Escape"] = ("Escape", 27, ""), ["Space"] = ("Space", 32, " "),
            ["ArrowUp"] = ("ArrowUp", 38, ""), ["ArrowDown"] = ("ArrowDown", 40, ""),
            ["ArrowLeft"] = ("ArrowLeft", 37, ""), ["ArrowRight"] = ("ArrowRight", 39, ""),
            ["Home"] = ("Home", 36, ""), ["End"] = ("End", 35, ""),
            ["PageUp"] = ("PageUp", 33, ""), ["PageDown"] = ("PageDown", 34, ""),
            ["Backspace"] = ("Backspace", 8, ""), ["Delete"] = ("Delete", 46, ""),
        };
        if (call.Name == "press_key" && !keys.ContainsKey(key)) return ToolError("unsupported key");
        var reference = JsonSerializer.Serialize(A("ref"));
        var result = await EvaluateAsync(core, call.Name == "hover" ? $"A.hoverPoint({reference})" : $"A.focus({reference})");
        if (JsonNode.Parse(result) is not JsonObject info || info["ok"]?.GetValue<bool>() != true) return result;
        ct.ThrowIfCancellationRequested();
        if (!AiReady) return ToolError("Sol AI unavailable");
        if (call.Name == "hover")
            await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", JsonSerializer.Serialize(new
            { type = "mouseMoved", x = info["x"]!.GetValue<double>(), y = info["y"]!.GetValue<double>() }));
        else
        {
            var entry = keys[key];
            var actualKey = key == "Space" ? " " : key == "Shift+Tab" ? "Tab" : key;
            var modifiers = key == "Shift+Tab" ? 8 : 0;
            await core.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", JsonSerializer.Serialize(new
            { type = "keyDown", key = actualKey, code = entry.Code, windowsVirtualKeyCode = entry.VirtualKey, modifiers, text = entry.Text }));
            await core.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", JsonSerializer.Serialize(new
            { type = "keyUp", key = actualKey, code = entry.Code, windowsVirtualKeyCode = entry.VirtualKey, modifiers }));
        }
        return """{"ok":true,"note":"Input dispatched. Read the page to verify the result."}""";
    }
}
