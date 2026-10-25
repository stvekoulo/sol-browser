using System.Text.Json;
using Microsoft.Web.WebView2.Core;
namespace Browser.App;
public sealed partial class PageHost
{
    private readonly HashSet<string> _deviceSync = new(), _deviceDark = new();
    private readonly HashSet<CoreWebView2> _deviceSyncWired = new();
    private readonly string _deviceSyncKey = Guid.NewGuid().ToString("N");
    public bool DeviceSyncEnabled(string id) => _deviceSync.Contains(id);
    public void ToggleDeviceSync(string id) {
        if(!_deviceSync.Add(id))_deviceSync.Remove(id);
        foreach(var sim in _sims.GetValueOrDefault(id,[]))sim.View.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new{ @event="device-sync",enabled=_deviceSync.Contains(id)}));
    }
    public async void ToggleDeviceDark(string id)
    {
        if(!_deviceDark.Add(id))_deviceDark.Remove(id);
        foreach(var sim in _sims.GetValueOrDefault(id,[]))
            if(sim.View.CoreWebView2 is {} core) await ApplyDeviceThemeAsync(id,core);
    }
    private async Task ApplyDeviceThemeAsync(string id, CoreWebView2 core)
    {
        try { await core.CallDevToolsProtocolMethodAsync("Emulation.setEmulatedMedia",JsonSerializer.Serialize(new {features=new[]{new{name="prefers-color-scheme",value=_deviceDark.Contains(id)?"dark":"light"}}})); }
        catch(Exception e) when(e is InvalidOperationException or System.Runtime.InteropServices.COMException) { }
    }
    private async Task WireDeviceSyncAsync(string id, CoreWebView2 core)
    {
        if(!_deviceSyncWired.Add(core))return;
        await core.AddScriptToExecuteOnDocumentCreatedAsync(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"inject","device-sync.js")).Replace("__DEVICE_SYNC_KEY__",_deviceSyncKey));
        core.DOMContentLoaded+=(_,_)=>core.PostWebMessageAsJson(JsonSerializer.Serialize(new{@event="device-sync",enabled=_deviceSync.Contains(id)}));
        core.WebMessageReceived+=async(_,e)=>
        {
            if(!_deviceSync.Contains(id)||!_sims.TryGetValue(id,out var sims)||sims.Count<2||e.WebMessageAsJson.Length>15000)return;
            try{
                using var json=JsonDocument.Parse(e.WebMessageAsJson);var data=json.RootElement;
                if(!data.TryGetProperty("solDeviceSync",out var key)||key.GetString()!=_deviceSyncKey
                    ||data.GetProperty("page").GetString()!=e.Source||core.Source!=e.Source)return;
                var origin=new Uri(e.Source).GetLeftPart(UriPartial.Authority);
                if(!e.Source.StartsWith("http"))return;
                foreach(var sim in sims.ToArray())
                    if(sim.View.CoreWebView2 is {} other&&other!=core&&new Uri(other.Source).GetLeftPart(UriPartial.Authority)==origin)
                        await other.ExecuteScriptAsync("window.__solDeviceApply?.("+data.GetRawText()+")");
            }catch(Exception error) when(error is JsonException or FormatException or KeyNotFoundException or InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException) { }
        };
        await ApplyDeviceThemeAsync(id,core);
    }
}
