using System.IO.Compression;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using SkiaSharp;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace MS.Microservice.Documents.Rendering.Tests;

internal static class WordPdfFixtures
{
    internal static byte[] Create(int pages = 2, Action<WordprocessingDocument>? edit = null, string mime = "image/png", byte[]? image = null,
        WordprocessingDocumentType type = WordprocessingDocumentType.Document)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, type))
        {
            var main = document.AddMainDocumentPart();
            var body = new Body();
            main.Document = new Document(body);
            var header = main.AddNewPart<HeaderPart>();
            header.Header = new Header(Paragraph("固定页眉 Header"));
            var footer = main.AddNewPart<FooterPart>();
            footer.Footer = new Footer(Paragraph("固定页脚 Footer"));
            body.Append(Paragraph("合同标题 中文 ABC 😀"), Paragraph("第一页正文 <&>"));
            body.Append(new Table(new TableProperties(new TableBorders(new TopBorder { Val = BorderValues.Single }, new LeftBorder { Val = BorderValues.Single },
                new BottomBorder { Val = BorderValues.Single }, new RightBorder { Val = BorderValues.Single })), new TableGrid(new GridColumn { Width = "4000" }, new GridColumn { Width = "4000" }),
                new TableRow(new TableCell(new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Dxa, Width = "4000" }), Paragraph("客户：张三")),
                    new TableCell(new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Dxa, Width = "4000" }), Paragraph("金额：123.45")))));
            var picture = main.AddImagePart(mime);
            using var png = new MemoryStream(image ?? Image());
            picture.FeedData(png);
            body.Append(new Paragraph(new Run(Picture(main.GetIdOfPart(picture)))));
            for (var page = 2; page <= pages; page++)
                body.Append(new Paragraph(new Run(new Break { Type = BreakValues.Page })), Paragraph($"第{page}页正文 Page {page}"));
            body.Append(new SectionProperties(new HeaderReference { Id = main.GetIdOfPart(header), Type = HeaderFooterValues.Default },
                new FooterReference { Id = main.GetIdOfPart(footer), Type = HeaderFooterValues.Default }, new PageSize { Width = 11906, Height = 16838 },
                new PageMargin { Top = 720, Bottom = 720, Left = 720, Right = 720, Header = 360, Footer = 360 }));
            edit?.Invoke(document);
            var errors = new OpenXmlValidator(FileFormatVersions.Microsoft365).Validate(document).ToArray();
            if (errors.Length > 0) throw new InvalidDataException(string.Join("\n", errors.Select(error => error.Description)));
        }
        return stream.ToArray();
    }

    internal static Paragraph Paragraph(string value) => new(new Run(new RunProperties(new RunFonts
    { Ascii = "Microsoft YaHei", HighAnsi = "Microsoft YaHei", EastAsia = "Microsoft YaHei" }, new FontSize { Val = "28" }), new Text(value) { Space = SpaceProcessingModeValues.Preserve }));

    internal static Paragraph ComplexField(params string[] pieces) => new(
        new[] { new Run(new FieldChar { FieldCharType = FieldCharValues.Begin }) }.Cast<OpenXmlElement>()
            .Concat(pieces.Select(piece => new Run(new FieldCode(piece) { Space = SpaceProcessingModeValues.Preserve })))
            .Concat([new Run(new FieldChar { FieldCharType = FieldCharValues.Separate }), new Run(new Text("999")), new Run(new FieldChar { FieldCharType = FieldCharValues.End })]));

    internal static byte[] Image(SKEncodedImageFormat format = SKEncodedImageFormat.Png)
    {
        using var bitmap = new SKBitmap(32, 16);
        bitmap.Erase(SKColors.Red);
        using var pixels = SKImage.FromBitmap(bitmap);
        using var data = pixels.Encode(format, 100);
        return data.ToArray();
    }

    internal static byte[] Rewrite(byte[] bytes, Action<ZipArchive> edit)
    {
        using var stream = new MemoryStream();
        stream.Write(bytes);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true)) edit(archive);
        return stream.ToArray();
    }

    internal static byte[] RewriteXml(byte[] bytes, string entryName, Func<string, string> edit) => Rewrite(bytes, archive =>
    {
        var entry = archive.GetEntry(entryName)!;
        string xml;
        using (var reader = new StreamReader(entry.Open())) xml = reader.ReadToEnd();
        entry.Delete();
        using var writer = new StreamWriter(archive.CreateEntry(entryName).Open(), new UTF8Encoding(false));
        writer.Write(edit(xml));
    });

    private static Drawing Picture(string id) => new(new DW.Inline(new DW.Extent { Cx = 914400, Cy = 457200 },
        new DW.EffectExtent { LeftEdge = 0, TopEdge = 0, RightEdge = 0, BottomEdge = 0 }, new DW.DocProperties { Id = 1, Name = "Proof picture" },
        new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
        new A.Graphic(new A.GraphicData(new PIC.Picture(new PIC.NonVisualPictureProperties(new PIC.NonVisualDrawingProperties { Id = 0, Name = "proof.png" }, new PIC.NonVisualPictureDrawingProperties()),
            new PIC.BlipFill(new A.Blip { Embed = id }, new A.Stretch(new A.FillRectangle())),
            new PIC.ShapeProperties(new A.Transform2D(new A.Offset { X = 0, Y = 0 }, new A.Extents { Cx = 914400, Cy = 457200 }),
                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }))) { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }))
        { DistanceFromTop = 0, DistanceFromBottom = 0, DistanceFromLeft = 0, DistanceFromRight = 0 });
}
