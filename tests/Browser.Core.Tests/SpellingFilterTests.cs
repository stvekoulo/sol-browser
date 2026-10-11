using Browser.Core;

public class SpellingFilterTests
{
    private static bool Keep(string text, string word) => SpellingFilter.Keep(text, text.IndexOf(word, StringComparison.Ordinal), word.Length);

    [Fact]
    public void Ordinary_misspelled_words_stay_underlined()
    {
        Assert.True(Keep("Un mesage envoyé.", "mesage"));
        Assert.True(Keep("« Bonjur », dit-il.", "Bonjur"));
        Assert.True(Keep("L'Europpe entière", "Europpe"));
    }

    [Fact]
    public void Addresses_identifiers_acronyms_and_brand_capitals_are_not_spelling_mistakes()
    {
        Assert.False(Keep("voir exemple.com pour la suite", "exemple"));
        Assert.False(Keep("écrire à steven@sol.dev", "steven"));
        Assert.False(Keep("https://sol.dev/telecharger", "telecharger"));
        Assert.False(Keep("le fichier notes_perso", "notes_perso"));
        Assert.False(Keep("la version v2beta", "v2beta"));
        Assert.False(Keep("une API REST", "REST"));
        Assert.False(Keep("sur WhatsApp et iPhone", "WhatsApp"));
        Assert.False(Keep("sur WhatsApp et iPhone", "iPhone"));
        Assert.False(Keep("court", "courte")); // hors du texte
    }
}
