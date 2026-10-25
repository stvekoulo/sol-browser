namespace Browser.Core;

// L'accord appartient au profil et n'est actif qu'après son enregistrement durable.
public static class SolAiPermissions
{
    public static bool VoiceAvailable => false;

    public static bool TrySet(Settings settings, string path, bool enabled)
    {
        var previous = settings.SolAiAutoApproveActions;
        settings.SolAiAutoApproveActions = enabled;
        if (JsonFileStore.Save(path, settings)) return true;
        settings.SolAiAutoApproveActions = previous;
        return false;
    }

    public static bool RequiresApproval(string tool, Settings settings) =>
        SolAiAgent.NeedsApproval(tool) && !settings.SolAiAutoApproveActions;
}
