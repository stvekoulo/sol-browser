using Browser.Core;
using System.Text.Json;

namespace Browser.App;

public sealed partial class BrowserController
{
    private WindowsVoice? _voice;
    private bool _voiceOpen, _voiceTurn;
    private string? _voiceApproval;

    private object VoiceState() => new
    {
        available = SolAiPermissions.VoiceAvailable,
        open = SolAiPermissions.VoiceAvailable && _voiceOpen && AiReady, provider = "windows",
        mode = _voice?.Mode is "listening" or "speaking" ? _voice.Mode : _voiceTurn && _chatBusy ? _chatTrace?.Pending is not null ? "approval" : "thinking" : _voice?.Mode ?? "idle",
        level = _voice?.Level ?? 0, text = _voice?.Text ?? "", error = _voice?.Error,
    };
    private void PublishVoice()
    {
        if (!_closingWindow && Chat is not null) Chat.Post(JsonSerializer.Serialize(new { voice = VoiceState() }));
    }
    private bool VoiceCommand(Command c)
    {
        if (!c.Type.StartsWith("voice.", StringComparison.Ordinal)) return false;
        if (c.Type == "voice.close") { CloseVoice(); return true; }
        if (!SolAiPermissions.VoiceAvailable) { CloseVoice(); return true; }
        if (!AiReady || Chat?.IsOpen != true) return true;
        if (c.Type == "voice.settings")
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:speech") { UseShellExecute = true });
            return true;
        }
        if (c.Type == "voice.stop") { _voiceTurn = false; _voiceApproval = null; _voice?.Stop(); PublishVoice(); return true; }
        if (c.Type is not ("voice.open" or "voice.listen")) return true;
        _voiceOpen = true;
        if (_voice is null)
        {
            _voice = new WindowsVoice(action => _window.DispatcherQueue.TryEnqueue(() => action()));
            _voice.Changed += PublishVoice;
            _voice.Recognized += text =>
            {
                if (!AiReady || !_voiceOpen || Chat?.IsOpen != true) return;
                if (_voiceApproval is { } approval)
                {
                    _voiceApproval = null;
                    if (_chatTrace?.Pending is not { } pending || pending.Id != approval) return;
                    if (SolVoiceText.Approval(text) is { } allow) AnswerApproval(new("ai.approve", Id: approval, Index: allow ? 1 : 0));
                    PublishVoice(); return;
                }
                if (_chatBusy) return;
                _voiceTurn = true; _ = SendChatAsync(text);
            };
        }
        if (c.Type == "voice.listen")
        {
            if (_chatBusy && _chatTrace?.Pending is null) return true;
            if (!_chatBusy) _voiceTurn = false;
            _voiceApproval = _chatTrace?.Pending?.Id;
            _voice.Listen(Language.Current, approvalOnly: _voiceApproval is not null);
        }
        PublishVoice(); return true;
    }
    private void FinishVoiceTurn(string answer, bool success)
    {
        if (!_voiceTurn || !_voiceOpen || !AiReady || Chat?.IsOpen != true) return;
        _voiceTurn = false;
        if (success) _voice?.Speak(SolVoiceText.ForSpeech(answer), Language.Current);
        PublishVoice();
    }
    private void SpeakVoiceApproval(string label)
    {
        if (_voiceOpen && _voiceTurn && AiReady)
            _voice?.Speak(label + ". " + T("Consultez les détails dans la discussion. Pour répondre à voix haute, appuyez sur Répondre oui ou non.",
                "Review the details in the conversation. To answer aloud, press Answer yes or no."), Language.Current);
    }
    private void CloseVoice()
    {
        _voiceOpen = false; _voiceTurn = false; _voiceApproval = null;
        _voice?.Dispose(); _voice = null;
        PublishVoice();
    }
}
