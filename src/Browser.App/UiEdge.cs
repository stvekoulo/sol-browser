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
/// La page affichée est photographiée telle quelle et deux filets d'un pixel y sont lus (son bord haut, son bord
/// gauche). Seulement quand quelque chose a pu changer et que la page est au repos : nouvelle page, fin d'un
/// défilement, couleur déclarée différente. Jamais pendant un défilement, et une page immobile n'est pas relue.
/// </summary>
/// <remarks>
/// La photo est celle de toute la zone visible, sans découpe : demander au moteur une zone découpée (« clip ») lui fait
/// rendre la page à part, et la vue reste vide le temps de deux ou trois images — un éclair à chaque lecture.
/// </remarks>
public sealed partial class UiBridge
{
    // Par onglet : la page lue, où en était son défilement, la couleur qu'elle déclarait, la couleur de ses bords,
    // jusqu'à quand elle finit de se peindre (elle est alors relue à chaque passage), et s'il reste à la relire une
    // fois le défilement arrêté. Revenir sur un onglet retrouve sa couleur aussitôt.
    private readonly Dictionary<string, (string Source, int Scroll, string Declared, string? Color, DateTime Settled, bool Moved)> _edges = new();
    private Task<string?>? _edgeRead;

    /// <returns>La couleur des bords, « rgb(r, g, b) », ou null si elle n'a pas pu être lue ou si les bords sont bigarrés.</returns>
    private async Task<string?> EdgeColorAsync(CoreWebView2 core, string id, string source, string declared, JsonElement probe)
    {
        double Number(string name) => probe.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : 0;
        double width = Number("vw"), height = Number("vh");
        // Appareil simulé : la page n'occupe pas la zone, ses bords ne touchent pas l'interface.
        if (width < 50 || height < 50 || pages?.DeviceOf(id) is not null) return null;
        var scroll = (int)Math.Round(Number("sy") / 24);
        var now = DateTime.UtcNow;
        if (!_edges.TryGetValue(id, out var known) || known.Source != source) known = (source, scroll, declared, null, now.AddSeconds(4), false);
        else if (known.Scroll != scroll)
        {
            // La page défile : pas de photo maintenant. Tant que sa couleur déclarée n'a pas changé, celle de ses
            // bords reste la plus probable ; sinon la page a changé de section, et sa couleur déclarée fait foi
            // jusqu'à l'arrêt.
            _edges[id] = known with { Scroll = scroll, Moved = true };
            return known.Declared == declared ? known.Color : null;
        }
        else if (!known.Moved && known.Declared == declared && now > known.Settled) return known.Color;
        // Une photo attend une image de la page : fenêtre réduite, elle ne vient pas. Jamais deux lectures à la fois.
        if (_edgeRead is { IsCompleted: false }) return known.Declared == declared ? known.Color : null;
        _edgeRead = ReadEdgeAsync(core, width, height);
        string? read = null;
        try { read = await _edgeRead.WaitAsync(TimeSpan.FromSeconds(2)); }
        catch (TimeoutException) { }
        // Bords bigarrés (une image, une animation qui passe) : la dernière couleur sûre de cette section reste en
        // place, plutôt que de faire alterner l'interface entre deux teintes.
        var color = read ?? (known.Declared == declared ? known.Color : null);
        // Deux photos de la même page ne donnent pas la même couleur à l'unité près : un écart infime ne vaut pas de
        // redessiner toute l'interface.
        if (color is not null && known.Color is not null && Rgb(color) is { } now3 && Rgb(known.Color) is { } before3 && AmbientColor.Distance(now3, before3) <= 8)
            color = known.Color;
        if (_edges.Count > 80) _edges.Clear(); // onglets fermés depuis longtemps
        _edges[id] = (source, scroll, declared, color, known.Settled, false);
        return color;
    }

    private static async Task<string?> ReadEdgeAsync(CoreWebView2 core, double width, double height)
    {
        try
        {
            var json = await core.CallDevToolsProtocolMethodAsync("Page.captureScreenshot", """{"format":"jpeg","quality":92,"optimizeForSpeed":true}""");
            using var doc = JsonDocument.Parse(json);
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(Convert.FromBase64String(doc.RootElement.GetProperty("data").GetString()!));
                await writer.StoreAsync();
            }
            var decoder = await BitmapDecoder.CreateAsync(stream);
            if (decoder.PixelWidth < 50 || decoder.PixelHeight < 50) return null;
            // Un peu à l'intérieur de la page (10 et 2 points) : un liseré à son bord ne donne pas sa couleur.
            var row = Math.Min(decoder.PixelHeight - 1, (uint)Math.Round(10 * decoder.PixelHeight / height));
            var column = Math.Min(decoder.PixelWidth - 1, (uint)Math.Round(2 * decoder.PixelWidth / width));
            var top = await StripAsync(decoder, new BitmapBounds { X = 0, Y = row, Width = decoder.PixelWidth, Height = 1 });
            var left = await StripAsync(decoder, new BitmapBounds { X = column, Y = 0, Width = 1, Height = decoder.PixelHeight });
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

    private static (byte R, byte G, byte B)? Rgb(string color) =>
        System.Text.RegularExpressions.Regex.Match(color, @"rgb\((\d+), (\d+), (\d+)\)") is { Success: true } m
            ? (byte.Parse(m.Groups[1].Value), byte.Parse(m.Groups[2].Value), byte.Parse(m.Groups[3].Value)) : null;

    // Un filet de la photo, en pixels « bleu, vert, rouge, alpha ».
    private static async Task<byte[]> StripAsync(BitmapDecoder decoder, BitmapBounds bounds) =>
        (await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, new BitmapTransform { Bounds = bounds },
            ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage)).DetachPixelData();
}
