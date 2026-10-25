using Browser.Core;
using Microsoft.Web.WebView2.Core;

namespace Browser.App;

// Même contrat pour les téléchargements WebView2 et les vidéos assemblées hors du moteur.
internal sealed class DownloadOperation
{
    private readonly CoreWebView2DownloadOperation? _native;
    private readonly VideoDownload? _video;
    public DownloadOperation(CoreWebView2DownloadOperation native) => _native = native;
    public DownloadOperation(VideoDownload video) => _video = video;
    public string ResultFilePath => _native?.ResultFilePath ?? _video!.ResultFilePath;
    public long BytesReceived => _native?.BytesReceived ?? _video!.BytesReceived;
    public long TotalBytesToReceive => _native?.TotalBytesToReceive ?? _video!.TotalBytesToReceive;
    public CoreWebView2DownloadState State => _native?.State ?? Enum.Parse<CoreWebView2DownloadState>(_video!.State);
    public bool CanResume => _native?.CanResume ?? _video!.CanResume;
    public string? Phase => _video?.Phase;
    public CoreWebView2DownloadInterruptReason InterruptReason => _native?.InterruptReason ?? (_video!.Paused
        ? CoreWebView2DownloadInterruptReason.UserPaused : _video.Canceled ? CoreWebView2DownloadInterruptReason.UserCanceled
        : State == CoreWebView2DownloadState.Interrupted ? CoreWebView2DownloadInterruptReason.NetworkFailed : CoreWebView2DownloadInterruptReason.None);
    public void Pause() { if (_native is not null) _native.Pause(); else _video!.Pause(); }
    public void Resume() { if (_native is not null) _native.Resume(); else _video!.Resume(); }
    public void Cancel() { if (_native is not null) _native.Cancel(); else _video!.Cancel(); }
}
