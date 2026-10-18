using Browser.Core;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Browser.App;

// Outils de Sol AI : lecture et actions sur l'onglet ouvert, notes. Chaque appel apparaît dans la trace de la vue ;
// cliquer, saisir ou ouvrir une adresse attend l'accord de l'utilisateur.
public sealed partial class BrowserController
{
    private sealed record AgentStep(string Id, string Kind, string Label, string Detail, string Status);
    private sealed class AgentTrace(Action publish)
    {
        public readonly List<AgentStep> Steps = [];
        public (string Id, TaskCompletionSource<bool> Answer, string Label, string Detail)? Pending;
        public void Publish() => publish();
        public object Json() => new
        {
            steps = Steps.Select(s => new { id = s.Id, kind = s.Kind, label = s.Label, detail = s.Detail, status = s.Status }),
            approval = Pending is { } p ? new { id = p.Id, label = p.Label, detail = p.Detail } : null,
        };
    }
    private static readonly HttpClient LinkHttp = new(new SocketsHttpHandler { UseCookies = false, AllowAutoRedirect = true, MaxAutomaticRedirections = 5 })
    { Timeout = TimeSpan.FromSeconds(15) };
    private string? _agentScript;

    private void AnswerApproval(Command c)
    {
        foreach (var trace in new[] { _aiTrace, _chatTrace })
            if (trace?.Pending is { } pending && pending.Id == c.Id) pending.Answer.TrySetResult(c.Index == 1);
    }

    // tabId() donne l'onglet au moment de l'appel : celui de la vue Sol AI, ou l'onglet actif pour la discussion.
    private Func<AgentCall, Task<string>> AgentTools(AgentTrace trace, Func<string?> tabId, CancellationToken ct) => async call =>
    {
        var (kind, label, detail) = Describe(call);
        var step = new AgentStep(call.CallId, kind, label, detail, "active");
        trace.Steps.Add(step); trace.Publish();
        string output, status = "complete";
        try
        {
            if (SolAiAgent.NeedsApproval(call.Name) && !await ApproveAsync(trace, call.CallId, label, detail, ct))
            { status = "denied"; output = """{"ok":false,"error":"The user declined this action."}"""; }
            else output = call.Name switch
            {
                "search_notes" => JsonSerializer.Serialize(SolAiAgent.SearchNotes(_notes.Items, SolAiAgent.Argument(call.Arguments, "query"))
                    .Select(n => new { id = n.Id, title = n.Title, excerpt = n.Text[..Math.Min(300, n.Text.Length)], tags = n.Tags, updated = n.Updated })),
                "read_note" => _notes.Items.FirstOrDefault(n => n.Id == SolAiAgent.Argument(call.Arguments, "id")) is { } note
                    ? JsonSerializer.Serialize(new { id = note.Id, title = note.Title, text = note.Text, tags = note.Tags, url = note.Url, updated = note.Updated })
                    : """{"ok":false,"error":"unknown note"}""",
                "create_note" => CreateNoteTool(call),
                _ => await PageToolAsync(tabId(), call, ct),
            };
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or InvalidOperationException or JsonException or HttpRequestException or TaskCanceledException)
        { status = "failed"; output = JsonSerializer.Serialize(new { ok = false, error = "tool failed: " + e.GetType().Name }); }
        trace.Steps[trace.Steps.IndexOf(step)] = step with { Status = status }; trace.Publish();
        return output.Length > 40_000 ? output[..40_000] : output;
    };

    private async Task<bool> ApproveAsync(AgentTrace trace, string id, string label, string detail, CancellationToken ct)
    {
        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        trace.Pending = (id, answer, label, detail); trace.Publish();
        using var registration = ct.Register(() => answer.TrySetResult(false));
        var approved = await answer.Task;
        trace.Pending = null;
        return approved;
    }

    private string CreateNoteTool(AgentCall call)
    {
        var title = SolAiAgent.Argument(call.Arguments, "title").Trim();
        var text = SolAiAgent.Argument(call.Arguments, "text");
        var draft = new QuickNote("", title[..Math.Min(120, title.Length)], text[..Math.Min(50_000, text.Length)], "", DateTimeOffset.UtcNow);
        if (!_notes.Save(draft)) return """{"ok":false,"error":"the note could not be saved"}""";
        PublishNotes();
        return JsonSerializer.Serialize(new { ok = true, id = _notes.Items.First().Id });
    }

