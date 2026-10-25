using System.Text.Json;
using Browser.Core;
namespace Browser.App;
public sealed partial class BrowserController
{
    private void ShowLanPreview(string id)
    {
        if (_tabs.Tabs.FirstOrDefault(t => t.Id == id) is not { } tab || !LanPreview.IsLocal(tab.Url)) return;
        HidePopup();
        _popup.ShowCentered(400, JsonSerializer.Serialize(new { popup = "lan-preview", items = LanPreview.Addresses()
            .Select(a => new { name = a.Name, address = a.Address, url = LanPreview.Rewrite(tab.Url, a.Address) }) }));
    }
}
