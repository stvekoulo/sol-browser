using System.Text.Json;
using Browser.Core;
using Microsoft.Web.WebView2.Core;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Browser.App;

/// <summary>
/// Couleur du fond de l'interface (barre du haut, colonne de la carte latérale) : celle que la page montre réellement à
/// ses bords, là où elle touche l'interface. La couleur que le site déclare ne suffit pas : un dégradé, une image de
/// fond ou un calque la recouvrent souvent, et la barre ne raccorde alors pas avec la page.
/// Deux filets d'un pixel sont lus dans la page affichée (son bord haut, son bord gauche), et seulement quand quelque
/// chose a pu changer : nouvelle page, défilement, couleur déclarée différente. Une page immobile n'est pas relue.
/// </summary>
public sealed partial class UiBridge
{
    // Par onglet : la page lue, où en était son défilement, la couleur qu'elle déclarait, la couleur de ses bords, et
    // jusqu'à quand elle finit de se peindre (elle est alors relue à chaque passage). Revenir sur un onglet retrouve
    // sa couleur aussitôt.
    private readonly Dictionary<string, (string Source, int Scroll, string Declared, string? Color, DateTime Settled)> _edges = new();
    private Task<string?>? _edgeRead;

    /// <returns>La couleur des bords, « rgb(r, g, b) », ou null si elle n'a pas pu être lue ou si les bords sont bigarrés.</returns>
    private async Task<string?> EdgeColorAsync(CoreWebView2 core, string id, string source, string declared, JsonElement probe)
    {
        double Number(string name) => probe.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : 0;
        double width = Number("vw"), height = Number("vh"), scrollX = Number("sx"), scrollY = Number("sy");
        // Appareil simulé : la page n'occupe pas la zone, ses bords ne touchent pas l'interface.
        if (width < 50 || height < 50 || pages?.DeviceOf(id) is not null) return null;
        var scroll = (int)Math.Round(scrollY / 24);
        var now = DateTime.UtcNow;
        if (!_edges.TryGetValue(id, out var known) || known.Source != source) known = (source, scroll, declared, null, now.AddSeconds(6));
        else if (known.Scroll == scroll && known.Declared == declared && now > known.Settled) return known.Color;
        // Une lecture attend une image de la page : fenêtre réduite, elle ne vient pas. Jamais deux lectures à la fois.
        if (_edgeRead is { IsCompleted: false }) return known.Color;
        _edgeRead = ReadEdgeAsync(core, scrollX, scrollY, width, height);
        string? read = null;
        try { read = await _edgeRead.WaitAsync(TimeSpan.FromSeconds(2)); }
        catch (TimeoutException) { }
        // Bords bigarrés (une image, une animation qui passe) : la dernière couleur sûre de cette page reste en place,
        // plutôt que de faire alterner l'interface entre deux teintes.
        if (_edges.Count > 80) _edges.Clear(); // onglets fermés depuis longtemps
        _edges[id] = (source, scroll, declared, read ?? known.Color, known.Settled);
        return read ?? known.Color;
    }

    private static async Task<string?> ReadEdgeAsync(CoreWebView2 core, double scrollX, double scrollY, double width, double height)
    {
        try
        {
            using var layout = JsonDocument.Parse(await core.CallDevToolsProtocolMethodAsync("Page.getLayoutMetrics", "{}"));
            var zoom = layout.RootElement.GetProperty("cssVisualViewport").GetProperty("zoom").GetDouble();
            // Un peu à l'intérieur de la page : un liseré d'un ou deux pixels à son bord ne donne pas sa couleur.
            var top = await StripAsync(core, scrollX * zoom, (scrollY + 10) * zoom, width * zoom, 1);
            var left = await StripAsync(core, (scrollX + 2) * zoom, scrollY * zoom, 1, height * zoom);
            var samples = new List<(byte R, byte G, byte B)>();
            // Le bord haut compte le plus, c'est lui que la barre prolonge : ses deux bouts, puis le coin où la colonne
            // de la carte le rejoint. Le milieu d'une page porte souvent un reflet, un titre ou une image ; plus bas,
            // son bord gauche change de couleur avec ses sections.
            void Take(byte[] pixels, double from, double to)
            {
                var count = pixels.Length / 4;
                for (var i = (int)(count * from); i < (int)(count * to); i += Math.Max(1, count / 400))
                    samples.Add((pixels[i * 4 + 2], pixels[i * 4 + 1], pixels[i * 4])); // octets : bleu, vert, rouge, alpha
            }
            Take(top, 0, 0.12); Take(top, 0.88, 1); Take(left, 0, 0.08);
            return AmbientColor.Edge(samples) is var (r, g, b) ? $"rgb({r}, {g}, {b})" : null;
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or JsonException or KeyNotFoundException or FormatException
            or System.Runtime.InteropServices.COMException) { return null; } // vue fermée ou page remplacée pendant la lecture
    }

    // Un filet de la page affichée, en pixels « bleu, vert, rouge, alpha ». Les mesures sont celles du protocole du
    // moteur : indépendantes du périphérique, zoom de la page compris.
    private static async Task<byte[]> StripAsync(CoreWebView2 core, double x, double y, double width, double height)
    {
        var json = await core.CallDevToolsProtocolMethodAsync("Page.captureScreenshot", JsonSerializer.Serialize(new
        {
            format = "png", captureBeyondViewport = false,
            clip = new { x, y, width = Math.Max(1, Math.Floor(width)), height = Math.Max(1, Math.Floor(height)), scale = 1 },
        }));
        using var doc = JsonDocument.Parse(json);
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(Convert.FromBase64String(doc.RootElement.GetProperty("data").GetString()!));
            await writer.StoreAsync();
        }
        var decoder = await BitmapDecoder.CreateAsync(stream);
        return (await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, new BitmapTransform(),
            ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage)).DetachPixelData();
    }
}
