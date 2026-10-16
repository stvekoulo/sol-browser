using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Browser.App;

public sealed partial class MainWindow
{
    private bool _chromePainted, _contentPainted, _layoutReady, _revealed;
    private async Task StartInterfaceAsync()
    {
        try { await InitAsync(); }
        catch(Exception error)
        {
            DebugLog.Write($"startup.failed {error}");
            StartupText.Text="Sol n’a pas pu ouvrir son interface. Fermez cette fenêtre et relancez le navigateur.";
        }
    }
    private void WirePresentation(UiBridge bridge, PageHost pages)
    {
        var slowPageTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(5)};
        slowPageTimer.Tick+=(_,_)=>
        {
            slowPageTimer.Stop();
            // Une page distante lente ne doit pas bloquer les commandes du navigateur.
            _contentPainted=true;
            DebugLog.Write("startup.content.timeout");
            RevealInterface();
        };
        Closed+=(_,_)=>slowPageTimer.Stop();
        bridge.LayoutRequested+=layout=>
        {
            if(_beforeFullScreen.HasValue)return;
            _sidebar!.Apply(layout.Sidebar);
            Root.RowDefinitions[0].Height=new GridLength(layout.Header);
            SidebarGrip.Visibility=layout.Sidebar>1?Visibility.Visible:Visibility.Collapsed;
            _layoutReady=true;
            RevealInterface();
        };
        bridge.Painted+=()=>
        {
            _chromePainted=true;
            DebugLog.Write("startup.chrome.ready");
            RevealInterface();
            if(!_revealed&&!_contentPainted)slowPageTimer.Start();
        };
        pages.ContentPainted+=()=>
        {
            _contentPainted=true;
            slowPageTimer.Stop();
            DebugLog.Write("startup.content.ready");
            RevealInterface();
        };
    }
    private void RevealInterface()
    {
        if(_revealed||!_chromePainted||!_contentPainted||!_layoutReady)return;
        _revealed=true;
        StartupShield.Visibility=Visibility.Collapsed;
        DebugLog.Write("startup.ready coordinated");
    }
}
