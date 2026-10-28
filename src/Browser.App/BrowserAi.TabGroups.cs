using Browser.Core;
using System.Text;
using System.Text.Json;

namespace Browser.App;

// Sol AI et les groupes d'onglets. Rien ne bouge sans un clic : Sol AI propose des groupes dans la barre latérale,
// l'utilisateur les applique ou les ignore ; dans l'éditeur de groupe, la suggestion remplit seulement le formulaire.
public sealed partial class BrowserController
{
    private bool _tabsSorting;
    private string? _tabsError;
    private AiGroup[] _tabsSuggestion = [];
    private int _tabsHintDismissed; // nombre d'onglets hors groupe au moment où la suggestion a été écartée
    private AiGroup? _groupSuggestion;
    private string? _groupSuggesting;

    private bool TabGroupAiCommand(Command c)
    {
        switch (c.Type)
        {
            case "tabs.ai": _ = SuggestTabGroupsAsync(); return true;
            case "tabs.ai.apply": ApplyTabGroups(); return true;
            case "tabs.ai.dismiss":
                _tabsSuggestion = []; _tabsError = null; _tabsHintDismissed = UngroupedTabs().Count; Refresh(); return true;
            case "group.ai": _ = SuggestGroupAsync(c.Id ?? ""); return true;
            default: return false;
        }
    }

    private List<AiTab> UngroupedTabs() => _tabs.Tabs
        .Where(t => !t.Pinned && !t.Private && t.GroupId is null)
        .Select(t => SolAiTabGroups.Tab(t.Id, t.Url, t.Title)).OfType<AiTab>().ToList();

    // État pour la barre latérale : la carte n'apparaît qu'à partir de six onglets hors groupe, ou avec une proposition.
    private object? TabGroupAiState()
    {
        if (!AiReady || PrivateActive) return null;
        var ungrouped = UngroupedTabs().Count;
        var hint = ungrouped >= SolAiTabGroups.MinUngrouped && ungrouped >= _tabsHintDismissed + 4;
        if (!hint && !_tabsSorting && _tabsSuggestion.Length == 0 && _tabsError == null) return null;
        var titles = _tabs.Tabs.ToDictionary(t => t.Id, t => t.Title);
        return new
        {
            ungrouped, busy = _tabsSorting, error = _tabsError,
            groups = _tabsSuggestion.Select(g => new { name = g.Name, color = g.Color, icon = g.Icon,
                tabs = g.TabIds.Where(titles.ContainsKey).Select(id => titles[id]) }),
        };
    }

    private async Task SuggestTabGroupsAsync()
    {
        if (_tabsSorting || !AiReady || PrivateActive) return;
        var tabs = UngroupedTabs();
        if (tabs.Count < 2) return;
        _tabsSorting = true; _tabsError = null; _tabsSuggestion = []; Refresh();
        var answer = new StringBuilder();
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            await _chatGpt.StreamAsync(SolAiTabGroups.Instruction(Language.Current, _tabs.Groups.Select(g => g.Name).ToArray()),
                SolAiTabGroups.Data(tabs), delta => answer.Append(delta), deadline.Token);
            _tabsSuggestion = SolAiTabGroups.Parse(answer.ToString(), tabs);
            if (_tabsSuggestion.Length == 0) _tabsError = "groups-empty";
        }
        catch (ChatGptException e) { _tabsError = e.Code; }
        catch (Exception e) when (e is HttpRequestException or IOException or JsonException or OperationCanceledException)
        { DebugLog.Write("chatgpt.tabs " + e.GetType().Name); _tabsError = "network"; }
        _tabsSorting = false;
        if (!_closingWindow) Refresh();
    }

    // Applique la proposition : chaque groupe est créé autour de son premier onglet encore libre, les autres le rejoignent.
    private void ApplyTabGroups()
    {
        foreach (var proposal in _tabsSuggestion)
        {
            var free = proposal.TabIds.Where(id => _tabs.Tabs.Any(t => t.Id == id && !t.Pinned && t.GroupId is null)).ToList();
            if (free.Count < 2 || _tabs.CreateGroup(free[0], proposal.Name, proposal.Color.Length > 0 ? proposal.Color : null, icon: proposal.Icon) is not { } group) continue;
            foreach (var id in free.Skip(1)) _tabs.AddToGroup(id, group.Id);
        }
        _tabsSuggestion = []; _tabsError = null; _tabsHintDismissed = UngroupedTabs().Count;
        KeepPairAdjacent();
        Refresh();
    }

    // Onglets que Sol AI regarde : ceux du groupe, ou, à la création, ceux qui vont le former (id = onglet d'origine).
    private List<AiTab> GroupAiTabs(string id)
    {
        var creating = _groupEditing is { Group: null, TabId: var tabId } && tabId == id;
        return _tabs.Tabs.Where(t => !t.Private && (creating ? _groupCreationTabs.Contains(t.Id) : t.GroupId == id))
            .Select(t => SolAiTabGroups.Tab(t.Id, t.Url, t.Title)).OfType<AiTab>().ToList();
    }

    // Suggestion pour l'éditeur de groupe : nom, couleur et icône d'après ses onglets.
    private async Task SuggestGroupAsync(string groupId)
    {
        if (!AiReady || PrivateActive || _groupSuggesting != null) return;
        var tabs = GroupAiTabs(groupId);
        if (tabs.Count == 0) return;
        _groupSuggesting = groupId; _groupSuggestion = null; PublishGroupEditor();
        var answer = new StringBuilder();
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(1));
            await _chatGpt.StreamAsync(SolAiTabGroups.GroupInstruction(Language.Current), SolAiTabGroups.Data(tabs), delta => answer.Append(delta), deadline.Token);
            _groupSuggestion = SolAiTabGroups.ParseOne(answer.ToString());
        }
        catch (Exception e) when (e is ChatGptException or HttpRequestException or IOException or JsonException or OperationCanceledException)
        { DebugLog.Write("chatgpt.group " + e.GetType().Name); }
        _groupSuggesting = null;
        if (!_closingWindow) PublishGroupEditor();
    }
}
