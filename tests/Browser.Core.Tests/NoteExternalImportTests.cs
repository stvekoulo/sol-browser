using Browser.Core;
using Microsoft.Data.Sqlite;
using System.IO.Compression;
public class NoteExternalImportTests
{
    [Fact] public void Markdown_zip_keeps_images_as_local_attachments()
    {
        using var stream=new MemoryStream();using(var zip=new ZipArchive(stream,ZipArchiveMode.Create,true))
        {
            using(var writer=new StreamWriter(zip.CreateEntry("dossier/note.md").Open()))writer.Write("# Note\n![Photo](../images/photo.png)\n#images");
            using var file=zip.CreateEntry("images/photo.png").Open();file.Write([137,80,78,71,13,10,26,10]);
        }
        var result=NoteTransfer.OpenArchive(stream.ToArray());Assert.NotNull(result);var note=Assert.Single(result.Value.Notes);
        Assert.Equal("note",note.Title);Assert.Single(note.Files);Assert.Equal("photo.png",result.Value.Files[note.Files[0]].Name);Assert.Equal(["images"],note.Tags);
    }
    [Fact] public void Memos_import_is_read_only_and_preserves_dates_archives_comments_and_references()
    {
        var dir=Directory.CreateTempSubdirectory("sol-memos").FullName;var path=Path.Combine(dir,"memos.db");
        try
        {
            using(var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Pooling=false}.ToString()))
            {
                db.Open();using var command=db.CreateCommand();command.CommandText="""
                    CREATE TABLE memo(id INTEGER PRIMARY KEY,content TEXT,created_ts INTEGER,updated_ts INTEGER,row_status TEXT,pinned INTEGER);
                    CREATE TABLE memo_relation(memo_id INTEGER,related_memo_id INTEGER,type TEXT);
                    INSERT INTO memo VALUES(1,'Première #projet',1000000000,1000000100,'ARCHIVED',1),(2,'Commentaire',1000000010,1000000110,'NORMAL',0),(3,'Deuxième',1000000020,1000000120,'NORMAL',0);
                    INSERT INTO memo_relation VALUES(2,1,'COMMENT'),(3,1,'REFERENCE');
                    """;command.ExecuteNonQuery();
            }
            var bytes=File.ReadAllBytes(path);var notes=NoteMemos.Read(path)!;Assert.Equal(2,notes.Count);Assert.True(notes[0].Pinned);Assert.True(notes[0].Archived);
            Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1000000000),notes[0].Created);Assert.Equal("Commentaire",Assert.Single(notes[0].Comments).Text);Assert.Contains("[[memo-1]]",notes[1].Text);
            Assert.Equal(bytes,File.ReadAllBytes(path));var library=new QuickNotes(null);Assert.True(library.Import(notes));Assert.Contains("[["+library.Items[0].Id+"]]",library.Items[1].Text);
            File.WriteAllText(Path.Combine(dir,"bad.db"),"invalid");Assert.Null(NoteMemos.Read(Path.Combine(dir,"bad.db")));
        }
        finally{Directory.Delete(dir,true);}
    }
}
