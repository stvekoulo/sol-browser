using Browser.Core;
namespace Browser.App;
public sealed partial class BrowserController
{
    private async Task ShowLinkPreviewAsync(string id,string url)
    {
        if(!LinkPreview.Allowed(url)||_tabs.Tabs.All(t=>t.Id!=id))return;
        HidePopup();await _pages.OpenPreviewAsync(id,url);
    }
    private void PromoteLinkPreview(string source,string url,bool beside)
    {
        if(_tabs.Tabs.All(t=>t.Id!=source))return;
        if(beside)Activate(source);
        var tab=OpenLink(source,url,activate:!beside);
        if(beside)Split(tab.Id);
        Refresh();
    }
    private bool PreviewCommand(Command c)
    {
        switch(c.Type)
        {
            case "preview.open": _=ShowLinkPreviewAsync(c.Id??"",c.Input??"");return true;
            case "preview.close":_pages.ClosePreview();return true;
            case "preview.tab":_pages.PromotePreview(false);return true;
            case "preview.split":_pages.PromotePreview(true);return true;
            case "preview.back":_pages.NavigatePreview("back");return true;
            case "preview.forward":_pages.NavigatePreview("forward");return true;
            case "preview.reload":_pages.NavigatePreview("reload");return true;
            default:return false;
        }
    }
}
