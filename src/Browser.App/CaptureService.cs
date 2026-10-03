using System.Text.Json;
using Browser.Core;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;
using Windows.Storage.Pickers;

namespace Browser.App;

/// <summary>Capture d'écran d'une page : une zone, la partie visible ou la page entière, en PNG ou en PDF.</summary>
public sealed class CaptureService(Window window)
{
    // Au-delà, le moteur ne sait plus produire une seule image ; une page plus longue est coupée (le PDF, lui, n'a pas de limite).
    private const int MaxImageHeight = 16000;

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

    /// <param name="mode">"region", "visible" ou "full".</param>
    public async Task RunAsync(CoreWebView2 core, string mode, string title)
    {
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
            System.Diagnostics.Debug.WriteLine($"Capture abandonnée : {e.Message}");
        }
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
            object? clip = mode switch
            {
                "region" => new { x = N(region!.Value, "x") + N(m, "sx"), y = N(region.Value, "y") + N(m, "sy"), width = N(region.Value, "w"), height = N(region.Value, "h"), scale = 1 },
                "full" => new { x = 0, y = 0, width = N(m, "w"), height = Math.Min(N(m, "h"), MaxImageHeight), scale = 1 },
                _ => null,
            };
            var parameters = new Dictionary<string, object?> { ["format"] = format, ["captureBeyondViewport"] = mode != "visible" };
            if (format == "jpeg") parameters["quality"] = 92;
            if (clip is not null) parameters["clip"] = clip;

            var json = await core.CallDevToolsProtocolMethodAsync("Page.captureScreenshot", JsonSerializer.Serialize(parameters));
            using var doc = JsonDocument.Parse(json);
            return (Convert.FromBase64String(doc.RootElement.GetProperty("data").GetString()!), N(m, "dpr"));
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
