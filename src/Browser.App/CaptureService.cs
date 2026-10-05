using System.Text.Json;
using Browser.Core;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;
using Windows.Storage.Pickers;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Browser.App;

/// <summary>Capture d'écran d'une page : une zone, la partie visible ou la page entière, en PNG ou en PDF.</summary>
public sealed class CaptureService(Window window, Action<string>? post = null)
{
    // Au-delà, le moteur ne sait plus produire une seule image ; une page plus longue est coupée (le PDF, lui, n'a pas de limite).
    private const int MaxImageHeight = 16000;
    private bool _running;
    public bool Busy => _running;
    private TaskCompletionSource<BitmapBounds?>? _selection;
    public void Select(BitmapBounds bounds) => _selection?.TrySetResult(bounds);
    public void CancelSelection() => _selection?.TrySetResult(null);
    private void Send(string kind, string? data = null) => post?.Invoke(JsonSerializer.Serialize(new { @event = "capture-state", kind, data }));

    // Sélection d'une zone à la souris, dessinée dans la page. Renvoie { x, y, w, h } dans la fenêtre, ou null (Échap).
    // API DOM uniquement, pour passer sur les sites qui imposent Trusted Types.
    private const string SelectRegion = """
        new Promise(resolve => {
          const veil = document.createElement('div'), box = document.createElement('div');
          veil.style.cssText = 'position:fixed;inset:0;z-index:2147483647;cursor:crosshair;background:rgba(0,0,0,.25)';
          box.style.cssText = 'position:fixed;display:none;border:1.5px solid #fff;box-shadow:0 0 0 99999px rgba(0,0,0,.35)';
          veil.appendChild(box);
          let x0, y0, rect = null;
          const done = value => {
            veil.remove();
            removeEventListener('keydown', onKey, true);
            // Deux images d'attente : le voile a disparu de l'écran avant la capture.
            requestAnimationFrame(() => requestAnimationFrame(() => resolve(value)));
          };
          const onKey = e => { if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); done(null); } };
          const update = e => {
            rect = { x: Math.min(x0, e.clientX), y: Math.min(y0, e.clientY), w: Math.abs(e.clientX - x0), h: Math.abs(e.clientY - y0) };
            box.style.left = rect.x + 'px'; box.style.top = rect.y + 'px';
            box.style.width = rect.w + 'px'; box.style.height = rect.h + 'px';
          };
          veil.addEventListener('mousedown', e => {
            e.preventDefault();
            x0 = e.clientX; y0 = e.clientY;
            veil.style.background = 'none'; box.style.display = 'block';
            update(e);
          });
          veil.addEventListener('mousemove', e => { if (x0 !== undefined) update(e); });
          veil.addEventListener('mouseup', () => { if (x0 !== undefined) done(rect && rect.w > 4 && rect.h > 4 ? rect : null); });
          addEventListener('keydown', onKey, true);
          document.documentElement.appendChild(veil);
        })
        """;

    private const string Metrics = """
        ({ w: Math.max(document.documentElement.scrollWidth, document.body ? document.body.scrollWidth : 0),
           h: Math.max(document.documentElement.scrollHeight, document.body ? document.body.scrollHeight : 0),
           sx: scrollX, sy: scrollY, vw: innerWidth, vh: innerHeight, dpr: devicePixelRatio })
        """;

    /// <param name="mode">"region", "visible" ou "full".</param>
    public async Task RunAsync(CoreWebView2 core, string mode, string title)
    {
        if (_running) return;
        _running = true;
        var failed = false;
        try
        {
            JsonElement? region = null;
            if (mode == "region")
            {
                region = await EvalAsync(core, SelectRegion);
                if (region is not { ValueKind: JsonValueKind.Object }) return; // sélection annulée
            }

            foreach (var path in await ChoosePathsAsync(title))
            {
                var pdf = path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
                if (pdf && mode == "full")
                {
                    await core.PrintToPdfAsync(path, null); // vraie page PDF, texte sélectionnable, sans limite de hauteur
                    continue;
                }
                var (image, dpr) = await ScreenshotAsync(core, mode, region, pdf ? "jpeg" : "png");
                // 96 pixels CSS par pouce, 72 points PDF par pouce.
                await File.WriteAllBytesAsync(path, pdf ? PdfImage.Wrap(image, dpr * 96 / 72) : image);
            }
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or InvalidDataException)
        {
            // page fermée pendant la capture, ou dossier non inscriptible : rien n'est enregistré
            Send("error", e.Message);
            failed = true;
        }
        finally { _running = false; if (!failed) Send("done"); }
    }

