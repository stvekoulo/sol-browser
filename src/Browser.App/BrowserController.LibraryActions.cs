using Browser.Core;

namespace Browser.App;

public sealed partial class BrowserController
{
    private void RemoveSavedSite(string url, bool history)
    {
        if (history) _db.RemoveHistory(url);
        else _db.RemoveBookmark(url);
        _categoryView = null;
        Refresh();
    }

    private void RemoveLibraryItem(Command command)
    {
        if (!_popup.IsOpen || (_panel != "library" && _panelNext != "library")
            || command.Input is not ("bookmarks" or "history")) return;
        RemoveSavedSite(command.Url!, command.Input == "history");
        _popup.Post(LibraryJson(command.Query!, command.Index!.Value, command.Input));
    }
}
