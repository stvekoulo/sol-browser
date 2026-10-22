using Browser.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using Windows.UI;
using Windows.System;
namespace Browser.App;

public sealed partial class PageHost
{
    private sealed record PreviewSurface(string SourceId, Grid Overlay, WebView2 View, TextBlock Address, TextBlock Status,
        Button Back, Button Forward, Button Reload, ProgressBar Progress)
    { public string Url = ""; public bool Loading; }
    private PreviewSurface? _preview;
    private readonly string _previewEscape = "sol.preview.close:"+Guid.NewGuid().ToString("N");
    public string? PreviewSourceId => _preview?.SourceId;
    public event Action<string,string,bool>? PreviewPromoted;

    public void ClosePreview()
    {
        if(_preview is not {} preview)return;
        _preview=null;container.Children.Remove(preview.Overlay);
        preview.View.Close();
        try{if(_views.TryGetValue(preview.SourceId,out var owner))owner.Focus(FocusState.Programmatic);}
        catch(InvalidOperationException){ }
    }
    public void PromotePreview(bool split)
    {
        if(_preview is not {} preview||!LinkPreview.Allowed(preview.Url))return;
        var id=preview.SourceId;var url=preview.Url;ClosePreview();PreviewPromoted?.Invoke(id,url,split);
    }
    public void NavigatePreview(string action)
    {
        if(_preview?.View.CoreWebView2 is not {} core)return;
        if(action=="back"&&core.CanGoBack)core.GoBack();
        else if(action=="forward"&&core.CanGoForward)core.GoForward();
        else if(action=="reload") {if(_preview.Loading)core.Stop();else core.Reload();}
    }
    public async Task OpenPreviewAsync(string sourceId,string url)
    {
        if(!LinkPreview.Allowed(url)||Core(sourceId) is not {} source)return;
        ClosePreview();
        var preview=CreatePreview(sourceId);preview.Url=url;preview.Address.Text=url;_preview=preview;
        container.Children.Add(preview.Overlay);
        try
        {
            await StartEngineAsync(preview.View,source.Profile.ProfileName);
            if(_preview!=preview)return;
            var core=preview.View.CoreWebView2;
            await Harden(core,withFan:false);
            if(_preview!=preview)return;
            core.Settings.AreDefaultContextMenusEnabled=true;
            core.Settings.IsStatusBarEnabled=true;
            var signal=System.Text.Json.JsonSerializer.Serialize(_previewEscape);
            await core.AddScriptToExecuteOnDocumentCreatedAsync("addEventListener('keydown',e=>{if(e.isTrusted&&e.key==='Escape'){e.preventDefault();e.stopImmediatePropagation();chrome.webview.postMessage("+signal+")}},true)");
            core.WebMessageReceived+=(_,e)=>{if(_preview==preview&&e.Source==core.Source&&e.WebMessageAsJson==signal)ClosePreview();};
            core.FrameCreated+=(_,e)=>e.Frame.WebMessageReceived+=(_,message)=>{if(_preview==preview&&message.WebMessageAsJson==signal)ClosePreview();};
            core.NavigationStarting+=(_,e)=>
            {
                if(!LinkPreview.Allowed(e.Uri)){e.Cancel=true;return;}
                preview.Loading=true;preview.Progress.Visibility=Visibility.Visible;preview.Status.Text="";
                preview.Reload.Content="×";
            };
            core.SourceChanged+=(_,_)=>{if(LinkPreview.Allowed(core.Source)){preview.Url=core.Source;preview.Address.Text=core.Source;ToolTipService.SetToolTip(preview.Address,core.Source);}};
            core.HistoryChanged+=(_,_)=>{preview.Back.IsEnabled=core.CanGoBack;preview.Forward.IsEnabled=core.CanGoForward;};
            core.NavigationCompleted+=(_,e)=>
            {
                preview.Loading=false;preview.Reload.Content="↻";preview.Progress.Visibility=Visibility.Collapsed;
                if(!e.IsSuccess&&e.WebErrorStatus!=CoreWebView2WebErrorStatus.OperationCanceled)
                    preview.Status.Text=Language.T("La page n’a pas pu être chargée. Réessayez avec ↻.","The page could not be loaded. Retry with ↻.");
            };
            core.NewWindowRequested+=(_,e)=>{e.Handled=true;if(LinkPreview.Allowed(e.Uri))core.Navigate(e.Uri);};
            core.DownloadStarting+=(_,e)=>{e.Cancel=true;preview.Status.Text=Language.T("Ouvrez cet aperçu dans un onglet pour télécharger.","Open this preview in a tab to download.");};
            core.ProcessFailed+=(_,_)=>preview.Status.Text=Language.T("Cet aperçu s’est interrompu. Rechargez la page.","This preview stopped. Reload the page.");
            core.Navigate(url);preview.View.Focus(FocusState.Programmatic);
        }
        catch(Exception e) when(e is InvalidOperationException or System.Runtime.InteropServices.COMException or IOException)
        {if(_preview==preview){preview.Progress.Visibility=Visibility.Collapsed;preview.Status.Text=Language.T("Impossible d’ouvrir cet aperçu.","Unable to open this preview.");}}
    }

