namespace Browser.Core;

public sealed record TabSession(string Id, string Name, string Site = "");

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
    public static bool IsSignIn(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return false;
        var keys = uri.Query.TrimStart('?').Split('&').Select(p => p.Split('=', 2)[0]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return uri.AbsolutePath.Contains("/__/auth/", StringComparison.OrdinalIgnoreCase)
            || keys.Contains("SAMLRequest") || keys.Contains("SAMLResponse")
            || keys.Contains("client_id") && (keys.Contains("redirect_uri") || keys.Contains("response_type"))
            || keys.Contains("state") && keys.Contains("code");
    }
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
    public string? Create(string name, string site = "")
    {
        if (Items.Count >= 30 || Clean(name) is not { Length: > 0 } clean) return null;
        var id = Guid.NewGuid().ToString("N");
        _items!.Add(new(id, clean, site)); Save(); return id;
    }
    // Une session appartient au site où elle a été créée. Les plus anciennes, sans site, prennent celui qu'elles chargent.
    private string SiteOfSession(string id)
    {
        var session = Items.FirstOrDefault(s => s.Id == id);
        if (session is null) return "";
        return session.Site.Length > 0 ? session.Site : Sites.FirstOrDefault(p => p.Value == id).Key ?? "";
    }
    public IReadOnlyList<TabSession> ForHost(string url) =>
        SiteOf(url) is { } site ? Items.Where(s => SiteOfSession(s.Id) == site).ToList() : [];
    /// <summary>Sessions d'une version précédente, encore rattachées à aucun site.</summary>
    public IReadOnlyList<TabSession> Unassigned => Items.Where(s => SiteOfSession(s.Id).Length == 0).ToList();
    public bool Belongs(string id, string url) => id.Length > 0 && SiteOf(url) is { } site && SiteOfSession(id) == site;
    public void Adopt(string id, string url)
    {
        var i = Items.ToList().FindIndex(s => s.Id == id);
        if (i < 0 || _items![i].Site.Length > 0 || SiteOf(url) is not { } site) return;
        _items[i] = _items[i] with { Site = SiteOfSession(id) is { Length: > 0 } known ? known : site };
        Save();
    }
    public bool Rename(string id, string name)
    {
        var i = Items.ToList().FindIndex(s => s.Id == id);
        if (i < 0 || Clean(name) is not { Length: > 0 } clean) return false;
        _items![i] = _items[i] with { Name = clean }; Save(); return true;
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
