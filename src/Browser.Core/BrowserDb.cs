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

    /// <summary>
    /// Reprend les favoris et l'historique d'un autre navigateur, en une seule opération. Un favori déjà là garde son
    /// titre ; une page déjà visitée additionne ses visites et garde le titre et la date de la visite la plus récente.
    /// </summary>
    public void Import(IEnumerable<Bookmark> bookmarks, IEnumerable<ImportedVisit> visits)
    {
        using var transaction = _db.BeginTransaction();
        var added = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var b in bookmarks)
            Exec("INSERT INTO bookmarks (url, title, added) VALUES ($url, $title, $added) ON CONFLICT(url) DO NOTHING",
                ("$url", b.Url), ("$title", b.Title.Length > 0 ? b.Title : b.Url), ("$added", added++));
        foreach (var v in visits)
            Exec("""
                INSERT INTO visits (url, title, count, last) VALUES ($url, $title, $count, $last)
                ON CONFLICT(url) DO UPDATE SET count = count + $count,
                    title = CASE WHEN $last > last THEN $title ELSE title END, last = MAX(last, $last)
                """, ("$url", v.Url), ("$title", v.Title), ("$count", v.Count), ("$last", v.Last.ToUnixTimeMilliseconds()));
        transaction.Commit();
    }

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

    /// <summary>Copie cohérente de la base dans un nouveau fichier, pendant qu'elle sert.</summary>
    public void BackupTo(string file) => Exec("VACUUM INTO $file", ("$file", file));

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
