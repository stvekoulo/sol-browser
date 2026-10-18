using System.Text.Json;
using Browser.Core;

namespace Browser.App;

public sealed partial class BrowserController
{
    private bool SidebarCommand(Command c)
    {
        var id = c.Id ?? "";
        switch (c.Type)
        {
            case "tab.pin":
                if (c.Index is not (0 or 1)) return true;
                PinTabs(id, c.Index == 1);
                break;
            case "group.move": _tabs.MoveGroup(id, c.Index!.Value); KeepPairAdjacent(); break;
            case "group.open": OpenInGroup(id); break;
            case "group.edit":
                if (_tabs.Groups.FirstOrDefault(g => g.Id == id) is { } group) EditGroup(null, group);
                return true;
            case "shortcut.move":
                if (!int.TryParse(id, out var from) || from < 0 || from >= _settings.Shortcuts.Count
                    || c.Index is not { } to || to < 0 || to >= _settings.Shortcuts.Count) return true;
                var shortcut = _settings.Shortcuts[from];
                _settings.Shortcuts.RemoveAt(from);
                _settings.Shortcuts.Insert(to, shortcut);
                SaveShortcuts();
                break;
            case "sidebar.menu":
                ShowMenu(c.X!.Value, c.Y!.Value,
                [
                    (T("Rouvrir le dernier onglet fermé", "Reopen last closed tab"), () => Execute(new("tab.reopen"))),
                    ("", null),
                    (T("Replier tous les groupes", "Collapse all groups"), () => FoldGroups(true)),
                    (T("Déplier tous les groupes", "Expand all groups"), () => FoldGroups(false)),
                ]);
                return true;
            default: return false;
        }
        HidePopup(); Refresh();
        return true;
    }

    private void FoldGroups(bool collapsed)
    {
        foreach (var group in _tabs.Groups.ToArray()) _tabs.UpdateGroup(group.Id, collapsed: collapsed);
    }

    private void PinTabs(string id, bool pinned)
    {
        var ids = _split.Contains(id) ? new[] { _split.PrimaryId!, _split.SecondaryId! } : [id];
        foreach (var tabId in ids)
        {
            if (!pinned)
                foreach (var shortcut in _shortcutTabs.Where(p => p.Value == tabId).Select(p => p.Key).ToArray())
                    _shortcutTabs.Remove(shortcut);
            _tabs.SetPinned(tabId, pinned);
        }
        KeepPairAdjacent();
    }

    private void OpenInGroup(string id)
    {
        if (_tabs.Groups.All(g => g.Id != id)) return;
        var tab = Open(_settings.HomePage);
        _tabs.AddToGroup(tab.Id, id);
        _tabs.UpdateGroup(id, collapsed: false);
        _bridge.FocusAddress();
    }

    private void OpenTabMenu(string id, double x, double y)
    {
        if (_tabs.Tabs.FirstOrDefault(t => t.Id == id) is not { } tab) return;
        var ids = _split.Contains(id) ? new[] { _split.PrimaryId!, _split.SecondaryId! } : [id];
        var block = ids.Length > 1;
        ShowMenu(x, y,
        [
            .. tab.Private ? [] : new (string, Action?)[] { (T("Session de cet onglet…", "Tab session…"), () => _afterMenu = () => OpenSessionMenu(id, x, y)) },
            (tab.Pinned ? (block ? T("Détacher les deux pages", "Unpin both pages") : T("Détacher l'onglet", "Unpin tab"))
                : (block ? T("Épingler les deux pages", "Pin both pages") : T("Épingler l'onglet", "Pin tab")), () => PinTabs(id, !tab.Pinned)),
            .. GroupEntries(tab),
            .. ShortcutEntry(tab),
            ("", null),
            (T("Dupliquer l'onglet", "Duplicate tab"), () => { var copy = Open(tab.Url, sessionId: tab.SessionId, isPrivate: tab.Private); if (tab.GroupId is { } g) _tabs.AddToGroup(copy.Id, g); }),
            (T("Fermer l'onglet", "Close tab"), () => Close(id)),
            (T("Fermer les autres onglets", "Close other tabs"), () =>
            {
                foreach (var other in _tabs.Tabs.Where(t => !ids.Contains(t.Id) && !t.Pinned).Select(t => t.Id).ToList())
                { _tabs.RemoveFromGroup(other); Close(other); }
            }),
        ]);
    }

    private void OpenGroupMenu(string id, double x, double y)
    {
        if (_tabs.Groups.FirstOrDefault(g => g.Id == id) is not { } group) return;
        var members = _tabs.Tabs.Where(t => t.GroupId == id).Select(t => t.Id).ToList();
        ShowMenu(x, y,
        [
            (T("Nouvel onglet dans ce groupe", "New tab in this group"), () => OpenInGroup(id)),
            (group.Collapsed ? T("Déplier le groupe", "Expand group") : T("Replier le groupe", "Collapse group"), () => _tabs.UpdateGroup(id, collapsed: !group.Collapsed)),
            (T("Modifier le groupe (nom, couleur, icône)", "Edit group (name, color, icon)"), () => _afterMenu = () => EditGroup(null, group)),
            ("", null),
            (T("Dissocier le groupe", "Ungroup"), () => _tabs.Ungroup(id)),
            (T("Mettre le groupe en veille", "Put group to sleep"), () => { foreach (var member in members) if (!_pages.IsAsleep(member)) Close(member); }),
            (T("Fermer le groupe", "Close group"), () => { _tabs.Ungroup(id); foreach (var member in members) Close(member); }),
        ]);
    }
}
