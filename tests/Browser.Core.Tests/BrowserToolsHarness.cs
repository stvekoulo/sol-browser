using Browser.Core;

namespace Browser.App;

// Le vrai exécuteur BrowserAi.BrowserTools.cs est compilé avec le vrai modèle onglets/notes.
// Seuls WebView2 et les effets d'affichage sont remplacés pour ces tests sans fenêtre.
public sealed partial class BrowserController
{
    private readonly TabManager _tabs = new();
    private readonly QuickNotes _notes = new(null);
    private readonly SplitLayout _split = new();
    private readonly FakePages _pages = new();
    private QuickNote _noteDraft = new("", "", "", "", DateTimeOffset.UtcNow);
    private string? _splitBlankTab, _noteError;
    private bool _closingWindow;
    public bool Ready { get; set; } = true;
    public bool PrivateContext { get; set; }
    private bool AiReady => Ready && !PrivateContext;
    public bool NoteDraftChanged { get; set; }
    public TabManager Tabs => _tabs;
    public QuickNotes Notes => _notes;
    public SplitLayout Layout => _split;
    public Task<string> Tool(string name, string arguments = "{}", CancellationToken ct = default) => BrowserToolAsync(new("test", name, arguments), ct);
    public Tab SeedTab(string url, bool isPrivate = false, bool pinned = false) => _tabs.Open(url, isPrivate: isPrivate, pinned: pinned);
    private Tab Open(string url, bool activate = true, bool? isPrivate = null) => _tabs.Open(url, activate, isPrivate: isPrivate ?? false);
    private void Activate(string id) => _tabs.Activate(id);
    private void Close(string id) => _tabs.Close(id);
    private void Split(string id) => _split.Open(_tabs.ActiveId!, id);
    private void Zoom(string id, string action, int? index) { }
    private void Refresh() { }
    private void PublishNotes() { }
    private static bool IsWebAddress(string address) => address.StartsWith("http");
    private Task<string> PageToolAsync(string id, AgentCall call, CancellationToken ct) => Task.FromResult("{}");
    private sealed class FakePages
    {
        public FakeCore? CoreOf(string id) => null;
        public bool IsAsleep(string id) => false;
        public bool IsLoading(string id) => false;
        public void Add(Tab tab) { }
        public void Back(string id) { }
        public void Forward(string id) { }
        public void Reload(string id) { }
    }
    private sealed class FakeCore
    {
        public string Source => "";
        public bool CanGoBack => false;
        public bool CanGoForward => false;
        public Task<string> ExecuteScriptAsync(string script) => Task.FromResult("\"complete\"");
    }
}
