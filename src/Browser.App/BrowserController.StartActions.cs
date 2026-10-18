using Browser.Core;

namespace Browser.App;

// Actions de l'éventail au coin des cartes de la page d'accueil : favoris, sujets de Sol AI et groupes d'onglets.
// La page d'accueil n'envoie que des commandes « start.* », vérifiées ici avant d'agir.
public sealed partial class BrowserController
{
    private const int TopicGroupSize = 6;

    private bool StartAction(string tabId, Command command)
    {
        var url = command.Input ?? "";
        switch (command.Type)
        {
            case "start.open":
                if (IsWeb(url)) { Open(url, activate: false); Refresh(); }
                return true;
            case "start.split":
                if (IsWeb(url) && _tabs.ActiveId == tabId) { Split(Open(url, activate: false).Id); Refresh(); }
                return true;
            case "start.copy":
                if (IsWeb(url))
                {
                    var data = new Windows.ApplicationModel.DataTransfer.DataPackage(); data.SetText(url);
                    Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data);
                }
                return true;
            case "start.topic": OpenTopicGroup(command.Title ?? ""); return true;
            case "start.groupedit": Execute(new Command("group.edit", Id: command.Id)); return true;
            default: return false;
        }
    }

    // Un sujet de « Vos sujets » devient un groupe d'onglets : ses liens principaux, ouverts ensemble et rangés sous son nom.
    private void OpenTopicGroup(string name)
    {
        if (AppPaths.Private || CategoryViews().FirstOrDefault(c => c.Name == name) is not { } topic) return;
        var tabs = topic.Links.Take(TopicGroupSize).Select(l => Open(l.Url, activate: false)).ToList();
        if (tabs.Count == 0 || _tabs.CreateGroup(tabs[0].Id, name) is not { } group) return;
        foreach (var tab in tabs.Skip(1)) _tabs.AddToGroup(tab.Id, group.Id);
        Activate(tabs[0].Id);
        Refresh();
    }
}
