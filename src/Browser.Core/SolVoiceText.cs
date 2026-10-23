using System.Text.RegularExpressions;

namespace Browser.Core;

public static class SolVoiceText
{
    public static string ForSpeech(string markdown)
    {
        var text = Regex.Replace(markdown, @"```[\s\S]*?```", " ", RegexOptions.None, TimeSpan.FromSeconds(1));
        text = Regex.Replace(text, @"!?\[([^\]]+)\]\([^\)]+\)", "$1", RegexOptions.None, TimeSpan.FromSeconds(1));
        text = Regex.Replace(text, @"https?://\S+|<[^>]+>|\|", " ", RegexOptions.None, TimeSpan.FromSeconds(1));
        text = Regex.Replace(text, @"[#*_`>]", "", RegexOptions.None, TimeSpan.FromSeconds(1));
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return text[..Math.Min(4000, text.Length)];
    }
    public static bool? Approval(string text) => text.Trim().TrimEnd('.', '!', '?').TrimEnd().ToLowerInvariant() switch
    {
        "oui" or "j’autorise" or "j'autorise" or "autoriser" or "je confirme" or "yes" or "approve" or "confirm" => true,
        "non" or "refuser" or "annuler" or "no" or "reject" or "cancel" => false,
        _ => null,
    };
}
