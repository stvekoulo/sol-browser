using Browser.Core;
using Microsoft.Web.WebView2.Core;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Browser.App;

public sealed partial class BrowserController
{
    private sealed record AiContext(string? Id, CoreWebView2? Core, string Url, string Token, string Text,
        string Title, string Language, bool Editable, bool Truncated, bool Note, bool Quick, string Original, int Start, int End, string NoteId);
    private AiContext? _aiContext;
    private bool _aiPopup, _aiRan;
    private bool _aiThinking;
    private string _aiAction = "summary";
    private string? _aiTextError;
    private const int AiLimit = 50_000;
    // Adaptation à la demande : réutilise le profil existant, sans copie d'historique ni entraînement.
    private string AiNavigation()
    {
        if (AppPaths.Private || !_settings.SolAiAdaptation) return "";
        var entries = _db.Bookmarks().Take(6).Select(b => (b.Url, b.Title))
            .Concat(_db.FrequentSites(6).Select(v => (v.Url, v.Title)))
            .Concat(_db.RecentHistory(6).Select(v => (v.Url, v.Title))).Distinct();
        return string.Join("\n", entries.Where(e => Uri.TryCreate(e.Url, UriKind.Absolute, out var u) && u.Scheme is "http" or "https" && u.UserInfo.Length == 0)
            .Select(e => new Uri(e.Url).Host + " — " + e.Title[..Math.Min(e.Title.Length, 100)].Replace('\n', ' ').Replace('\r', ' ')));
    }
    private async Task OpenPageAiAsync(string id, PageAiText? selected = null)
    {
        if (!_settings.SolAiEnabled || !_ai.Available || _pages.CoreOf(id) is not { } core || !IsWebAddress(core.Source) || core.Source.StartsWith("https://start.sol/")) return;
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
                    + "const clone=document.cloneNode(true);clone.querySelectorAll('input,textarea,script,style,[hidden],[aria-hidden=true]').forEach(n=>n.remove());"
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
        if (_closingWindow || _pages.CoreOf(id) != core || core.Source != url) return;
        ShowTextAi(new(id, core, url, selected?.Token ?? "", text[..Math.Min(text.Length, AiLimit)], title[..Math.Min(120, title.Length)],
            language, selected?.Editable == true, truncated, false, false, "", 0, 0, ""), selected?.Editable == true ? text.Length == 0 ? "write" : "improve" : selected?.Text.Length > 0 ? "explain" : "summary");
    }
    private void OpenNoteAi(Command command)
    {
        if (!_settings.SolAiEnabled || !_ai.Available || command.Width is not { } from || command.Height is not { } to || from != (int)from || to != (int)to || from < 0 || to < from || to > _noteDraft.Text.Length) return;
        var start = (int)from; var end = (int)to;
        if (start == end) { start = 0; end = _noteDraft.Text.Length; }
        var text = _noteDraft.Text[start..end];
        ShowTextAi(new(null, null, _noteDraft.Url, "", text[..Math.Min(text.Length, AiLimit)], _noteDraft.Title, Language.Current, true,
            text.Length > AiLimit, true, _notesPopup, _noteDraft.Text, start, end, _noteDraft.Id), text.Length == 0 ? "write" : "improve");
    }
    private void ShowTextAi(AiContext context, string action)
    {
        HidePopup();
        _aiContext = context; _aiPopup = true; _aiRan = false; _aiAction = action; _aiTextError = null;
        _popup.ShowCentered(490, TextAiJson(), top: 72);
    }
    private string TextAiJson() => JsonSerializer.Serialize(new {
        popup = "ai-action", action = _aiAction, text = _aiContext?.Text ?? "", source = _aiContext?.Title ?? "",
        editable = _aiContext?.Editable == true, note = _aiContext?.Note == true, truncated = _aiContext?.Truncated == true,
        language = Language.Current == "en" ? "en" : "fr", target = PageTranslation.TargetLanguage(_aiContext?.Language, Language.Current),
        configured = _settings.SolAiModel.Length > 0 && _ai.Models.Any(m => m.Id == _settings.SolAiModel && m.Cached),
        busy = _ai.Busy, warm = _ai.Warm, thinking = _ai.Busy ? _aiThinking : _settings.SolAiThinking, percent = _ai.Percent, phase = _ai.Phase, error = _aiTextError ?? (_aiRan ? _ai.Error : null),
        result = _aiRan ? _ai.Busy ? _ai.Partial : _ai.Result : null, incomplete = _ai.Incomplete,
    });
    private void PublishAi() { if (_aiPopup && !_closingWindow) _popup.Post(TextAiJson()); }
    private async Task PrepareAiAsync(string operation,string? id)
    {
        await _ai.RunAsync(operation,id);
        if(!_closingWindow && _settings.SolAiEnabled)_ai.Prepare(_settings.SolAiModel);
    }
    private async Task RunTextAiAsync(Command command)
    {
        if (!_aiPopup || _aiContext is not { } context || _ai.Busy || !_settings.SolAiEnabled) return;
        var action = command.Query ?? "";
        if (!SolAiText.Actions.Contains(action) || !context.Editable && action is "write" or "rephrase" or "improve") return;
        var request = new SolAiRequest(action, context.Text, command.Title ?? "", command.Input ?? "",
            _settings.SolAiTone, _settings.SolAiLength, action is "write" or "explain" ? AiNavigation() : "", _settings.SolAiThinking);
        if (!SolAiText.Valid(request)) { _aiTextError = "invalid-request"; PublishAi(); return; }
        if (!_ai.Models.Any(m => m.Id == _settings.SolAiModel && m.Cached)) { _aiTextError = _ai.Models.Any(m => m.Cached) ? "choose-model" : "not-installed"; PublishAi(); return; }
        _aiAction = action; _aiRan = true; _aiTextError = null;
        _aiThinking = request.Thinking;
        await _ai.RunAsync("generate", _settings.SolAiModel, request);
        if (_aiContext == context) PublishAi();
    }
    private async Task ApplyTextAiAsync()
    {
        if (!_aiPopup || !_aiRan || _ai.Busy || _ai.Error != null || _ai.Incomplete || _ai.Result is not { Length: > 0 } text ||
            _aiContext is not { Editable: true, Truncated: false } context || _aiAction is not ("write" or "rephrase" or "improve" or "translate")) return;
        bool applied;
        if (context.Note)
        {
            applied = _noteDraft.Id == context.NoteId && _noteDraft.Text == context.Original && context.Original.Length - (context.End - context.Start) + text.Length <= 50_000;
            if (applied) _noteDraft = _noteDraft with { Text = context.Original[..context.Start] + text + context.Original[context.End..] };
        }
        else applied = context.Id != null && _pages.CoreOf(context.Id) == context.Core && context.Core != null
            && await _pages.ApplyAiAsync(context.Core, context.Url, context.Token, text);
        if (_aiContext != context) return;
        if (!applied) { _aiTextError = "changed-text"; PublishAi(); return; }
        HidePopup();
        if (context.Note) { PublishNotes(); if (context.Quick) OpenNotes(450, 60); }
    }
    private void CopyAi()
    {
        if (!_aiPopup || !_aiRan || _ai.Busy || _ai.Result is not { Length: > 0 } text) return;
        var data = new Windows.ApplicationModel.DataTransfer.DataPackage(); data.SetText(text);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data);
    }
    private void NoteAi()
    {
        if (!_aiPopup || !_aiRan || _ai.Busy || _ai.Error != null || _ai.Incomplete || _ai.Result is not { Length: > 0 } text || _aiContext is not { } context) return;
        if (NoteDraftChanged) { _aiTextError = "note-draft"; PublishAi(); return; }
        var url = context.Url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0 || url.Length > 4096) url = "";
        _noteDraft = new("", context.Title, text, url, DateTimeOffset.UtcNow);
        OpenNotes(450, 60); PublishNotes();
    }
    private void OpenAiSettings()
    {
        HidePopup(); _settingsOpen = true;
        var data = JsonSerializer.SerializeToNode(JsonSerializer.Deserialize<JsonElement>(SettingsJson()))!;
        data["section"] = "about";
        _popup.ShowCentered(420, data.ToJsonString());
    }
}
