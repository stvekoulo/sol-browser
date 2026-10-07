using Browser.Core;

public class BrowserShortcutsTests
{
    [Fact]
    public void Bindings_are_unique_and_share_localized_labels()
    {
        var shortcuts = BrowserShortcuts.All;
        Assert.Equal(shortcuts.Count, shortcuts.Select(s => (s.Key, s.Modifiers)).Distinct().Count());
        Assert.All(shortcuts, s => Assert.False(string.IsNullOrWhiteSpace(s.French) || string.IsNullOrWhiteSpace(s.English)));
        Assert.Contains(shortcuts, s => s.Key == 0x48 && s.Modifiers == 2 && s.Action == "history.open");
        Assert.Equal("Ctrl+H", BrowserShortcuts.TextFor("history.open", false));
        Assert.Equal("Ctrl+Maj+P", BrowserShortcuts.TextFor("palette.open", false));
        Assert.Equal("Ctrl+Shift+P", BrowserShortcuts.TextFor("palette.open", true));
        Assert.Equal("F9", BrowserShortcuts.TextFor("reader.toggle", false));
        Assert.All(shortcuts.Where(s => (s.Modifiers & 1) != 0), alt =>
            Assert.Contains(shortcuts, alternative => alternative.Action == alt.Action && (alternative.Modifiers & 1) == 0));
        Assert.Null(CommandParser.Parse("""{"type":"design.toggle"}"""));
        Assert.Equal(new Command("design.toggle", Id: "t1"), CommandParser.Parse("""{"type":"design.toggle","id":"t1"}"""));
    }
}
