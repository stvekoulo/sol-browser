using System.Runtime.InteropServices;
using System.Text.Json;
using Browser.Core;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;
using Windows.Storage.Pickers;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Browser.App;

/// <summary>Capture d'écran d'une page : une zone, la partie visible ou la page entière, en PNG ou en PDF.</summary>
public sealed class CaptureService(Window window, Action<string>? post = null, Func<CoreWebView2, Task>? restoreViewport = null)
{
    // Au-delà, le moteur ne sait plus produire une seule image ; une page plus longue est coupée (le PDF, lui, n'a pas de limite).
    private const int MaxImageHeight = 16000;
    private bool _running;
    private bool _cancelled;
    public bool Busy => _running;
    private TaskCompletionSource<BitmapBounds?>? _selection;
    private TaskCompletionSource<bool>? _decision;
    public void Select(BitmapBounds bounds) => _selection?.TrySetResult(bounds);
    /// <summary>L'aperçu est accepté : reste à choisir où l'enregistrer.</summary>
    public void Save() => _decision?.TrySetResult(true);
    public void Cancel() { _cancelled = true; _selection?.TrySetResult(null); _decision?.TrySetResult(false); }
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
           sx: scrollX, sy: scrollY, dpr: devicePixelRatio })
        """;

    /// <summary>La page seule, rendue par le moteur.</summary>
    /// <param name="mode">"region", "visible" ou "full".</param>
    public Task RunAsync(CoreWebView2 core, string mode, string title) => GuardAsync(async () =>
    {
        JsonElement? region = null;
        if (mode == "region")
        {
            region = await EvalAsync(core, SelectRegion);
            if (region is not { ValueKind: JsonValueKind.Object }) return; // sélection annulée
        }
        var (png, dpr) = await ScreenshotAsync(core, mode, region);
        // Page entière en PDF : une vraie page PDF, texte sélectionnable, sans limite de hauteur.
        await ReviewAsync(png, dpr, title, mode == "full" ? async path => await core.PrintToPdfAsync(path, null) : null);
    });

    /// <summary>
    /// Ce que la fenêtre affiche, tel que Windows le compose : interface, pages et coques des appareils simulés.
    /// Ni le bureau ni les autres fenêtres n'y figurent, et aucun sélecteur de partage ne s'ouvre.
    /// </summary>
    /// <param name="area">Partie de la fenêtre à garder (null : toute la fenêtre). Une zone tracée l'est toujours sur la fenêtre entière.</param>
    public Task RunWindowAsync(Windows.Foundation.Rect? area, string mode, string title) => GuardAsync(async () =>
    {
        await Task.Delay(150); // les contrôles de capture viennent d'être masqués : le temps que l'écran le montre
        var scale = window.Content.XamlRoot.RasterizationScale;
        var (pixels, width, height) = await GrabAsync();
        if (_cancelled) return;
        var bounds = new BitmapBounds { Width = width, Height = height };
        if (mode == "region")
        {
            _selection = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var whole = await EncodeAsync(pixels, width, height, bounds, false);
            post?.Invoke(JsonSerializer.Serialize(new { @event = "capture-preview", data = "data:image/png;base64," + Convert.ToBase64String(whole), width, height }));
            if (await _selection.Task is not { } chosen) return;
            if (chosen.Width < 1 || chosen.Height < 1 || chosen.X >= width || chosen.Y >= height || chosen.Width > width - chosen.X || chosen.Height > height - chosen.Y) throw new InvalidDataException("Zone de capture invalide.");
            bounds = chosen;
        }
        else if (area is { } kept)
        {
            var left = (uint)Math.Clamp(Math.Round(kept.X * scale), 0, width - 1);
            var top = (uint)Math.Clamp(Math.Round(kept.Y * scale), 0, height - 1);
            bounds = new BitmapBounds
            {
                X = left, Y = top,
                Width = (uint)Math.Clamp(Math.Round(kept.Width * scale), 1, width - left),
                Height = (uint)Math.Clamp(Math.Round(kept.Height * scale), 1, height - top),
            };
        }
        await ReviewAsync(await EncodeAsync(pixels, width, height, bounds, false), scale, title);
    });

    private async Task GuardAsync(Func<Task> capture)
    {
        if (_running) return;
        _running = true;
        _cancelled = false;
        try { await capture(); Send("done"); }
        // page fermée pendant la capture, ou dossier non inscriptible : rien n'est enregistré
        catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException or COMException or InvalidDataException or ArgumentException)
        { Send("error", e.Message); }
        finally { _selection = null; _decision = null; _running = false; }
    }

    // L'image est montrée avant tout enregistrement ; elle n'est écrite que si l'utilisateur la garde.
    private async Task ReviewAsync(byte[] png, double pixelsPerDip, string title, Func<string, Task>? pdfPage = null)
    {
        while (!_cancelled)
        {
            _decision = new(TaskCreationOptions.RunContinuationsAsynchronously);
            post?.Invoke(JsonSerializer.Serialize(new { @event = "capture-review", data = "data:image/png;base64," + Convert.ToBase64String(png) }));
            if (!await _decision.Task) return;
            var paths = await ChoosePathsAsync(title);
            if (_cancelled) return;
            if (paths.Count == 0) continue; // dialogue d'enregistrement annulé : l'aperçu reste affiché
            foreach (var path in paths)
            {
                if (!path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) await File.WriteAllBytesAsync(path, png);
                else if (pdfPage is not null) await pdfPage(path);
                // 96 pixels CSS par pouce, 72 points PDF par pouce.
                else await File.WriteAllBytesAsync(path, PdfImage.Wrap(await JpegAsync(png), pixelsPerDip * 96 / 72));
            }
            return;
        }
    }

    // PW_CLIENTONLY | PW_RENDERFULLCONTENT : le contenu de la fenêtre, vues web comprises, sans son cadre.
    private async Task<(byte[] Pixels, uint Width, uint Height)> GrabAsync()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        if (!GetClientRect(hwnd, out var client) || client.Right < 1 || client.Bottom < 1) throw new InvalidOperationException("Fenêtre indisponible.");
        return await Task.Run(() =>
        {
            var info = new BitmapInfoHeader { Size = 40, Width = client.Right, Height = -client.Bottom, Planes = 1, BitCount = 32 };
            var dc = CreateCompatibleDC(0);
            var bitmap = CreateDIBSection(dc, ref info, 0, out var bits, 0, 0);
            var previous = SelectObject(dc, bitmap);
            try
            {
                if (bitmap == 0 || !PrintWindow(hwnd, dc, 3)) throw new InvalidOperationException("Fenêtre indisponible.");
                var pixels = new byte[client.Right * client.Bottom * 4];
                Marshal.Copy(bits, pixels, 0, pixels.Length);
                for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255; // GDI ne renseigne pas l'opacité
                return (pixels, (uint)client.Right, (uint)client.Bottom);
            }
            finally { SelectObject(dc, previous); DeleteObject(bitmap); DeleteDC(dc); }
        });
    }

    private static async Task<byte[]> JpegAsync(byte[] png)
    {
        using var input = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(input.GetOutputStreamAt(0))) { writer.WriteBytes(png); await writer.StoreAsync(); }
        var decoder = await BitmapDecoder.CreateAsync(input);
        var pixels = (await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage)).DetachPixelData();
        return await EncodeAsync(pixels, decoder.PixelWidth, decoder.PixelHeight, new BitmapBounds { Width = decoder.PixelWidth, Height = decoder.PixelHeight }, true);
    }

    private static async Task<byte[]> EncodeAsync(byte[] pixels, uint width, uint height, BitmapBounds bounds, bool jpeg)
    {
        using var output = new InMemoryRandomAccessStream();
        var encoder = jpeg
            ? await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, output, [new("ImageQuality", new BitmapTypedValue(0.95f, Windows.Foundation.PropertyType.Single))])
            : await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, width, height, 96, 96, pixels);
        encoder.BitmapTransform.Bounds = bounds;
        await encoder.FlushAsync();
        using var reader = new DataReader(output.GetInputStreamAt(0)); await reader.LoadAsync((uint)output.Size);
        var result = new byte[(int)output.Size]; reader.ReadBytes(result); return result;
    }

    private async Task<(byte[] Image, double Dpr)> ScreenshotAsync(CoreWebView2 core, string mode, JsonElement? region)
    {
        // Les bandes de flou du navigateur ne font pas partie de la page.
        const string blurHost = "window.__navBlurHost && (window.__navBlurHost.style.display = '{0}')";
        await EvalAsync(core, string.Format(blurHost, "none"));
        JsonElement? previousMetrics = null;
        var source = core.Source;
        try
        {
            var m = (await EvalAsync(core, Metrics))!.Value;
            previousMetrics = m;
            double N(JsonElement e, string name) => e.GetProperty(name).GetDouble();
            var width = mode == "region" ? N(region!.Value, "w") : N(m, "w");
            var height = mode == "region" ? N(region!.Value, "h") : N(m, "h");
            var dpr = N(m, "dpr");
            using var layout = JsonDocument.Parse(await core.CallDevToolsProtocolMethodAsync("Page.getLayoutMetrics", "{}"));
            var zoom = layout.RootElement.GetProperty("cssVisualViewport").GetProperty("zoom").GetDouble();
            // L'image a les pixels de l'écran : un pixel affiché, un pixel enregistré, quel que soit le zoom de la page.
            var maxHeight = Math.Min(MaxImageHeight, 48_000_000 / Math.Max(1, width * dpr)); // plafond mémoire pour les pages longues
            // Partie visible : la vue telle quelle, sans découpe. Sinon, un clip CDP, en pixels indépendants du périphérique
            // (avant le zoom du moteur).
            var parameters = new Dictionary<string, object> { ["format"] = "png", ["captureBeyondViewport"] = mode != "visible" };
            if (mode != "visible") parameters["clip"] = new {
                x = (mode == "full" ? 0 : N(m, "sx") + N(region!.Value, "x")) * zoom,
                y = (mode == "full" ? 0 : N(m, "sy") + N(region!.Value, "y")) * zoom,
                width = Math.Max(1, Math.Floor(width * zoom)), // le moteur arrondit le clip aux pixels entiers
                height = Math.Max(1, Math.Floor(Math.Min(height, maxHeight / dpr) * zoom)), scale = 1,
            };
            var json = await core.CallDevToolsProtocolMethodAsync("Page.captureScreenshot", JsonSerializer.Serialize(parameters));
            using var doc = JsonDocument.Parse(json);
            return (Convert.FromBase64String(doc.RootElement.GetProperty("data").GetString()!), dpr);
        }
        finally
        {
            try
            {
                // Une capture hors écran peut laisser WebView2 avec une surface agrandie, sans changer innerWidth.
                if (mode != "visible")
                {
                    if (restoreViewport is not null) await restoreViewport(core);
                    else await core.CallDevToolsProtocolMethodAsync("Emulation.clearDeviceMetricsOverride", "{}");
                }
            }
            finally
            {
                await EvalAsync(core, string.Format(blurHost, ""));
                if (previousMetrics is { } m && core.Source == source)
                {
                    var scroll = JsonSerializer.Serialize(new { left = m.GetProperty("sx").GetDouble(), top = m.GetProperty("sy").GetDouble(), behavior = "instant" });
                    await EvalAsync(core, $"scrollTo({scroll})");
                }
            }
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

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size; public int Width, Height; public ushort Planes, BitCount;
        public uint Compression, SizeImage; public int XPelsPerMeter, YPelsPerMeter; public uint ClrUsed, ClrImportant;
    }
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool PrintWindow(nint hwnd, nint dc, uint flags);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateDIBSection(nint dc, ref BitmapInfoHeader info, uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint item);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint item);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
}
