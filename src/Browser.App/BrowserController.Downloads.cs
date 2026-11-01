using Browser.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.UI.Dispatching;
using System.Runtime.InteropServices;
namespace Browser.App;
public sealed partial class BrowserController
{
    // Téléchargements de la session, du plus récent au plus ancien.
    private readonly List<(string Id, DownloadOperation Op, string TabId)> _downloads = new();
    private bool _downloadsOpen;
    private DateTime _downloadsPosted;
    private int _downloadCounter; // la liste peut raccourcir (réessai) : un identifiant ne resert jamais
    private readonly HashSet<string> _pausedDownloads=new(),_canceledDownloads=new();
    private readonly List<DispatcherQueueTimer> _downloadTimers=new();

    private DownloadOperation? FindDownload(string id) =>
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
            var native = e.DownloadOperation;
            var op = new DownloadOperation(native);
            _downloads.Insert(0, ($"d{++_downloadCounter}", op, tabId));
            var id = _downloads[0].Id;
            native.BytesReceivedChanged += (_, _) => DownloadsChanged(throttle: true);
            native.StateChanged += (_, _) => Changed();
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
                if (_tabs.Tabs.Count > 1 && _pages.CoreOf(tabId) is { CanGoBack: false, Source: "about:blank" })
                    Close(tabId);
                ReleaseClosedTabView(tabId);
                SaveDownloadHistory();
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
                    if(DateTime.UtcNow-finished>Linger(op)){UpdateDock();Stop();}return;
                }
                // Les opérations WebView2 se relisent sur le fil UI, même si le moteur omet une notification.
                var current=(op.State,op.BytesReceived,op.CanResume);
                if(current.State!=last.State||current.CanResume!=last.CanResume||!DownloadPending(id,op))
                {last=current;Changed();}
                else if(current.BytesReceived!=last.BytesReceived)
                {last=current;DownloadsChanged(throttle:true);}
            };
            void Stop(){monitor.Stop();_downloadTimers.Remove(monitor);}
            _downloadTimers.Add(monitor);monitor.Start();
        }
        finally {deferral.Complete();}
    }
    // Onglet fermé pendant son téléchargement : sa vue masquée n'a plus de raison d'exister une fois le fichier terminé.
    private void ReleaseClosedTabView(string tabId)
    {
        if(_tabs.Tabs.All(t=>t.Id!=tabId)&&!_downloads.Any(d=>d.TabId==tabId&&DownloadPending(d.Id,d.Op)))_pages.Remove(tabId);
    }
    // Historique sur disque, comme Chrome : les fichiers terminés restent listés d'une session à l'autre.
    private static string DownloadsFile=>Path.Combine(AppPaths.DataDir,"downloads.json");
    private void LoadDownloadHistory()
    {
        if(AppPaths.Private)return;
        foreach(var saved in JsonFileStore.Load<List<SavedDownload>>(DownloadsFile).Where(s=>s is {Path.Length:>0}).Take(100).Reverse())
            _downloads.Insert(0,($"d{++_downloadCounter}",new DownloadOperation(saved),""));
    }
    private void SaveDownloadHistory()
    {
        if(AppPaths.Private)return;
        var isPrivate=_tabs.Tabs.Where(t=>t.Private).Select(t=>t.Id).ToHashSet();
        JsonFileStore.Save(DownloadsFile,_downloads.Where(d=>!isPrivate.Contains(d.TabId)&&!_canceledDownloads.Contains(d.Id)&&!DownloadPending(d.Id,d.Op)&&d.Op.State!=CoreWebView2DownloadState.InProgress).Take(100).Select(d=>d.Op.Save()).ToList());
    }
    private void ForgetDownload(string id)
    {
        if(FindDownload(id) is not {} op||DownloadPending(id,op))return;
        _downloads.RemoveAll(d=>d.Id==id);_finishedAt.Remove(id);_canceledDownloads.Remove(id);
        SaveDownloadHistory();DownloadsChanged(throttle:false);Refresh();
    }
    private void RetryDownload(string id)
    {
        var download=_downloads.FirstOrDefault(d=>d.Id==id);
        if(download.Op is not {State:CoreWebView2DownloadState.Interrupted,Uri:{Length:>0} uri}||_canceledDownloads.Contains(id)||DownloadPending(id,download.Op))return;
        _finishedAt.Remove(id);_downloads.RemoveAll(d=>d.Id==id);SaveDownloadHistory();
        // L'onglet d'origine porte la session du site ; un fichier relancé ne remplace pas sa page.
        if(_pages.CoreOf(download.TabId) is {} core&&_tabs.Tabs.Any(t=>t.Id==download.TabId))core.Navigate(uri);else Open(uri);
        DownloadsChanged(throttle:false);
    }
    private bool DownloadPaused(string id,DownloadOperation op)=>!_canceledDownloads.Contains(id)&&op.State!=CoreWebView2DownloadState.Completed&&(_pausedDownloads.Contains(id)||op.InterruptReason==CoreWebView2DownloadInterruptReason.UserPaused);
    private bool DownloadPending(string id,DownloadOperation op)=>!_canceledDownloads.Contains(id)&&(op.State==CoreWebView2DownloadState.InProgress||DownloadPaused(id,op)||op.State==CoreWebView2DownloadState.Interrupted&&op.CanResume);
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
                ReleaseClosedTabView(source);
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
        if (_downloadsOpen) _popup.Post(DownloadsJson(_downloads));
        UpdateDock();
    }

    // Encart en bas à droite : les téléchargements en cours, et ceux qui viennent de finir pendant quelques secondes.
    private static readonly TimeSpan DockLinger = TimeSpan.FromSeconds(6);
    // Un échec reste plus longtemps : il demande une décision (réessayer), là où un fichier terminé se lit d'un coup d'œil.
    private TimeSpan Linger(DownloadOperation op) => op.State==CoreWebView2DownloadState.Interrupted?TimeSpan.FromSeconds(30):DockLinger;
    private readonly Dictionary<string, DateTime> _finishedAt = new();
    private bool _dockShown;
    private bool _updateDismissed; // « Plus tard » : l'encart ne reparle plus de la mise à jour, les réglages si
}