    /// <summary>Assemble les surfaces visibles de Sol, sans filmer le bureau ni ouvrir un sélecteur de partage Windows.</summary>
    public async Task RunWindowAsync(FrameworkElement root, string mode)
    {
        if (_running) return;
        _running = true;
        try
        {
            if (root.ActualWidth < 1 || root.ActualHeight < 1) throw new InvalidOperationException("Fenêtre indisponible.");
            var scale = Math.Min(3840 / root.ActualWidth, 2160 / root.ActualHeight);
            var width = (uint)Math.Max(1, Math.Round(root.ActualWidth * scale));
            var height = (uint)Math.Max(1, Math.Round(root.ActualHeight * scale));
            var pixels = new byte[checked((int)(width * height * 4))];
            var background = (root as Panel)?.Background as SolidColorBrush;
            var color = background?.Color ?? UiBridge.InitialBackground;
            for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = color.B; pixels[i+1] = color.G; pixels[i+2] = color.R; pixels[i+3] = 255; }
            foreach (var view in Views(root))
            {
                if (view.Name == "CaptureControls" || view.CoreWebView2 is not { } core || view.ActualWidth < 1 || view.ActualHeight < 1) continue;
                var origin = view.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
                var left = (int)Math.Round(origin.X * scale); var top = (int)Math.Round(origin.Y * scale);
                if (left >= width || top >= height) continue;
                var targetWidth = (uint)Math.Max(1, Math.Round(view.ActualWidth * scale));
                var targetHeight = (uint)Math.Max(1, Math.Round(view.ActualHeight * scale));
                // Le moteur rend chaque surface à la résolution finale : texte et icônes restent nets.
                var metrics = (await EvalAsync(core, "({w:innerWidth,h:innerHeight,dpr:devicePixelRatio,sx:scrollX,sy:scrollY})"))!.Value;
                var w = metrics.GetProperty("w").GetDouble(); var h = metrics.GetProperty("h").GetDouble();
                var result = await core.CallDevToolsProtocolMethodAsync("Page.captureScreenshot", JsonSerializer.Serialize(new { format = "png", captureBeyondViewport = false, clip = new { x = metrics.GetProperty("sx").GetDouble(), y = metrics.GetProperty("sy").GetDouble(), width = w, height = h, scale = targetWidth / (w * metrics.GetProperty("dpr").GetDouble()) } }));
                using var json = JsonDocument.Parse(result);
                using var image = new InMemoryRandomAccessStream();
                using (var writer = new DataWriter(image.GetOutputStreamAt(0))) { writer.WriteBytes(Convert.FromBase64String(json.RootElement.GetProperty("data").GetString()!)); await writer.StoreAsync(); }
                var decoder = await BitmapDecoder.CreateAsync(image);
                var data = (await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, new BitmapTransform { ScaledWidth = targetWidth, ScaledHeight = targetHeight }, ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage)).DetachPixelData();
                for (var y = 0; y < targetHeight; y++) for (var x = 0; x < targetWidth; x++)
                {
                    var dx = left + x; var dy = top + y;
                    if (dx < 0 || dy < 0 || dx >= width || dy >= height) continue;
                    var source = (int)((y * targetWidth + x) * 4); var dest = (int)((dy * width + dx) * 4); var alpha = data[source+3];
                    for (var channel = 0; channel < 3; channel++) pixels[dest+channel] = (byte)Math.Min(255, data[source+channel] + (pixels[dest+channel] * (255-alpha) + 127) / 255);
                }
            }
            var region = new BitmapBounds { Width = width, Height = height };
            if (mode == "region")
            {
                _selection = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var png = await EncodeAsync(pixels, width, height, region, false);
                post?.Invoke(JsonSerializer.Serialize(new { @event = "capture-preview", data = "data:image/png;base64," + Convert.ToBase64String(png), width, height }));
                var chosen = await _selection.Task;
                if (chosen is null) { Send("done"); return; }
                region = chosen.Value;
                if (region.Width < 1 || region.Height < 1 || region.X >= width || region.Y >= height || region.Width > width-region.X || region.Height > height-region.Y) throw new InvalidDataException("Zone de capture invalide.");
            }
            Send("saving");
            foreach (var path in await ChoosePathsAsync(Language.T("du navigateur", "of browser")))
            {
                var pdf = path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
                var image = await EncodeAsync(pixels, width, height, region, pdf);
                await File.WriteAllBytesAsync(path, pdf ? PdfImage.Wrap(image, scale * 96 / 72) : image);
            }
            Send("done");
        }
        catch (Exception error) when (error is InvalidOperationException or IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or InvalidDataException)
        { Send("error", error.Message); }
        finally { _selection = null; _running = false; }
    }

    private static IEnumerable<WebView2> Views(DependencyObject parent)
    {
        if (parent is UIElement { Visibility: Visibility.Collapsed }) yield break;
        if (parent is WebView2 view) { yield return view; yield break; }
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in Views(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }

    private static async Task<byte[]> EncodeAsync(byte[] pixels, uint width, uint height, BitmapBounds bounds, bool jpeg)
    {
        using var output = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(jpeg ? BitmapEncoder.JpegEncoderId : BitmapEncoder.PngEncoderId, output);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, width, height, 96, 96, pixels);
        encoder.BitmapTransform.Bounds = bounds;
        await encoder.FlushAsync();
        using var reader = new DataReader(output.GetInputStreamAt(0)); await reader.LoadAsync((uint)output.Size);
        var result = new byte[(int)output.Size]; reader.ReadBytes(result); return result;
    }

    private static async Task<(byte[] Image, double Dpr)> ScreenshotAsync(CoreWebView2 core, string mode, JsonElement? region, string format)
    {
        // Les bandes de flou du navigateur ne font pas partie de la page.
        const string blurHost = "window.__navBlurHost && (window.__navBlurHost.style.display = '{0}')";
        await EvalAsync(core, string.Format(blurHost, "none"));
        try
        {
            var m = (await EvalAsync(core, Metrics))!.Value;
            double N(JsonElement e, string name) => e.GetProperty(name).GetDouble();
            var width = mode == "region" ? N(region!.Value, "w") : N(m, mode == "full" ? "w" : "vw");
            var height = mode == "region" ? N(region!.Value, "h") : N(m, mode == "full" ? "h" : "vh");
            var dpr = N(m, "dpr");
            var scale = Math.Max(1, 3840 / (width * dpr)); // rendu du moteur en 4K, sans redimensionner le PNG après capture
            var physicalWidth = width * dpr * scale;
            var maxHeight = Math.Min(MaxImageHeight, 48_000_000 / physicalWidth); // plafond mémoire pour les pages longues
            var clip = new {
                x = mode == "full" ? 0 : N(m, "sx") + (mode == "region" ? N(region!.Value, "x") : 0),
                y = mode == "full" ? 0 : N(m, "sy") + (mode == "region" ? N(region!.Value, "y") : 0),
                width, height = Math.Min(height, maxHeight / (dpr * scale)), scale,
            };
            var parameters = new Dictionary<string, object?> { ["format"] = format, ["captureBeyondViewport"] = mode != "visible" };
            if (format == "jpeg") parameters["quality"] = 100;
            parameters["clip"] = clip;

            var json = await core.CallDevToolsProtocolMethodAsync("Page.captureScreenshot", JsonSerializer.Serialize(parameters));
            using var doc = JsonDocument.Parse(json);
            return (Convert.FromBase64String(doc.RootElement.GetProperty("data").GetString()!), dpr * scale);
        }
        finally
        {
            await EvalAsync(core, string.Format(blurHost, ""));
        }
    }

    // Demande où enregistrer et sous quel format. Annulé = aucune destination.
    private async Task<IReadOnlyList<string>> ChoosePathsAsync(string title)
    {
        // Diagnostic : avec NAVIGATEUR_CAPTURE_DIR, pas de boîte de dialogue, les deux formats sont écrits dans ce dossier.
        if (Environment.GetEnvironmentVariable("NAVIGATEUR_CAPTURE_DIR") is { Length: > 0 } dir)
            return [Path.Combine(dir, "capture.png"), Path.Combine(dir, "capture.pdf")];

        var name = string.Concat((title.Length > 60 ? title[..60] : title).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? ' ' : c)).Trim();
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            SuggestedFileName = $"Capture {(name.Length > 0 ? name : Language.T("de page", "page"))} {DateTime.Now:yyyy-MM-dd HH.mm.ss}",
        };
        picker.FileTypeChoices.Add(Language.T("Image PNG", "PNG image"), [".png"]);
        picker.FileTypeChoices.Add(Language.T("Document PDF", "PDF document"), [".pdf"]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));
        return await picker.PickSaveFileAsync() is { } file ? [file.Path] : [];
    }

    // Évalue une expression dans la page et attend sa valeur (promesse comprise). Aucun canal de message n'est ouvert pour cela.
    private static async Task<JsonElement?> EvalAsync(CoreWebView2 core, string expression)
    {
        var json = await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate",
            JsonSerializer.Serialize(new { expression, awaitPromise = true, returnByValue = true }));
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("result").TryGetProperty("value", out var value) ? value.Clone() : null;
    }
}
