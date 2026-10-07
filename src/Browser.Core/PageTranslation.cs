namespace Browser.Core;

public static class PageTranslation
{
    public static string TargetLanguage(string? pageLanguage, string browserLanguage) =>
        (pageLanguage ?? "").Trim().Split('-', '_')[0].ToLowerInvariant() switch
        {
            "fr" => "en",
            "en" => "fr",
            _ => browserLanguage == "fr" ? "fr" : "en",
        };
    public static bool IsGooglePage(string source) => Uri.TryCreate(source, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && (uri.Host is "translate.google.com" or "consent.google.com" || uri.Host.EndsWith(".translate.goog", StringComparison.OrdinalIgnoreCase));
    public static string? GoogleUrl(string source, string language)
    {
        if (language is not ("fr" or "en") || !Uri.TryCreate(source, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length != 0
            || uri.IsLoopback || !uri.Host.Contains('.') || uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)) return null;
        return $"https://translate.google.com/translate?sl=auto&tl={language}&u={Uri.EscapeDataString(source)}";
    }
}
