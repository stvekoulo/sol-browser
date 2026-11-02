using System.Net;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Net.Http;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Browser.App;

// Image de l'éventail : la meilleure version trouvée par inject/fan.js est récupérée avec la session du site (cookies,
// page d'origine), puis enregistrée dans les téléchargements ou copiée comme image.
public sealed partial class BrowserController
{
    private static readonly HttpClient ImageHttp = new(new SocketsHttpHandler { UseCookies = false, AutomaticDecompression = DecompressionMethods.All })
        { Timeout = TimeSpan.FromSeconds(30) };
    private const int ImageLimit = 64 << 20;

    private async Task<(byte[] Bytes, string Type)?> FetchImageAsync(string tabId, string url)
    {
        if (_pages.CoreOf(tabId) is not { } core || !IsWeb(url)) return null;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            var cookies = await core.CookieManager.GetCookiesAsync(url);
            if (cookies.Count > 0) request.Headers.TryAddWithoutValidation("Cookie", string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}")));
            request.Headers.TryAddWithoutValidation("User-Agent", core.Settings.UserAgent);
            request.Headers.TryAddWithoutValidation("Accept", "image/avif,image/webp,image/apng,image/*,*/*;q=0.8");
            if (IsWeb(core.Source)) request.Headers.Referrer = new Uri(core.Source);
            using var response = await ImageHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            var type = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!response.IsSuccessStatusCode || !type.StartsWith("image/") || response.Content.Headers.ContentLength > ImageLimit) return null;
            var bytes = await response.Content.ReadAsByteArrayAsync();
            return bytes.Length is > 0 and <= ImageLimit ? (bytes, type) : null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or ArgumentException or UriFormatException or System.Runtime.InteropServices.COMException) { return null; }
    }

    private async Task DownloadImageAsync(string tabId, string url)
    {
        if (await FetchImageAsync(tabId, url) is not { } image || _pages.CoreOf(tabId) is not { } core) { ImageFailed(); return; }
        var folder = Environment.GetEnvironmentVariable("NAVIGATEUR_TEST_DOWNLOAD_DIR") is { Length: > 0 } test && Directory.Exists(test) ? test : core.Profile.DefaultDownloadFolderPath;
        var path = UniquePath(folder, ImageFileName(url, image.Type));
        try { Directory.CreateDirectory(folder); await File.WriteAllBytesAsync(path, image.Bytes); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { ImageFailed(); return; }
        // Rangée avec les autres téléchargements : l'encart la montre quelques secondes, l'historique la garde.
        var id = $"d{++_downloadCounter}";
        _downloads.Insert(0, (id, new DownloadOperation(new SavedDownload(path, url, image.Bytes.Length, image.Bytes.Length, true)), tabId));
        _finishedAt[id] = DateTime.UtcNow;
        SaveDownloadHistory(); DownloadsChanged(throttle: false);
        var later = _window.DispatcherQueue.CreateTimer(); later.Interval = DockLinger + TimeSpan.FromMilliseconds(200); later.IsRepeating = false;
        later.Tick += (_, _) => { if (!_closingWindow) UpdateDock(); }; later.Start();
    }

    // Copiée en PNG : toutes les applications collent un PNG, pas toujours un WebP ou un AVIF.
    private async Task CopyImageAsync(string tabId, string url)
    {
        if (await FetchImageAsync(tabId, url) is not { } image) { ImageFailed(); return; }
        try
        {
            using var source = new InMemoryRandomAccessStream();
            await source.WriteAsync(image.Bytes.AsBuffer());
            source.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(source);
            using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            var png = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, png);
            encoder.SetSoftwareBitmap(bitmap);
            await encoder.FlushAsync();
            png.Seek(0);
            var data = new DataPackage();
            data.SetBitmap(RandomAccessStreamReference.CreateFromStream(png));
            Clipboard.SetContent(data);
        }
        // SVG ou format que Windows ne décode pas : le lien reste utile.
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or ArgumentException or InvalidOperationException)
        {
            Copy(url);
            Notice(T("Image", "Image"), T("Windows ne sait pas copier ce format d’image : son lien a été copié.", "Windows cannot copy this image format: its link was copied."));
        }
    }

    private void ImageFailed() => Notice(T("Image", "Image"), T("Le site n’a pas fourni cette image. Ouvrez-la dans un onglet pour l’enregistrer.", "The site did not provide this image. Open it in a tab to save it."));

    internal static string ImageFileName(string url, string type)
    {
        var extension = type switch { "image/jpeg" => ".jpg", "image/svg+xml" => ".svg", "image/x-icon" or "image/vnd.microsoft.icon" => ".ico", _ => "." + type[6..].Split('+')[0] };
        var name = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? Path.GetFileNameWithoutExtension(Uri.UnescapeDataString(uri.AbsolutePath.TrimEnd('/'))) : "";
        name = string.Concat(name.Where(c => !Path.GetInvalidFileNameChars().Contains(c))).Trim(' ', '.');
        if (name.Length > 80) name = name[..80];
        return (name.Length > 0 ? name : "image") + extension;
    }

    private static string UniquePath(string folder, string file)
    {
        var path = Path.Combine(folder, file);
        for (var n = 2; File.Exists(path); n++) path = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(file)} ({n}){Path.GetExtension(file)}");
        return path;
    }
}
