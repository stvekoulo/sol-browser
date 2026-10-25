using Browser.Core;
using System.Text.Json;

namespace Browser.App;

public sealed partial class BrowserController
{
    private string WorkspacePath => Path.Combine(AppPaths.DataDir, "workspaces.json");
    private List<WorkspaceSnapshot> Workspaces() => JsonFileStore.Load<List<WorkspaceSnapshot>>(WorkspacePath);
    private void ShowWorkspaces(string error = "")
    {
        HidePopup();
        _popup.ShowCentered(420, JsonSerializer.Serialize(new { popup = "workspaces", error, canSave = !AppPaths.Private,
            items = Workspaces().Select(w => new { w.Id, w.Name, count = w.Session.Urls.Count, updated = w.Updated.ToString("u") }) }));
    }
    private bool WorkspaceCommand(Command c)
    {
        if (!c.Type.StartsWith("workspace.")) return false;
        if (c.Type == "workspace.open") { ShowWorkspaces(); return true; }
        if (AppPaths.Private) return true;
        var items = Workspaces();
        if (c.Type == "workspace.save")
        {
            var name = new string((c.Title ?? "").Where(ch => !char.IsControl(ch)).Take(80).ToArray()).Trim();
            if (name.Length == 0) return true;
            if (items.Count >= 30) { ShowWorkspaces(T("Limite de 30 espaces atteinte.", "30 workspace limit reached.")); return true; }
            items.Insert(0, new(Guid.NewGuid().ToString("N"), name, DateTimeOffset.UtcNow, CaptureWorkspace(), _pages.ComparisonDevices.ToArray()));
            JsonFileStore.Save(WorkspacePath, items); ShowWorkspaces();
        }
        else if (c.Type == "workspace.delete")
        { items.RemoveAll(w => w.Id == c.Id); JsonFileStore.Save(WorkspacePath, items); ShowWorkspaces(); }
        else if (c.Type == "workspace.restore" && items.FirstOrDefault(w => w.Id == c.Id) is { } snapshot)
        {
            // Ajouter l'espace aux onglets existants protège les formulaires et téléchargements en cours.
            HidePopup(); _pages.ComparisonDevices = snapshot.ComparisonDevices.Where(n => Device.Find(n) is not null).ToArray();
            RestoreSession(snapshot.Session, true); Refresh();
        }
        return true;
    }
}
