using System.Text.Json;

namespace Browser.Core;

public static class SolAiDiagnostics
{
    public static string Clip(string text, int maximum)
    {
        var safe = NetworkTools.Redact(text);
        return safe.Length > maximum ? safe[..maximum] + "…" : safe;
    }

    public static string Report(string export, string query, bool errorsOnly)
    {
        using var json = JsonDocument.Parse(export);
        static string Text(JsonElement item, string key) => item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
        var requests = json.RootElement.GetProperty("requests").EnumerateArray().Where(request =>
            (!errorsOnly || request.GetProperty("status").GetInt32() >= 400 || Text(request, "failed").Length > 0)
            && (Text(request, "url") + " " + Text(request, "method") + " " + Text(request, "failed")).Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        var console = json.RootElement.GetProperty("console").EnumerateArray().Where(line =>
            (!errorsOnly || Text(line, "level") is "error" or "warning" or "warn" or "assert")
            && Text(line, "text").Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        return JsonSerializer.Serialize(new { ok = true,
            requests = requests.TakeLast(25).Select(request => new {
                id = Text(request, "id"), method = Text(request, "method"), url = Clip(NetworkTools.SafeUrl(Text(request, "url")), 300),
                status = request.GetProperty("status").GetInt32(), type = Text(request, "type"), failed = Clip(Text(request, "failed"), 200),
                durationMs = request.GetProperty("durationMs").Clone(), size = request.GetProperty("size").GetInt64(),
            }),
            console = console.TakeLast(15).Select(line => new { level = Text(line, "level"), text = Clip(Text(line, "text"), 700) }),
            matchingRequests = requests.Length, matchingMessages = console.Length, truncated = requests.Length > 25 || console.Length > 15,
            note = "Only captured events are available. Start capture before reproducing. A pending status 0 does not prove a failure; response mocks may affect results.",
        });
    }
}
