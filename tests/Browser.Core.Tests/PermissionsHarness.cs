using Browser.Core;

namespace Browser.App;

// Compile le vrai gestionnaire d'autorisations ; seuls ses effets d'affichage sont simulés.
public sealed partial class BrowserController
{
    private readonly Settings _settings = new();
    public string AiPermissionsPath { get; set; } = "";
    private AgentTrace? _aiTrace, _chatTrace;
    private bool PrivateActive => PrivateContext;
    private sealed class AgentTrace
    {
        public (string Id, TaskCompletionSource<bool> Answer)? Pending;
    }
    public Settings PermissionSettings => _settings;
    public string? PermissionNotice { get; private set; }
    public Task<bool> PendingPermission(string id, bool chat = true)
    {
        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var trace = new AgentTrace { Pending = (id, answer) };
        if (chat) _chatTrace = trace; else _aiTrace = trace;
        return answer.Task;
    }
    public void GrantAlways(string id) => AlwaysApprove(new("ai.approve.always", Id: id));
    public void CancelPermission() => _chatTrace?.Pending?.Answer.TrySetResult(false);
    public void SetPermission(bool enabled) => SetAiAutoApproveActions(enabled);
    private void PublishAiSettings() { }
    private void PublishChat() { }
    private void PublishAi() { }
    private void Notice(string title, string text) => PermissionNotice = text;
    private static string T(string french, string english) => french;
}
