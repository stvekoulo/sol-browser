namespace Browser.Core;

public sealed record Tab(string Id, string Url, string Title);

public sealed class TabManager
{
    private readonly List<Tab> _tabs = new();
    // ponytail: pile non bornée ; plafonner si la mémoire d'une longue session pose problème
    private readonly Stack<string> _closed = new();
    private int _nextId = 1;

    public IReadOnlyList<Tab> Tabs => _tabs;
    public string? ActiveId { get; private set; }
    public event Action? Changed;

    public Tab Open(string url, bool activate = true)
    {
        var tab = new Tab($"t{_nextId++}", url, url);
        _tabs.Add(tab);
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

    public bool Move(string id, int index)
    {
        var i = IndexOf(id);
        if (i < 0 || index < 0 || index >= _tabs.Count) return false;
        var tab = _tabs[i];
        _tabs.RemoveAt(i);
        _tabs.Insert(index, tab);
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

    private int IndexOf(string id) => _tabs.FindIndex(t => t.Id == id);
}