    private async Task<string> PageToolAsync(string? tabId, AgentCall call, CancellationToken ct)
    {
        if (tabId is null || _pages.CoreOf(tabId) is not { } core || !IsWebAddress(core.Source) || core.Source.StartsWith("https://start.sol/")
            || _tabs.Tabs.Any(t => t.Id == tabId && t.Private))
            return """{"ok":false,"error":"no readable web page is open"}""";
        string Arg(string name) => JsonSerializer.Serialize(SolAiAgent.Argument(call.Arguments, name));
        if (call.Name == "navigate")
        {
            var url = SolAiAgent.Argument(call.Arguments, "url");
            if (!Uri.TryCreate(url, UriKind.Absolute, out var target) || target.Scheme is not ("http" or "https")) return """{"ok":false,"error":"only http and https addresses"}""";
            _pages.Navigate(tabId, target.AbsoluteUri);
            return """{"ok":true,"note":"The page is loading; call read_page to see it."}""";
        }
        var expression = call.Name switch
        {
            "read_page" => "A.snapshot()",
            "find_in_page" => $"A.find({Arg("query")})",
            "show_element" => $"A.show({Arg("ref")})",
            "click" => $"A.click({Arg("ref")})",
            "fill" => $"A.fill({Arg("ref")},{Arg("text")})",
            "read_link" => $"A.readLink({Arg("target")})",
            _ => null,
        };
        if (expression is null) return """{"ok":false,"error":"unknown tool"}""";
        var result = await EvaluateAsync(core, expression);
        // Lien d'un autre site : lu par Sol sans cookies, et seulement s'il figure parmi les liens de la page.
        if (call.Name == "read_link" && JsonNode.Parse(result)?["external"]?.GetValue<string>() is { } external)
        {
            var listed = JsonSerializer.Deserialize<string[]>(await EvaluateAsync(core, "A.links()")) ?? [];
            if (!listed.Contains(external) || !Uri.TryCreate(external, UriKind.Absolute, out var link) || link.Scheme is not ("http" or "https"))
                return """{"ok":false,"error":"only links listed by read_page can be read on other sites"}""";
            using var response = await LinkHttp.GetAsync(link, HttpCompletionOption.ResponseHeadersRead, ct);
            var type = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!type.Contains("html") && !type.StartsWith("text/")) return JsonSerializer.Serialize(new { ok = false, error = "not a text page: " + type });
            var body = await response.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Serialize(new { ok = response.IsSuccessStatusCode, url = link.AbsoluteUri, text = SolAiAgent.HtmlText(body[..Math.Min(body.Length, 2_000_000)]) });
        }
        return result;
    }

    // Runtime.evaluate attend la fin des promesses (lecture d'un lien), ce que ExecuteScriptAsync ne fait pas.
    private async Task<string> EvaluateAsync(Microsoft.Web.WebView2.Core.CoreWebView2 core, string expression)
    {
        _agentScript ??= File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "inject", "agent.js"));
        var source = $"(async()=>{{const A={_agentScript};return JSON.stringify(await {expression});}})()";
        var json = await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate",
            JsonSerializer.Serialize(new { expression = source, awaitPromise = true, returnByValue = true }));
        var result = JsonNode.Parse(json);
        return result?["result"]?["value"]?.GetValue<string>()
            ?? JsonSerializer.Serialize(new { ok = false, error = result?["exceptionDetails"]?["exception"]?["description"]?.GetValue<string>() ?? "script failed" });
    }

    private static (string Kind, string Label, string Detail) Describe(AgentCall call)
    {
        string A(string name) => SolAiAgent.Argument(call.Arguments, name);
        return call.Name switch
        {
            "read_page" => ("read", T("Lecture de la page", "Reading the page"), ""),
            "find_in_page" => ("search", T("Recherche dans la page", "Searching the page"), A("query")),
            "read_link" => ("read", T("Lecture d’un lien", "Reading a link"), A("target")),
            "show_element" => ("run", T("Affichage dans la page", "Showing on the page"), A("ref")),
            "click" => ("run", T("Cliquer sur la page", "Click on the page"), A("reason")),
            "fill" => ("write", T("Remplir un champ", "Fill a field"), A("text")),
            "navigate" => ("run", T("Ouvrir une adresse", "Open an address"), A("url")),
            "search_notes" => ("search", T("Recherche dans les notes", "Searching notes"), A("query")),
            "read_note" => ("read", T("Lecture d’une note", "Reading a note"), ""),
            "create_note" => ("write", T("Création d’une note", "Creating a note"), A("title")),
            _ => ("run", call.Name, ""),
        };
    }
}
