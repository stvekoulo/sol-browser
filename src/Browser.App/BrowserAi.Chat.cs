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
    private static readonly object[] ChatTools = [.. SolAiAgent.PageTools, .. SolAiAgent.NoteTools, .. SolAiBrowserTools.Tools, .. SolAiDeveloperTools.Tools];
    private const int ChatLimit = 40, ChatTextLimit = 20_000;

    private bool ChatCommand(Command c)
    {
        if (VoiceCommand(c)) return true;
        switch (c.Type)
        {
            case "chat.toggle":
                if (Chat is null) return true;
                if (PrivateActive) return true;
                // Sans compte connecté, le panneau propose la connexion.
                if (c.Id is "notes" or "developer") { _chatFocus = c.Id; _ = Chat.ShowAsync(); PublishChat(); }
                else if (Chat.IsOpen) { CloseVoice(); Chat.Hide(); } else { _chatFocus = ""; _ = Chat.ShowAsync(); }
                Refresh(); return true;
            case "chat.ready": _bridge.BroadcastTheme(); PublishChat(); return true;
            case "chat.send": _ = SendChatAsync(c.Input ?? ""); return true;
            // Depuis une note ou un sujet : le panneau s'ouvre et la question part tout de suite.
            case "chat.ask":
                if (Chat is null || PrivateActive) return true;
                if (c.Id is "notes" or "developer") _chatFocus = c.Id;
                _ = Chat.ShowAsync(); Refresh(); _ = SendChatAsync(c.Input ?? ""); return true;
            // Éventail d'une réponse : la copier, ou en faire une note.
            case "chat.copy" or "chat.note":
                if (!int.TryParse(c.Id, out var at) || at < 0 || at >= _chat.Count || _chat[at].Role != "assistant" || _chat[at].Text.Length == 0) return true;
                var answer = _chat[at].Text.ToString();
                if (c.Type == "chat.copy")
                {
                    var data = new Windows.ApplicationModel.DataTransfer.DataPackage(); data.SetText(answer);
                    Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data);
                }
                else if (!NoteDraftChanged)
                {
                    var question = at > 0 ? _chat[at - 1].Text.ToString() : "Sol AI";
                    _noteDraft = new("", question[..Math.Min(question.Length, 120)].ReplaceLineEndings(" "), answer[..Math.Min(answer.Length, 50_000)], "", DateTimeOffset.UtcNow);
                    OpenNotes(450, 60); PublishNotes();
                }
                return true;
            case "chat.cancel": _voiceTurn = false; _voice?.Stop(); try { _chatCancel?.Cancel(); } catch (ObjectDisposedException) { } return true;
            case "chat.clear":
                if (_chatBusy) return true;
                _voiceTurn = false; _voice?.Stop(); _chat.Clear(); _chatError = null; PublishChat(); return true;
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
            voice = VoiceState(),
            autoApproveActions = !PrivateActive && _settings.SolAiAutoApproveActions,
        }));
    }

    private async Task SendChatAsync(string question)
    {
        question = question.Trim();
        if (_chatBusy || question.Length is 0 or > ChatTextLimit) return;
        if (!AiReady) { _chatError = "signed-out"; PublishChat(); return; }
        _voiceTurn = _voiceOpen;
        _voice?.Stop();
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
                + "You can operate browser tabs, prepare workspaces, read and navigate sites, fill forms, use the keyboard and manage Sol notes with the tools provided. "
                + (_voiceTurn ? "Voice mode is active. Windows handles speech locally and provides the user's text. Keep your final answer concise for spoken playback. " : "")
                + SolAiAgent.Rules + SolAiBrowserTools.Rules + SolAiDeveloperTools.Rules, history.Select(m => (object)new { role = m.Role, content = m.Item2 }).ToList(), ChatTools, AgentTools(trace, () => _tabs.ActiveId, cancel.Token), output.Append, cancel.Token);
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
            FinishVoiceTurn(answer.ToString(), _chatError is null);
        });
    }

    private void CancelChat() { try { _chatCancel?.Cancel(); } catch (ObjectDisposedException) { } }
    private void ProtectPrivateAi()
    {
        if (!PrivateActive) return;
        CloseVoice(); CancelAi(); CancelChat(); CancelCategories();
        if (_aiPopup) HidePopup();
        if (Chat?.IsOpen == true) { Chat.Hide(); PublishChat(); }
    }
}
