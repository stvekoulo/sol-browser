using Browser.Core;
using Microsoft.Web.WebView2.Core;

namespace Browser.App;

// Même contrat pour les téléchargements WebView2 et les vidéos assemblées hors du moteur.
internal sealed record SavedDownload(string Path, string? Uri, long Received, long Total, bool Completed);

internal sealed class DownloadOperation
{
    private readonly CoreWebView2DownloadOperation? _native;
    private readonly VideoDownload? _video;
    private readonly SavedDownload? _saved; // téléchargement d'une session précédente : un souvenir, plus rien à piloter
    public DownloadOperation(CoreWebView2DownloadOperation native) => _native = native;
    public DownloadOperation(VideoDownload video) => _video = video;
    public DownloadOperation(SavedDownload saved) => _saved = saved;
    public SavedDownload Save() => new(ResultFilePath, Uri, BytesReceived, TotalBytesToReceive, State == CoreWebView2DownloadState.Completed);
    public string ResultFilePath => _saved?.Path ?? _native?.ResultFilePath ?? _video!.ResultFilePath;
    public long BytesReceived => _saved?.Received ?? _native?.BytesReceived ?? _video!.BytesReceived;
    public long TotalBytesToReceive => _saved?.Total ?? _native?.TotalBytesToReceive ?? _video!.TotalBytesToReceive;
    public CoreWebView2DownloadState State => _saved is { } saved ? (saved.Completed ? CoreWebView2DownloadState.Completed : CoreWebView2DownloadState.Interrupted) : _native?.State ?? Enum.Parse<CoreWebView2DownloadState>(_video!.State);
    public bool CanResume => _saved is null && (_native?.CanResume ?? _video!.CanResume);
    public string? Phase => _video?.Phase;
    public string? Uri => _saved?.Uri ?? _native?.Uri;
    public CoreWebView2DownloadInterruptReason InterruptReason => _saved is not null ? (_saved.Completed ? CoreWebView2DownloadInterruptReason.None : CoreWebView2DownloadInterruptReason.NetworkFailed) : _native?.InterruptReason ?? (_video!.Paused
        ? CoreWebView2DownloadInterruptReason.UserPaused : _video.Canceled ? CoreWebView2DownloadInterruptReason.UserCanceled
        : State == CoreWebView2DownloadState.Interrupted ? CoreWebView2DownloadInterruptReason.NetworkFailed : CoreWebView2DownloadInterruptReason.None);
    public void Pause() { if (_saved is not null) return; if (_native is not null) _native.Pause(); else _video!.Pause(); }
    public void Resume() { if (_saved is not null) return; if (_native is not null) _native.Resume(); else _video!.Resume(); }
    public void Cancel() { if (_saved is not null) return; if (_native is not null) _native.Cancel(); else _video!.Cancel(); }
}
