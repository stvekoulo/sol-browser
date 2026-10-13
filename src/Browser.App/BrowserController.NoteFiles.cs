using System.Runtime.InteropServices;
using System.Text;
using Browser.Core;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Browser.App;

public sealed partial class BrowserController
{
    private void OwnPicker(object picker) => WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(_window));
    private static async Task<byte[]> NoteBytesAsync(StorageFile file,int limit)
    {
        if((await file.GetBasicPropertiesAsync()).Size>(ulong)limit)throw new IOException("File too large");
        var buffer=await FileIO.ReadBufferAsync(file);if(buffer.Length>limit)throw new IOException("File too large");
        var data=new byte[buffer.Length];using var reader=Windows.Storage.Streams.DataReader.FromBuffer(buffer);reader.ReadBytes(data);return data;
    }
    private async Task AttachNoteAsync()
    {
        var draft=_noteDraft;
        try
        {
            var picker=new FileOpenPicker {SuggestedStartLocation=PickerLocationId.DocumentsLibrary};picker.FileTypeFilter.Add("*");OwnPicker(picker);
            var file=await picker.PickSingleFileAsync();if(file is null)return;
            if(_noteDraft.Id!=draft.Id&&(_noteDraft.Title!=draft.Title||_noteDraft.Text!=draft.Text))return;
            var data=await NoteBytesAsync(file,NoteFiles.MaxFile);
            if(_noteDraft.Files.Length>=30 || _noteFiles.Add(file.Name,data) is not { } asset)throw new IOException("Could not store attachment");
            _noteDraft=_noteDraft with {Files=_noteDraft.Files.Append(asset.Id).ToArray()};_noteError=null;
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or COMException or ArgumentException)
        {_noteError=T("Pièce jointe non ajoutée. Maximum : 20 Mo par fichier et 100 Mo de pièces jointes dans ce profil.","Attachment not added. Maximum: 20 MB per file and 100 MB of attachments in this profile.");}
        PublishNotes();
    }
    private async Task SaveNoteFileAsync(string id)
    {
        if(!_noteDraft.Files.Contains(id)||_noteFiles.Find(id) is not { } file)return;
        try
        {
            var picker=new FileSavePicker {SuggestedFileName=Path.GetFileNameWithoutExtension(file.Name),SuggestedStartLocation=PickerLocationId.DocumentsLibrary};
            var extension=Path.GetExtension(file.Name);picker.FileTypeChoices.Add(T("Fichier","File"),[extension.Length>1?extension:".bin"]);OwnPicker(picker);
            if(await picker.PickSaveFileAsync() is { } target)await FileIO.WriteBytesAsync(target,file.Data);
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or COMException or ArgumentException)
        {_noteError=T("Le fichier n’a pas été enregistré. La pièce jointe est conservée.","The file was not saved. The attachment is kept.");PublishNotes();}
    }
    private async Task ImportNotesAsync()
    {
        if(NoteDraftChanged){_noteError=T("Enregistrez le brouillon avant l’import.","Save the draft before importing.");PublishNotes();return;}
        var previousFiles=_noteFiles.All.Select(f=>f.Id).ToHashSet();
        try
        {
            var picker=new FileOpenPicker {SuggestedStartLocation=PickerLocationId.DocumentsLibrary};
            foreach(var extension in new[]{".md",".txt",".json",".csv",".zip",".db"})picker.FileTypeFilter.Add(extension);OwnPicker(picker);
            var selected=await picker.PickMultipleFilesAsync();if(selected.Count==0||NoteDraftChanged)return;
            var notes=new List<QuickNote>();long size=0;
            foreach(var file in selected)
            {
                size+=(long)(await file.GetBasicPropertiesAsync()).Size;if(size>NoteTransfer.MaxImport)throw new InvalidDataException();
                IReadOnlyList<QuickNote>? imported;
                if(file.FileType.Equals(".db",StringComparison.OrdinalIgnoreCase))imported=NoteMemos.Read(file.Path);
                else
                {
                    var bytes=await NoteBytesAsync(file,NoteTransfer.MaxImport);
                    if(file.FileType.Equals(".zip",StringComparison.OrdinalIgnoreCase))
                    {
                        var archive=NoteTransfer.OpenArchive(bytes);if(archive is null)throw new InvalidDataException();
                        var ids=_noteFiles.Import(archive.Value.Files);if(ids is null)throw new IOException();
                        imported=archive.Value.Notes.Select(n=>n with {Files=n.Files.Select(id=>ids[id]).ToArray()}).ToList();
                    }
                    else{imported=NoteTransfer.Parse(Encoding.UTF8.GetString(bytes),file.Name);if(imported?.Any(n=>n.Files.Length>0)==true)throw new InvalidDataException("Attachments require ZIP archive");}
                }
                if(imported is null)throw new InvalidDataException();notes.AddRange(imported);if(notes.Count>1000)throw new InvalidDataException();
            }
            if(!_notes.Import(notes))throw new InvalidDataException();
            _noteDraft=_notes.Items[0];_noteError=null;
        }
        catch(Exception e) when(e is IOException or InvalidDataException or UnauthorizedAccessException or COMException or ArgumentException)
        {_noteFiles.Keep(previousFiles);_noteError=T("Import refusé. Utilisez un Markdown, JSON Blinko/Sol, CSV, ZIP ou une base Memos valide. Vos notes sont conservées.","Import refused. Use valid Markdown, Blinko/Sol JSON, CSV, ZIP or a Memos database. Your notes are kept.");}
        PublishNotes();
    }
    private async Task ExportNotesAsync(string format="markdown",string? id=null,string? selection=null)
    {
        if(format is not ("markdown" or "json" or "csv" or "zip"))return;
        var notes=_notes.All.Where(n=>n.Deleted is null&&(string.IsNullOrEmpty(id)||n.Id==id)).ToList();if(notes.Count==0)return;
        try
        {
            if(selection is not null){if(selection.Length>90000)return;var ids=System.Text.Json.JsonSerializer.Deserialize<string[]>(selection);if(ids is null||ids.Length>1000)return;notes=notes.Where(n=>ids.Contains(n.Id)).ToList();if(notes.Count==0)return;}
            var extension=format switch {"markdown"=>".md","json"=>".json","csv"=>".csv",_=>".zip"};
            var picker=new FileSavePicker {SuggestedFileName="Sol notes",SuggestedStartLocation=PickerLocationId.DocumentsLibrary};
            picker.FileTypeChoices.Add(format=="zip"?T("Notes et pièces jointes","Notes and attachments"):format,[extension]);OwnPicker(picker);
            if(await picker.PickSaveFileAsync() is { } file)
            {if(format=="zip")await FileIO.WriteBytesAsync(file,NoteTransfer.Archive(notes,_noteFiles));else await FileIO.WriteTextAsync(file,NoteTransfer.Export(notes,format));}
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or COMException or ArgumentException or System.Text.Json.JsonException)
        {_noteError=T("Export impossible. Vos notes sont conservées.","Export failed. Your notes are kept.");PublishNotes();}
    }
}
