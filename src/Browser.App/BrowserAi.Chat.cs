using Browser.Core;
using System.Text;
using System.Text.Json;

namespace Browser.App;

// Discussion libre avec Sol AI dans le panneau de droite. La conversation reste en mémoire dans cette fenêtre.
public sealed partial class BrowserController
{
    private readonly List<(string Role, StringBuilder Text)> _chat = [];
    private bool _chatBusy;
    private string? _chatError;
    private CancellationTokenSource? _chatCancel;
    private AgentTrace? _chatTrace;
    private string _chatFocus = ""; // « notes » quand le panneau est ouvert depuis les notes
    private static readonly object[] ChatTools = [.. SolAiAgent.PageTools, .. SolAiAgent.NoteTools];
    private const int ChatLimit = 40, ChatTextLimit = 20_000;

    private bool ChatCommand(Command c)
    {
        switch (c.Type)
        {
            case "chat.toggle":
                if (Chat is null) return true;
                if (PrivateActive) return true;
                // Sans compte connecté, le panneau propose la connexion.
                if (c.Id == "notes") { _chatFocus = "notes"; _ = Chat.ShowAsync(); PublishChat(); }
                else if (Chat.IsOpen) Chat.Hide(); else { _chatFocus = ""; _ = Chat.ShowAsync(); }
                Refresh(); return true;
            case "chat.ready": _bridge.BroadcastTheme(); PublishChat(); return true;
            case "chat.send": _ = SendChatAsync(c.Input ?? ""); return true;
            case "chat.cancel": try { _chatCancel?.Cancel(); } catch (ObjectDisposedException) { } return true;
            case "chat.clear":
                if (_chatBusy) return true;
                _chat.Clear(); _chatError = null; PublishChat(); return true;
            default: return false;
        }
    }

    private void PublishChat()
    {
        if (Chat is null || _closingWindow) return;
        Chat.Post(JsonSerializer.Serialize(new
        {
            chat = true, ready = AiReady, signingIn = _chatGpt.SigningIn,
            account = PrivateActive ? "" : _chatGpt.Tokens?.Email ?? "", busy = _chatBusy, error = _chatError ?? _chatGpt.Error,
            messages = _chat.Where(_ => !PrivateActive).Select(m => new { from = m.Role, text = m.Text.ToString() }),
            agent = PrivateActive ? null : _chatTrace?.Json(), focus = _chatFocus,
        }));
    }

    private async Task SendChatAsync(string question)
    {
        question = question.Trim();
        if (_chatBusy || question.Length is 0 or > ChatTextLimit) return;
        if (!AiReady) { _chatError = "signed-out"; PublishChat(); return; }
        // Les plus anciens échanges sortent de la discussion pour garder des requêtes raisonnables.
        while (_chat.Count >= ChatLimit || _chat.Count > 1 && _chat.Sum(m => m.Text.Length) + question.Length > 80_000) _chat.RemoveRange(0, 2);
        _chat.Add(("user", new StringBuilder(question)));
        var answer = new StringBuilder();
        _chat.Add(("assistant", answer));
        using var cancel = _chatCancel = new CancellationTokenSource();
        _chatBusy = true; _chatError = null; _chatTrace = null; PublishChat();
        var history = _chat.Take(_chat.Count - 1).Select(m => (m.Role, m.Text.ToString())).ToList();
        var output = new AiStreamBuffer(_window.DispatcherQueue, delta => { if (!cancel.IsCancellationRequested) { answer.Append(delta); PublishChat(); } });
        try
        {
            // Questions libres ; l'onglet actif et les notes sont consultés seulement quand la question les concerne.
            var trace = _chatTrace = new AgentTrace(PublishChat);
            await _chatGpt.RunAsync("You are Sol AI, the assistant built into the Sol web browser. Answer the user's questions helpfully and concisely, in the language they use, with Markdown when it helps. "
                + "You can read and act on the tab the user has open (read_page, find_in_page, read_link, show_element, click, fill, navigate) and use their Sol notes (search_notes, read_note, create_note: summarize, find or write notes on request). "
                + SolAiAgent.Rules, history.Select(m => (object)new { role = m.Role, content = m.Item2 }).ToList(), ChatTools, AgentTools(trace, () => _tabs.ActiveId, cancel.Token), output.Append, cancel.Token);
        }
        catch (ChatGptException e) { _chatError = e.Code; }
        catch (OperationCanceledException) { _chatError = cancel.IsCancellationRequested ? "cancelled" : "network"; }
        catch (Exception e) when (e is HttpRequestException or IOException or JsonException or FormatException)
        { DebugLog.Write("chatgpt.chat " + e.GetType().Name); _chatError = "network"; }
        _window.DispatcherQueue.TryEnqueue(() =>
        {
            output.Flush();
            if (_chatCancel == cancel) _chatCancel = null;
            // Une réponse vide n'a pas sa place dans l'historique renvoyé ensuite.
            if (answer.Length == 0 && _chat.Count > 0 && _chat[^1].Text == answer) _chat.RemoveAt(_chat.Count - 1);
            _chatBusy = false; PublishChat();
        });
    }

    private void CancelChat() { try { _chatCancel?.Cancel(); } catch (ObjectDisposedException) { } }
    private void ProtectPrivateAi()
    {
        if (!PrivateActive) return;
        CancelAi(); CancelChat(); CancelCategories();
        if (_aiPopup) HidePopup();
        if (Chat?.IsOpen == true) { Chat.Hide(); PublishChat(); }
    }
}
