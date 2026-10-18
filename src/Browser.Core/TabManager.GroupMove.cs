namespace Browser.Core;

public sealed partial class TabManager
{
    /// <summary>Déplace le groupe entier vers une frontière de la liste, sans couper un autre groupe.</summary>
    public bool MoveGroup(string id, int index)
    {
        var first = _tabs.FindIndex(t => t.GroupId == id);
        if (first < 0 || index < 0 || index > _tabs.Count) return false;
        var members = _tabs.Where(t => t.GroupId == id).ToList();
        var end = first + members.Count;
        index = Math.Max(PinnedCount, index);
        if (index >= first && index <= end) return true;
        if (index < _tabs.Count && _tabs[index].GroupId is { } target)
            index = index > first ? _tabs.FindLastIndex(t => t.GroupId == target) + 1
                : _tabs.FindIndex(t => t.GroupId == target);
        _tabs.RemoveRange(first, members.Count);
        _tabs.InsertRange(index > first ? index - members.Count : index, members);
        Changed?.Invoke();
        return true;
    }
}
