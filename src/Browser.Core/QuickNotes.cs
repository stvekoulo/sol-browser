namespace Browser.Core;

public sealed record QuickNote(string Id, string Title, string Text, string Url, DateTimeOffset Updated);

/// <summary>Notes du profil ; un chemin null conserve uniquement la mémoire de la fenêtre privée.</summary>
public sealed class QuickNotes(string? path)
{
    public const string PageUrl = "https://start.sol/index.html#notes";
    private List<QuickNote> _items = path is null ? [] : JsonFileStore.Load<List<QuickNote>>(path).OfType<QuickNote>().ToList();
    public IReadOnlyList<QuickNote> Items => _items;
    public bool Save(string id, string title, string text, string url)
    {
        if (title.Length > 120 || text.Length > 50_000 || string.IsNullOrWhiteSpace(text)) return false;
        if (id.Length > 0 && !_items.Any(n => n.Id == id)) return false;
        if (url.Length > 4096 || (url.Length > 0 && (!Uri.TryCreate(url, UriKind.Absolute, out var source) || source.Scheme is not ("http" or "https") || source.UserInfo.Length > 0))) return false;
        var note = new QuickNote(id.Length == 0 ? Guid.NewGuid().ToString("N") : id,
            title.Trim(), text, url, DateTimeOffset.UtcNow);
        return Commit(_items.Where(n => n.Id != id).Prepend(note).ToList());
    }
    public bool Delete(string id) => _items.Any(n => n.Id == id) && Commit(_items.Where(n => n.Id != id).ToList());
    private bool Commit(List<QuickNote> next)
    {
        if (path is not null && !JsonFileStore.Save(path, next)) return false;
        _items = next;
        return true;
    }
    public string Export() => string.Join("\n\n---\n\n", _items.Select(n => $"{n.Title}\n{n.Text}\n{n.Url}\n{n.Updated:O}"));
}
