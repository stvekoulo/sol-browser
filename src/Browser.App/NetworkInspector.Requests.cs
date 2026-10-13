using System.Text.Json;
using System.Text;
using Browser.Core;
using Microsoft.Web.WebView2.Core;

namespace Browser.App;

public sealed partial class NetworkInspector
{
    public async Task ReplayAsync(string tabId, string json)
    {
        if (_replays.ContainsKey(tabId)) return;
        var draft = NetworkTools.Parse(json);
        if (draft is null) { Result(tabId, new { error = "invalid" }); return; }
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(20)); _replays[tabId] = cancel; _results.Remove(tabId); Post();
        try
        {
            using var request = new HttpRequestMessage(new HttpMethod(draft.Method), draft.Url);
            if (draft.Body.Length > 0) request.Content = new StringContent(draft.Body, Encoding.UTF8);
            foreach (var (name,value) in draft.Headers)
            {
                if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                { request.Content ??= new StringContent(""); request.Content.Headers.Remove(name); if(!request.Content.Headers.TryAddWithoutValidation(name,value))throw new ArgumentException(); }
                else if (!request.Headers.TryAddWithoutValidation(name,value)) throw new ArgumentException();
            }
            using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel.Token);
            using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cancel.Token));
            var buffer = new char[MaxBody + 1]; var length = await reader.ReadBlockAsync(buffer.AsMemory(), cancel.Token);
            var headers = response.Headers.Concat(response.Content.Headers).ToDictionary(h=>h.Key,h=>NetworkTools.Secret(h.Key)?NetworkTools.Mask:NetworkTools.Redact(string.Join(", ",h.Value)));
            Result(tabId, new { status = (int)response.StatusCode, body = NetworkTools.Redact(new string(buffer,0,Math.Min(length,MaxBody))), headers, truncated = length > MaxBody });
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or ArgumentException or IOException)
        { Result(tabId, new { error = e is OperationCanceledException ? (_canceled.Contains(tabId) ? "canceled" : "timeout") : "failed", reason = e is HttpRequestException http ? http.HttpRequestError.ToString() : "" }); }
        finally { if (_replays.GetValueOrDefault(tabId) == cancel) _replays.Remove(tabId); _canceled.Remove(tabId); Post(); }
    }

    public void CancelReplay(string tabId) { if (_replays.TryGetValue(tabId, out var c)) { _canceled.Add(tabId); c.Cancel(); } }
    private void Result(string tabId, object result) { if (!_listening.Contains(tabId)) return; _results[tabId] = result; Post(); }

    public async Task MockAsync(string tabId, CoreWebView2 core, string? json)
    {
        if (!_mockLocks.TryGetValue(tabId,out var gate)) _mockLocks[tabId]=gate=new(1,1);
        await gate.WaitAsync();
        try
        {
        if (!_listening.Contains(tabId)) return;
        _mockBusy.Add(tabId);
        Post();
        var draft = json is null ? null : NetworkTools.Parse(json);
        if (json is not null && draft is null) { _mockBusy.Remove(tabId); Result(tabId, new { error = "invalid" }); return; }
        _mocks.Remove(tabId);
        if (_mockCancel.Remove(tabId,out var old)) { old.Cancel(); old.Dispose(); }
        try
        {
            await core.CallDevToolsProtocolMethodAsync("Fetch.disable", "{}");
            if (draft is not null)
            {
                _mocks[tabId] = draft; _mockCancel[tabId] = new();
                await core.CallDevToolsProtocolMethodAsync("Fetch.enable", JsonSerializer.Serialize(new { patterns = new[] { new { urlPattern = "*", resourceType = "XHR", requestStage = "Request" }, new { urlPattern = "*", resourceType = "Fetch", requestStage = "Request" } } }));
            }
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException)
        { _mocks.Remove(tabId); Result(tabId, new { error = "mock-unavailable" }); }
        finally { _mockBusy.Remove(tabId); }
        Post();
        }
        finally { gate.Release(); }
    }

    private async Task PausedAsync(string tabId, CoreWebView2 core, JsonElement p)
    {
        var id = Text(p,"requestId"); if (id is null) return;
        try
        {
            if (!_mocks.TryGetValue(tabId,out var draft) || !p.TryGetProperty("request",out var request) || Text(request,"url") != draft.Url || Text(request,"method") != draft.Method)
            { await core.CallDevToolsProtocolMethodAsync("Fetch.continueRequest", JsonSerializer.Serialize(new { requestId=id })); return; }
            var token = _mockCancel[tabId].Token;
            try { await Task.Delay(draft.Delay, token); } catch (OperationCanceledException) { return; } // Fetch.disable resumes the request.
            if (token.IsCancellationRequested) return;
            if (draft.Mode == "timeout") await core.CallDevToolsProtocolMethodAsync("Fetch.failRequest", JsonSerializer.Serialize(new { requestId=id, errorReason="TimedOut" }));
            else
            {
                var body = draft.Mode == "empty" ? "" : draft.Mode == "invalid" ? "{" : draft.Body;
                await core.CallDevToolsProtocolMethodAsync("Fetch.fulfillRequest", JsonSerializer.Serialize(new { requestId=id, responseCode=draft.Status, responseHeaders=draft.Headers.Select(h=>new { name=h.Key,value=h.Value }), body=Convert.ToBase64String(Encoding.UTF8.GetBytes(body)) }));
            }
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            try { await core.CallDevToolsProtocolMethodAsync("Fetch.continueRequest", JsonSerializer.Serialize(new { requestId=id })); }
            catch (Exception retry) when (retry is InvalidOperationException or System.Runtime.InteropServices.COMException) { }
        }
    }
}

