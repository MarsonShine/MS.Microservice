using System.IO.Compression;
using System.Text;
using System.Xml;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using SkiaSharp;
using Xunit;

namespace MS.Microservice.Documents.Rendering.Tests;

public sealed class WordRenderPolicyTests
{
    [Fact]
    public async Task ImageHeader_RealPackageStreamCanBeReadAndStaysOpen()
    {
        using var document = WordprocessingDocument.Open(new MemoryStream(WordPdfFixtures.Create()), false);
        using var encoded = document.MainDocumentPart!.ImageParts.Single().GetStream(FileMode.Open, FileAccess.Read);
        var data = await WordPdfConverter.ReadAsync(encoded, 1024, "DOCX image");
        RenderImageValidator.Validate(data, "image/png", 512);
        Assert.True(encoded.CanRead);
    }

    [Fact]
    public async Task Validate_ReadOnlyNormalDocumentAndExactZipImageBoundaries()
    {
        var data = WordPdfFixtures.Create();
        var snapshot = data.ToArray();
        using var archive = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read);
        var limits = new WordPdfLimits { MaximumExpandedBytes = archive.Entries.Sum(entry => entry.Length), MaximumEntries = archive.Entries.Count, MaximumImagePixels = 512 };
        await WordRenderPolicy.ValidateAsync(data, limits);
        Assert.Equal(snapshot, data);
        await Assert.ThrowsAsync<InvalidDataException>(() => WordRenderPolicy.ValidateAsync(data, limits with { MaximumExpandedBytes = limits.MaximumExpandedBytes - 1 }));
        await Assert.ThrowsAsync<InvalidDataException>(() => WordRenderPolicy.ValidateAsync(data, limits with { MaximumEntries = limits.MaximumEntries - 1 }));
        await Assert.ThrowsAsync<InvalidDataException>(() => WordRenderPolicy.ValidateAsync(data, limits with { MaximumImagePixels = 511 }));
    }

    [Theory]
    [InlineData("PAGE")]
    [InlineData(" NUMPAGES ")]
    [InlineData("section \\* roman ")]
    [InlineData("SECTIONPAGES \\* Arabic \\* MERGEFORMAT")]
    [InlineData("page \\* CHARFORMAT")]
    public async Task Validate_LocalSimpleAndSplitComplexFields(string instruction)
    {
        var data = WordPdfFixtures.Create(edit: document =>
        {
            document.MainDocumentPart!.HeaderParts.Single().Header!.Append(new Paragraph(new SimpleField(new Run(new Text("cached"))) { Instruction = instruction }));
            document.MainDocumentPart.FooterParts.Single().Footer!.Append(WordPdfFixtures.ComplexField(instruction[..1], instruction[1..]));
        });
        await WordRenderPolicy.ValidateAsync(data, new());
    }

    [Theory]
    [InlineData("INCLUDETEXT \"file:///canary\"")]
    [InlineData("INCLUDEPICTURE \"http://127.0.0.1/canary\"")]
    [InlineData("DDEAUTO app topic item")]
    [InlineData("HYPERLINK \"file:///canary\"")]
    [InlineData("PAGE INCLUDETEXT path")]
    [InlineData("PAGE \\* UNKNOWN")]
    [InlineData("DATE")]
    [InlineData("")]
    public async Task Validate_RejectsUnknownOrInjectedFieldsAcrossRuns(string instruction)
    {
        var data = WordPdfFixtures.Create(edit: doc => doc.MainDocumentPart!.Document!.Body!.InsertAt(WordPdfFixtures.ComplexField(instruction[..(instruction.Length / 2)], instruction[(instruction.Length / 2)..]), 0));
        await Assert.ThrowsAsync<NotSupportedException>(() => WordRenderPolicy.ValidateAsync(data, new()));
    }

    [Theory]
    [InlineData("begin")]
    [InlineData("separate")]
    [InlineData("end")]
    [InlineData("instruction")]
    [InlineData("double-separate")]
    public async Task Validate_RejectsMalformedFieldState(string variant)
    {
        var data = WordPdfFixtures.Create(edit: doc =>
        {
            var paragraph = new Paragraph();
            if (variant == "instruction") paragraph.Append(new Run(new FieldCode("PAGE")));
            else if (variant == "double-separate")
                paragraph.Append(new Run(new FieldChar { FieldCharType = FieldCharValues.Begin }), new Run(new FieldCode("PAGE")),
                    new Run(new FieldChar { FieldCharType = FieldCharValues.Separate }), new Run(new FieldChar { FieldCharType = FieldCharValues.Separate }));
            else paragraph.Append(new Run(new FieldChar { FieldCharType = variant switch { "begin" => FieldCharValues.Begin, "separate" => FieldCharValues.Separate, _ => FieldCharValues.End } }));
            if (variant == "begin") doc.MainDocumentPart!.HeaderParts.Single().Header!.Append(paragraph);
            else doc.MainDocumentPart!.Document!.Body!.InsertAt(paragraph, 0);
        });
        await Assert.ThrowsAsync<InvalidDataException>(() => WordRenderPolicy.ValidateAsync(data, new()));
    }

    [Fact]
    public async Task Validate_RejectsNestedUnsafeFieldAndExcessiveInstruction()
    {
        var nested = WordPdfFixtures.Create(edit: doc =>
        {
            var paragraph = WordPdfFixtures.ComplexField("PAGE");
            paragraph.InsertAt(new Run(new FieldChar { FieldCharType = FieldCharValues.Begin }, new FieldCode("INCLUDETEXT path"), new FieldChar { FieldCharType = FieldCharValues.End }), 3);
            doc.MainDocumentPart!.Document!.Body!.InsertAt(paragraph, 0);
        });
        await Assert.ThrowsAsync<NotSupportedException>(() => WordRenderPolicy.ValidateAsync(nested, new()));
        var huge = WordPdfFixtures.Create(edit: doc => doc.MainDocumentPart!.Document!.Body!.InsertAt(WordPdfFixtures.ComplexField("PAGE " + new string(' ', 1024)), 0));
        await Assert.ThrowsAsync<NotSupportedException>(() => WordRenderPolicy.ValidateAsync(huge, new()));
    }

    [Theory]
    [InlineData("text-before")]
    [InlineData("text-after")]
    [InlineData("deleted-text")]
    [InlineData("tab")]
    [InlineData("symbol")]
    [InlineData("nested-complex")]
    [InlineData("nested-simple")]
    public async Task Validate_RejectsNonInstructionContentDuringFieldCommand(string variant)
    {
        var data = WordPdfFixtures.Create(edit: doc =>
        {
            var paragraph = WordPdfFixtures.ComplexField("PAGE");
            OpenXmlElement content = variant switch
            {
                "deleted-text" => new Run(new DeletedText("INCLUDETEXT \"file:///canary\" ") { Space = SpaceProcessingModeValues.Preserve }),
                "tab" => new Run(new TabChar()),
                "symbol" => new Run(new SymbolChar { Font = "Arial", Char = "0049" }),
                "nested-complex" => new Run(new FieldChar { FieldCharType = FieldCharValues.Begin }, new FieldCode("NUMPAGES"), new FieldChar { FieldCharType = FieldCharValues.End }),
                "nested-simple" => new SimpleField(new Run(new Text("INCLUDETEXT file:///canary"))) { Instruction = "NUMPAGES" },
                _ => new Run(new Text("INCLUDETEXT \"file:///canary\" ") { Space = SpaceProcessingModeValues.Preserve })
            };
            paragraph.InsertAt(content, variant == "text-after" ? 2 : 1);
            doc.MainDocumentPart!.Document!.Body!.InsertAt(paragraph, 0);
        });
        await Assert.ThrowsAsync<NotSupportedException>(() => WordRenderPolicy.ValidateAsync(data, new()));
    }

    [Fact]
    public async Task Validate_AllowsOrdinaryCachedTextAndNestedLocalFieldsAfterSeparator()
    {
        var data = WordPdfFixtures.Create(edit: doc =>
        {
            var paragraph = WordPdfFixtures.ComplexField("PA", "GE ");
            paragraph.InsertAt(new Run(new Text("CACHED INCLUDETEXT file:///not-an-instruction")), 4);
            paragraph.InsertAt(new SimpleField(new Run(new Text("999"))) { Instruction = "NUMPAGES" }, 4);
            paragraph.InsertAt(new Run(new FieldChar { FieldCharType = FieldCharValues.Begin }, new FieldCode("SECTIONPAGES"),
                new FieldChar { FieldCharType = FieldCharValues.Separate }, new Text("999"), new FieldChar { FieldCharType = FieldCharValues.End }), 4);
            doc.MainDocumentPart!.Document!.Body!.InsertAt(paragraph, 0);
        });
        await WordRenderPolicy.ValidateAsync(data, new());
    }

    [Fact]
    public async Task Validate_AllowsFormattedInstructionsWithoutSeparatorButNeverPairsAcrossParts()
    {
        var valid = WordPdfFixtures.Create(edit: doc => doc.MainDocumentPart!.HeaderParts.Single().Header!.Append(new Paragraph(
            new Run(new FieldChar { FieldCharType = FieldCharValues.Begin }),
            new Run(new RunProperties(new Bold()), new FieldCode("PA")), new Run(new FieldCode("GE")),
            new Run(new FieldChar { FieldCharType = FieldCharValues.End }))));
        await WordRenderPolicy.ValidateAsync(valid, new());
        var crossPart = WordPdfFixtures.Create(edit: doc =>
        {
            doc.MainDocumentPart!.HeaderParts.Single().Header!.Append(new Paragraph(new Run(new FieldChar { FieldCharType = FieldCharValues.Begin }, new FieldCode("PAGE"))));
            doc.MainDocumentPart.FooterParts.Single().Footer!.Append(new Paragraph(new Run(new FieldChar { FieldCharType = FieldCharValues.End })));
        });
        await Assert.ThrowsAsync<InvalidDataException>(() => WordRenderPolicy.ValidateAsync(crossPart, new()));
    }

    [Theory]
    [InlineData("root")]
    [InlineData("body")]
    [InlineData("header")]
    [InlineData("footer")]
    [InlineData("hyperlink")]
    public async Task Validate_RejectsExternalRelationshipsThroughoutPackage(string location)
    {
        var data = WordPdfFixtures.Create(edit: doc =>
        {
            OpenXmlPartContainer part = location switch { "root" => doc, "header" => doc.MainDocumentPart!.HeaderParts.Single(), "footer" => doc.MainDocumentPart!.FooterParts.Single(), _ => doc.MainDocumentPart! };
            if (location == "hyperlink") part.AddHyperlinkRelationship(new Uri("https://example.invalid/canary"), true);
            else part.AddExternalRelationship("urn:proof-external", new Uri("file:///canary"));
        });
        await Assert.ThrowsAsync<NotSupportedException>(() => WordRenderPolicy.ValidateAsync(data, new()));
    }

    [Theory]
    [InlineData("fill")]
    [InlineData("image")]
    public async Task Validate_RejectsVmlMarkupAndDirectSourceWithoutRelationships(string variant)
    {
        var data = WordPdfFixtures.Create(edit: doc =>
        {
            OpenXmlElement source = variant == "fill" ? new DocumentFormat.OpenXml.Vml.Fill { Source = "file:///canary.png" }
                : new DocumentFormat.OpenXml.Vml.ImageData { Title = "Legacy VML image" };
            doc.MainDocumentPart!.Document!.Body!.InsertAt(new Paragraph(new Run(new Picture(new DocumentFormat.OpenXml.Vml.Shape(source) { Id = "canary", Style = "width:72pt;height:36pt" }))), 0);
        });
        await Assert.ThrowsAsync<NotSupportedException>(() => WordRenderPolicy.ValidateAsync(data, new()));
    }

    [Fact]
    public async Task Validate_RejectsSvgAndEmbeddedAlternativeFormats()
    {
        var svg = WordPdfFixtures.Create(mime: "image/svg+xml", image: Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><image href='file:///canary.png'/></svg>"));
        await Assert.ThrowsAsync<NotSupportedException>(() => WordRenderPolicy.ValidateAsync(svg, new()));
        var chunk = WordPdfFixtures.Create(edit: doc =>
        {
            var part = doc.MainDocumentPart!.AddAlternativeFormatImportPart(AlternativeFormatImportPartType.Html);
            using var content = new MemoryStream(Encoding.UTF8.GetBytes("<img src='file:///canary.png'>"));
            part.FeedData(content);
            var main = doc.MainDocumentPart ?? throw new InvalidDataException("Missing fixture main part.");
            var body = main.Document?.Body ?? throw new InvalidDataException("Missing fixture body.");
            body.InsertAt(new AltChunk { Id = main.GetIdOfPart(part) }, 0);
        });
        await Assert.ThrowsAsync<NotSupportedException>(() => WordRenderPolicy.ValidateAsync(chunk, new()));
    }

    [Fact]
    public async Task Validate_RejectsProtectedStrictCorruptDuplicateAndXmlBudgetInputs()
    {
        var data = WordPdfFixtures.Create();
        var protectedDoc = WordPdfFixtures.Create(edit: doc => doc.MainDocumentPart!.AddNewPart<DocumentSettingsPart>().Settings =
            new Settings(new DocumentProtection { Edit = DocumentProtectionValues.ReadOnly, Enforcement = true }));
        await Assert.ThrowsAsync<NotSupportedException>(() => WordRenderPolicy.ValidateAsync(protectedDoc, new()));
        var strict = WordPdfFixtures.RewriteXml(data, "word/document.xml", xml => xml.Replace("http://schemas.openxmlformats.org/wordprocessingml/2006/main", "http://purl.oclc.org/ooxml/wordprocessingml/main", StringComparison.Ordinal));
        await Assert.ThrowsAsync<NotSupportedException>(() => WordRenderPolicy.ValidateAsync(strict, new()));
        await Assert.ThrowsAsync<InvalidDataException>(() => WordRenderPolicy.ValidateAsync([1, 2, 3], new()));
        var duplicate = WordPdfFixtures.Rewrite(data, archive => { using var writer = new StreamWriter(archive.CreateEntry("word/document.xml").Open()); writer.Write("duplicate"); });
        await Assert.ThrowsAsync<InvalidDataException>(() => WordRenderPolicy.ValidateAsync(duplicate, new()));
        await Assert.ThrowsAsync<XmlException>(() => WordRenderPolicy.ValidateAsync(data, new() { MaximumXmlCharactersPerPart = 32 }));
        var dtd = WordPdfFixtures.RewriteXml(data, "word/document.xml", xml => xml.Insert(xml.IndexOf('>') + 1, "<!DOCTYPE document [<!ENTITY proof 'entity'>]>"));
        await Assert.ThrowsAsync<XmlException>(() => WordRenderPolicy.ValidateAsync(dtd, new()));
    }

    [Fact]
    public async Task Validate_JpegStaticFrameCountAndMismatchedImageMime()
    {
        var jpeg = WordPdfFixtures.Image(SKEncodedImageFormat.Jpeg);
        await WordRenderPolicy.ValidateAsync(WordPdfFixtures.Create(mime: "image/jpeg", image: jpeg), new());
        await Assert.ThrowsAsync<InvalidDataException>(() => WordRenderPolicy.ValidateAsync(WordPdfFixtures.Create(image: jpeg), new()));
        await Assert.ThrowsAsync<InvalidDataException>(() => WordRenderPolicy.ValidateAsync(WordPdfFixtures.Create(image: [1, 2, 3]), new()));
    }

    [Theory]
    [InlineData("macro-type")]
    [InlineData("signature-origin")]
    [InlineData("embedded-package")]
    public async Task Validate_RejectsMacroSignatureAndEmbeddedPackageSupportDomains(string variant)
    {
        var data = WordPdfFixtures.Create(edit: doc =>
        {
            if (variant == "signature-origin") doc.AddNewPart<DigitalSignatureOriginPart>();
            else if (variant == "embedded-package")
            {
                var part = doc.MainDocumentPart!.AddEmbeddedPackagePart("application/vnd.openxmlformats-officedocument.wordprocessingml.document");
                using var content = new MemoryStream([1, 2, 3]);
                part.FeedData(content);
            }
        }, type: variant == "macro-type" ? WordprocessingDocumentType.MacroEnabledDocument : WordprocessingDocumentType.Document);
        await Assert.ThrowsAsync<NotSupportedException>(() => WordRenderPolicy.ValidateAsync(data, new()));
    }
}
