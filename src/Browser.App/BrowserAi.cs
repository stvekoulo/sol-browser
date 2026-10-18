using Browser.Core;
using Microsoft.Web.WebView2.Core;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Browser.App;

// Sol AI : actions sur la page ou la sélection, calculées par ChatGPT avec le forfait de l'utilisateur.
public sealed partial class BrowserController
{
    private sealed record AiContext(string Id, CoreWebView2 Core, string Url, string Token, string Text, string Title, string Language, bool Editable, bool Truncated, string Scope);
    private readonly ChatGptAccount _chatGpt = new();
    private AiContext? _aiContext;
    private bool _aiPopup, _aiRan, _aiBusy;
    private string _aiAction = "summary";
    private string? _aiError;
    private readonly StringBuilder _aiResult = new();
    private CancellationTokenSource? _aiCancel;
    private AgentTrace? _aiTrace;
    private (string Action, string Language, string Input)? _aiRequest; // dernière demande envoyée, pour la vue
    private const int AiLimit = 50_000;
    private bool AiReady => !PrivateActive && _chatGpt.Ready;

    // run : action lancée tout de suite depuis le menu contextuel, sans passer par le choix dans la vue.
    private async Task OpenPageAiAsync(string id, PageAiText? selected = null, string? run = null, string? tab = null)
    {
        if (!AiReady || _tabs.Tabs.Any(t => t.Id == id && t.Private) || _pages.CoreOf(id) is not { } core || !IsWebAddress(core.Source) || core.Source.StartsWith("https://start.sol/")) return;
        var url = core.Source;
        var text = selected?.Text ?? "";
        var truncated = text.Length > AiLimit;
        var title = selected?.Title ?? core.DocumentTitle;
        var language = selected?.Language ?? "";
        if (selected?.Editable != true && text.Length == 0)
        {
            if (_pages.IsPdf(id)) { Notice("Sol AI", T("Sélectionnez d’abord un passage du PDF.", "Select a passage from the PDF first.")); return; }
            try
            {
                var script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "inject", "Readability.js"));
                var json = await core.ExecuteScriptAsync("(() => {" + script + "\n"
                    + "const clone=document.cloneNode(true);clone.querySelectorAll('input,textarea,[contenteditable],[role=textbox],script,style,[hidden],[aria-hidden=true]').forEach(n=>n.remove());"
                    + "const article=new Readability(clone,{maxElemsToParse:20000}).parse();const text=(article?.textContent || clone.body?.innerText || clone.body?.textContent || '').trim();"
                    + $"return {{text:text.slice(0,{AiLimit}),truncated:text.length>{AiLimit},language:document.documentElement.lang||'',title:article?.title||document.title}};" + "})()");
                using var data = JsonDocument.Parse(json);
                text = data.RootElement.GetProperty("text").GetString() ?? "";
                truncated = data.RootElement.GetProperty("truncated").GetBoolean();
                title = data.RootElement.GetProperty("title").GetString() ?? title;
                language = data.RootElement.GetProperty("language").GetString() ?? "";
            }
            catch (Exception e) when (e is COMException or InvalidOperationException or IOException or JsonException)
            { Notice("Sol AI", T("Le texte de cette page n’est pas accessible. Sélectionnez un passage.", "The page text is unavailable. Select a passage.")); return; }
        }
        if (_closingWindow || !AiReady || _pages.CoreOf(id) != core || core.Source != url) return;
        HidePopup();
        _aiContext = new(id, core, url, selected?.Token ?? "", text[..Math.Min(text.Length, AiLimit)], title[..Math.Min(120, title.Length)], language, selected?.Editable == true, truncated,
            selected?.Editable == true ? "field" : text.Length > 0 && selected?.Text.Length > 0 ? "selection" : "page");
        _aiPopup = true; _aiRan = false; _aiError = null; _aiResult.Clear(); _aiRequest = null; _aiTrace = null;
        _aiAction = selected?.Editable == true ? text.Length == 0 ? "write" : "improve" : selected?.Text.Length > 0 ? "explain" : "summary";
        _aiAction = run ?? tab ?? _aiAction;
        _popup.ShowCentered(490, TextAiJson(), top: 72);
        if (run != null)
            await RunTextAiAsync(new Command("ai.run", Title: run == "translate" ? PageTranslation.TargetLanguage(language, Language.Current) : Language.Current == "en" ? "en" : "fr", Query: run));
    }

    private string TextAiJson() => JsonSerializer.Serialize(new {
        popup = "ai-action", action = _aiAction, text = _aiContext?.Text ?? "", source = _aiContext?.Title ?? "",
        editable = _aiContext?.Editable == true, truncated = _aiContext?.Truncated == true, scope = _aiContext?.Scope ?? "page",
        language = Language.Current == "en" ? "en" : "fr", target = PageTranslation.TargetLanguage(_aiContext?.Language, Language.Current),
        ready = AiReady, account = _chatGpt.Tokens?.Email ?? "", busy = _aiBusy, error = _aiError,
        result = _aiRan ? _aiResult.ToString() : null, agent = _aiTrace?.Json(),
        request = _aiRequest is { } sent ? new { action = sent.Action, language = sent.Language, input = sent.Input } : null,
    });
    private void PublishAi() { if (_aiPopup && !_closingWindow) _popup.Post(TextAiJson()); }

    // Envoi explicite : seul le texte affiché dans la vue part chez OpenAI, après le clic de l'utilisateur.
    private async Task RunTextAiAsync(Command command)
    {
        if (!_aiPopup || _aiContext is not { } context || _aiBusy) return;
        var action = command.Query ?? "";
        if (!SolAiText.Actions.Contains(action) || !context.Editable && action is "write" or "rephrase" or "improve") return;
        var request = new SolAiRequest(action, context.Text, command.Title ?? "", command.Input ?? "", _settings.SolAiTone, _settings.SolAiLength);
        if (!SolAiText.Valid(request)) { _aiError = "invalid-request"; PublishAi(); return; }
        if (!AiReady) { _aiError = "signed-out"; PublishAi(); return; }
        using var cancel = _aiCancel = new CancellationTokenSource();
        _aiAction = action; _aiRan = true; _aiBusy = true; _aiError = null; _aiResult.Clear(); _aiTrace = null;
        _aiRequest = (action, request.Language, request.Instruction);
        PublishAi();
        string? error = null;
        var output = new AiStreamBuffer(_window.DispatcherQueue, delta =>
        {
            if (_aiContext != context || cancel.IsCancellationRequested) return;
            _aiResult.Append(delta); PublishAi();
        });
        try
        {
            if (action == "ask")
            {
                // Question : Sol AI consulte l'onglet en direct (page, recherche, liens) au lieu d'un extrait figé.
                var trace = _aiTrace = new AgentTrace(PublishAi);
                await _chatGpt.RunAsync(SolAiText.AskInstruction(request.Language), [new { role = "user", content = SolAiText.AskData(request, context.Title, context.Url, context.Scope) }],
                    SolAiAgent.PageTools, AgentTools(trace, () => context.Id, cancel.Token), output.Append, cancel.Token);
            }
            else await _chatGpt.StreamAsync(SolAiText.Instruction(request), SolAiText.Data(request), output.Append, cancel.Token);
        }
        catch (ChatGptException e) { error = e.Code; }
        catch (OperationCanceledException) { error = cancel.IsCancellationRequested ? "cancelled" : "network"; }
        catch (Exception e) when (e is HttpRequestException or IOException or JsonException or FormatException)
        { DebugLog.Write("chatgpt.run " + e.GetType().Name); error = "network"; }
        // Les derniers morceaux passent par la file de l'interface : la fin les suit.
        _window.DispatcherQueue.TryEnqueue(() =>
        {
            output.Flush();
            if (_aiCancel == cancel) _aiCancel = null;
            if (_aiContext != context) return;
            _aiError = error; _aiBusy = false; PublishAi();
        });
    }
    private void CancelAi() { try { _aiCancel?.Cancel(); } catch (ObjectDisposedException) { } }

    private bool AiDone => _aiPopup && _aiRan && !_aiBusy && _aiError == null && _aiResult.Length > 0;
    private async Task ApplyTextAiAsync()
    {
        if (!AiDone || _aiContext is not { Editable: true, Truncated: false } context || _aiAction is not ("write" or "rephrase" or "improve" or "translate")) return;
        var applied = _pages.CoreOf(context.Id) == context.Core && await _pages.ApplyAiAsync(context.Core, context.Url, context.Token, _aiResult.ToString());
        if (_aiContext != context) return;
        if (!applied) { _aiError = "changed-text"; PublishAi(); return; }
        HidePopup();
    }
    private void CopyAi()
    {
        if (!AiDone) return;
        var data = new Windows.ApplicationModel.DataTransfer.DataPackage(); data.SetText(_aiResult.ToString());
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data);
    }
    private void NoteAi()
    {
        if (!AiDone || _aiContext is not { } context) return;
        if (NoteDraftChanged) { _aiError = "note-draft"; PublishAi(); return; }
        var url = context.Url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0 || url.Length > 4096) url = "";
        _noteDraft = new("", context.Title, _aiResult.ToString(), url, DateTimeOffset.UtcNow);
        OpenNotes(450, 60); PublishNotes();
    }
    private object AiState() => new
    {
        connected = !PrivateActive && _chatGpt.Tokens != null, ready = AiReady, email = PrivateActive ? "" : _chatGpt.Tokens?.Email ?? "",
        signingIn = !PrivateActive && _chatGpt.SigningIn, error = PrivateActive ? null : _chatGpt.Error, isPrivate = PrivateActive,
        tone = _settings.SolAiTone, length = _settings.SolAiLength,
        categories = _settings.SolAiCategories, categorizing = _categorizing, categoriesError = _categoriesError,
        categoriesUpdated = _categories.Categories.Length > 0 ? _categories.UpdatedAt.ToUnixTimeMilliseconds() : (long?)null,
    };
}
