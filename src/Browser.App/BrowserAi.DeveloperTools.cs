using Browser.Core;
using System.Text.Json;

namespace Browser.App;

public sealed partial class BrowserController
{
    private async Task<string> DeveloperToolAsync(AgentCall call, string? activeId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!AiReady || _closingWindow) return ToolError("Sol AI is unavailable");
        if (call.Name == "list_devices") return JsonSerializer.Serialize(new { ok = true,
            devices = Device.All.Select(device => new { device.Name, device.Width, device.Height, device.Kind }) });
        var requestedId = SolAiAgent.Argument(call.Arguments, "id");
        var id = requestedId.Length > 0 ? requestedId : activeId;
        if (id is null || !_tabs.Tabs.Any(tab => tab.Id == id && !tab.Private) || _pages.CoreOf(id) is not { } core || !IsWebAddress(core.Source)
            || core.Source.StartsWith("https://start.sol/") || core.Source.StartsWith("https://ui.navigateur/")) return ToolError("no available non-private web tab");
        switch (call.Name)
        {
            case "read_diagnostics":
                using (var arguments = JsonDocument.Parse(call.Arguments))
                {
                    if (!arguments.RootElement.TryGetProperty("errors_only", out var errors) || errors.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                        return ToolError("errors_only must be a boolean");
                    return JsonSerializer.Serialize(new { capture = _network.AiCaptureState(id),
                        diagnostics = JsonSerializer.Deserialize<JsonElement>(SolAiDiagnostics.Report(_network.Export(id), SolAiAgent.Argument(call.Arguments, "query"), errors.GetBoolean())) });
                }
            case "read_network_request":
                var detail = await _network.AiDetailAsync(id, core, SolAiAgent.Argument(call.Arguments, "request_id"));
                ct.ThrowIfCancellationRequested();
                return AiReady && _pages.CoreOf(id) == core ? detail : ToolError("tab or account changed");
            case "open_devtools":
                core.Settings.AreDevToolsEnabled = true; core.OpenDevToolsWindow();
                return """{"ok":true,"opened":true,"note":"DevTools opened locally; no error has been diagnosed yet."}""";
            case "emulate_device":
                var device = SolAiAgent.Argument(call.Arguments, "device");
                if (device.Length > 0 && Device.Find(device) is null) return ToolError("unknown device; call list_devices first");
                _pages.SetDevice(id, device.Length == 0 ? null : device); Refresh();
                return JsonSerializer.Serialize(new { ok = true, device = device.Length == 0 ? "desktop" : device });
            case "start_diagnostics":
                Activate(id); await _tools.ShowAsync();
                if (!AiReady || _pages.CoreOf(id) != core) return ToolError("tab or account changed");
                _network.Shown = id; await _network.ListenAsync(id, core);
                ct.ThrowIfCancellationRequested();
                if (!AiReady || _pages.CoreOf(id) != core) return ToolError("tab or account changed");
                _network.Configure(id, "pause", false); _network.Post(); Refresh();
                return JsonSerializer.Serialize(new { ok = _network.Listening,
                    note = "Capturing locally from now on. Earlier traffic is unavailable. No diagnostic contents were sent to ChatGPT." });
            case "configure_diagnostics":
                var action = SolAiAgent.Argument(call.Arguments, "action");
                if (action is not ("pause" or "resume" or "clear" or "preserve" or "clear_on_navigation")) return ToolError("invalid capture action");
                await _network.ListenAsync(id, core);
                ct.ThrowIfCancellationRequested();
                if (!AiReady || _pages.CoreOf(id) != core) return ToolError("tab or account changed");
                if (!_network.IsListening(id)) return ToolError("diagnostic capture could not start");
                if (action == "clear") _network.Clear(id);
                else _network.Configure(id, action is "pause" or "resume" ? "pause" : "preserve", action is "pause" or "preserve");
                return """{"ok":true,"note":"Local capture configuration updated; no captured contents were returned."}""";
            case "mock_network":
                var draft = SolAiDeveloperTools.MockDraft(call.Arguments);
                Activate(id); await _tools.ShowAsync();
                if (!AiReady || _pages.CoreOf(id) != core) return ToolError("tab or account changed");
                _network.Shown = id; await _network.ListenAsync(id, core);
                ct.ThrowIfCancellationRequested();
                if (!AiReady || _pages.CoreOf(id) != core) return ToolError("tab or account changed");
                if (!_network.IsListening(id)) return ToolError("diagnostic capture could not start");
                await _network.MockAsync(id, core, draft is null ? null : JsonSerializer.Serialize(draft));
                var active = _network.ActiveMocks.Contains(id);
                return JsonSerializer.Serialize(new { ok = active == (draft is not null), active,
                    note = "Simulation only affects matching Fetch/XHR requests in this tab. No server data was modified." });
            default: return ToolError("unknown developer tool");
        }
    }
}
