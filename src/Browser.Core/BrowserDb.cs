using Microsoft.Data.Sqlite;

namespace Browser.Core;

public sealed record Visit(string Url, string Title, int Count, DateTimeOffset Last);
public sealed record Bookmark(string Url, string Title);

public sealed class BrowserDb : IDisposable
{
    private readonly SqliteConnection _db;

    public BrowserDb(string dataSource)
    {
        _db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dataSource }.ToString());
        _db.Open();
        Exec("""
            CREATE TABLE IF NOT EXISTS visits (
                url TEXT PRIMARY KEY, title TEXT NOT NULL, count INTEGER NOT NULL, last INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS bookmarks (
                url TEXT PRIMARY KEY, title TEXT NOT NULL, added INTEGER NOT NULL);
            """);
    }

    public void RecordVisit(string url, string title, DateTimeOffset when) => Exec("""
        INSERT INTO visits (url, title, count, last) VALUES ($url, $title, 1, $last)
        ON CONFLICT(url) DO UPDATE SET title = $title, count = count + 1, last = $last
        """, ("$url", url), ("$title", title), ("$last", when.ToUnixTimeMilliseconds()));

    public IReadOnlyList<Visit> RecentHistory(int limit) => Visits(
        "SELECT url, title, count, last FROM visits ORDER BY last DESC LIMIT $limit", ("$limit", limit));

    public IReadOnlyList<Visit> Suggest(string query, int limit)
    {
        var pattern = "%" + query.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
        return Visits("""
            SELECT url, title, count, last FROM visits
            WHERE url LIKE $p ESCAPE '\' OR title LIKE $p ESCAPE '\'
            ORDER BY count DESC, last DESC LIMIT $limit
            """, ("$p", pattern), ("$limit", limit));
    }

    public void AddBookmark(string url, string title) => Exec("""
        INSERT INTO bookmarks (url, title, added) VALUES ($url, $title, $added)
        ON CONFLICT(url) DO UPDATE SET title = $title
        """, ("$url", url), ("$title", title), ("$added", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));

    public void RemoveBookmark(string url) => Exec("DELETE FROM bookmarks WHERE url = $url", ("$url", url));

    public bool IsBookmarked(string url)
    {
        using var cmd = Command("SELECT 1 FROM bookmarks WHERE url = $url", ("$url", url));
        return cmd.ExecuteScalar() is not null;
    }

    public IReadOnlyList<Bookmark> Bookmarks()
    {
        using var cmd = Command("SELECT url, title FROM bookmarks ORDER BY added");
        using var r = cmd.ExecuteReader();
        var list = new List<Bookmark>();
        while (r.Read()) list.Add(new Bookmark(r.GetString(0), r.GetString(1)));
        return list;
    }

    public void Dispose() => _db.Dispose();

    private IReadOnlyList<Visit> Visits(string sql, params (string, object)[] args)
    {
        using var cmd = Command(sql, args);
        using var r = cmd.ExecuteReader();
        var list = new List<Visit>();
        while (r.Read())
            list.Add(new Visit(r.GetString(0), r.GetString(1), r.GetInt32(2),
                DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(3))));
        return list;
    }

    private void Exec(string sql, params (string, object)[] args)
    {
        using var cmd = Command(sql, args);
        cmd.ExecuteNonQuery();
    }

    private SqliteCommand Command(string sql, params (string Name, object Value)[] args)
    {
        var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value);
        return cmd;
    }
}
