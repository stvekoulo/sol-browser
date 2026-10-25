using Browser.Core;

namespace Browser.App;

public sealed partial class BrowserController
{
    // Sélection temporaire de cette fenêtre ; elle ne fait pas partie de la session enregistrée.
    private readonly HashSet<string> _selectedTabs = new(StringComparer.Ordinal);
    private string[] _groupCreationTabs = [];

    private List<Tab> SelectedTabs()
    {
        _selectedTabs.RemoveWhere(id => !_tabs.Tabs.Any(t => t.Id == id && !t.Pinned));
        return _tabs.Tabs.Where(t => _selectedTabs.Contains(t.Id)).ToList();
    }

    private void ToggleTabSelection(string id)
    {
        if (_tabs.Tabs.FirstOrDefault(t => t.Id == id && !t.Pinned) is not { } tab) return;
        var selection = SelectedTabs();
        if (selection.Any(t => t.Private != tab.Private)) _selectedTabs.Clear();
        if (_selectedTabs.Count == 0 && _tabs.Tabs.FirstOrDefault(t => t.Id == _tabs.ActiveId && !t.Pinned && t.Private == tab.Private) is { } active)
        {
            _selectedTabs.Add(active.Id);
            if (_split.Contains(active.Id))
            {
                _selectedTabs.Add(_split.PrimaryId!);
                _selectedTabs.Add(_split.SecondaryId!);
            }
        }
        // Une paire scindée reste ensemble, y compris lors de la création d'un groupe.
        var pair = _split.Contains(id) ? new[] { _split.PrimaryId!, _split.SecondaryId! } : [id];
        var remove = _selectedTabs.Contains(id);
        foreach (var member in pair)
            if (remove) _selectedTabs.Remove(member); else _selectedTabs.Add(member);
        if (!remove) Activate(id);
    }

    private List<Tab> GroupingTabs(string id)
    {
        var selection = SelectedTabs();
        var ids = selection.Count > 1 && selection.Any(t => t.Id == id)
            ? selection.Select(t => t.Id).ToHashSet() : new HashSet<string> { id };
        if (ids.Any(_split.Contains))
        {
            if (_split.PrimaryId is { } primary) ids.Add(primary);
            if (_split.SecondaryId is { } secondary) ids.Add(secondary);
        }
        return _tabs.Tabs.Where(t => ids.Contains(t.Id) && !t.Pinned).ToList();
    }

    private void CreateSelectedGroup(Command command)
    {
        var ids = _groupEditing is { Group: null } editing && editing.TabId == command.Id
            ? _groupCreationTabs : GroupingTabs(command.Id ?? "").Select(t => t.Id).ToArray();
        _tabs.CreateGroupFromTabs(ids, command.Title!, command.Query, command.Input);
        _selectedTabs.Clear(); _groupCreationTabs = [];
        KeepPairAdjacent();
    }

    private bool OpenSelectedTabsMenu(string id, double x, double y)
    {
        var members = GroupingTabs(id);
        if (members.Count < 2 || !_selectedTabs.Contains(id)) return false;
        var ids = members.Select(t => t.Id).ToArray();
        var entries = new List<(string, Action?)>
        {
            (T($"Créer un groupe avec ces {ids.Length} onglets…", $"Create a group with these {ids.Length} tabs…"),
                () => _afterMenu = () => EditGroup(id, null)),
        };
        foreach (var group in _tabs.Groups.Where(g => members.Any(t => t.GroupId != g.Id)
                     && _tabs.Tabs.Where(t => t.GroupId == g.Id).All(t => t.Private == members[0].Private)))
        {
            entries.Add((T($"Ajouter au groupe « {group.Name} »", $"Add to group “{group.Name}”"), () =>
            {
                foreach (var member in ids) _tabs.AddToGroup(member, group.Id);
                _tabs.UpdateGroup(group.Id, collapsed: false);
                _selectedTabs.Clear(); KeepPairAdjacent();
            }));
        }
        entries.Add(("", null));
        entries.Add((T("Annuler la sélection", "Clear selection"), () => _selectedTabs.Clear()));
        ShowMenu(x, y, entries);
        return true;
    }
}
