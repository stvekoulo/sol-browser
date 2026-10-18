using Microsoft.UI.Xaml;
namespace Browser.App;
public sealed partial class BrowserController
{
    private readonly DispatcherTimer _noteMaintenance=new(){Interval=TimeSpan.FromMinutes(1)};
    private void StartNoteMaintenance()
    {
        _notes.AutoArchive(_settings.NotesArchiveDays,DateTimeOffset.UtcNow,_noteDraft.Id);
        _noteMaintenance.Tick+=(_,_)=>{
            var count=_notes.Items.Count(n=>n.Archived);
            if(_notes.AutoArchive(_settings.NotesArchiveDays,DateTimeOffset.UtcNow,_noteDraft.Id)&&count!=_notes.Items.Count(n=>n.Archived))PublishNotes();
        };
        _noteMaintenance.Start();
    }
}
