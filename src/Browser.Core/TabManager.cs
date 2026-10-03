namespace Browser.Core;

public sealed record Tab(string Id, string Url, string Title, bool Pinned = false, string? GroupId = null);

/// <summary>Groupe d'onglets : un nom, une couleur, et un état replié (ses onglets sont alors masqués dans la barre).</summary>
public sealed record TabGroup(string Id, string Name, string Color, bool Collapsed = false);

/// <summary>
/// Onglets ouverts, dans l'ordre de la barre. Les onglets épinglés sont toujours en tête et ne sont jamais
/// groupés ; les onglets d'un même groupe sont toujours voisins.
/// </summary>
public sealed class TabManager
{
    public static readonly IReadOnlyList<string> GroupColors = ["blue", "green", "yellow", "orange", "pink", "purple", "grey"];
    private const int MaxGroupName = 30;

    private readonly List<Tab> _tabs = new();
    private readonly List<TabGroup> _groups = new();
    // ponytail: pile non bornée ; plafonner si la mémoire d'une longue session pose problème
    private readonly Stack<string> _closed = new();
    private int _nextId = 1, _nextGroupId = 1;

    public IReadOnlyList<Tab> Tabs => _tabs;
    public IReadOnlyList<TabGroup> Groups => _groups;
    public string? ActiveId { get; private set; }
    public int PinnedCount => _tabs.Count(t => t.Pinned);
    public event Action? Changed;

    public Tab Open(string url, bool activate = true, bool pinned = false)
    {
        var tab = new Tab($"t{_nextId++}", url, url, pinned);
        _tabs.Insert(pinned ? PinnedCount : _tabs.Count, tab);
        if (activate || ActiveId is null) ActiveId = tab.Id;
        Changed?.Invoke();
        return tab;
    }

    public bool Close(string id)
    {
        var i = IndexOf(id);
        if (i < 0) return false;
        _closed.Push(_tabs[i].Url);
        _tabs.RemoveAt(i);
        if (ActiveId == id)
            ActiveId = _tabs.Count == 0 ? null : _tabs[Math.Min(i, _tabs.Count - 1)].Id;
        PruneGroups();
        Changed?.Invoke();
        return true;
    }