/// <summary>
/// Panneau du bas (outils de développement) : une vue d'interface dans sa propre rangée, sous les pages. Elle n'est
/// créée qu'à la première ouverture : un navigateur qui ne s'en sert pas ne la paie pas.
/// </summary>
public sealed class PanelHost(Microsoft.UI.Xaml.Controls.RowDefinition row, UiBridge bridge, Func<Task> init, Browser.Core.Settings settings)
{
    public const double MinHeight = 140, MaxHeight = 900;
    private bool _ready;

    public bool IsOpen => row.Height.Value > 0;

    public async Task ShowAsync()
    {
        row.Height = new Microsoft.UI.Xaml.GridLength(Math.Clamp(settings.PanelHeight, MinHeight, MaxHeight));
        if (_ready) return;
        _ready = true;
        await init();
    }

    /// <summary>Change la hauteur du panneau ouvert (son bord haut est tiré à la souris).</summary>
    public void Resize(double height)
    {
        if (!IsOpen) return;
        settings.PanelHeight = Math.Clamp(Math.Round(height), MinHeight, MaxHeight);
        row.Height = new Microsoft.UI.Xaml.GridLength(settings.PanelHeight);
    }

    public void Hide() => row.Height = new Microsoft.UI.Xaml.GridLength(0);

    public void Post(string json) => bridge.Post(json);
}

/// <summary>
/// Accueil : une vue d'interface qui couvre toute la fenêtre, à la première ouverture ou quand l'utilisateur demande à
/// reprendre les données d'un autre navigateur. Créée seulement quand elle sert.
/// </summary>
public sealed class WelcomeHost(Microsoft.UI.Xaml.Controls.WebView2 view, UiBridge bridge, Func<Task> init)
{
    private bool _ready;

    public bool IsOpen => view.Visibility == Microsoft.UI.Xaml.Visibility.Visible;

    public async Task ShowAsync()
    {
        view.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        if (_ready) { bridge.Post("""{"welcome":"reset"}"""); return; }
        _ready = true;
        await init();
    }

    public void Hide() => view.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;

    public void Post(string json) => bridge.Post(json);
}
