using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Browser.App;

// Transforme un fichier en ce que ChatGPT sait lire : images réduites, pages de PDF en images, documents en texte.
// Tout se fait localement, avec les composants de Windows.
internal static class ChatAttachmentFiles
{
    public const long MaxFileBytes = 30 * 1024 * 1024;
    public const int MaxTextChars = 200_000, MaxPdfPages = 8;
    private const int MaxImageSide = 2048, KeepImageBytes = 2 * 1024 * 1024;
    private static readonly string[] Images = [".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp"];
    public static readonly string[] Types =
    [
        .. Images, ".pdf", ".docx", ".txt", ".md", ".csv", ".tsv", ".json", ".log", ".xml", ".html", ".htm", ".css", ".js", ".ts", ".tsx",
        ".jsx", ".py", ".cs", ".java", ".c", ".cpp", ".h", ".go", ".rs", ".php", ".rb", ".sh", ".ps1", ".sql", ".yaml", ".yml", ".toml", ".ini",
    ];

    public static async Task<List<(string Name, string Mime, byte[] Bytes, string? Text)>> ReadAsync(string name, byte[] bytes)
    {
        var ext = Path.GetExtension(name).ToLowerInvariant();
        if (bytes.Length == 0 || bytes.Length > MaxFileBytes) throw new InvalidDataException();
        if (Images.Contains(ext))
        {
            var (mime, image) = await ImageAsync(bytes);
            return [(name, mime, image, null)];
        }
        if (ext == ".pdf") return await PdfAsync(name, bytes);
        if (!Types.Contains(ext)) throw new InvalidDataException();
        var text = ext == ".docx" ? Docx(bytes) : Decode(bytes);
        if (text.Length == 0 || text.Contains('\0')) throw new InvalidDataException();
        if (text.Length > MaxTextChars) text = text[..MaxTextChars] + "\n[…truncated]";
        return [(name, "", Encoding.UTF8.GetBytes(text), text)];
    }

    private static string Decode(byte[] bytes)
    {
        // Les anciens fichiers Windows ne sont pas en UTF-8 : ils restent lisibles.
        try { return new UTF8Encoding(false, true).GetString(bytes).TrimStart('﻿'); }
        catch (ArgumentException) { return Encoding.Latin1.GetString(bytes); }
    }

    private static string Docx(byte[] bytes)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var entry = zip.GetEntry("word/document.xml");
        if (entry is null || entry.Length > 64 * 1024 * 1024) throw new InvalidDataException();
        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        return string.Join("\n", document.Descendants(w + "p").Select(p => string.Concat(p.Descendants(w + "t").Select(t => t.Value))));
    }

    private static async Task<List<(string, string, byte[], string?)>> PdfAsync(string name, byte[] bytes)
    {
        using var source = await StreamOf(bytes);
        var pdf = await PdfDocument.LoadFromStreamAsync(source);
        var pages = new List<(string, string, byte[], string?)>();
        for (uint index = 0; index < Math.Min(pdf.PageCount, MaxPdfPages); index++)
        {
            using var page = pdf.GetPage(index);
            using var rendered = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(rendered, new PdfPageRenderOptions { DestinationWidth = 1400 });
            var data = new byte[rendered.Size];
            rendered.Seek(0);
            await rendered.AsStreamForRead().ReadExactlyAsync(data);
            var (mime, image) = await ImageAsync(data);
            pages.Add(($"{name} · p.{index + 1}/{pdf.PageCount}", mime, image, null));
        }
        return pages;
    }

    private static async Task<(string Mime, byte[] Bytes)> ImageAsync(byte[] bytes)
    {
        var original = Sniff(bytes);
        try
        {
            using var source = await StreamOf(bytes);
            var decoder = await BitmapDecoder.CreateAsync(source);
            var side = Math.Max(decoder.PixelWidth, decoder.PixelHeight);
            if (original is not null && side <= MaxImageSide && bytes.Length <= KeepImageBytes) return (original, bytes);
            var scale = Math.Min(1, (double)MaxImageSide / side);
            var transform = new BitmapTransform { ScaledWidth = (uint)Math.Max(1, decoder.PixelWidth * scale), ScaledHeight = (uint)Math.Max(1, decoder.PixelHeight * scale), InterpolationMode = BitmapInterpolationMode.Fant };
            using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, transform, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb);
            using var output = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, output);
            encoder.SetSoftwareBitmap(bitmap);
            await encoder.FlushAsync();
            var data = new byte[output.Size];
            output.Seek(0);
            await output.AsStreamForRead().ReadExactlyAsync(data);
            return ("image/jpeg", data);
        }
        // WebP sans décodeur Windows, par exemple : envoyé tel quel s'il reste d'une taille raisonnable.
        catch (COMException) when (original is not null && bytes.Length <= 5 * 1024 * 1024) { return (original, bytes); }
        catch (COMException) { throw new InvalidDataException(); }
    }

    private static string? Sniff(byte[] b)
    {
        if (b.Length < 12) return null;
        if (b[0] == 137 && b[1] == 80 && b[2] == 78 && b[3] == 71) return "image/png";
        if (b[0] == 255 && b[1] == 216 && b[2] == 255) return "image/jpeg";
        if (b[0] == 71 && b[1] == 73 && b[2] == 70) return "image/gif";
        if (Encoding.ASCII.GetString(b, 0, 4) == "RIFF" && Encoding.ASCII.GetString(b, 8, 4) == "WEBP") return "image/webp";
        return null;
    }

    private static async Task<InMemoryRandomAccessStream> StreamOf(byte[] bytes)
    {
        var stream = new InMemoryRandomAccessStream();
        await stream.AsStreamForWrite().WriteAsync(bytes);
        await stream.AsStreamForWrite().FlushAsync();
        stream.Seek(0);
        return stream;
    }
}
