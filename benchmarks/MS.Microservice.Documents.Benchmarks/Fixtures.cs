using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using SkiaSharp;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

internal static class Fixtures
{
    internal static byte[] Photo()
    {
        using var bitmap = new SKBitmap(2048, 2048, SKColorType.Bgra8888, SKAlphaType.Opaque);
        var random = new Random(7512);
        var pixels = new SKColor[bitmap.Width * bitmap.Height];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = (SKColor)(0xff000000u | (uint)random.Next(1 << 24));
        }
        bitmap.Pixels = pixels;
        using var jpeg = bitmap.Encode(SKEncodedImageFormat.Jpeg, 92);
        return jpeg.ToArray();
    }

    internal static byte[] Pdf(byte[] photo)
    {
        using var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(360, 480);
        page.AddText("Fixture", 12, new PdfPoint(20, 380), builder.AddStandard14Font(Standard14Font.Helvetica));
        if (photo.Length > 0)
        {
            page.AddJpeg(photo, new PdfRectangle(20, 20, 300, 300));
        }
        return builder.Build();
    }

    internal static (byte[] Bytes, string Expected) WordSections(int headers, string title = "Report")
    {
        using var output = new MemoryStream();
        var headerText = new SortedDictionary<string, string>(StringComparer.Ordinal);
        using (var doc = WordprocessingDocument.Create(output, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            var body = new Body();
            main.Document = new Document(body);
            body.Append(new Paragraph(new Run(new Text(title))));
            for (int i = 0; i < headers; i++)
            {
                var header = main.AddNewPart<HeaderPart>();
                string text = $"页眉 {i:D2} 🐈";
                header.Header = new Header(new Paragraph(new Run(new Text(text))));
                headerText.Add(header.Uri.OriginalString, text);
                var section = new SectionProperties(new HeaderReference
                {
                    Id = main.GetIdOfPart(header), Type = HeaderFooterValues.Default
                });
                if (i == headers - 1)
                {
                    body.Append(section);
                }
                else
                {
                    body.Append(new Paragraph(new ParagraphProperties(section), new Run(new Text("Section"))));
                }
            }
        }
        var expected = new[] { title }.Concat(Enumerable.Repeat("Section", Math.Max(0, headers - 1))).Concat(headerText.Values);
        return (output.ToArray(), string.Join('\n', expected));
    }

    internal static byte[] WordTemplate(byte[] photo, string original)
    {
        using var output = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(output, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body(new Paragraph(new SdtRun(
                new SdtProperties(new SdtId { Val = 1 }, new Tag { Val = "name" }, new SdtContentRichText()),
                new SdtContentRun(new Run(new Text("Before")))))));
            if (original.Length > 0)
            {
                main.Document.Body!.Append(new Paragraph(new Run(new Text(original))));
            }
            if (photo.Length > 0)
            {
                using var source = new MemoryStream(photo, writable: false);
                main.AddImagePart(ImagePartType.Jpeg).FeedData(source);
            }
        }
        return output.ToArray();
    }

    internal static bool VerifyTemplate(MemoryStream output, byte[] photo, string expected)
    {
        output.Position = 0;
        using var doc = WordprocessingDocument.Open(output, false);
        var main = doc.MainDocumentPart!;
        if (main.Document?.Body?.InnerText != expected)
        {
            return false;
        }
        if (photo.Length == 0)
        {
            return !main.ImageParts.Any();
        }
        using var stream = main.ImageParts.Single().GetStream();
        using var content = new MemoryStream();
        stream.CopyTo(content);
        return content.GetBuffer().AsSpan(0, (int)content.Length).SequenceEqual(photo);
    }
}