    public bool Activate(string id)
    {
        if (IndexOf(id) < 0) return false;
        ActiveId = id;
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// Déplace un onglet. Il reste dans sa zone (épinglés en tête, les autres après). Déposé entre deux onglets
    /// d'un groupe, il rejoint ce groupe ; sorti de son groupe, il le quitte.
    /// </summary>
    public bool Move(string id, int index)
    {
        var i = IndexOf(id);
        if (i < 0 || index < 0 || index >= _tabs.Count) return false;
        var tab = _tabs[i];
        var pinned = PinnedCount;
        index = tab.Pinned ? Math.Min(index, pinned - 1) : Math.Max(index, pinned);
        _tabs.RemoveAt(i);
        _tabs.Insert(index, tab);

        var left = index > 0 ? _tabs[index - 1].GroupId : null;
        var right = index < _tabs.Count - 1 ? _tabs[index + 1].GroupId : null;
        if (!tab.Pinned && left is not null && left == right) _tabs[index] = tab with { GroupId = left };
        else if (tab.GroupId is { } own && left != own && right != own && _tabs.Count(t => t.GroupId == own) > 1)
            _tabs[index] = tab with { GroupId = null };
        PruneGroups();
        Changed?.Invoke();
        return true;
    }

    /// <summary>Épingle ou détache un onglet ; il rejoint la frontière entre les deux zones. Épinglé, il quitte son groupe.</summary>
    public bool SetPinned(string id, bool pinned)
    {
        var i = IndexOf(id);
        if (i < 0) return false;
        if (_tabs[i].Pinned == pinned) return true;
        var tab = _tabs[i] with { Pinned = pinned, GroupId = pinned ? null : _tabs[i].GroupId };
        _tabs.RemoveAt(i);
        _tabs.Insert(PinnedCount, tab); // fin des épinglés = début des autres
        PruneGroups();
        Changed?.Invoke();
        return true;
    }

    public Tab? ReopenClosed() => _closed.Count == 0 ? null : Open(_closed.Pop());

    public bool Update(string id, string? url = null, string? title = null)
    {
        var i = IndexOf(id);
        if (i < 0) return false;
        _tabs[i] = _tabs[i] with { Url = url ?? _tabs[i].Url, Title = title ?? _tabs[i].Title };
        Changed?.Invoke();
        return true;
    }

    /// <summary>Crée un groupe autour d'un onglet. Un onglet épinglé ne se groupe pas.</summary>
    public TabGroup? CreateGroup(string tabId, string name, string? color = null, bool collapsed = false)
    {
        var i = IndexOf(tabId);
        if (i < 0 || _tabs[i].Pinned) return null;
        var clean = CleanName(name);
        var group = new TabGroup($"g{_nextGroupId++}", clean.Length > 0 ? clean : "Groupe",
            color is not null && GroupColors.Contains(color) ? color : GroupColors[_groups.Count % GroupColors.Count], collapsed);
        _groups.Add(group);
        _tabs[i] = _tabs[i] with { GroupId = group.Id };
        PruneGroups();
        Changed?.Invoke();
        return group;
    }

    /// <summary>Ajoute un onglet à un groupe ; il se range à la fin du groupe.</summary>
    public bool AddToGroup(string tabId, string groupId)
    {
        var i = IndexOf(tabId);
        if (i < 0 || _tabs[i].Pinned || _groups.All(g => g.Id != groupId)) return false;
        if (_tabs[i].GroupId == groupId) return true;
        var tab = _tabs[i] with { GroupId = groupId };
        _tabs.RemoveAt(i);
        _tabs.Insert(_tabs.FindLastIndex(t => t.GroupId == groupId) + 1, tab);
        PruneGroups();
        Changed?.Invoke();
        return true;
    }

    /// <summary>Retire un onglet de son groupe ; s'il était au milieu, il se range juste après le groupe.</summary>
    public bool RemoveFromGroup(string tabId)
    {
        var i = IndexOf(tabId);
        if (i < 0 || _tabs[i].GroupId is not { } groupId) return false;
        var tab = _tabs[i] with { GroupId = null };
        _tabs.RemoveAt(i);
        var last = _tabs.FindLastIndex(t => t.GroupId == groupId);
        var first = _tabs.FindIndex(t => t.GroupId == groupId);
        _tabs.Insert(first >= 0 && i > first && i <= last ? last + 1 : i, tab);
        PruneGroups();
        Changed?.Invoke();
        return true;
    }

    /// <summary>L'onglet prend le groupe d'un autre sans bouger. À l'appelant de les garder voisins (vue scindée).</summary>
    public void MatchGroup(string tabId, string otherId)
    {
        int i = IndexOf(tabId), o = IndexOf(otherId);
        if (i < 0 || o < 0 || _tabs[i].GroupId == _tabs[o].GroupId) return;
        _tabs[i] = _tabs[i] with { GroupId = _tabs[o].GroupId };
        PruneGroups();
        Changed?.Invoke();
    }

    /// <summary>Renomme, recolore, replie ou déplie un groupe.</summary>
    public bool UpdateGroup(string groupId, string? name = null, string? color = null, bool? collapsed = null)
    {
        var g = _groups.FindIndex(x => x.Id == groupId);
        if (g < 0) return false;
        var group = _groups[g];
        if (name is not null && CleanName(name) is { Length: > 0 } clean) group = group with { Name = clean };
        if (color is not null && GroupColors.Contains(color)) group = group with { Color = color };
        if (collapsed is { } fold)
        {
            // Replier le groupe de l'onglet actif : un onglet d'ailleurs prend la main. S'il n'y en a pas, on ne replie pas.
            if (fold && _tabs.FirstOrDefault(t => t.Id == ActiveId)?.GroupId == groupId)
            {
                if (_tabs.FirstOrDefault(t => t.GroupId != groupId) is not { } other) return false;
                ActiveId = other.Id;
            }
            group = group with { Collapsed = fold };
        }
        _groups[g] = group;
        Changed?.Invoke();
        return true;
    }

    /// <summary>Supprime le groupe ; ses onglets restent ouverts.</summary>
    public bool Ungroup(string groupId)
    {
        if (_groups.RemoveAll(g => g.Id == groupId) == 0) return false;
        for (var i = 0; i < _tabs.Count; i++)
            if (_tabs[i].GroupId == groupId) _tabs[i] = _tabs[i] with { GroupId = null };
        Changed?.Invoke();
        return true;
    }

    private void PruneGroups() => _groups.RemoveAll(g => _tabs.All(t => t.GroupId != g.Id));

    private static string CleanName(string name)
    {
        var text = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return text.Length > MaxGroupName ? text[..MaxGroupName].Trim() : text;
    }

    private int IndexOf(string id) => _tabs.FindIndex(t => t.Id == id);
}
