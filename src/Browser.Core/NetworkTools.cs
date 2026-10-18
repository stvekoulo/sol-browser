using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Text.Encodings.Web;
using System.Text.Unicode;

namespace Browser.Core;

public sealed record NetworkDraft(string Url, string Method, Dictionary<string,string> Headers, string Body, int Status = 200, int Delay = 0, string Mode = "response");

public static class NetworkTools
{
    public const string Mask = "[masqué]";
    private static readonly JsonSerializerOptions RedactedJson = new() { Encoder = JavaScriptEncoder.Create(UnicodeRanges.All) };
    public static bool Secret(string name) => Regex.IsMatch(name, "password|passwd|secret|token|authorization|cookie|api[-_]?key", RegexOptions.IgnoreCase);
    public static string Redact(string text)
    {
        text = text[..Math.Min(text.Length, 60000)];
        try
        {
            var node = JsonNode.Parse(text);
            void Visit(JsonNode? item)
            {
                if (item is JsonObject obj) foreach (var key in obj.Select(p => p.Key).ToArray())
                {
                    if (Secret(key)) obj[key] = Mask;
                    else if (obj[key] is JsonValue value && value.TryGetValue<string>(out var textValue)) obj[key] = MaskText(textValue);
                    else Visit(obj[key]);
                }
                else if (item is JsonArray array) for (var i = 0; i < array.Count; i++)
                {
                    if (array[i] is JsonValue value && value.TryGetValue<string>(out var textValue)) array[i] = MaskText(textValue);
                    else Visit(array[i]);
                }
            }
            if (node is JsonValue scalar && scalar.TryGetValue<string>(out var stringValue)) return JsonSerializer.Serialize(MaskText(stringValue), RedactedJson);
            Visit(node); return node?.ToJsonString(RedactedJson) ?? text;
        }
        catch (JsonException) { }
        return MaskText(text);
    }
    private static string MaskText(string text)
    {
        text = Regex.Replace(text, @"(?i)((?:password|passwd|secret|token|authorization|cookie|api[-_]?key)[""']?\s*[=:]\s*)(?:""(?:\\[\s\S]|[^""\\])*""?|'(?:\\[\s\S]|[^'\\])*'?|[^\s&;,]+)", "$1" + Mask);
        return Regex.Replace(text, @"(?i)Bearer\s+[A-Za-z0-9._~+/=-]+", "Bearer " + Mask);
    }
    public static string SafeUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return Redact(url);
        var builder = new UriBuilder(uri) { UserName = "", Password = "" };
        builder.Query = string.Join('&', uri.Query.TrimStart('?').Split('&').Select(p =>
        { var parts = p.Split('=', 2); return Secret(Uri.UnescapeDataString(parts[0])) ? parts[0] + "=" + Mask : p; }));
        return builder.Uri.AbsoluteUri;
    }
    public static NetworkDraft? Parse(string json)
    {
        if (json.Length > 50000) return null;
        try
        {
            var d = JsonSerializer.Deserialize<NetworkDraft>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (d is null || d.Url is null || d.Method is null || d.Headers is null || d.Body is null ||
                !Uri.TryCreate(d.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0 ||
                d.Url.Length > 8000 || d.Url.Contains(Mask) || d.Url.Contains(Uri.EscapeDataString(Mask), StringComparison.OrdinalIgnoreCase) ||
                d.Body.Length > 20000 || d.Body.Contains(Mask) || d.Headers.Count > 40 || d.Status is < 200 or > 599 || d.Delay is < 0 or > 10000 ||
                d.Mode is not ("response" or "timeout" or "invalid" or "empty") ||
                d.Method is not ("GET" or "POST" or "PUT" or "PATCH" or "DELETE" or "HEAD" or "OPTIONS")) return null;
            foreach (var (key,value) in d.Headers)
                if (!Regex.IsMatch(key, "^[A-Za-z0-9-]{1,80}$") || value is null || value.Length > 4000 || value.Contains('\r') || value.Contains('\n') || value.Contains(Mask) ||
                    key.ToLowerInvariant() is "cookie" or "set-cookie" or "host" or "content-length" or "transfer-encoding" or "connection" or "proxy-authorization") return null;
            return d with { Url = uri.AbsoluteUri };
        }
        catch (JsonException) { return null; }
    }
}
