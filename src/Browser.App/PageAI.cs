using Browser.Core;
using Microsoft.Web.WebView2.Core;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Browser.App;

public sealed record PageAiText(string Text, bool Editable, string Token, string Language, string Title);

public sealed partial class PageHost
{
    public async Task<PageAiText?> CaptureAiAsync(string id)
    {
        try
        {
            if (Core(id) is not { } core) return null;
            var json = await core.ExecuteScriptAsync("window.__solAI?.current() ?? null");
            return JsonSerializer.Deserialize<PageAiText>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception e) when (e is COMException or InvalidOperationException or JsonException) { return null; }
    }
    public async Task<bool> ApplyAiAsync(CoreWebView2 core, string url, string token, string text)
    {
        try { return core.Source == url && await core.ExecuteScriptAsync($"window.__solAI?.apply({JsonSerializer.Serialize(token)},{JsonSerializer.Serialize(text)})") == "true"; }
        catch (Exception e) when (e is COMException or InvalidOperationException) { return false; }
    }
}
