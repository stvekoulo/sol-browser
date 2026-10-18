using System.Text.Json;
using Browser.Core;

namespace Browser.App;

public sealed partial class BrowserController
{
    private sealed record NoteMeta(string Kind, string[] Tags, DateTimeOffset? Due);
    private bool ExecuteNoteExtras(Command c)
    {
        var id = c.Id ?? "";
        switch (c.Type)
        {
            case "notes.autosave":
                if (id != _noteDraft.Id || c.Title != _noteDraft.Title || c.Input != _noteDraft.Text || id.Length==0&&_noteDraft.Text.Trim().Length == 0 && _noteDraft.Files.Length == 0) return true;
                if (_notes.Save(_noteDraft)) { _noteDraft = _notes.Items.First(n=>id.Length==0||n.Id==id); _noteError = null; TrimNoteFiles(); }
                else _noteError = T("Enregistrement impossible. Votre brouillon est conservé.", "Could not save. Your draft is kept.");
                break;
            case "notes.meta":
                if (id != _noteDraft.Id || c.Input!.Length > 10000) return true;
                try
                {
                    var meta = JsonSerializer.Deserialize<NoteMeta>(c.Input, new JsonSerializerOptions {PropertyNameCaseInsensitive=true,RespectNullableAnnotations=true});
                    if (meta is null) return true;
                    var next = _noteDraft with { Kind = meta.Kind, Tags = meta.Tags, Due = meta.Due };
                    if (QuickNotes.Valid(next,allowEmpty:true)) { _noteDraft = next; _noteError=null; } else _noteError=T("Tags ou type de note invalides.","Invalid tags or note type.");
                }
                catch (JsonException) { _noteError=T("Options de note invalides.","Invalid note options."); }
                break;
            case "notes.action":
                if (c.Input!.Length > 90000) return true;
                try
                {
                    var ids=JsonSerializer.Deserialize<string[]>(c.Input);
                    if(ids is null || NoteDraftChanged && ids.Contains(_noteDraft.Id)) { _noteError=T("Enregistrez le brouillon avant cette action.","Save the draft before this action."); break; }
                    if(!_notes.Action(ids,c.Query!)) { _noteError=T("Cette action n’a pas été appliquée.","The action was not applied."); break; }
                    _noteError=null;
                    if(ids.Contains(_noteDraft.Id)) _noteDraft=_notes.Items.FirstOrDefault(n=>n.Id==_noteDraft.Id)??new("","","","",DateTimeOffset.UtcNow);
                    if(c.Query=="purge")_noteFiles.Keep(_notes.All.SelectMany(n=>n.Files).Concat(_noteDraft.Files).ToHashSet());
                }
                catch(JsonException) { _noteError=T("Sélection invalide.","Invalid selection."); }
                break;
            case "notes.version":
                if(NoteDraftChanged || !_notes.RestoreRevision(id,c.Index!.Value)) { _noteError=T("Enregistrez le brouillon avant de restaurer une version.","Save the draft before restoring a version."); break; }
                _noteDraft=_notes.Items.First(n=>n.Id==id);_noteError=null;break;
            case "notes.tag":
                var dirtyBeforeTag=NoteDraftChanged;
                if(!_notes.RenameTag(id,c.Title!))_noteError=T("Le tag n’a pas été modifié.","Could not change the tag.");
                else { _noteError=null; if(_noteDraft.Id.Length>0&&!dirtyBeforeTag)_noteDraft=_notes.Items.FirstOrDefault(n=>n.Id==_noteDraft.Id)??_noteDraft; }
                break;
            case "notes.comment":
                if(!_notes.Comment(id,c.Input!,c.Query))_noteError=T("Annotation non enregistrée.","Annotation not saved.");else _noteError=null;
                if(_noteDraft.Id==id&&_notes.Items.FirstOrDefault(n=>n.Id==id) is { } commented)_noteDraft=_noteDraft with { Comments=commented.Comments };
                break;
            case "notes.import": _=ImportNotesAsync();return true;
            case "notes.attach":
                if(c.Input is null){_=AttachNoteAsync();return true;}
                if(c.Id!=_noteDraft.Id||c.Title is null||c.Title.Length>180||c.Input.Length>(NoteFiles.MaxFile+2)/3*4||_noteDraft.Files.Length>=30)return true;
                try{var asset=_noteFiles.Add(c.Title,Convert.FromBase64String(c.Input));if(asset is null)_noteError=T("Pièce jointe non ajoutée.","Attachment not added.");else{_noteDraft=_noteDraft with {Files=_noteDraft.Files.Append(asset.Id).ToArray()};_noteError=null;}}
                catch(FormatException){_noteError=T("Fichier invalide.","Invalid file.");}break;
            case "notes.file": _=SaveNoteFileAsync(c.Query!);return true;
            case "notes.detach": _noteDraft=_noteDraft with { Files=_noteDraft.Files.Where(f=>f!=c.Query).ToArray() };break;
            case "notes.order":
                if(c.Input!.Length>90000)return true;
                try{var order=JsonSerializer.Deserialize<string[]>(c.Input);if(order is null||!_notes.Order(order))_noteError=T("Ordre non enregistré.","Order not saved.");else _noteError=null;}catch(JsonException){return true;}
                break;
            case "notes.dates":
                if(NoteDraftChanged||!DateTimeOffset.TryParse(c.Title,out var created)||!DateTimeOffset.TryParse(c.Query,out var updated)||!_notes.Dates(id,created,updated))return true;
                if(_noteDraft.Id==id)_noteDraft=_notes.Items.First(n=>n.Id==id);break;
            default:return false;
            case "notes.archive-policy":
                if(c.Index is not (>=0 and <=3650))return true;
                var previousDays=_settings.NotesArchiveDays;_settings.NotesArchiveDays=c.Index.Value;
                if(!AppPaths.Private&&!JsonFileStore.Save(AppPaths.Settings,_settings)){_settings.NotesArchiveDays=previousDays;_noteError=T("Réglage non enregistré.","Setting was not saved.");}
                else {_notes.AutoArchive(_settings.NotesArchiveDays,DateTimeOffset.UtcNow,_noteDraft.Id);_noteError=null;}break;
        }
        PublishNotes();return true;
    }
    private string NotesJson() => JsonSerializer.Serialize(new { popup="notes",items=_notes.All.Select(n=>n with{History=[],Comments=[]}),draft=_noteDraft,error=_noteError,privateMode=AppPaths.Private,aiEnabled=AiReady,archiveDays=_settings.NotesArchiveDays,
        attachments=_noteDraft.Files.Select(id=>_noteFiles.Find(id)).OfType<NoteFile>().Select(f=>new { f.Id,f.Name,f.Mime,size=f.Data.Length,
            data=f.Mime.StartsWith("image/")||f.Mime.StartsWith("audio/")?"data:"+f.Mime+";base64,"+Convert.ToBase64String(f.Data):null }) },
        new JsonSerializerOptions {PropertyNamingPolicy=JsonNamingPolicy.CamelCase});
    private void OpenNotes(double x,double y) {HidePopup();_notesPopup=true;_popup.ShowCentered(480,NotesJson());}
    private void PublishNotes()
    {
        var json=NotesJson();if(_notesPopup)_popup.Post(json);
        foreach(var tab in _tabs.Tabs.Where(t=>t.Url==QuickNotes.PageUrl))_pages.PostStart(tab.Id,json);
    }
    private void TrimNoteFiles()=>_noteFiles.Keep(_notes.All.SelectMany(n=>n.Files).Concat(_noteDraft.Files).ToHashSet());
}
