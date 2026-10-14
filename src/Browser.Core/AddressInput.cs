using System.Net;

namespace Browser.Core;

public static class AddressInput
{
    private static readonly string[] Schemes = ["http://", "https://", "file://", "about:"];

    public static string Resolve(string input, string searchUrlTemplate)
    {
        var text = input.Trim();
        if (text.Equals("/notes", StringComparison.OrdinalIgnoreCase) || text.Equals(":notes", StringComparison.OrdinalIgnoreCase)
            || text.TrimEnd('/').Equals("sol://notes", StringComparison.OrdinalIgnoreCase)) return QuickNotes.PageUrl;
        if (Schemes.Any(s => text.StartsWith(s, StringComparison.OrdinalIgnoreCase))) return text;

        // « :3000 » : le serveur de développement de cet ordinateur.
        if (text.Length is >= 2 and <= 6 && text[0] == ':' && text[1..].All(char.IsAsciiDigit)) return "http://localhost" + text;

        if (!text.Contains(' '))
        {
            var host = text.Split('/', 2)[0].Split(':', 2)[0];
            // IPAddress.TryParse accepte "42" (0.0.0.42) : on exige un point pour ne pas détourner une recherche
            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || (host.Contains('.') && IPAddress.TryParse(host, out _)))
                return "http://" + text;
            if (host.Contains('.')) return "https://" + text;
        }
        return string.Format(searchUrlTemplate, Uri.EscapeDataString(text));
    }
}
