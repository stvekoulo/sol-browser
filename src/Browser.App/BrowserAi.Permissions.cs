using Browser.Core;

namespace Browser.App;

public sealed partial class BrowserController
{
    private void AlwaysApprove(Command command)
    {
        // Une ancienne carte ou un identifiant inventé ne peut pas accorder un droit permanent.
        if (!AiReady || !new[] { _aiTrace, _chatTrace }.Any(trace =>
            trace?.Pending is { } pending && pending.Id == command.Id && !pending.Answer.Task.IsCompleted)) return;
        SetAiAutoApproveActions(true);
    }

    private void SetAiAutoApproveActions(bool enabled)
    {
        if (PrivateActive) return;
        if (!SolAiPermissions.TrySet(_settings, AiPermissionsPath, enabled))
        {
            Notice("Sol AI", T("Impossible d’enregistrer l’autorisation. Réessayez ; le réglage précédent est conservé.",
                "Could not save the permission. Please retry; the previous setting is preserved."));
            return;
        }
        if (enabled)
            foreach (var trace in new[] { _aiTrace, _chatTrace })
                if (trace?.Pending is { } pending) pending.Answer.TrySetResult(true);
        PublishAiSettings(); PublishChat(); PublishAi();
    }
}
