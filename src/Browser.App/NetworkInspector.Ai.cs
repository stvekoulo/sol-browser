using Browser.Core;
using Microsoft.Web.WebView2.Core;
using System.Text.Json;

namespace Browser.App;

public sealed partial class NetworkInspector
{
    public bool IsListening(string tabId) => _listening.Contains(tabId);
    public object AiCaptureState(string tabId) => new { listening = IsListening(tabId), paused = _paused.Contains(tabId),
        preserve = _preserve.Contains(tabId), mock = _mocks.GetValueOrDefault(tabId) is { } mock
            ? new { url = NetworkTools.SafeUrl(mock.Url), mock.Method, mock.Mode, mock.Status, mock.Delay } : null };

    // Lecture demandée explicitement : le gestionnaire d'outils contrôle l'accord avant cet appel.
    public async Task<string> AiDetailAsync(string tabId, CoreWebView2 core, string requestId)
    {
        if (_log.GetValueOrDefault(tabId)?.FindLast(entry => entry.Id == requestId) is not { } entry)
            return """{"ok":false,"error":"unknown captured request"}""";
        var body = ""; var note = "";
        if (!entry.Mime.Contains("json") && !entry.Mime.StartsWith("text/") && !entry.Mime.Contains("javascript") && !entry.Mime.Contains("xml")) note = "non-text response excluded";
        else try
        {
            using var json = JsonDocument.Parse(await core.CallDevToolsProtocolMethodAsync("Network.getResponseBody", JsonSerializer.Serialize(new { requestId })));
            if (json.RootElement.TryGetProperty("base64Encoded", out var binary) && binary.ValueKind == JsonValueKind.True) note = "binary response excluded";
            else body = SolAiDiagnostics.Clip(Text(json.RootElement, "body") ?? "", 8000);
        }
        catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.COMException or JsonException)
        { note = "response body unavailable; the browser may have released it"; }
        Dictionary<string, string> Headers(Dictionary<string, string> values) => values.Take(20).ToDictionary(pair => pair.Key,
            pair => NetworkTools.Secret(pair.Key) ? NetworkTools.Mask : SolAiDiagnostics.Clip(pair.Value, 200));
        return JsonSerializer.Serialize(new { ok = true, id = entry.Id, url = entry.Url, method = entry.Method,
            status = entry.Status, mime = entry.Mime, failed = entry.Failed, durationMs = entry.Ms,
            requestHeaders = Headers(entry.RequestHeaders), responseHeaders = Headers(entry.ResponseHeaders), body, note,
            truncated = body.EndsWith('…') || entry.RequestHeaders.Count > 20 || entry.ResponseHeaders.Count > 20 });
    }
}
