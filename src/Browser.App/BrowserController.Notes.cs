using System.Text.Json;
using Browser.Core;
namespace Browser.App;
public sealed partial class BrowserController
{
    private bool ExecuteNotes(Command c)
    {
        var id = c.Id ?? "";
        switch(c.Type)
        {
            case "notes.manage":
                HidePopup();
                if (_tabs.Tabs.FirstOrDefault(t => t.Url == QuickNotes.PageUrl) is { } notesTab) Activate(notesTab.Id);
                else Open(QuickNotes.PageUrl);
                Refresh(); return true;
            case "notes.menu":
                var source=_tabs.Tabs.FirstOrDefault(t=>t.Id==(c.Id??_tabs.ActiveId));
                var actions=new List<(string Label,Action? Run)>{(T("Ouvrir les notes","Open notes"),()=>Execute(new("notes.manage")))};
                if(source is not null&&source.Url!=QuickNotes.PageUrl)actions.Add((T("Ouvrir à côté de l’onglet actuel","Open beside the current tab"),()=>OpenNotesBeside(source.Id)));
                ShowMenu(c.X!.Value-Scaled(260),c.Y!.Value,actions);return true;
            case "notes.split": OpenNotesBeside(id);return true;
            case "notes.visit":
                if (_notes.Items.FirstOrDefault(n => n.Id == id)?.Url is { Length: > 0 } noteUrl && Uri.TryCreate(noteUrl, UriKind.Absolute, out var noteUri) && noteUri.Scheme is "https" or "http" && noteUri.UserInfo.Length == 0)
                { Open(noteUrl); Refresh(); } return true;
            case "notes.link":
                if (c.Url is { Length: <= 4096 } link && Uri.TryCreate(link, UriKind.Absolute, out var linkUri) && linkUri.Scheme is "http" or "https" && linkUri.UserInfo.Length == 0)
                { Open(link); Refresh(); } return true;
            case "notes.open": OpenNotes(c.X ?? 450, c.Y ?? 60); return true;
            case "notes.draft":
                if (c.Title!.Length <= 120 && c.Input!.Length <= 50_000 && c.Id == _noteDraft.Id)
                    _noteDraft = _noteDraft with { Title = c.Title, Text = c.Input };
                return true;
            case "notes.edit":
                if (NoteDraftChanged)
                { _noteError = T("Enregistrez ou abandonnez le brouillon avant de changer de note.", "Save or discard your draft before changing notes."); PublishNotes(); return true; }
                _noteDraft = _notes.Items.FirstOrDefault(n => n.Id == id) ?? new("", "", "", "", DateTimeOffset.UtcNow);
                _noteError = null; PublishNotes(); return true;
            case "notes.discard":
                _noteDraft = new("", "", "", "", DateTimeOffset.UtcNow); _noteError = null; TrimNoteFiles(); PublishNotes(); return true;
            case "notes.save":
                if (_notes.Save(_noteDraft))
                { _noteDraft = _notesPopup ? new("", "", "", "", DateTimeOffset.UtcNow) : _notes.Items.First(n=>_noteDraft.Id.Length==0||n.Id==_noteDraft.Id); _noteError = null; TrimNoteFiles(); }
                else _noteError = T("La note n’a pas été enregistrée. Vérifiez son contenu et l’espace disque.", "The note could not be saved. Check its content and disk space.");
                PublishNotes(); return true;
            case "notes.delete":
                if (!_notes.Delete(id)) _noteError = T("Impossible de supprimer cette note.", "Could not delete this note.");
                else { _noteError = null; if (_noteDraft.Id == id) _noteDraft = new("", "", "", "", DateTimeOffset.UtcNow); }
                PublishNotes(); return true;
            case "notes.export": _ = ExportNotesAsync(c.Query ?? "markdown", c.Id,c.Input); return true;
            case "notes.select":
                if (_noteSelection is not { } passage) return true;
                if (id.Length == 0)
                {
                    if (NoteDraftChanged)
                    { Notice(T("Un brouillon est en cours", "A draft is open"), T("Enregistrez votre brouillon dans les notes rapides avant de créer une nouvelle note.", "Save your quick notes draft before creating a new note.")); return true; }
                    _noteDraft = passage; _noteSelection = null;
                }
                else if (_notes.Items.FirstOrDefault(n => n.Id == id) is { } destination)
                {
                    var added = destination.Text + "\n\n" + passage.Text + (passage.Url.Length > 0 ? "\nSource : " + passage.Url : "");
                    if (!_notes.Save(id, destination.Title, added, destination.Url))
                    { Notice(T("Ajout impossible", "Could not add passage"), T("La note est trop longue ou le disque est indisponible. Son contenu est conservé.", "The note is too long or the disk is unavailable. Its content is kept.")); return true; }
                    if (_noteDraft.Id == id) _noteDraft = _noteDraft with { Text = _noteDraft.Text + "\n\n" + passage.Text + (passage.Url.Length > 0 ? "\nSource : " + passage.Url : ""), Updated = DateTimeOffset.UtcNow };
                    _noteSelection = null;
                }
                else return true;
                _noteError = null; OpenNotes(450, 60); return true;
            default: return ExecuteNoteExtras(c);
        }
    }
    private void OpenNotesBeside(string sourceId)
    {
        if(_tabs.Tabs.FirstOrDefault(t=>t.Id==sourceId) is not { } source||source.Url==QuickNotes.PageUrl)return;
        HidePopup();var notes=_tabs.Tabs.FirstOrDefault(t=>t.Url==QuickNotes.PageUrl)??Open(QuickNotes.PageUrl,activate:false);
        Activate(source.Id);Split(notes.Id);Refresh();
    }
}
