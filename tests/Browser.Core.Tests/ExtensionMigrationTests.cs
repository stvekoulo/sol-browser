using Browser.Core;

public sealed class ExtensionMigrationTests
{
    [Fact]
    public void Unopened_sessions_are_merged_without_enabling_disabled_extensions_or_writing_preferences()
    {
        var root = Path.Combine(Path.GetTempPath(), "sol-migration-" + Guid.NewGuid());
        var a = Path.Combine(root, "A"); var b = Path.Combine(root, "B");
        Directory.CreateDirectory(a); Directory.CreateDirectory(b);
        var id = new string('a', 32); var other = new string('b', 32);
        var first = """{"extensions":{"settings":{"A":{"disable_reasons":[]},"B":{"state":1},"invalid":{"state":1}}}}"""
            .Replace("\"A\"", "\"" + id + "\"").Replace("\"B\"", "\"" + other + "\"");
        var second = """{"extensions":{"settings":{"A":{"disable_reasons":[1]},"B":{"disable_reasons":0}}}}"""
            .Replace("\"A\"", "\"" + id + "\"").Replace("\"B\"", "\"" + other + "\"");
        try
        {
            File.WriteAllText(Path.Combine(a, "Secure Preferences"), first);
            File.WriteAllText(Path.Combine(b, "Preferences"), second);
            var state = ExtensionMigration.Read([a, b, Path.Combine(root, "missing")]);
            Assert.Equal(2, state.Count); Assert.False(state[id]); Assert.True(state[other]);
            Assert.Equal(first, File.ReadAllText(Path.Combine(a, "Secure Preferences")));
            Assert.Equal(second, File.ReadAllText(Path.Combine(b, "Preferences")));
            File.WriteAllText(Path.Combine(b, "Preferences"), "invalid json");
            Assert.True(ExtensionMigration.Read([a, b])[id]);
            File.WriteAllText(Path.Combine(b, "Preferences"), "[]");
            Assert.True(ExtensionMigration.Read([a, b])[id]);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
