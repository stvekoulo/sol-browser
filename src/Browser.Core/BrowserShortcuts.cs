namespace Browser.Core;

public sealed record BrowserShortcut(uint Key, uint Modifiers, string Action, string French, string English, string KeyName)
{
    public string Text(bool english)
    {
        var parts = new List<string>();
        if ((Modifiers & 2) != 0) parts.Add("Ctrl");
        if ((Modifiers & 1) != 0) parts.Add("Alt");
        if ((Modifiers & 4) != 0) parts.Add(english ? "Shift" : "Maj");
        parts.Add(KeyName);
        return string.Join("+", parts);
    }
}

// Windows virtual keys. This list feeds registration, settings and palette hints.
public static class BrowserShortcuts
{
    public static readonly IReadOnlyList<BrowserShortcut> All = [
        new(0x54,2,"tab.open","Nouvel onglet","New tab","T"),
        new(0x57,2,"tab.close","Fermer l’onglet","Close tab","W"),
        new(0x54,6,"tab.reopen","Rouvrir l’onglet fermé","Reopen closed tab","T"),
        new(0x4E,6,"window.private","Navigation privée","Private browsing","N"),
        new(0x4D,6,"notes.manage","Ouvrir les notes","Open notes","M"),
        new(0x51,6,"notes.open","Ajouter une note rapide","Add a quick note","Q"),
        new(0x4E,3,"notes.manage","Ouvrir les notes","Open notes","N"),
        new(0x4E,7,"notes.open","Ajouter une note rapide","Add a quick note","N"),
        new(0x50,6,"palette.open","Palette de commandes","Command palette","P"),
        new(0x48,2,"history.open","Historique","History","H"),
        new(0x4A,2,"downloads.open","Téléchargements","Downloads","J"),
        new(0x4F,6,"library.open","Favoris","Bookmarks","O"),
        new(0x44,2,"bookmark.toggle","Ajouter ou retirer des favoris","Add or remove bookmark","D"),
        new(0xBC,2,"settings.open","Paramètres","Settings",","),
        new(0x45,6,"extensions.open","Extensions","Extensions","E"),
        new(0x53,6,"capture.menu","Capture d’écran","Screenshot","S"),
        new(0x78,0,"reader.toggle","Mode lecteur","Reader mode","F9"),
        new(0x44,6,"design.toggle","Inspecteur de design","Design inspector","D"),
        new(0x55,6,"reader.listen","Écouter, pause ou reprendre","Listen, pause or resume","U"),
        new(0x58,6,"reader.stop","Arrêter la lecture vocale","Stop reading aloud","X"),
        new(0x59,6,"translation.open","Traduire la page","Translate page","Y"),
        new(0x49,6,"network.toggle","Inspecteur réseau","Network inspector","I"),
        new(0x32,6,"split.open","Afficher côte à côte","Show side by side","2"),
        new(0x31,6,"split.close","Quitter la vue côte à côte","Exit side by side","1"),
        new(0x41,6,"profile.menu","Espaces et profils","Spaces and profiles","A"),
        new(0x42,6,"session.menu","Session de cet onglet","Tab session","B"),
        new(0x7A,0,"window.maximize","Plein écran","Full screen","F11"),
        new(0x4C,2,"address.focus","Barre d’adresse","Address bar","L"),
        new(0x52,2,"nav.reload","Recharger la page","Reload page","R"),
        new(0x74,0,"nav.reload","Recharger la page","Reload page","F5"),
        new(0x09,2,"tab.next","Onglet suivant","Next tab","Tab"),
        new(0x09,6,"tab.previous","Onglet précédent","Previous tab","Tab"),
        new(0x25,1,"nav.back","Page précédente","Previous page","←"),
        new(0x27,1,"nav.forward","Page suivante","Next page","→"),
        new(0x21,6,"nav.back","Page précédente","Previous page","Page ↑"),
        new(0x22,6,"nav.forward","Page suivante","Next page","Page ↓"),
        new(0xBB,2,"zoom.in","Agrandir la page","Zoom in","+"),
        new(0x6B,2,"zoom.in","Agrandir la page","Zoom in","+"),
        new(0xBD,2,"zoom.out","Réduire la page","Zoom out","−"),
        new(0x6D,2,"zoom.out","Réduire la page","Zoom out","−"),
        new(0x30,2,"zoom.reset","Zoom à 100 %","Reset zoom to 100%","0"),
        new(0x31,2,"tab.1","Onglet 1","Tab 1","1"),
        new(0x32,2,"tab.2","Onglet 2","Tab 2","2"),
        new(0x33,2,"tab.3","Onglet 3","Tab 3","3"),
        new(0x34,2,"tab.4","Onglet 4","Tab 4","4"),
        new(0x35,2,"tab.5","Onglet 5","Tab 5","5"),
        new(0x36,2,"tab.6","Onglet 6","Tab 6","6"),
        new(0x37,2,"tab.7","Onglet 7","Tab 7","7"),
        new(0x38,2,"tab.8","Onglet 8","Tab 8","8"),
        new(0x39,2,"tab.9","Dernier onglet","Last tab","9"),
    ];

    public static string TextFor(string action, bool english) => All.FirstOrDefault(s => s.Action == action)?.Text(english) ?? "";
}
