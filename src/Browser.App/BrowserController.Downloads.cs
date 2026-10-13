using Microsoft.Web.WebView2.Core;
using Microsoft.UI.Dispatching;
using System.Runtime.InteropServices;
namespace Browser.App;
public sealed partial class BrowserController
{
    // Téléchargements de la session, du plus récent au plus ancien.
    private readonly List<(string Id, Microsoft.Web.WebView2.Core.CoreWebView2DownloadOperation Op, string TabId)> _downloads = new();
    private bool _downloadsOpen;
    private DateTime _downloadsPosted;
    private readonly HashSet<string> _pausedDownloads=new(),_canceledDownloads=new();
    private readonly List<DispatcherQueueTimer> _downloadTimers=new();

    private Microsoft.Web.WebView2.Core.CoreWebView2DownloadOperation? FindDownload(string id) =>
        _downloads.FirstOrDefault(d => d.Id == id).Op;

    private void OnDownloadStarting(string tabId, Microsoft.Web.WebView2.Core.CoreWebView2DownloadStartingEventArgs e)
    {
        using var deferral=e.GetDeferral();
        try
        {
            e.Handled = true; // pas de fenêtre de téléchargement du moteur : le navigateur affiche la sienne
            // Vérification dans un profil jetable : les fichiers de test restent avec ses données.
            if (Environment.GetEnvironmentVariable("NAVIGATEUR_TEST_DOWNLOAD_DIR") is { Length: > 0 } dir && Directory.Exists(dir))
                e.ResultFilePath = Path.Combine(dir, Path.GetFileName(e.ResultFilePath));
            var op = e.DownloadOperation;
            _downloads.Insert(0, ($"d{_downloads.Count + 1}", op, tabId));
            var id = _downloads[0].Id;
            op.BytesReceivedChanged += (_, _) => DownloadsChanged(throttle: true);
            op.StateChanged += (_, _) => Changed();
            void Changed()
            {
                DebugLog.Write($"download.state {id} {op.State} {op.InterruptReason} resume={op.CanResume}");
                if(op.State==CoreWebView2DownloadState.Interrupted&&op.InterruptReason is not (CoreWebView2DownloadInterruptReason.UserPaused or CoreWebView2DownloadInterruptReason.UserCanceled))_pausedDownloads.Remove(id);
                if(op.State==CoreWebView2DownloadState.InProgress)_pausedDownloads.Remove(id);
                if(DownloadPending(id,op)){_finishedAt.Remove(id);DownloadsChanged(throttle:false);return;}
                if(_finishedAt.ContainsKey(id))return;
                _finishedAt[id] = DateTime.UtcNow;
                // Un onglet ouvert seulement pour ce téléchargement n'a rien à afficher : il se referme une fois le fichier arrivé.
                // Pas avant : fermer la vue d'origine interrompt le téléchargement (constaté).
                if ((op.State == CoreWebView2DownloadState.Completed||_canceledDownloads.Contains(id))
                    && _tabs.Tabs.Count > 1 && _pages.CoreOf(tabId) is { CanGoBack: false, Source: "about:blank" })
                    Close(tabId);
                DownloadsChanged(throttle: false);
            }
            DownloadsChanged(throttle: false);
            var last=(op.State,op.BytesReceived,op.CanResume);
            var monitor=_window.DispatcherQueue.CreateTimer();monitor.Interval=TimeSpan.FromMilliseconds(500);
            monitor.Tick+=(_,_)=>
            {
                if(_closingWindow){Stop();return;}
                if(_finishedAt.TryGetValue(id,out var finished))
                {
                    if(DateTime.UtcNow-finished>DockLinger){UpdateDock();Stop();}return;
                }
                // Les opérations WebView2 se relisent sur le fil UI, même si le moteur omet une notification.
                var current=(op.State,op.BytesReceived,op.CanResume);
                if(current!=last||!DownloadPending(id,op)){last=current;Changed();}
            };
            void Stop(){monitor.Stop();_downloadTimers.Remove(monitor);}
            _downloadTimers.Add(monitor);monitor.Start();
        }
        finally {deferral.Complete();}
    }
    private bool DownloadPaused(string id,CoreWebView2DownloadOperation op)=>!_canceledDownloads.Contains(id)&&op.State!=CoreWebView2DownloadState.Completed&&(_pausedDownloads.Contains(id)||op.InterruptReason==CoreWebView2DownloadInterruptReason.UserPaused);
    private bool DownloadPending(string id,CoreWebView2DownloadOperation op)=>!_canceledDownloads.Contains(id)&&(op.State==CoreWebView2DownloadState.InProgress||DownloadPaused(id,op)||op.State==CoreWebView2DownloadState.Interrupted&&op.CanResume);
    private void ControlDownload(string id,string action)
    {
        if(FindDownload(id) is not { } op||op.State==CoreWebView2DownloadState.Completed||_canceledDownloads.Contains(id))return;
        var paused=_pausedDownloads.Contains(id);
        try
        {
            if(action=="download.pause"&&op.State==CoreWebView2DownloadState.InProgress){_pausedDownloads.Add(id);op.Pause();}
            else if(action=="download.resume"&&op.CanResume){_pausedDownloads.Remove(id);op.Resume();}
            else if(action=="download.cancel")
            {
                _canceledDownloads.Add(id);_pausedDownloads.Remove(id);op.Cancel();_finishedAt[id]=DateTime.UtcNow;
                var source=_downloads.First(d=>d.Id==id).TabId;
                if(_tabs.Tabs.Count>1&&_pages.CoreOf(source) is {CanGoBack:false,Source:"about:blank"})Close(source);
            }
        }
        catch(Exception e) when(e is COMException or InvalidOperationException)
        {
            if(paused)_pausedDownloads.Add(id);else _pausedDownloads.Remove(id);_canceledDownloads.Remove(id);
            Notice(T("Téléchargement","Download"),T("Cette action n’a pas pu être appliquée. Réessayez depuis les téléchargements.","The action could not be applied. Try again from downloads."));
        }
        DownloadsChanged(throttle:false);
    }

    private void DownloadsChanged(bool throttle)
    {
        // La progression arrive très souvent : quatre mises à jour par seconde suffisent.
        if (throttle && (DateTime.UtcNow - _downloadsPosted).TotalMilliseconds < 250) return;
        _downloadsPosted = DateTime.UtcNow;
        Refresh();
        if (_downloadsOpen) _popup.Post(DownloadsJson(_downloads));
        UpdateDock();
    }

    // Encart en bas à droite : les téléchargements en cours, et ceux qui viennent de finir pendant quelques secondes.
    private static readonly TimeSpan DockLinger = TimeSpan.FromSeconds(6);
    private readonly Dictionary<string, DateTime> _finishedAt = new();
    private bool _dockShown;
    private bool _updateDismissed; // « Plus tard » : l'encart ne reparle plus de la mise à jour, les réglages si
}
