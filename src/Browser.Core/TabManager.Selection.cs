namespace Browser.Core;

public sealed partial class TabManager
{
    /// <summary>Regroupe une sélection d'onglets, dans leur ordre actuel, sans séparer les groupes restants.</summary>
    public TabGroup? CreateGroupFromTabs(IEnumerable<string> tabIds, string name, string? color = null, string? icon = null)
    {
        var ids = tabIds.ToHashSet(StringComparer.Ordinal);
        var members = _tabs.Where(t => ids.Contains(t.Id)).ToList();
        if (members.Count == 0 || members.Count != ids.Count || members.Any(t => t.Pinned)
            || members.Any(t => t.Private != members[0].Private)) return null;

        var first = members[0];
        var position = first.GroupId is { } originalGroup
            ? _tabs.FindIndex(t => t.GroupId == originalGroup) : _tabs.IndexOf(first);
        var insertion = _tabs.Take(position).Count(t => !ids.Contains(t.Id));
        var group = new TabGroup($"g{_nextGroupId++}", CleanName(name) is { Length: > 0 } clean ? clean : "Groupe",
            NormalizeColor(color) ?? GroupColors[_groups.Count % GroupColors.Count], Icon: NormalizeIcon(icon));
        _tabs.RemoveAll(t => ids.Contains(t.Id));
        _tabs.InsertRange(insertion, members.Select(t => t with { GroupId = group.Id }));
        _groups.Add(group);
        PruneGroups();
        Changed?.Invoke();
        return group;
    }
}
