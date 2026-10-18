using Browser.Core;
using System.Text.Json;

namespace Browser.App;

// Barre d'actions au-dessus d'une sélection de texte : copier, Sol AI, expliquer, traduire, et « Plus » pour le reste
// (résumer, poser une question, rechercher, ajouter à une note). Elle ne prend pas le focus : la page garde la main.
public sealed partial class BrowserController
{
    private const double SelectionBarWidth = 300;
    private (string TabId, PageAiText Text, double X, double Top)? _selection;
    private bool _selectionBar, _selectionMore;

    private void WireSelection()
    {
        _pages.SelectionChanged += ShowSelectionBar;
        _pages.SelectionCleared += id => { if (_selectionBar && _selection?.TabId == id) HidePopup(); };
    }

    private void ShowSelectionBar(string tabId, double x, double top, double bottom, PageAiText text)
    {
        // Seulement sur la page affichée, et sans chasser un menu ou une vue déjà ouverts.
        if (_closingWindow || (tabId != _tabs.ActiveId && !_split.Contains(tabId)) || (_popup.IsOpen && !_selectionBar)) return;
        HidePopup();
        _selection = (tabId, text, x, top);
        _selectionBar = true; _selectionMore = false;
        _popup.Show(x - Scaled(SelectionBarWidth) / 2, top - 6, SelectionBarWidth, SelectionJson(), focus: false, above: true);
    }

    private string SelectionJson() => JsonSerializer.Serialize(new
    {
        popup = "selection", ai = AiReady && _selection is { } s && !_tabs.Tabs.Any(t => t.Id == s.TabId && t.Private), more = _selectionMore,
    });

    private bool SelectionCommand(Command c)
    {
        if (!c.Type.StartsWith("selection.", StringComparison.Ordinal)) return false;
        if (!_selectionBar || _selection is not { } s) return true;
        switch (c.Type)
        {
            case "selection.more": _selectionMore = !_selectionMore; _popup.Post(SelectionJson()); return true;
            case "selection.copy":
                var data = new Windows.ApplicationModel.DataTransfer.DataPackage(); data.SetText(s.Text.Text);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data);
                HidePopup(); return true;
            case "selection.search":
                HidePopup();
                Open(Resolve(s.Text.Text.Length > 300 ? s.Text.Text[..300] : s.Text.Text)); Refresh(); return true;
            case "selection.note": HidePopup(); AskNoteForSelection(s.TabId, s.Text.Text, s.X, s.Top); return true;
            case "selection.ai":
                // « open » : la vue Sol AI complète ; sinon l'action démarre tout de suite (« ask » ouvre la question).
                var action = c.Query is "explain" or "translate" or "summary" ? c.Query : null;
                HidePopup();
                if (AiReady) _ = OpenPageAiAsync(s.TabId, s.Text, action, c.Query == "ask" ? "ask" : null);
                return true;
            default: return true;
        }
    }

    // Choix de la note qui reçoit le passage, comme « Ajouter à une note… » du menu contextuel.
    private void AskNoteForSelection(string tabId, string selection, double x, double y)
    {
        var source = _tabs.Tabs.FirstOrDefault(t => t.Id == tabId);
        var url = source?.Url ?? "";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0 || url.Length > 4096) url = "";
        _noteSelection = new("", (source?.Title ?? "")[..Math.Min(source?.Title.Length ?? 0, 120)], selection, url, DateTimeOffset.UtcNow);
        HidePopup();
        _popup.Show(x, y, 360, JsonSerializer.Serialize(new { popup = "notes-select",
            items = _notes.Items.Select(n => new { id = n.Id, title = n.Title.Length > 0 ? n.Title : n.Text[..Math.Min(80, n.Text.Length)] }) }), focus: true);
    }
}
