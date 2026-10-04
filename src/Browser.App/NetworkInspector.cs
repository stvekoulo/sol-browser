using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace Browser.App;

/// <summary>
/// Inspecteur réseau : les requêtes d'un onglet, lues par le protocole de débogage du moteur (rien n'est injecté dans
/// la page). L'écoute d'un onglet commence quand le panneau l'affiche pour la première fois ; ce qui a été chargé avant
/// n'est pas connu. Tout ce qui vient de la page (adresses, en-têtes, corps) est transmis comme du texte.
/// </summary>
public sealed class NetworkInspector(Action<string> post)
{
    private sealed class Entry
    {
        public string Id = "", Method = "", Url = "", Type = "", Mime = "", PostData = "";
        public int Status;
        public long Size;
        public double Started;
        public double? Ms;
        public string? Failed;
        public Dictionary<string, string> RequestHeaders = new(), ResponseHeaders = new();
    }

    // ponytail: journal gardé d'une page à l'autre et plafonné ; le vider à chaque navigation si cela gêne.
    private const int MaxEntries = 400, MaxBody = 60_000, MaxPostData = 20_000;
    private readonly Dictionary<string, List<Entry>> _log = new();
    private readonly HashSet<string> _listening = new();
    // Les récepteurs d'évènements du moteur sont gardés ici : sans référence, le ramasse-miettes les libère, les
    // requêtes n'arrivent plus et le moteur finit par appeler un objet disparu (plantage constaté en version publiée).
    private readonly Dictionary<string, List<CoreWebView2DevToolsProtocolEventReceiver>> _receivers = new();
    private bool _postPending;

    /// <summary>Onglet affiché par le panneau ; null : panneau fermé.</summary>
    public string? Shown { get; set; }

    public async Task ListenAsync(string tabId, CoreWebView2 core)
    {
        if (!_listening.Add(tabId)) return;
        var log = _log[tabId] = new List<Entry>();
        Entry? Find(JsonElement p) => Text(p, "requestId") is { } id ? log.FindLast(e => e.Id == id) : null;

        On(core, "Network.requestWillBeSent", p =>
        {
            if (Text(p, "requestId") is not { } id || !p.TryGetProperty("request", out var request)) return;
            var entry = Find(p);
            if (entry is null)
            {
                log.Add(entry = new Entry { Id = id });
                if (log.Count > MaxEntries) log.RemoveAt(0);
            }
            (entry.Method, entry.Url, entry.Type) = (Text(request, "method") ?? "", Text(request, "url") ?? "", Text(p, "type") ?? entry.Type);
            entry.Started = Number(p, "timestamp");
            entry.RequestHeaders = Headers(request);
            var body = Text(request, "postData") ?? "";
            entry.PostData = body.Length > MaxPostData ? body[..MaxPostData] : body;
        });
        On(core, "Network.responseReceived", p =>
        {
            if (Find(p) is not { } entry || !p.TryGetProperty("response", out var response)) return;
            entry.Status = (int)Number(response, "status");
            entry.Mime = Text(response, "mimeType") ?? "";
            entry.Type = Text(p, "type") ?? entry.Type;
            entry.ResponseHeaders = Headers(response);
        });
        On(core, "Network.loadingFinished", p =>
        {
            if (Find(p) is not { } entry) return;
            entry.Size = (long)Number(p, "encodedDataLength");
            entry.Ms = Math.Max(0, (Number(p, "timestamp") - entry.Started) * 1000);
        });
        On(core, "Network.loadingFailed", p =>
        {
            if (Find(p) is not { } entry) return;
            entry.Failed = p.TryGetProperty("canceled", out var canceled) && canceled.ValueKind == JsonValueKind.True ? "canceled" : Text(p, "errorText") ?? "failed";
            entry.Ms = Math.Max(0, (Number(p, "timestamp") - entry.Started) * 1000);
        });
        try { await core.CallDevToolsProtocolMethodAsync("Network.enable", "{}"); }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException) { _listening.Remove(tabId); }

        void On(CoreWebView2 target, string name, Action<JsonElement> handle)
        {
            var receiver = target.GetDevToolsProtocolEventReceiver(name);
            if (!_receivers.TryGetValue(tabId, out var kept)) _receivers[tabId] = kept = new List<CoreWebView2DevToolsProtocolEventReceiver>();
            kept.Add(receiver);
            receiver.DevToolsProtocolEventReceived += (_, e) =>
            {
                try
                {
                    using var doc = JsonDocument.Parse(e.ParameterObjectAsJson);
                    handle(doc.RootElement);
                }
                catch (JsonException) { return; }
                if (Shown == tabId) PostSoon();
            };
        }
    }

    public void Clear(string tabId)
    {
        _log.GetValueOrDefault(tabId)?.Clear();
        Post();
    }

    /// <summary>Onglet fermé : son journal part avec lui.</summary>
    public void Forget(string tabId)
    {
        _log.Remove(tabId);
        _listening.Remove(tabId);
        _receivers.Remove(tabId);
    }

    // Les requêtes arrivent par rafales : le panneau est mis à jour quatre fois par seconde au plus.
    private async void PostSoon()
    {
        if (_postPending) return;
        _postPending = true;
        await Task.Delay(250);
        _postPending = false;
        Post();
    }

    public void Post()
    {
        if (Shown is null) return;
        post(JsonSerializer.Serialize(new
        {
            panel = "network",
            items = _log.GetValueOrDefault(Shown, []).Select(e => new
            {
                id = e.Id, method = e.Method, url = e.Url, type = e.Type, status = e.Status, mime = e.Mime, size = e.Size, ms = e.Ms, failed = e.Failed,
            }),
        }));
    }

    /// <summary>Détail d'une requête : en-têtes, corps envoyé, et réponse si elle est du texte encore en mémoire.</summary>
    public async Task PostDetailAsync(string tabId, CoreWebView2 core, string requestId)
    {
        if (_log.GetValueOrDefault(tabId)?.FindLast(e => e.Id == requestId) is not { } entry) return;
        string body = "", note = "";
        try
        {
            using var doc = JsonDocument.Parse(await core.CallDevToolsProtocolMethodAsync("Network.getResponseBody", JsonSerializer.Serialize(new { requestId })));
            if (doc.RootElement.TryGetProperty("base64Encoded", out var binary) && binary.ValueKind == JsonValueKind.True) note = "binary";
            else
            {
                body = Text(doc.RootElement, "body") ?? "";
                if (body.Length > MaxBody) (body, note) = (body[..MaxBody], "truncated");
            }
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException or JsonException or ArgumentException)
        {
            note = "unavailable"; // réponse sans corps, ou déjà sortie de la mémoire du moteur
        }
        post(JsonSerializer.Serialize(new
        {
            panel = "network-detail", id = entry.Id, url = entry.Url, method = entry.Method, status = entry.Status, mime = entry.Mime,
            failed = entry.Failed, requestHeaders = entry.RequestHeaders, responseHeaders = entry.ResponseHeaders, postData = entry.PostData, body, note,
        }));
    }

    private static string? Text(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double Number(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    private static Dictionary<string, string> Headers(JsonElement e)
    {
        var headers = new Dictionary<string, string>();
        if (e.TryGetProperty("headers", out var all) && all.ValueKind == JsonValueKind.Object)
            foreach (var header in all.EnumerateObject()) headers[header.Name] = header.Value.ToString();
        return headers;
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
