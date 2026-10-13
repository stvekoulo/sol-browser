using Browser.Core;
using System.Text;
using System.Text.Json;
using System.IO.Compression;

public class NoteLibraryTests : IDisposable
{
    private readonly string _dir=Directory.CreateTempSubdirectory("sol-notes").FullName;
    public void Dispose()=>Directory.Delete(_dir,true);
    private QuickNotes Library()=>new(Path.Combine(_dir,"notes.json"));
    private static QuickNote Draft(string text="Contenu")=>new("","Titre",text,"https://example.com",DateTimeOffset.UtcNow);

    [Fact] public void Old_notes_keep_their_content_and_new_metadata_survives_reload()
    {
        var old=new{Id="old",Title="Ancienne",Text="Texte",Url="",Updated=DateTimeOffset.UtcNow};
        File.WriteAllText(Path.Combine(_dir,"notes.json"),JsonSerializer.Serialize(new[]{old}));
        var notes=Library();Assert.Equal("note",Assert.Single(notes.Items).Kind);
        Assert.True(notes.Save(notes.Items[0] with {Kind="task",Tags=["travail/projet"],Due=DateTimeOffset.UtcNow.AddDays(1)}));
        var saved=Assert.Single(Library().Items);Assert.Equal("task",saved.Kind);Assert.Equal(["travail/projet"],saved.Tags);Assert.NotNull(saved.Due);
        Assert.Equal(old.Updated,saved.Created);
    }
    [Fact] public void Bulk_actions_are_atomic_and_trash_is_recoverable()
    {
        var notes=Library();Assert.True(notes.Save(Draft()));var id=notes.Items[0].Id;
        Assert.False(notes.Action([id,"unknown"],"trash"));Assert.Single(notes.Items);
        Assert.False(notes.Action([id],"purge"));
        Assert.True(notes.Action([id],"pin"));Assert.True(notes.Action([id],"kind:idea"));
        Assert.True(notes.Delete(id));Assert.Empty(Library().Items);Assert.NotNull(Assert.Single(Library().All).Deleted);
        Assert.True(notes.Action([id],"restore"));Assert.True(notes.Items[0].Pinned);Assert.Equal("idea",notes.Items[0].Kind);
        Assert.True(notes.Action([id],"duplicate"));Assert.Equal(2,notes.Items.Count);Assert.NotEqual(id,notes.Items[0].Id);
        Assert.True(notes.Delete(id));Assert.True(notes.Action([id],"purge"));Assert.Single(Library().All);
    }
    [Fact] public void Versions_comments_tag_hierarchy_and_manual_order_are_preserved()
    {
        var notes=Library();Assert.True(notes.Save(Draft("Bonjour #travail/projet") with{Tags=["travail/projet"]}));var a=notes.Items[0];
        Assert.True(notes.Save(a with{Text="Suite #travail/projet"}));Assert.Equal(a.Text,Assert.Single(notes.Items[0].History).Text);
        Assert.True(notes.RestoreRevision(a.Id,0));Assert.Equal(a.Text,notes.Items[0].Text);Assert.Equal(2,notes.Items[0].History.Length);
        Assert.True(notes.Comment(a.Id,"Annotation"));var c=Assert.Single(notes.Items[0].Comments);
        Assert.True(notes.Comment(a.Id,"",c.Id));Assert.Empty(notes.Items[0].Comments);
        Assert.True(notes.RenameTag("travail","projets"));Assert.Equal(["projets/projet"],notes.Items[0].Tags);Assert.Contains("#projets/projet",notes.Items[0].Text);
        Assert.True(notes.Save(Draft("B")));var b=notes.Items[0];Assert.True(notes.Order([a.Id,b.Id]));
        Assert.True(notes.Save(notes.Items[0] with{Title="Modifiée"}));Assert.Equal(a.Id,notes.Items[0].Id);
        Assert.False(notes.Order([a.Id,a.Id]));Assert.Equal([a.Id,b.Id],Library().Items.Select(n=>n.Id));
    }
    [Fact] public void Imports_remap_links_and_reject_duplicate_ids_without_changing_notes()
    {
        var notes=Library();var a=Draft("A") with{Id="a"};var b=Draft("[[a]]") with{Id="b"};
        Assert.True(notes.Import([a,b]));Assert.Equal("[["+notes.Items[0].Id+"]]",notes.Items[1].Text);
        Assert.False(notes.Import([a,a]));Assert.Equal(2,notes.Items.Count);
        Assert.False(notes.Import([Draft() with{Tags=[null!]}]));Assert.False(notes.Import([Draft() with{History=[null!]}]));
    }
    [Fact] public void Disabled_auto_archiving_changes_nothing_and_pinned_or_open_ideas_are_kept()
    {
        var notes=Library();var now=DateTimeOffset.UtcNow;var old=now.AddDays(-100);
        Assert.True(notes.Import([Draft() with{Id="a",Kind="idea",Updated=old},Draft() with{Id="b",Kind="idea",Updated=old,Pinned=true},Draft() with{Id="c",Kind="note",Updated=old},Draft() with{Id="d",Kind="idea",Updated=old}]));
        Assert.False(notes.AutoArchive(0,now));Assert.False(notes.AutoArchive(3651,now));Assert.True(notes.AutoArchive(90,now,notes.Items[3].Id));
        Assert.True(notes.Items[0].Archived);Assert.All(notes.Items.Skip(1),n=>Assert.False(n.Archived));
    }
    [Fact] public void Disk_failure_keeps_notes_and_files_in_memory_unchanged()
    {
        var blocked=Path.Combine(_dir,"blocked");Directory.CreateDirectory(blocked);
        var notes=new QuickNotes(blocked);Assert.False(notes.Save(Draft()));Assert.Empty(notes.All);
        var files=new NoteFiles(blocked);Assert.Null(files.Add("image.png",[137,80,78,71,13,10,26,10]));Assert.Empty(files.All);
        var memory=new NoteFiles(null);Assert.NotNull(memory.Add("private.pdf","%PDF-"u8.ToArray()));Assert.Empty(new NoteFiles(null).All);
    }
    [Fact] public void Import_parsers_accept_Blinko_CSV_and_Markdown_and_reject_malformed_records()
    {
        var blinko=NoteTransfer.Parse("[{\"content\":\"Idée française\",\"type\":0,\"tags\":[{\"name\":\"projets\"}]}]","blinko.json");
        Assert.Equal("idea",Assert.Single(blinko!).Kind);Assert.Equal(["projets"],blinko![0].Tags);
        var a=Draft("Texte, virgule\n\"citation\"");var csv=NoteTransfer.Export([a],"csv");
        Assert.Equal(a.Text,Assert.Single(NoteTransfer.Parse(csv,"notes.csv")!).Text);
        var formula=Draft("=HYPERLINK(\"evil\")") with {Title="'=Original"};var safeCsv=NoteTransfer.Export([formula],"csv");Assert.Contains("'=HYPERLINK",safeCsv);
        Assert.Equal(formula.Text,Assert.Single(NoteTransfer.Parse(safeCsv,"notes.csv")!).Text);Assert.Equal(formula.Title,NoteTransfer.Parse(safeCsv,"notes.csv")![0].Title);
        Assert.Equal("Titre",Assert.Single(NoteTransfer.Parse("# Texte","Titre.md")!).Title);
        Assert.Null(NoteTransfer.Parse("[null]","notes.json"));Assert.Null(NoteTransfer.Parse("[{\"content\":5}]","notes.json"));
        Assert.Null(NoteTransfer.Parse("title,content\na,b,c","notes.csv"));Assert.Null(NoteTransfer.Parse("[]","notes.json"));
        Assert.Null(NoteTransfer.Parse("[{\"Id\":null,\"Text\":\"T\",\"Title\":\"A\",\"Url\":\"\"}]","notes.json"));
    }
    [Fact] public void Zip_roundtrip_restores_binary_files_and_metadata_and_never_extracts_paths()
    {
        var files=new NoteFiles(null);var a=files.Add("../../photo.png",[137,80,78,71,13,10,26,10])!;
        var note=Draft("Image") with {Id="a",Files=[a.Id],Tags=["images"],Comments=[new("c","Texte",DateTimeOffset.UtcNow)]};
        var opened=NoteTransfer.OpenArchive(NoteTransfer.Archive([note],files));Assert.NotNull(opened);
        Assert.Equal(a.Data,opened.Value.Files[a.Id].Data);Assert.Equal("photo.png",opened.Value.Files[a.Id].Name);
        Assert.Equal(note.Comments,opened.Value.Notes[0].Comments);Assert.Equal(note.Tags,opened.Value.Notes[0].Tags);
        var imported=new NoteFiles(null);var ids=imported.Import(opened.Value.Files)!;Assert.NotEqual(a.Id,ids[a.Id]);
        Assert.Equal(a.Data,imported.Find(ids[a.Id])!.Data);Assert.Null(NoteTransfer.OpenArchive([1,2,3]));
        using var data=new MemoryStream();using(var zip=new ZipArchive(data,ZipArchiveMode.Create,true))using(var w=new StreamWriter(zip.CreateEntry("notes.json").Open()))w.Write("[{\"bad\":true}]");
        Assert.Null(NoteTransfer.OpenArchive(data.ToArray()));
        Assert.Contains("notes-assets.json",Backup.ProfileFiles);
    }
    [Fact] public void Attachments_reject_excessive_size_and_import_is_atomic()
    {
        var files=new NoteFiles(null);Assert.Null(files.Add("big",new byte[NoteFiles.MaxFile+1]));Assert.Null(files.Add("empty",[]));
        Assert.Null(files.Import(new(){["good"]=("a",[1]),["bad"]=("b",[])}));Assert.Empty(files.All);
        var a=files.Add("file.txt",[1,2,3])!;Assert.True(files.Keep(new HashSet<string>()));Assert.Null(files.Find(a.Id));
    }
    [Fact] public void Clearing_a_saved_note_keeps_its_id_history_and_export_roundtrip()
    {
        var notes=Library();Assert.True(notes.Save(Draft()));var id=notes.Items[0].Id;
        Assert.True(notes.Save(notes.Items[0] with {Title="",Text=""}));Assert.Equal(id,Assert.Single(Library().Items).Id);Assert.Equal("Contenu",Assert.Single(notes.Items[0].History).Text);
        foreach(var format in new[]{"json","csv"}){var parsed=NoteTransfer.Parse(NoteTransfer.Export(notes.Items,format),"notes."+format);Assert.NotNull(parsed);Assert.Empty(Assert.Single(parsed).Text);}
        Assert.False(notes.Import([null!]));Assert.True(notes.RestoreRevision(id,0));Assert.Equal("Contenu",notes.Items[0].Text);
    }
}
