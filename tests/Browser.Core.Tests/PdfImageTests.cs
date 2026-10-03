using System.Text;
using Browser.Core;

public class PdfImageTests
{
    // En-tête JPEG minimal : SOI, un segment APP0 de 4 octets, puis SOF0 annonçant 300 × 200.
    private static readonly byte[] Jpeg =
    [
        0xFF, 0xD8,
        0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00,
        0xFF, 0xC0, 0x00, 0x11, 0x08, 0x00, 0xC8, 0x01, 0x2C, 0x03, 0x01, 0x22, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01,
        0xFF, 0xD9,
    ];

    [Fact]
    public void JpegSize_reads_width_and_height_from_the_frame_header() =>
        Assert.Equal((300, 200), PdfImage.JpegSize(Jpeg));

    [Fact]
    public void JpegSize_rejects_other_data() =>
        Assert.Throws<InvalidDataException>(() => PdfImage.JpegSize([0x89, 0x50, 0x4E, 0x47]));

    [Fact]
    public void Wrap_produces_a_one_page_pdf_sized_to_the_image()
    {
        var pdf = PdfImage.Wrap(Jpeg, pixelsPerPoint: 2);
        var text = Encoding.Latin1.GetString(pdf);
        Assert.StartsWith("%PDF-1.4", text);
        Assert.Contains("/MediaBox [0 0 150 100]", text);
        Assert.Contains("/Width 300 /Height 200", text);
        Assert.Contains($"/Filter /DCTDecode /Length {Jpeg.Length}", text);
        Assert.EndsWith("%%EOF\n", text);
    }

    [Fact]
    public void Wrap_writes_a_cross_reference_table_that_points_at_each_object()
    {
        var text = Encoding.Latin1.GetString(PdfImage.Wrap(Jpeg, 1));
        var xref = int.Parse(text[(text.LastIndexOf("startxref\n") + 10)..].Split('\n')[0]);
        Assert.StartsWith("xref\n0 6\n", text[xref..]);
        var entries = text[xref..].Split('\n').Skip(3).Take(5).ToList();
        for (var n = 1; n <= 5; n++)
            Assert.StartsWith($"{n} 0 obj", text[int.Parse(entries[n - 1][..10])..]);
    }
}
