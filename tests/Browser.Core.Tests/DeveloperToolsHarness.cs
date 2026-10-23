using Browser.Core;

namespace Browser.App;

public sealed partial class BrowserController
{
    private readonly FakeNetwork _network = new();
    private readonly FakeToolsPanel _tools = new();
    public Task<string> DeveloperTool(string name, string arguments = "{}", CancellationToken ct = default) =>
        DeveloperToolAsync(new("test", name, arguments), _tabs.ActiveId, ct);
    public Tab DeveloperTab(string url = "https://example.org/", bool isPrivate = false)
    {
        var tab = SeedTab(url, isPrivate);
        _pages.Cores[tab.Id] = new FakeCore { Source = url };
        return tab;
    }
    public bool MockActive => _network.ActiveMocks.Length > 0;
    public bool DevToolsOpened(string id) => _pages.Cores[id].DevToolsOpened;
    public string? EmulatedDevice => _pages.EmulatedDevice;
    public bool DeveloperPanelOpen => _tools.Open;
    public bool CapturePaused => _network.Paused;
    private sealed class FakeToolsPanel
    {
        public bool Open;
        public Task ShowAsync() { Open = true; return Task.CompletedTask; }
    }
    private sealed class FakeNetwork
    {
        public string? Shown;
        public bool Listening, Paused;
        public string[] ActiveMocks = [];
        public Task ListenAsync(string id, FakeCore core) { Listening = true; return Task.CompletedTask; }
        public void Configure(string id, string option, bool enabled) { if (option == "pause") Paused = enabled; }
        public void Clear(string id) { }
        public void Post() { }
        public bool IsListening(string id) => Listening;
        public object AiCaptureState(string id) => new { listening = Listening, paused = Paused };
        public string Export(string id) => """{"requests":[],"console":[]}""";
        public Task<string> AiDetailAsync(string id, FakeCore core, string request) => Task.FromResult("""{"ok":false,"error":"unknown captured request"}""");
        public Task MockAsync(string id, FakeCore core, string? json) { ActiveMocks = json is null ? [] : [id]; return Task.CompletedTask; }
    }
}

internal sealed record Device(string Name, int Width, int Height, string Kind)
{
    public static readonly Device[] All = [new("Test phone", 390, 844, "phone")];
    public static Device? Find(string name) => All.FirstOrDefault(device => device.Name == name);
}
