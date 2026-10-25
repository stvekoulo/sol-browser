using System.Text.Json;

namespace Browser.Core;

// Commandes locales du navigateur. Les onglets privés restent exclus.
public static class SolAiBrowserTools
{
    private static readonly object Text = new { type = "string" };
    private static object Strings(int minimum, int maximum) => new { type = "array", items = Text, minItems = minimum, maxItems = maximum };
    private static object Tool(string name, string description, object properties, params string[] required) => new
    {
        type = "function", name, description, strict = true,
        parameters = new { type = "object", properties, required, additionalProperties = false },
    };
    public static readonly object[] Tools =
    [
        Tool("list_tabs", "List non-private tabs, their ids, addresses, groups and the active split view. Use these ids for other tab tools.", new { }),
        Tool("find_tabs", "Find existing non-private tabs by title or URL before opening duplicates. Empty query lists all matches.", new { query = Text }, "query"),
        Tool("duplicate_tabs", "Open copies of 1 to 8 existing web tabs by id, preserving originals. Copies use the normal profile session; private and isolated-session tabs are excluded.", new { ids = Strings(1, 8) }, "ids"),
        Tool("pin_tabs", "Pin or unpin 1 to 16 existing tabs. Grouped and split tabs must be ungrouped or unsplit before pinning. Only act when requested.", new { ids = Strings(1, 16), pinned = new { type = "boolean" } }, "ids", "pinned"),
        Tool("open_tabs", "Open 1 to 8 http/https addresses in new tabs, preserving existing tabs. The first new tab becomes active.", new { urls = Strings(1, 8) }, "urls"),
        Tool("activate_tab", "Switch to an existing non-private tab using its id from list_tabs or open_tabs.", new { id = Text }, "id"),
        Tool("read_tab", "Read a non-private web tab by id. Waits for it to load. Activate a sleeping tab first.", new { id = Text }, "id"),
        Tool("browser_history", "Go back, forward or reload a tab by id. This may discard unsaved site changes, so Sol manages approval, including standing permission.", new { id = Text, action = new { type = "string", @enum = new[] { "back", "forward", "reload" } } }, "id", "action"),
        Tool("close_tabs", "Close tabs by id with approval managed by Sol. Like Sol's close button, grouped or pinned tabs may be suspended instead of removed. Never close tabs unless explicitly requested.", new { ids = Strings(1, 16) }, "ids"),
        Tool("reopen_tab", "Restore the most recently closed non-private tab, on the user's request.", new { }),
        Tool("zoom_tab", "Change a tab's page zoom: in, out, reset.", new { id = Text, direction = new { type = "string", @enum = new[] { "in", "out", "reset" } } }, "id", "direction"),
        Tool("group_tabs", "Group existing unpinned tabs under a title. Existing split pairs must stay together.", new { ids = Strings(1, 16), title = Text }, "ids", "title"),
        Tool("split_tabs", "Show exactly two existing non-private tabs side by side. Sol supports two visible panes; other tabs remain accessible in their group.", new { ids = Strings(2, 2) }, "ids"),
        Tool("close_split", "Return to the normal single-page view, keeping all tabs open.", new { }),
        Tool("open_notes", "Open Sol's notes in a tab. Pass a note id to edit that saved note, or an empty id for the notes library. Preserves unsaved drafts.", new { id = Text }, "id"),
        Tool("prepare_workspace", "Prepare a workspace in one action: open URLs in a named tab group and optionally create a Sol note. Empty note_title skips the note. Empty note_text creates a heading for taking notes. side_by_side displays the first page beside the note, or the first two pages if no note is requested. At most two panes are visible.",
            new { title = Text, urls = Strings(1, 8), note_title = Text, note_text = Text,
                layout = new { type = "string", @enum = new[] { "tabs", "side_by_side" } } }, "title", "urls", "note_title", "note_text", "layout"),
    ];
    public static bool Handles(string name) => name is "list_tabs" or "open_tabs" or "activate_tab" or "read_tab"
        or "group_tabs" or "split_tabs" or "close_split" or "open_notes" or "prepare_workspace" or "browser_history"
        or "close_tabs" or "reopen_tab" or "zoom_tab" or "find_tabs" or "duplicate_tabs" or "pin_tabs";

    public const string Rules = " For browser setup requests, use prepare_workspace to open resources, group them and optionally create a note in one call. "
        + "For other tab operations, list_tabs first and use the returned ids. Only change the browser when the user's request calls for it. "
        + "Before opening resources, find_tabs to reuse existing pages when appropriate. Group related resources and compare pages using read_tab and structured data. "
        + "You may open known service homepages or a search URL using the user's exact search query, but never invent article or video URLs. "
        + "When a particular resource is ambiguous, ask for its link or open a search; never claim a search result is the requested article. "
        + "Use read_tab before summarizing a resource. Opening a URL is not evidence you read it. "
        + "Sol currently supports two simultaneous panes, not three; explain this when the user requests more. "
        + "Report only operations whose tool result confirms success, and explain partial results and errors.";

    public static string[] ArrayArgument(string arguments, string name, int minimum, int maximum)
    {
        using var doc = JsonDocument.Parse(arguments);
        if (!doc.RootElement.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array
            || array.GetArrayLength() < minimum || array.GetArrayLength() > maximum) throw new ArgumentException("invalid " + name);
        return array.EnumerateArray().Select(v => v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 and <= 4096 } text
            ? text : throw new ArgumentException("invalid " + name)).ToArray();
    }
    public static string[] Urls(string arguments) => ArrayArgument(arguments, "urls", 1, 8).Select(url =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0
            && !url.Any(char.IsControl) && !uri.IdnHost.Equals("start.sol", StringComparison.OrdinalIgnoreCase)
            && !uri.IdnHost.Equals("ui.navigateur", StringComparison.OrdinalIgnoreCase)
        ? uri.AbsoluteUri : throw new ArgumentException("only http/https web addresses without credentials are allowed")).ToArray();
}
