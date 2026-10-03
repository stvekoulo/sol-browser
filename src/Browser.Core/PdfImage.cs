using System.Globalization;
using System.Text;

namespace Browser.Core;

/// <summary>Enveloppe une image JPEG dans un PDF d'une page, à la taille de l'image. Sert aux captures d'écran.</summary>
public static class PdfImage
{
    /// <param name="jpeg">Image JPEG complète.</param>
    /// <param name="pixelsPerPoint">Pixels de l'image par point PDF (1/72 de pouce) ; fixe la taille de la page.</param>
    public static byte[] Wrap(byte[] jpeg, double pixelsPerPoint)
    {
        var (width, height) = JpegSize(jpeg);
        var w = (width / pixelsPerPoint).ToString("0.##", CultureInfo.InvariantCulture);
        var h = (height / pixelsPerPoint).ToString("0.##", CultureInfo.InvariantCulture);
        var draw = $"q {w} 0 0 {h} 0 0 cm /Im0 Do Q";

        using var pdf = new MemoryStream();
        var offsets = new List<long>();
        void Text(string s) => pdf.Write(Encoding.ASCII.GetBytes(s));
        void Object(string body) { offsets.Add(pdf.Position); Text($"{offsets.Count} 0 obj\n{body}\nendobj\n"); }

        Text("%PDF-1.4\n");
        Object("<< /Type /Catalog /Pages 2 0 R >>");
        Object("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        Object($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {w} {h}] /Resources << /XObject << /Im0 4 0 R >> >> /Contents 5 0 R >>");

        offsets.Add(pdf.Position);
        Text($"4 0 obj\n<< /Type /XObject /Subtype /Image /Width {width} /Height {height} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {jpeg.Length} >>\nstream\n");
        pdf.Write(jpeg);
        Text("\nendstream\nendobj\n");

        Object($"<< /Length {draw.Length} >>\nstream\n{draw}\nendstream");

        var xref = pdf.Position;
        Text($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets) Text($"{offset:D10} 00000 n \n");
        Text($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return pdf.ToArray();
    }

    /// <summary>Lit la taille dans l'en-tête JPEG (segment SOF).</summary>
    public static (int Width, int Height) JpegSize(byte[] jpeg)
    {
        if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8) throw new InvalidDataException("Ce n'est pas une image JPEG.");
        var i = 2;
        while (i + 9 < jpeg.Length)
        {
            if (jpeg[i] != 0xFF) throw new InvalidDataException("En-tête JPEG illisible.");
            var marker = jpeg[i + 1];
            var length = (jpeg[i + 2] << 8) | jpeg[i + 3];
            // SOF0 à SOF15, sauf DHT (C4), JPG (C8) et DAC (CC), qui ne décrivent pas l'image.
            if (marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC))
                return ((jpeg[i + 7] << 8) | jpeg[i + 8], (jpeg[i + 5] << 8) | jpeg[i + 6]);
            i += 2 + length;
        }
        throw new InvalidDataException("Taille de l'image JPEG introuvable.");
    }
}
