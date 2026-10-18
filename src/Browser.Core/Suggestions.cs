using System.Text.Json;

namespace Browser.Core;

/// <summary>Kind : "search" (recherche), "go" (adresse tapée), "history" (page déjà visitée), "site" (site connu).</summary>
public sealed record Suggestion(string Url, string Title, string Kind);

/// <summary>Suggestions de la barre d'adresse : saisie, historique, sites connus, puis complétions du moteur de recherche.</summary>
public static class Suggestions
{
    private const int MaxSites = 2;

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

    public static IReadOnlyList<Suggestion> Build(string query, IReadOnlyList<Visit> history, string searchUrlTemplate,
        IReadOnlyList<string>? online = null, int limit = 8)
    {
        var q = query.Trim();
        if (q.Length == 0) return [];

        // Première ligne : ce que fera la touche Entrée.
        var resolved = AddressInput.Resolve(q, searchUrlTemplate);
        var isSearch = resolved.StartsWith(searchUrlTemplate.Split("{0}")[0], StringComparison.Ordinal);
        var list = new List<Suggestion> { new(resolved, resolved == QuickNotes.PageUrl ? "Notes" : q, isSearch ? "search" : "go") };

        list.AddRange(history.Select(v => new Suggestion(v.Url, v.Title, "history")));

        // Sites connus dont le nom ou l'adresse commence par la saisie, s'ils ne sont pas déjà dans l'historique proposé.
        var sites = 0;
        foreach (var (host, name) in KnownSites)
        {
            if (sites == MaxSites) break;
            var matches = host.StartsWith(q, StringComparison.OrdinalIgnoreCase) || name.StartsWith(q, StringComparison.OrdinalIgnoreCase);
            var known = list.Any(s => s.Kind != "search" && Uri.TryCreate(s.Url, UriKind.Absolute, out var u) && u.Host.EndsWith(host, StringComparison.OrdinalIgnoreCase));
            if (!matches || known) continue;
            list.Add(new Suggestion("https://" + host, name, "site"));
            sites++;
        }

        // Complétions du moteur de recherche : chacune lance cette recherche.
        foreach (var text in online ?? [])
            if (!text.Equals(q, StringComparison.OrdinalIgnoreCase))
                list.Add(new Suggestion(string.Format(searchUrlTemplate, Uri.EscapeDataString(text)), text, "search"));

        return list.Take(limit).ToList();
    }

    /// <summary>Lit la réponse du service de complétion de Google : ["saisie", ["complétion", …], …].</summary>
    public static IReadOnlyList<string> ParseGoogle(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() < 2 || root[1].ValueKind != JsonValueKind.Array) return [];
            return root[1].EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!)
                .Where(s => s.Length is > 0 and <= 200)
                .Take(6).ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
