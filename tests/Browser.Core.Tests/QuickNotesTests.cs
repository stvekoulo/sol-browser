using Browser.Core;

public class QuickNotesTests
{
    [Fact]
    public void NotesPersistPerProfileAndRejectInvalidInput()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(dir, "notes.json");
            var notes = new QuickNotes(path);
            Assert.True(notes.Save("", "Titre", "Texte", "https://example.com"));
            var note = Assert.Single(new QuickNotes(path).Items);
            Assert.Empty(new QuickNotes(Path.Combine(dir, "other", "notes.json")).Items);
            Assert.False(notes.Save("", "", "", ""));
            Assert.False(notes.Save("", "", new string('a', 50001), ""));
            Assert.False(notes.Save("", "", "secret", "file:///secret"));
            Assert.False(notes.Save("", "", "secret", "https://user:password@example.com"));
            Assert.True(notes.Save(note.Id, "Modifié", "Suite", note.Url));
            Assert.Contains("Suite", notes.Export());
            Assert.True(notes.Delete(note.Id));
            Assert.Empty(new QuickNotes(path).Items);
            var memory = new QuickNotes(null);
            Assert.True(memory.Save("", "", "Privé", ""));
            Assert.Empty(new QuickNotes(null).Items);
            var blockedPath = Path.Combine(dir, "blocked");
            Directory.CreateDirectory(blockedPath);
            var blocked = new QuickNotes(blockedPath);
            Assert.False(blocked.Save("", "", "Le disque refuse l’écriture", ""));
            Assert.Empty(blocked.Items);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
