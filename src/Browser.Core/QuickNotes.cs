using System.Text.RegularExpressions;

namespace Browser.Core;

public sealed record NoteRevision(string Title, string Text, DateTimeOffset Updated);
public sealed record NoteComment(string Id, string Text, DateTimeOffset Created);
public sealed record QuickNote(string Id, string Title, string Text, string Url, DateTimeOffset Updated)
{
    public string Kind { get; init; } = "note";
    public string[] Tags { get; init; } = [];
    public bool Pinned { get; init; }
    public bool Archived { get; init; }
    public bool Reviewed { get; init; }
    public DateTimeOffset? Deleted { get; init; }
    public DateTimeOffset? Due { get; init; }
    public DateTimeOffset Created { get; init; }
    public string[] Files { get; init; } = [];
    public NoteRevision[] History { get; init; } = [];
    public NoteComment[] Comments { get; init; } = [];
}

/// <summary>Notes locales du profil ; un chemin null conserve seulement la mémoire de la fenêtre privée.</summary>
public sealed class QuickNotes(string? path)
{
    public const string PageUrl = "https://start.sol/index.html#notes";
    private List<QuickNote> _items = path is null ? [] : JsonFileStore.Load<List<QuickNote>>(path).OfType<QuickNote>().Where(n=>Valid(n,true)).ToList();
    public IReadOnlyList<QuickNote> All => _items;
    public IReadOnlyList<QuickNote> Items => _items.Where(n => n.Deleted is null).ToList();
    public static bool Valid(QuickNote n, bool allowEmpty = false) => n is {Id:not null,Title:not null,Text:not null,Url:not null,Tags:not null,Files:not null,History:not null,Comments:not null} && n.Id.Length <= 80 && n.Title.Length <= 120 && n.Text.Length <= 50_000 &&
        (allowEmpty || !string.IsNullOrWhiteSpace(n.Text) || n.Files.Length > 0) && n.Kind is "note" or "idea" or "task" && n.Tags.Length <= 40 &&
        n.Tags.All(t => t is not null && Regex.IsMatch(t, @"^[\p{L}\p{N}_/-]{1,64}$")) && n.Files.Length <= 30 &&
        n.Files.All(f => f is not null && Regex.IsMatch(f, "^[a-f0-9]{32}$")) && n.History.Length <= 50 && n.History.All(h => h is {Title:not null,Text:not null} && h.Title.Length <= 120 && h.Text.Length <= 50000) &&
        n.Comments.Length <= 500 && n.Comments.All(c => c is {Id:not null,Text:not null} && c.Id.Length <= 80 && c.Text.Length <= 5000) && n.Url.Length <= 4096 &&
        (n.Url.Length == 0 || Uri.TryCreate(n.Url, UriKind.Absolute, out var source) && source.Scheme is "http" or "https" && source.UserInfo.Length == 0);

