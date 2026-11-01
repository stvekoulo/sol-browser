using System.Net;
using System.Text.Json;

namespace Browser.Core;

/// <summary>Une faute : Kind 0 = orthographe, 1 = grammaire ou typographie.</summary>
public sealed record SpellingIssue(int Start, int Length, int Kind, string[] Suggestions);

/// <summary>
/// Correction complète par l'API publique gratuite de LanguageTool (20 requêtes et 75 000 caractères par minute, sans
/// compte). Le texte quitte la machine : l'appelant ne l'utilise jamais pour un onglet privé ou une page de connexion.
/// </summary>
public sealed class OnlineSpelling(HttpClient http, Func<DateTime>? clock = null)
{
    public const string Endpoint = "https://api.languagetool.org/v2/check";
    public const int MaxLength = 20_000;
    private const int MinLength = 12; // trop court pour détecter une langue ou une faute de grammaire
    private DateTime _next = DateTime.MinValue;
    private DateTime Now => clock?.Invoke() ?? DateTime.UtcNow;

    /// <summary>Faux tant que le quota de l'API impose d'attendre : la correction locale reste affichée.</summary>
    public bool Worth(string text) => text.Length is >= MinLength and <= MaxLength && text.Any(char.IsWhiteSpace) && Now >= _next;

    public static string Language(int mode) => mode switch { 1 => "fr", 2 => "en-US", _ => "auto" };

    /// <summary>Null si le service n'a pas répondu : il sera retenté plus tard, sans bloquer la saisie.</summary>
    public async Task<SpellingIssue[]?> CheckAsync(string text, int mode, CancellationToken ct = default)
    {
        if (!Worth(text)) return null;
        // Un texte long consomme le quota en caractères : l'intervalle grandit avec lui.
        _next = Now + TimeSpan.FromSeconds(Math.Max(3.5, text.Length / 75_000.0 * 60));
        try
        {
            using var body = new FormUrlEncodedContent(new Dictionary<string, string> { ["text"] = text, ["language"] = Language(mode) });
            using var response = await http.PostAsync(Endpoint, body, ct);
            if (response.StatusCode == HttpStatusCode.TooManyRequests) { _next = Now + TimeSpan.FromSeconds(60); return null; }
            if (!response.IsSuccessStatusCode) { _next = Now + TimeSpan.FromSeconds(30); return null; }
            return Parse(await response.Content.ReadAsStringAsync(ct), text.Length);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            _next = Now + TimeSpan.FromSeconds(30);
            return null;
        }
    }

    public static SpellingIssue[] Parse(string json, int textLength)
    {
        using var doc = JsonDocument.Parse(json);
        var issues = new List<SpellingIssue>();
        foreach (var match in doc.RootElement.GetProperty("matches").EnumerateArray())
        {
            var start = match.GetProperty("offset").GetInt32();
            var length = match.GetProperty("length").GetInt32();
            if (start < 0 || length <= 0 || start > textLength - length) continue;
            var rule = match.TryGetProperty("rule", out var found) ? found : default;
            var type = rule.ValueKind == JsonValueKind.Object && rule.TryGetProperty("issueType", out var issue) ? issue.GetString() : null;
            var category = rule.ValueKind == JsonValueKind.Object && rule.TryGetProperty("category", out var group) && group.TryGetProperty("id", out var groupId) ? groupId.GetString() : null;
            // Style et usage relèvent du goût : seules les vraies fautes sont soulignées. Les règles françaises sont
            // souvent « uncategorized » : le refus porte donc sur ce qui est du style, pas sur une liste de fautes.
            if (type is "style" or "locale-violation" or "register" or "addition" or "omission"
                || category is "STYLE" or "REDUNDANCY" or "COLLOQUIALISMS" or "PLAIN_ENGLISH" or "WIKIPEDIA" or "GENDER_NEUTRALITY" or "SEMANTICS") continue;
            var suggestions = match.GetProperty("replacements").EnumerateArray()
                .Select(r => r.GetProperty("value").GetString() ?? "").Where(v => v.Length > 0).Take(5).ToArray();
            issues.Add(new(start, length, type == "misspelling" || category == "TYPOS" ? 0 : 1, suggestions));
        }
        return issues.ToArray();
    }
}
