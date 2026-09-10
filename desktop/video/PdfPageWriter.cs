using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace VideoGridDesktop;

public sealed record PdfPageImage(byte[] Jpeg, int Width, int Height);

public static class PdfPageWriter
{
    public static void Write(string outputPath, IReadOnlyList<PdfPageImage> pages)
    {
        if (pages.Count == 0)
        {
            throw new InvalidOperationException("PDF 没有可写入的页面。");
        }

        string? directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var pageRefs = new List<string>();
        for (int page = 0; page < pages.Count; page++)
        {
            int pageId = 3 + page * 3;
            pageRefs.Add($"{pageId} 0 R");
        }

        var objects = new List<byte[]>();
        objects.Add(Ascii($"1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n"));
        objects.Add(Ascii(
            "2 0 obj\n<< /Type /Pages /Kids [" +
            string.Join(" ", pageRefs) +
            "] /Count " + pages.Count +
            " >>\nendobj\n"));

        for (int page = 0; page < pages.Count; page++)
        {
            PdfPageImage image = pages[page];
            int pageObject = 3 + page * 3;
            int contentObject = pageObject + 1;
            int imageObject = pageObject + 2;

            string content = $"q\n{image.Width} 0 0 {image.Height} 0 0 cm\n/Im0 Do\nQ\n";
            byte[] contentBytes = Ascii(content);
            byte[] pageObjectBytes = Ascii(
                $"{pageObject} 0 obj\n" +
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {image.Width} {image.Height}] " +
                $"/Resources << /XObject << /Im0 {imageObject} 0 R >> >> /Contents {contentObject} 0 R >>\n" +
                "endobj\n");
            byte[] contentObjectBytes = Ascii(
                $"{contentObject} 0 obj\n<< /Length {contentBytes.Length} >>\nstream\n");
            byte[] streamEnd = Ascii("endstream\nendobj\n");
            byte[] imageObjectBytes = Ascii(
                $"{imageObject} 0 obj\n" +
                $"<< /Type /XObject /Subtype /Image /Width {image.Width} /Height {image.Height} " +
                "/ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode " +
                $"/Length {image.Jpeg.Length} >>\nstream\n");
            byte[] imageEnd = Ascii("endstream\nendobj\n");

            objects.Add(pageObjectBytes);
            objects.Add(Concat(contentObjectBytes, contentBytes, streamEnd));
            objects.Add(Concat(imageObjectBytes, image.Jpeg, imageEnd));
        }

        using var stream = new FileStream(
            outputPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None);
        WriteAscii(stream, "%PDF-1.4\n");
        stream.Write(new byte[] { 0x25, 0xE2, 0xE3, 0xCF, 0xD3 }, 0, 5);
        stream.WriteByte(0x0A);

        var offsets = new List<long> { 0 };
        foreach (byte[] objectBytes in objects)
        {
            offsets.Add(stream.Position);
            stream.Write(objectBytes, 0, objectBytes.Length);
        }

        long xrefOffset = stream.Position;
        WriteAscii(stream, $"xref\n0 {offsets.Count}\n");
        WriteAscii(stream, "0000000000 65535 f \n");
        for (int index = 1; index < offsets.Count; index++)
        {
            WriteAscii(stream, $"{offsets[index]:D10} 00000 n \n");
        }

        WriteAscii(stream, $"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\n");
        WriteAscii(stream, $"startxref\n{xrefOffset}\n%%EOF\n");
    }

    private static byte[] Ascii(string value)
    {
        return Encoding.ASCII.GetBytes(value);
    }

    private static void WriteAscii(Stream stream, string value)
    {
        byte[] bytes = Ascii(value);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static byte[] Concat(params byte[][] parts)
    {
        int length = 0;
        foreach (byte[] part in parts)
        {
            length += part.Length;
        }

        var result = new byte[length];
        int offset = 0;
        foreach (byte[] part in parts)
        {
            Buffer.BlockCopy(part, 0, result, offset, part.Length);
            offset += part.Length;
        }

        return result;
    }
}
