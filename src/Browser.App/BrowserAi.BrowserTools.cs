using Browser.Core;
using System.Text.Json;

namespace Browser.App;

public sealed partial class BrowserController
{
    private async Task<string> BrowserToolAsync(AgentCall call, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!AiReady || _closingWindow) return ToolError("Sol AI is unavailable in this context");
        string A(string key) => SolAiAgent.Argument(call.Arguments, key).Trim();
        Tab? TabOf(string id) => _tabs.Tabs.FirstOrDefault(t => t.Id == id && !t.Private);
        string[] Ids(int min, int max)
        {
            var ids = SolAiBrowserTools.ArrayArgument(call.Arguments, "ids", min, max);
            if (ids.Distinct().Count() != ids.Length || ids.Any(id => TabOf(id) is null)) throw new ArgumentException("unknown, repeated or private tab");
            return ids;
        }
        switch (call.Name)
        {
            case "list_tabs":
                return JsonSerializer.Serialize(new { ok = true, activeId = _tabs.ActiveId,
                    tabs = _tabs.Tabs.Where(t => !t.Private).Select(t => new { t.Id, t.Title, t.Url, t.GroupId, t.Pinned, asleep = _pages.IsAsleep(t.Id) }),
                    groups = _tabs.Groups.Where(g => _tabs.Tabs.Any(t => !t.Private && t.GroupId == g.Id)),
                    split = new { _split.PrimaryId, _split.SecondaryId, visible = _split.Contains(_tabs.ActiveId), maxPanes = 2 } });
            case "open_tabs":
                var urls = SolAiBrowserTools.Urls(call.Arguments); // tout valider avant le premier onglet
                var opened = urls.Select(url => Open(url, activate: false, isPrivate: false)).ToArray();
                Activate(opened[0].Id); Refresh();
                return JsonSerializer.Serialize(new { ok = true, tabs = opened.Select(t => new { t.Id, t.Url }), loading = true });
            case "activate_tab":
                if (TabOf(A("id")) is not { } active) return ToolError("unknown or private tab");
                Activate(active.Id); Refresh();
                return JsonSerializer.Serialize(new { ok = true, id = active.Id });
            case "read_tab":
                if (TabOf(A("id")) is not { } readable) return ToolError("unknown or private tab");
                if (_pages.IsAsleep(readable.Id)) return ToolError("activate this sleeping tab first");
                // Add() initialise WebView2 de façon asynchrone ; attendre aussi sa première navigation.
                for (var attempt = 0; attempt < 40; attempt++)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!AiReady || TabOf(readable.Id) is null) return ToolError("tab or account is no longer available");
                    if (!_pages.IsLoading(readable.Id) && _pages.CoreOf(readable.Id) is { } core && IsWebAddress(core.Source)
                        && await core.ExecuteScriptAsync("document.readyState") is "\"interactive\"" or "\"complete\"")
                        return await PageToolAsync(readable.Id, call with { Name = "read_page" }, ct);
                    await Task.Delay(250, ct);
                }
                return ToolError("page is still loading; retry read_tab");
            case "browser_history":
                if (TabOf(A("id")) is not { } history || _pages.CoreOf(history.Id) is not { } navigation) return ToolError("tab is unavailable");
                switch (A("action"))
                {
                    case "back" when navigation.CanGoBack: _pages.Back(history.Id); break;
                    case "forward" when navigation.CanGoForward: _pages.Forward(history.Id); break;
                    case "reload": _pages.Reload(history.Id); break;
                    default: return ToolError("navigation is unavailable");
                }
                return """{"ok":true,"loading":true}""";
            case "close_tabs":
                var closing = Ids(1, 16);
                foreach (var id in closing) Close(id);
                Refresh();
                return JsonSerializer.Serialize(new { ok = true, removed = closing.Where(id => TabOf(id) is null),
                    suspended = closing.Where(id => TabOf(id) is not null && _pages.IsAsleep(id)) });
            case "reopen_tab":
                if (_tabs.ReopenClosed() is not { } restored) return ToolError("no closed tab to restore");
                _pages.Add(restored); Activate(restored.Id); Refresh();
                return JsonSerializer.Serialize(new { ok = true, restored.Id, restored.Url });
            case "zoom_tab":
                if (TabOf(A("id")) is not { } zoomTab || A("direction") is not ("in" or "out" or "reset")) return ToolError("invalid tab or zoom direction");
                Zoom(zoomTab.Id, "zoom." + A("direction"), null); Refresh();
                return """{"ok":true}""";
            case "group_tabs":
                var ids = Ids(1, 16);
                if (_tabs.Tabs.Any(t => ids.Contains(t.Id) && t.Pinned)) return ToolError("pinned tabs cannot be grouped");
                if (ids.Any(_split.Contains) && (!ids.Contains(_split.PrimaryId!) || !ids.Contains(_split.SecondaryId!)))
                    return ToolError("include both split tabs, or close_split first");
                if (A("title").Length is 0 or > 120) return ToolError("provide a group title of 1 to 120 characters");
                var group = _tabs.CreateGroupFromTabs(ids, A("title"));
                Refresh();
                return group is null ? ToolError("group could not be created") : JsonSerializer.Serialize(new { ok = true, group });
            case "split_tabs":
                var pair = Ids(2, 2);
                if (_tabs.Tabs.Any(t => pair.Contains(t.Id) && t.Pinned)) return ToolError("unpin these tabs before splitting them");
                Activate(pair[0]); Split(pair[1]); Refresh();
                return JsonSerializer.Serialize(new { ok = true, primaryId = pair[0], secondaryId = pair[1], maxPanes = 2 });
            case "close_split":
                // Ne pas appeler CloseSplit : il peut supprimer l'onglet vide provisoire.
                _splitBlankTab = null; _split.Close(); Refresh();
                return """{"ok":true}""";
            case "open_notes":
                if (NoteDraftChanged) return ToolError("save or discard your unsaved note draft first");
                var note = A("id").Length == 0 ? null : _notes.Items.FirstOrDefault(n => n.Id == A("id"));
                if (A("id").Length > 0 && note is null) return ToolError("unknown note");
                var notesTab = ShowNotesTool(note);
                Activate(notesTab.Id); Refresh();
                return JsonSerializer.Serialize(new { ok = true, tabId = notesTab.Id, noteId = note?.Id });
            case "prepare_workspace": return PrepareWorkspaceTool(call);
            default: return ToolError("unknown browser tool");
        }
    }

    private string PrepareWorkspaceTool(AgentCall call)
    {
        var urls = SolAiBrowserTools.Urls(call.Arguments);
        string A(string key) => SolAiAgent.Argument(call.Arguments, key).Trim();
        var title = A("title"); var noteTitle = A("note_title"); var text = A("note_text"); var layout = A("layout");
        if (title.Length is 0 or > 120 || noteTitle.Length > 120 || text.Length > 50_000 || layout is not ("tabs" or "side_by_side"))
            return ToolError("invalid workspace title, note or layout");
        if (noteTitle.Length == 0 && text.Length > 0) return ToolError("provide a title for the note");
        if (noteTitle.Length > 0 && NoteDraftChanged) return ToolError("save or discard your unsaved note draft first");
        if (layout == "side_by_side" && urls.Length < 2 && noteTitle.Length == 0) return ToolError("two resources are required for side by side");
        // Sauvegarder avant d'ouvrir les pages pour ne pas préparer un espace dont la note manque.
        QuickNote? note = null;
        if (noteTitle.Length > 0)
        {
            if (!_notes.Save(new QuickNote("", noteTitle, text.Length > 0 ? text : "# " + noteTitle + "\n\n", "", DateTimeOffset.UtcNow)))
                return ToolError("the note could not be saved; no tabs were opened");
            note = _notes.Items.First();
        }
        var tabs = urls.Select(url => Open(url, activate: false, isPrivate: false)).ToList();
        Tab? notesTab = null;
        if (note is not null) { notesTab = ShowNotesTool(note, newTab: true); tabs.Add(notesTab); }
        var group = _tabs.CreateGroupFromTabs(tabs.Select(t => t.Id), title);
        Activate(tabs[0].Id);
        if (layout == "side_by_side") Split(notesTab?.Id ?? tabs[1].Id);
        Refresh(); PublishNotes();
        return JsonSerializer.Serialize(new { ok = group is not null, group, tabs = tabs.Select(t => new { t.Id, t.Url }),
            noteId = note?.Id, layout, maxPanes = 2, loading = true });
    }

    private Tab ShowNotesTool(QuickNote? note, bool newTab = false)
    {
        if (note is not null) { _noteDraft = note; _noteError = null; }
        var tab = newTab ? null : _tabs.Tabs.FirstOrDefault(t => !t.Private && t.Url == QuickNotes.PageUrl);
        tab ??= Open(QuickNotes.PageUrl, activate: false, isPrivate: false);
        PublishNotes(); return tab;
    }
    private static string ToolError(string message) => JsonSerializer.Serialize(new { ok = false, error = message });
}
