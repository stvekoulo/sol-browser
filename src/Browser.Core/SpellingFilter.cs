namespace Browser.Core;

/// <summary>
/// Ce qu'un dictionnaire signale et qui n'est pas une faute de frappe : adresses, noms de fichiers, identifiants,
/// mots avec chiffres, sigles et noms à majuscule interne (« WhatsApp », « iPhone »).
/// </summary>
public static class SpellingFilter
{
    /// <summary>Faux : le mot signalé à cet endroit du texte n'est pas à souligner.</summary>
    public static bool Keep(string text, int start, int length)
    {
        if (start < 0 || length <= 0 || start > text.Length - length) return false;
        // Le mot dans son entourage, jusqu'aux espaces : « exemple.com », « nom@site.fr », « v2 », « C:\\dossier ».
        int from = start, to = start + length;
        while (from > 0 && !char.IsWhiteSpace(text[from - 1])) from--;
        while (to < text.Length && !char.IsWhiteSpace(text[to])) to++;
        var around = text.AsSpan(from, to - from);
        if (around.IndexOfAny("@/\\_#") >= 0) return false;
        for (var i = 0; i < around.Length; i++)
        {
            if (char.IsDigit(around[i])) return false;
            // Un point entre deux lettres : domaine ou nom de fichier, pas une fin de phrase.
            if (around[i] == '.' && i > 0 && i < around.Length - 1 && char.IsLetter(around[i - 1]) && char.IsLetter(around[i + 1])) return false;
        }
        var word = text.AsSpan(start, length);
        bool lower = false, innerUpper = false;
        for (var i = 0; i < word.Length; i++)
        {
            if (char.IsLower(word[i])) lower = true;
            else if (char.IsUpper(word[i]) && i > 0 && char.IsLetter(word[i - 1])) innerUpper = true;
        }
        // Tout en majuscules (sigle) ou majuscule au milieu d'un mot : un nom, pas une faute.
        return lower && !innerUpper;
    }
}
