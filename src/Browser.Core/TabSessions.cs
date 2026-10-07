namespace Browser.Core;

public sealed record TabSession(string Id, string Name);

/// <summary>Identités stables des profils WebView2, distinctes des noms affichés.</summary>
public sealed class TabSessions(string path)
{
    private List<TabSession>? _items;
    private Dictionary<string, string>? _sites;
    private Dictionary<string, string> Sites => _sites ??= JsonFileStore.Load<Dictionary<string, string>>(path + ".sites.json", recoverBackup: false);
    public IReadOnlyList<TabSession> Items => _items ??= JsonFileStore.Load<List<TabSession>>(path, recoverBackup: false)
        .Where(s => s is not null && ValidId(s.Id) && Clean(s.Name).Length > 0).DistinctBy(s => s.Id).Take(30).ToList();
    public static bool ValidId(string? id) => id is { Length: 32 } && id.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    public string Resolve(string? id) => Items.Any(s => s.Id == id) ? id! : "";
    public string Name(string id) => Items.FirstOrDefault(s => s.Id == id)?.Name ?? "";
    // Un hôte = un choix : mail.example et video.example restent indépendants.
    public static string? SiteOf(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.IdnHost == "start.sol") return null;
        var host = uri.IdnHost.ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal)) host = host[4..];
        return host + (uri.IsDefaultPort ? "" : ":" + uri.Port);
    }
    public static bool SameSite(string a, string b) => SiteOf(a) is { } site && site == SiteOf(b);
    public string ForSite(string url) => SiteOf(url) is { } site ? Resolve(Sites.GetValueOrDefault(site)) : "";
    public void SetSite(string url, string id)
    {
        if (SiteOf(url) is not { } site || id.Length > 0 && Resolve(id).Length == 0) return;
        if (id.Length == 0) Sites.Remove(site); else Sites[site] = id;
        JsonFileStore.Save(path + ".sites.json", Sites);
    }
    public void Seed()
    {
        // Un fichier vide volontairement conserve la suppression des sessions proposées.
        if (File.Exists(path)) return;
        foreach (var name in new[] { "Admin", "User", "Invité" }) Create(name);
    }
    public string? Create(string name)
    {
        if (Items.Count >= 30 || Clean(name) is not { Length: > 0 } clean) return null;
        var id = Guid.NewGuid().ToString("N");
        _items!.Add(new(id, clean)); Save(); return id;
    }
    public bool Rename(string id, string name)
    {
        var i = Items.ToList().FindIndex(s => s.Id == id);
        if (i < 0 || Clean(name) is not { Length: > 0 } clean) return false;
        _items![i] = new(id, clean); Save(); return true;
    }
    public void Delete(string id)
    {
        _ = Items; _items!.RemoveAll(s => s.Id == id); Save();
        foreach (var site in Sites.Where(s => s.Value == id).Select(s => s.Key).ToArray()) Sites.Remove(site);
        JsonFileStore.Save(path + ".sites.json", Sites);
    }
    private void Save() => JsonFileStore.Save(path, _items);
    private static string Clean(string? name) => new string((name ?? "").Where(c => !char.IsControl(c)).Take(30).ToArray()).Trim();
}
