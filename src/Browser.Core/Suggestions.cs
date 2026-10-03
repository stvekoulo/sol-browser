namespace Browser.Core;

/// <summary>Kind : "search" (recherche), "go" (adresse tapée), "history" (page déjà visitée), "site" (site connu).</summary>
public sealed record Suggestion(string Url, string Title, string Kind);

/// <summary>Suggestions de la barre d'adresse, calculées sur la machine : rien n'est envoyé pendant la frappe.</summary>
public static class Suggestions
{
    private static readonly (string Host, string Name)[] KnownSites =
    [
        ("google.com", "Google"), ("youtube.com", "YouTube"), ("github.com", "GitHub"),
        ("wikipedia.org", "Wikipédia"), ("mail.google.com", "Gmail"), ("facebook.com", "Facebook"),
        ("instagram.com", "Instagram"), ("x.com", "X"), ("reddit.com", "Reddit"),
        ("amazon.com", "Amazon"), ("netflix.com", "Netflix"), ("linkedin.com", "LinkedIn"),
        ("web.whatsapp.com", "WhatsApp"), ("chatgpt.com", "ChatGPT"), ("claude.ai", "Claude"),
        ("stackoverflow.com", "Stack Overflow"), ("developer.mozilla.org", "MDN Web Docs"),
        ("npmjs.com", "npm"), ("twitch.tv", "Twitch"), ("tiktok.com", "TikTok"),
        ("discord.com", "Discord"), ("figma.com", "Figma"), ("notion.so", "Notion"),
        ("vercel.com", "Vercel"), ("spotify.com", "Spotify"),
    ];

    public static IReadOnlyList<Suggestion> Build(string query, IReadOnlyList<Visit> history, string searchUrlTemplate, int limit = 7)
    {
        var q = query.Trim();
        if (q.Length == 0) return [];

        // Première ligne : ce que fera la touche Entrée.
        var resolved = AddressInput.Resolve(q, searchUrlTemplate);
        var isSearch = resolved.StartsWith(searchUrlTemplate.Split("{0}")[0], StringComparison.Ordinal);
        var list = new List<Suggestion> { new(resolved, q, isSearch ? "search" : "go") };

        list.AddRange(history.Select(v => new Suggestion(v.Url, v.Title, "history")));

        // Sites connus dont le nom ou l'adresse commence par la saisie, s'ils ne sont pas déjà dans l'historique proposé.
        foreach (var (host, name) in KnownSites)
        {
            var matches = host.StartsWith(q, StringComparison.OrdinalIgnoreCase) || name.StartsWith(q, StringComparison.OrdinalIgnoreCase);
            var known = list.Any(s => Uri.TryCreate(s.Url, UriKind.Absolute, out var u) && u.Host.EndsWith(host, StringComparison.OrdinalIgnoreCase) && s.Kind != "search");
            if (matches && !known) list.Add(new Suggestion("https://" + host, name, "site"));
        }
        return list.Take(limit).ToList();
    }
}