    private PreviewSurface CreatePreview(string sourceId)
    {
        var overlay=new Grid{Background=new SolidColorBrush(Color.FromArgb(100,0,0,0))};Grid.SetColumnSpan(overlay,3);Canvas.SetZIndex(overlay,50);
        var card=new Grid{Margin=new Thickness(24,18,56,18),CornerRadius=new CornerRadius(16),Background=new SolidColorBrush(UiBridge.InitialBackground),Padding=new Thickness(1),Shadow=new ThemeShadow(),Translation=new System.Numerics.Vector3(0,0,24)};
        card.RowDefinitions.Add(new(){Height=new GridLength(0)});card.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});card.RowDefinitions.Add(new(){Height=GridLength.Auto});
        overlay.Children.Add(card);
        overlay.PointerPressed+=(_,e)=>{if(ReferenceEquals(e.OriginalSource,overlay)){ClosePreview();e.Handled=true;}};
        overlay.KeyDown+=(_,e)=>{if(e.Key==VirtualKey.Escape){ClosePreview();e.Handled=true;}};
        var ink=new SolidColorBrush(UiBridge.InitialBackground.R<128?Color.FromArgb(255,245,245,245):Color.FromArgb(255,25,25,28));
        var rail=new StackPanel{Spacing=6,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,18,12,0)};
        var toolbar=new StackPanel{Orientation=Orientation.Horizontal,Spacing=2,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(34,28,0,0),CornerRadius=new CornerRadius(9),Background=new SolidColorBrush(UiBridge.InitialBackground)};
        var navigation=new StackPanel{Orientation=Orientation.Horizontal,Visibility=Visibility.Collapsed};
        overlay.Children.Add(toolbar);overlay.Children.Add(rail);Canvas.SetZIndex(toolbar,2);Canvas.SetZIndex(rail,2);
        Button Action(string label,string glyph,StackPanel parent,Action run)
        {
            var button=new Button{Content=glyph,Width=32,Height=32,MinWidth=28,MinHeight=28,CornerRadius=new CornerRadius(8),Padding=new Thickness(5),Foreground=ink,Background=new SolidColorBrush(UiBridge.InitialBackground),BorderThickness=new Thickness(0)};
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button,label);ToolTipService.SetToolTip(button,label);button.Click+=(_,_)=>run();parent.Children.Add(button);return button;
        }
        var menu=Action(Language.T("Navigation de l’aperçu","Preview navigation"),"☰",toolbar,()=>navigation.Visibility=navigation.Visibility==Visibility.Visible?Visibility.Collapsed:Visibility.Visible);
        toolbar.Children.Add(navigation);
        var back=Action(Language.T("Précédent","Back"),"‹",navigation,()=>NavigatePreview("back"));back.IsEnabled=false;
        var forward=Action(Language.T("Suivant","Forward"),"›",navigation,()=>NavigatePreview("forward"));forward.IsEnabled=false;
        var reload=Action(Language.T("Recharger / arrêter","Reload / stop"),"↻",navigation,()=>NavigatePreview("reload"));
        var address=new TextBlock{TextTrimming=TextTrimming.CharacterEllipsis,MaxWidth=180,VerticalAlignment=VerticalAlignment.Center,Foreground=ink,FontSize=12,Margin=new Thickness(8,0,8,0)};
        navigation.Children.Add(address);
        Action(Language.T("Fermer l’aperçu (Échap)","Close preview (Escape)"),"×",rail,ClosePreview);
        Action(Language.T("Ouvrir dans un nouvel onglet","Open in a new tab"),"↗",rail,()=>PromotePreview(false));
        Action(Language.T("Ouvrir à côté","Open beside"),"◫",rail,()=>PromotePreview(true));
        var view=new CursorWebView{DefaultBackgroundColor=UiBridge.InitialBackground};Grid.SetRow(view,1);card.Children.Add(view);
        view.SizeChanged+=(_,_)=>CornerClip.Set(view,12);
        var progress=new ProgressBar{IsIndeterminate=true,Height=2,VerticalAlignment=VerticalAlignment.Top};Grid.SetRow(progress,1);Canvas.SetZIndex(progress,2);card.Children.Add(progress);
        overlay.SizeChanged+=(_,_)=>{
            var left=Math.Clamp(overlay.ActualWidth*.08,16,80);
            card.Margin=new Thickness(left,18,56,18);toolbar.Margin=new Thickness(left+10,28,0,0);
            address.MaxWidth=Math.Max(0,Math.Min(180,overlay.ActualWidth-260));
        };
        var status=new TextBlock{Foreground=ink,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(12,3,12,3),FontSize=12};Grid.SetRow(status,2);card.Children.Add(status);
        return new(sourceId,overlay,view,address,status,back,forward,reload,progress);
    }
}
