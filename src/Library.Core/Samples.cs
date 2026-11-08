using System.IO.Compression;
using System.Text;
namespace Library.Core;

/// <summary>Собственные учебные файлы для воспроизводимых проверок.</summary>
public static class Samples
{
    public static byte[] Pdf()
    {
        string[] objects = { "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            "<< /Length 55 >>\nstream\nBT /F1 18 Tf 70 750 Td (Library study document) Tj ET\nendstream" };
        var text = new StringBuilder("%PDF-1.4\n");
        var positions = new List<int>();
        for (int i = 0; i < objects.Length; i++)
        { positions.Add(text.Length); text.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n"); }
        int start = text.Length;
        text.Append("xref\n0 6\n0000000000 65535 f \n");
        foreach (int p in positions) text.Append(p.ToString("D10") + " 00000 n \n");
        text.Append($"trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{start}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(text.ToString());
    }
    public static byte[] Zip(params (string Name, byte[] Bytes)[] entries)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
            foreach (var item in entries)
            {
                using var stream = zip.CreateEntry(item.Name, CompressionLevel.NoCompression).Open();
                stream.Write(item.Bytes);
            }
        return output.ToArray();
    }
    public static byte[] Epub() => Zip(
        ("mimetype", Encoding.UTF8.GetBytes("application/epub+zip")),
        ("META-INF/container.xml", Encoding.UTF8.GetBytes("<container xmlns='urn:oasis:names:tc:opendocument:xmlns:container' version='1.0'><rootfiles><rootfile full-path='OEBPS/book.opf' media-type='application/oebps-package+xml'/></rootfiles></container>")),
        ("OEBPS/book.opf", Encoding.UTF8.GetBytes("<package xmlns='http://www.idpf.org/2007/opf' version='3.0' unique-identifier='id'><metadata xmlns:dc='http://purl.org/dc/elements/1.1/'><dc:identifier id='id'>urn:uuid:11111111-1111-4111-8111-111111111111</dc:identifier><dc:title>Study book</dc:title><dc:language>en</dc:language><meta property='dcterms:modified'>2026-11-08T00:00:00Z</meta></metadata><manifest><item id='c' href='chapter.xhtml' media-type='application/xhtml+xml'/></manifest><spine><itemref idref='c'/></spine></package>")),
        ("OEBPS/chapter.xhtml", Encoding.UTF8.GetBytes("<html xmlns='http://www.w3.org/1999/xhtml'><head><title>Study book</title></head><body><p>Own study document.</p></body></html>")));
}
