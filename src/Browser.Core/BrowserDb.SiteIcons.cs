namespace Browser.Core;

public sealed partial class BrowserDb
{
    // Cache par profil : les échecs sont aussi mémorisés pour ne pas recommencer à chaque ouverture.
    public (bool Found, string? Icon) CachedSiteIcon(string domain)
    {
        using var cmd = Command("SELECT icon, checked FROM site_icons WHERE domain=$domain", ("$domain", domain));
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return (false, null);
        var icon = reader.IsDBNull(0) ? null : reader.GetString(0);
        var age = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - reader.GetInt64(1);
        // Une icône périmée reste disponible comme dernier visuel connu pendant son actualisation.
        return (age >= 0 && age < TimeSpan.FromDays(icon is null ? 1 : 7).TotalMilliseconds, icon);
    }

    public void CacheSiteIcon(string domain, string? icon)
    {
        if (domain.Length is 0 or > 300 || icon is { Length: > 140_000 }) return;
        using var transaction = _db.BeginTransaction();
        Exec("""
            INSERT INTO site_icons (domain,icon,checked) VALUES ($domain,$icon,$checked)
            ON CONFLICT(domain) DO UPDATE SET icon=COALESCE($icon, site_icons.icon),checked=$checked
            """, ("$domain", domain), ("$icon", (object?)icon ?? DBNull.Value), ("$checked", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        Exec("""
            DELETE FROM site_icons
            WHERE domain NOT IN (SELECT domain FROM site_icons ORDER BY checked DESC, domain LIMIT 512)
              AND (icon IS NULL OR domain NOT IN (SELECT site_icon_domain(url) FROM bookmarks))
            """);
        transaction.Commit();
    }
}