    public bool Save(string id, string title, string text, string url) => Save((_items.Find(n => n.Id == id) ??
        new QuickNote(id, title, text, url, DateTimeOffset.UtcNow)) with { Title = title, Text = text, Url = url });
    public bool Save(QuickNote draft)
    {
        var previous = _items.Find(n => n.Id == draft.Id);
        if (!Valid(draft,allowEmpty:previous is not null) || draft.Deleted is not null) return false;
        if (draft.Id.Length > 0 && previous is null) return false;
        var history = previous?.History ?? [];
        if (previous is not null && (previous.Title != draft.Title || previous.Text != draft.Text))
            history = history.Append(new(previous.Title, previous.Text, previous.Updated)).TakeLast(50).ToArray();
        var now = DateTimeOffset.UtcNow;
        var note = draft with { Id = draft.Id.Length == 0 ? Guid.NewGuid().ToString("N") : draft.Id, Title = draft.Title.Trim(), Updated = now,
            Created = previous is null ? now : previous.Created == default ? previous.Updated : previous.Created, History = history,
            Tags = draft.Tags.Distinct(StringComparer.OrdinalIgnoreCase).ToArray() };
        return Commit(previous is null ? _items.Prepend(note).ToList() : _items.Select(n=>n.Id==note.Id?note:n).ToList());
    }
    public bool Delete(string id) => Action([id], "trash");
    public bool Action(IReadOnlyList<string> ids, string action)
    {
        if (ids.Count is 0 or > 1000 || ids.Distinct().Count() != ids.Count || ids.Any(id => _items.All(n => n.Id != id))) return false;
        var chosen = _items.Where(n => ids.Contains(n.Id)).ToList();
        if (action is not ("pin" or "unpin" or "archive" or "unarchive" or "trash" or "restore" or "purge" or "review" or "unreview" or "duplicate" or "kind:idea" or "kind:note" or "kind:task")) return false;
        if (action == "purge" && chosen.Any(n => n.Deleted is null)) return false;
        if (action is not ("restore" or "purge") && chosen.Any(n => n.Deleted is not null)) return false;
        if (action == "duplicate") return Commit(chosen.Select(n => n with { Id = Guid.NewGuid().ToString("N"), Pinned = false,
            Title = n.Title.Length > 110 ? n.Title[..110] + " (copie)" : n.Title + " (copie)", History = [], Created = DateTimeOffset.UtcNow, Updated = DateTimeOffset.UtcNow }).Concat(_items).ToList());
        return Commit(_items.Where(n => action != "purge" || !ids.Contains(n.Id)).Select(n => !ids.Contains(n.Id) ? n : action switch
        {
            "pin" => n with { Pinned = true }, "unpin" => n with { Pinned = false },
            "archive" => n with { Archived = true }, "unarchive" => n with { Archived = false },
            "trash" => n with { Deleted = DateTimeOffset.UtcNow }, "restore" => n with { Deleted = null },
            "review" => n with { Reviewed = true }, "unreview" => n with { Reviewed = false },
            "kind:idea"=>n with {Kind="idea"},"kind:note"=>n with {Kind="note"},"kind:task"=>n with {Kind="task"}, _ => n,
        }).ToList());
    }
    public bool RestoreRevision(string id, int index)
    {
        var note = _items.Find(n => n.Id == id && n.Deleted is null);
        if (note is null || index < 0 || index >= note.History.Length) return false;
        var revision = note.History[index]; return Save(note with { Title = revision.Title, Text = revision.Text });
    }
    public bool Comment(string id, string text, string? remove = null)
    {
        var note = _items.Find(n => n.Id == id && n.Deleted is null);
        if (note is null || text.Length > 5000 || remove is null && string.IsNullOrWhiteSpace(text)) return false;
        var comments = remove is null ? note.Comments.Append(new(Guid.NewGuid().ToString("N"), text.Trim(), DateTimeOffset.UtcNow)).ToArray()
            : note.Comments.Where(c => c.Id != remove).ToArray();
        if (comments.Length > 500) return false;
        return Commit(_items.Select(n => n.Id == id ? n with { Comments = comments } : n).ToList());
    }
    public bool RenameTag(string from, string to)
    {
        if (!Regex.IsMatch(from, @"^[\p{L}\p{N}_/-]{1,64}$") || to.Length > 0 && !Regex.IsMatch(to, @"^[\p{L}\p{N}_/-]{1,64}$")) return false;
        string Rename(string tag) => tag.Equals(from,StringComparison.OrdinalIgnoreCase)||tag.StartsWith(from+"/",StringComparison.OrdinalIgnoreCase) ? to.Length==0?"":to+tag[from.Length..] : tag;
        var next=_items.Select(n=>{
            var text=Regex.Replace(n.Text,@"(^|\s)#([\p{L}\p{N}_/-]{1,64})",m=>{var tag=Rename(m.Groups[2].Value);return m.Groups[1].Value+(tag.Length==0?"":"#"+tag);});
            return n with {Text=text,Tags=n.Tags.Select(Rename).Where(t=>t.Length>0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                History=text==n.Text?n.History:n.History.Append(new(n.Title,n.Text,n.Updated)).TakeLast(50).ToArray()};
        }).ToList();
        return next.All(n=>Valid(n,true))&&Commit(next);
    }
    public bool Import(IEnumerable<QuickNote> notes)
    {
        var incoming = notes.ToList();
        if (incoming.Count is 0 or > 1000 || incoming.Any(n => !Valid(n,allowEmpty:n is {Id:{Length:>0}})) || incoming.Where(n=>n.Id.Length>0).Select(n=>n.Id).Distinct().Count()!=incoming.Count(n=>n.Id.Length>0)) return false;
        var ids=incoming.Where(n=>n.Id.Length>0).Select(n=>n.Id).Distinct().ToDictionary(id=>id,_=>Guid.NewGuid().ToString("N"));
        return Commit(incoming.Select(n => n with { Id = ids.GetValueOrDefault(n.Id,Guid.NewGuid().ToString("N")), Deleted = null,
            Text=Regex.Replace(n.Text,@"\[\[([^\]]+)\]\]",m=>ids.TryGetValue(m.Groups[1].Value,out var next)?"[["+next+"]]":m.Value),
            Created = n.Created == default ? n.Updated : n.Created }).Concat(_items).ToList());
    }
    public bool Dates(string id,DateTimeOffset created,DateTimeOffset updated) => _items.Any(n=>n.Id==id&&n.Deleted is null)&&
        Commit(_items.Select(n=>n.Id==id?n with {Created=created,Updated=updated}:n).ToList());
    public bool Order(IReadOnlyList<string> ids) => ids.Count==_items.Count&&ids.Distinct().Count()==ids.Count&&ids.All(id=>_items.Any(n=>n.Id==id))&&
        Commit(ids.Select(id=>_items.First(n=>n.Id==id)).ToList());
    public bool AutoArchive(int days,DateTimeOffset now,string openId="")
    {
        if(days is <1 or >3650)return false;
        var ids=_items.Where(n=>n.Kind=="idea"&&!n.Pinned&&!n.Archived&&n.Deleted is null&&n.Id!=openId&&n.Updated<=now.AddDays(-days)).Select(n=>n.Id).ToArray();
        return ids.Length==0||Action(ids,"archive");
    }
    private bool Commit(List<QuickNote> next)
    {
        if (path is not null && !JsonFileStore.Save(path, next)) return false;
        _items = next; return true;
    }
    public string Export() => string.Join("\n\n---\n\n", Items.Select(n => $"# {n.Title}\n\n{n.Text}\n\n{n.Url}\n{n.Updated:O}"));
}
