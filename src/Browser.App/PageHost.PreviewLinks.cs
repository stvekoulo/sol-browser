using Browser.Core;
using Microsoft.Web.WebView2.Core;
using System.Text.Json;
namespace Browser.App;
public sealed partial class PageHost
{
    public event Action<string,string>? LinkPreviewRequested;
    private async Task WirePreviewLinksAsync(string id,CoreWebView2 core)
    {
        var key="sol.preview:"+Guid.NewGuid().ToString("N");
        var script="(() => {const key="+JsonSerializer.Serialize(key)+";addEventListener('click',e=>{if(!e.isTrusted||!e.altKey||e.ctrlKey||e.metaKey||e.shiftKey||e.button!==0)return;const a=e.target.closest?.('a[href]');if(!a||!/^https?:$/.test(new URL(a.href).protocol))return;e.preventDefault();e.stopImmediatePropagation();chrome.webview.postMessage({preview:key,url:a.href})},true)})()";
        await core.AddScriptToExecuteOnDocumentCreatedAsync(script);
        void Message(CoreWebView2WebMessageReceivedEventArgs e)
        {
            if(core!=Core(id)||e.WebMessageAsJson.Length>18000)return;
            try{using var json=JsonDocument.Parse(e.WebMessageAsJson);var data=json.RootElement;
                if(data.ValueKind==JsonValueKind.Object&&data.TryGetProperty("preview",out var token)&&token.GetString()==key
                    &&data.TryGetProperty("url",out var url)&&LinkPreview.Allowed(url.GetString()))LinkPreviewRequested?.Invoke(id,url.GetString()!);
            }catch(Exception error) when(error is JsonException or InvalidOperationException){}
        }
        core.WebMessageReceived+=(_,e)=>Message(e);
        core.FrameCreated+=(_,e)=>e.Frame.WebMessageReceived+=(_,message)=>Message(message);
    }
}
