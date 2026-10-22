using Browser.Core;
using System.Text.Json;

namespace Browser.App;

// Éventail d'actions dans les pages (inject/fan.js) : au coin du texte sélectionné, d'un lien en forme de carte,
// d'une image ou d'un bloc de code. Sur une sélection, « Plus » ouvre le menu du clic droit pour tout le reste.
public sealed partial class BrowserController
{
    private void WireSelection() => _pages.FanAction += OnPageFan;

    private void OnPageFan(string tabId, string kind, string action, double x, double y, PageAiText value)
    {
        if (_closingWindow) return;
        var text = value.Text;
        switch (action)
        {
            case "download" when kind == "video" && _settings.VideoDownloads: OpenVideos(tabId); break;
            case "more" when kind == "video":
                ShowMenu(x, y, [
                    (T("Télécharger la vidéo…", "Download video…"), () => _afterMenu = () => OpenVideos(tabId)),
                    (T("Copier le lien vidéo", "Copy video link"), () => Copy(text)),
                    (T("Ouvrir dans un nouvel onglet", "Open in a new tab"), () => { if (IsWeb(text)) { Open(text, activate: false); Refresh(); } })
                ]);
                break;
            case "tab" when IsWeb(text): Open(text, activate: false); Refresh(); break;
            case "split" when IsWeb(text) && _tabs.ActiveId == tabId: Split(Open(text, activate: false).Id); Refresh(); break;
            case "link" when IsWeb(text):
            case "copy" when kind is "code" or "selection":
                Copy(text);
                break;
            case "explain" when kind == "code" && AiReady: _ = OpenPageAiAsync(tabId, value, "explain"); break;
            // Sélection : la vue Sol AI s'ouvre sur le passage ; sans compte, le panneau propose la connexion.
            case "sol" when kind == "selection":
                if (AiReady) _ = OpenPageAiAsync(tabId, value);
                else if (!PrivateActive && Chat is not null) { _ = Chat.ShowAsync(); Refresh(); }
                break;
            case "more" when kind == "selection": ShowSelectionMenu(tabId, value, x, y); break;
            case "note" when kind != "image" || IsWeb(text):
                AskNoteForSelection(tabId, kind == "image" ? $"![]({text})" : kind == "code" ? "```\n" + text + "\n```" : text, x, y);
                break;
        }
    }

    private static void Copy(string text)
    {
        var data = new Windows.ApplicationModel.DataTransfer.DataPackage(); data.SetText(text);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data);
    }

    // « Plus » de l'éventail d'une sélection : les actions du clic droit sur un passage.
    private void ShowSelectionMenu(string tabId, PageAiText selection, double x, double y)
    {
        var text = selection.Text;
        var items = new List<(string Label, Action? Run)>();
        if (AiReady)
        {
            foreach (var (label, run) in new[] { (T("Expliquer avec Sol AI", "Explain with Sol AI"), "explain"), (T("Traduire avec Sol AI", "Translate with Sol AI"), "translate"),
                (T("Résumer avec Sol AI", "Summarize with Sol AI"), "summary") })
                items.Add((label, () => _afterMenu = () => _ = OpenPageAiAsync(tabId, selection, run)));
            items.Add((T("Poser une question sur ce passage…", "Ask about this passage…"), () => _afterMenu = () => _ = OpenPageAiAsync(tabId, selection, null, "ask")));
            items.Add(("", null));
        }
        var shown = text.Length > 30 ? text[..30].TrimEnd() + "…" : text;
        items.Add((T($"Rechercher « {shown} »", $"Search for “{shown}”"), () => { Open(Resolve(text.Length > 300 ? text[..300] : text)); Refresh(); }));
        items.Add((T("Ajouter à une note…", "Add to a note…"), () => _afterMenu = () => AskNoteForSelection(tabId, text, x, y)));
        items.Add((T("Copier", "Copy"), () => Copy(text)));
        items.Add(("", null));
        items.Add((T("Capturer une zone", "Capture an area"), () => Capture("region")));
        if (AiReady) items.Add(("Sol AI…", () => _afterMenu = () => _ = OpenPageAiAsync(tabId, selection)));
        ShowMenu(x, y, items);
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
