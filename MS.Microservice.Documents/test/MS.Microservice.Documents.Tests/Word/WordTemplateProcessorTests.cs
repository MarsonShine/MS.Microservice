using System.IO.Compression;
using System.Text;
using System.Xml;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using MS.Microservice.Documents.Word;
using Xunit;
using Lock = DocumentFormat.OpenXml.Wordprocessing.Lock;

namespace MS.Microservice.Documents.Tests.Word;

public sealed class WordTemplateProcessorTests
{
    private readonly WordTemplateProcessor _processor = new();
    private static int _nextId;

    [Fact]
    public async Task Fill_ReplacesAcrossRunsBodyTableHeaderFooterAndKeepsFormattingAndOtherParts()
    {
        var original = CreateDocument(document =>
        {
            var main = document.MainDocumentPart!;
            main.Document!.Body!.Append(new Paragraph(new Run(new RunProperties(new Italic()), new Text("Untouched"))),
                new Paragraph(Control("Name")), TableWith(Control("Name")), Block("Block"));
            var header = main.AddNewPart<HeaderPart>();
            header.Header = new Header(new Paragraph(Control("Header")));
            var footer = main.AddNewPart<FooterPart>();
            footer.Footer = new Footer(new Paragraph(Control("Footer")));
            main.Document.Body.Append(new SectionProperties(
                new HeaderReference { Id = main.GetIdOfPart(header), Type = HeaderFooterValues.Default },
                new FooterReference { Id = main.GetIdOfPart(footer), Type = HeaderFooterValues.Default }));
            var custom = main.AddCustomXmlPart(CustomXmlPartType.CustomXml);
            using var customStream = new MemoryStream(Encoding.UTF8.GetBytes("<metadata>Keep &amp; preserve</metadata>"));
            custom.FeedData(customStream);
        });
        using var input = new MemoryStream(original);
        using var output = new MemoryStream();
        var result = await _processor.FillAsync(input, output, new Dictionary<string, string?>
        {
            ["Name"] = "  中文 😀 <&>  ", ["Block"] = "Block value", ["Header"] = "页眉", ["Footer"] = "页脚"
        });
        Assert.Equal(5, result.FilledControls);
        using var filled = OpenValid(output.ToArray());
        var mainPart = filled.MainDocumentPart!;
        Assert.Equal("Untouched", mainPart.Document!.Body!.Elements<Paragraph>().First().InnerText);
        Assert.NotNull(mainPart.Document.Body.Elements<Paragraph>().First().GetFirstChild<Run>()!.RunProperties!.Italic);
        foreach (var control in mainPart.Document.Descendants<SdtRun>())
        {
            Assert.Equal("  中文 😀 <&>  ", control.InnerText);
            var run = Assert.Single(control.SdtContentRun!.Elements<Run>());
            Assert.NotNull(run.RunProperties!.Bold);
            Assert.Equal("336699", run.RunProperties.Color!.Val!.Value);
            Assert.Equal(SpaceProcessingModeValues.Preserve, run.GetFirstChild<Text>()!.Space!.Value);
            Assert.Null(control.SdtProperties!.GetFirstChild<ShowingPlaceholder>());
            Assert.NotNull(control.SdtProperties.GetFirstChild<Tag>());
        }
        var paragraph = mainPart.Document.Descendants<SdtBlock>().Single().SdtContentBlock!.GetFirstChild<Paragraph>()!;
        Assert.Equal(JustificationValues.Center, paragraph.ParagraphProperties!.Justification!.Val!.Value);
        Assert.Equal("Block value", paragraph.InnerText);
        Assert.Equal("页眉", mainPart.HeaderParts.Single().Header!.InnerText);
        Assert.Equal("页脚", mainPart.FooterParts.Single().Footer!.InnerText);
        Assert.Equal(ReadEntry(original, "customXml/item.xml"), ReadEntry(output.ToArray(), "customXml/item.xml"));
        Assert.Equal(original, input.ToArray());
        Assert.True(input.CanRead);
        Assert.True(output.CanWrite);
    }

    [Theory]
    [InlineData("a\r\nb\rc\nd\te", 3, 1)]
    [InlineData("\n\t\r\n", 2, 1)]
    [InlineData("", 0, 0)]
    [InlineData(null, 0, 0)]
    public async Task Fill_EmitsLegalBreakTabAndEmptyTextNodes(string? text, int breaks, int tabs)
    {
        var bytes = await FillSimple(text);
        using var document = OpenValid(bytes);
        var run = document.MainDocumentPart!.Document!.Descendants<SdtRun>().Single().SdtContentRun!.Elements<Run>().Single();
        Assert.Equal(breaks, run.Elements<Break>().Count());
        Assert.Equal(tabs, run.Elements<TabChar>().Count());
        Assert.Equal((text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'), TextValue(run));
        Assert.DoesNotContain(run.Elements<Text>(), node => node.Text.IndexOfAny(['\r', '\n', '\t']) >= 0);
    }

    [Fact]
    public async Task Fill_UsesOrdinalTagsFillsDuplicatesIgnoresUnusedAndLeavesUntagged()
    {
        var bytes = CreateDocument(document => document.MainDocumentPart!.Document!.Body!.Append(
            new Paragraph(Control("Name")), new Paragraph(Control("name")), new Paragraph(Control(null)), new Paragraph(Control("Name"))));
        using var input = new MemoryStream(bytes);
        using var output = new MemoryStream();
        var count = await _processor.FillAsync(input, output, new Dictionary<string, string?>
        { ["Name"] = "Upper", ["name"] = "lower", ["unused"] = "\0" });
        Assert.Equal(3, count.FilledControls);
        using var result = OpenValid(output.ToArray());
        Assert.Equal(new[] { "Upper", "lower", "Place holder", "Upper" }, result.MainDocumentPart!.Document!.Descendants<SdtRun>().Select(control => control.InnerText));
    }

    [Theory]
    [InlineData(MissingWordTemplateValue.Throw)]
    [InlineData(MissingWordTemplateValue.KeepOriginal)]
    public async Task Fill_MissingValuesRespectPolicyAndNeverMutateSource(MissingWordTemplateValue policy)
    {
        var bytes = CreateDocument(document => document.MainDocumentPart!.Document!.Body!.Append(new Paragraph(Control("Given")), new Paragraph(Control("Missing"))));
        using var input = new MemoryStream(bytes);
        using var output = new MemoryStream();
        var fill = _processor.FillAsync(input, output, new Dictionary<string, string?> { ["Given"] = "Provided" }, new() { MissingValue = policy });
        if (policy == MissingWordTemplateValue.Throw)
        {
            await Assert.ThrowsAsync<KeyNotFoundException>(() => fill);
            Assert.Empty(output.ToArray());
        }
        else
        {
            Assert.Equal(1, (await fill).FilledControls);
            using var result = OpenValid(output.ToArray());
            Assert.Equal(new[] { "Provided", "Place holder" }, result.MainDocumentPart!.Document!.Descendants<SdtRun>().Select(control => control.InnerText));
        }
        Assert.Equal(bytes, input.ToArray());
    }

    [Theory]
    [InlineData("binding")]
    [InlineData("picture")]
    [InlineData("date")]
    [InlineData("locked")]
    [InlineData("field")]
    [InlineData("nested")]
    [InlineData("multipleParagraphs")]
    public async Task Fill_RejectsTargetSemanticsThatCannotBeSafelyReplaced(string variant)
    {
        var bytes = CreateDocument(document =>
        {
            var control = Control("Value");
            switch (variant)
            {
                case "binding": control.SdtProperties!.Append(new DataBinding { XPath = "/root", StoreItemId = "{A7D95648-0AC7-4FCE-A79C-B9B80EED3E44}" }); break;
                case "picture": control.SdtProperties!.RemoveAllChildren<SdtContentRichText>(); control.SdtProperties.Append(new SdtContentPicture()); break;
                case "date": control.SdtProperties!.RemoveAllChildren<SdtContentRichText>(); control.SdtProperties.Append(new SdtContentDate()); break;
                case "locked": control.SdtProperties!.Append(new Lock { Val = LockingValues.ContentLocked }); break;
                case "field": control.SdtContentRun!.GetFirstChild<Run>()!.Append(new FieldCode("DATE")); break;
                case "nested": control.SdtContentRun!.Append(Control(null)); break;
                case "multipleParagraphs":
                    var block = Block("Value");
                    block.SdtContentBlock!.Append(new Paragraph(new Run(new Text("Second"))));
                    document.MainDocumentPart!.Document!.Body!.Append(block);
                    return;
            }
            document.MainDocumentPart!.Document!.Body!.Append(new Paragraph(control));
        });
        using var input = new MemoryStream(bytes);
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<NotSupportedException>(() => _processor.FillAsync(input, output, new Dictionary<string, string?> { ["Value"] = "Replacement" }));
        Assert.Empty(output.ToArray());
        Assert.Equal(bytes, input.ToArray());
    }

    [Fact]
    public async Task Fill_DeletionLockIsPreservedAndDoesNotPreventTextReplacement()
    {
        var bytes = CreateDocument(document =>
        {
            var control = Control("Value");
            control.SdtProperties!.Append(new Lock { Val = LockingValues.SdtLocked });
            document.MainDocumentPart!.Document!.Body!.Append(new Paragraph(control));
        });
        using var input = new MemoryStream(bytes);
        using var output = new MemoryStream();
        await _processor.FillAsync(input, output, new Dictionary<string, string?> { ["Value"] = "Allowed" });
        using var result = OpenValid(output.ToArray());
        var filled = result.MainDocumentPart!.Document!.Descendants<SdtRun>().Single();
        Assert.Equal("Allowed", filled.InnerText);
        Assert.Equal(LockingValues.SdtLocked, filled.SdtProperties!.GetFirstChild<Lock>()!.Val!.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Fill_PlainTextMultilineConstraintIsRespected(bool multiline)
    {
        var bytes = CreateDocument(document => document.MainDocumentPart!.Document!.Body!.Append(new Paragraph(Control("Value", multiline))));
        using var input = new MemoryStream(bytes);
        using var output = new MemoryStream();
        var task = _processor.FillAsync(input, output, new Dictionary<string, string?> { ["Value"] = "一\n二" });
        if (multiline)
        {
            await task;
            using var result = OpenValid(output.ToArray());
            Assert.Single(result.MainDocumentPart!.Document!.Descendants<Break>());
        }
        else
        {
            await Assert.ThrowsAsync<ArgumentException>(() => task);
            Assert.Empty(output.ToArray());
        }
    }

    [Fact]
    public async Task Fill_ReadsNonseekableShortReadsAtCurrentPositionAndWritesAtCurrentPosition()
    {
        var bytes = CreateSimple();
        using var input = new ShortReadStream([9, 8, 7, .. bytes], prefix: 3);
        using var output = new MemoryStream();
        output.Write([1, 2, 3]);
        await _processor.FillAsync(input, output, Values("Current"));
        Assert.Equal(new byte[] { 1, 2, 3 }, output.ToArray()[..3]);
        using var result = OpenValid(output.ToArray()[3..]);
        Assert.Equal("Current", result.MainDocumentPart!.Document!.InnerText);
        Assert.True(input.CanRead);
        Assert.True(output.CanWrite);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Fill_InputLimitAcceptsExactAndStopsAtOneByteOver(int difference)
    {
        var bytes = CreateSimple();
        using var input = new ShortReadStream(bytes);
        using var output = new MemoryStream();
        var options = new WordTemplateOptions { Limits = new() { MaximumInputBytes = bytes.Length + difference } };
        var task = _processor.FillAsync(input, output, Values("Exact"), options);
        if (difference == 0) Assert.Equal(1, (await task).FilledControls);
        else { await Assert.ThrowsAsync<InvalidDataException>(() => task); Assert.Empty(output.ToArray()); }
        Assert.True(input.BytesRead <= bytes.Length + difference + 1);
        Assert.True(input.CanRead);
    }

    [Theory]
    [InlineData("output")]
    [InlineData("expanded")]
    [InlineData("xml")]
    [InlineData("entries")]
    [InlineData("replacement")]
    public async Task Fill_RejectsEveryResourceBudgetBeforeCallerWrites(string budget)
    {
        var bytes = CreateSimple();
        var limits = budget switch
        {
            "output" => new WordTemplateLimits { MaximumOutputBytes = bytes.Length - 1 },
            "expanded" => new WordTemplateLimits { MaximumExpandedBytes = ExpandedSize(bytes) - 1 },
            "xml" => new WordTemplateLimits { MaximumXmlCharactersPerPart = 20 },
            "entries" => new WordTemplateLimits { MaximumEntries = 1 },
            _ => new WordTemplateLimits { MaximumReplacementCharacters = 3 }
        };
        using var input = new MemoryStream(bytes);
        using var output = new MemoryStream();
        var task = _processor.FillAsync(input, output, Values("Long text"), new() { Limits = limits });
        if (budget == "xml") await Assert.ThrowsAsync<XmlException>(() => task);
        else await Assert.ThrowsAsync<InvalidDataException>(() => task);
        Assert.Empty(output.ToArray());
        Assert.True(input.CanRead);
        Assert.True(output.CanWrite);
    }

    [Fact]
    public async Task Fill_RepeatedTagCountsCharactersForEachOccurrence()
    {
        var bytes = CreateDocument(document => document.MainDocumentPart!.Document!.Body!.Append(new Paragraph(Control("Value")), new Paragraph(Control("Value"))));
        foreach (var maximum in new[] { 6, 5 })
        {
            using var input = new MemoryStream(bytes);
            using var output = new MemoryStream();
            var task = _processor.FillAsync(input, output, Values("abc"), new() { Limits = new() { MaximumReplacementCharacters = maximum } });
            if (maximum == 6) Assert.Equal(2, (await task).FilledControls);
            else { await Assert.ThrowsAsync<InvalidDataException>(() => task); Assert.Empty(output.ToArray()); }
        }
    }

    [Fact]
    public async Task Fill_RechecksExpandedXmlBudgetAfterSubstitution()
    {
        var bytes = CreateSimple();
        using var input = new MemoryStream(bytes);
        using var output = new MemoryStream();
        var options = new WordTemplateOptions { Limits = new() { MaximumExpandedBytes = ExpandedSize(bytes) + 20 } };
        await Assert.ThrowsAsync<InvalidDataException>(() => _processor.FillAsync(input, output, Values(new string('a', 1000)), options));
        Assert.Empty(output.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Fill_ExpandedBudgetAcceptsExactInputAndLargestPositiveLimit(bool largestLimit)
    {
        var bytes = CreateSimple();
        using var input = new MemoryStream(bytes);
        using var output = new MemoryStream();
        Assert.Equal(1, (await _processor.FillAsync(input, output, Values("Place holder"), new()
        {
            Limits = new() { MaximumExpandedBytes = largestLimit ? long.MaxValue : ExpandedSize(bytes) }
        })).FilledControls);
        using var result = OpenValid(output.ToArray());
        Assert.Equal("Place holder", result.MainDocumentPart!.Document!.InnerText);
    }

    [Fact]
    public async Task Fill_FinalOutputLimitAcceptsExactSizeAndRejectsOneLess()
    {
        var random = new Random(1234);
        var value = string.Concat(Enumerable.Range(0, 15000).Select(_ => (char)random.Next('A', 'Z' + 1)));
        var bytes = CreateSimple();
        using var baselineInput = new MemoryStream(bytes);
        using var baselineOutput = new MemoryStream();
        await _processor.FillAsync(baselineInput, baselineOutput, Values(value));
        Assert.True(baselineOutput.Length > bytes.Length);
        foreach (var difference in new[] { 0, -1 })
        {
            using var input = new MemoryStream(bytes);
            using var output = new MemoryStream();
            var task = _processor.FillAsync(input, output, Values(value), new()
            { Limits = new() { MaximumOutputBytes = (int)baselineOutput.Length + difference } });
            if (difference == 0)
            {
                await task;
                using var result = OpenValid(output.ToArray());
                Assert.Equal(value, result.MainDocumentPart!.Document!.InnerText);
            }
            else { await Assert.ThrowsAsync<InvalidDataException>(() => task); Assert.Empty(output.ToArray()); }
        }
    }

    [Theory]
    [InlineData("macro")]
    [InlineData("template")]
    [InlineData("signed")]
    [InlineData("protected")]
    public async Task Fill_RejectsPackagesWithOtherDocumentOrProtectionContracts(string variant)
    {
        var bytes = CreateDocument(document =>
        {
            document.MainDocumentPart!.Document!.Body!.Append(new Paragraph(Control("Value")));
            switch (variant)
            {
                case "macro": document.Save(); document.ChangeDocumentType(WordprocessingDocumentType.MacroEnabledDocument); break;
                case "template": document.Save(); document.ChangeDocumentType(WordprocessingDocumentType.Template); break;
                case "signed": document.AddDigitalSignatureOriginPart(); break;
                case "protected":
                    document.MainDocumentPart.AddNewPart<DocumentSettingsPart>().Settings = new Settings(new DocumentProtection { Enforcement = true });
                    break;
            }
        });
        using var input = new MemoryStream(bytes);
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<NotSupportedException>(() => _processor.FillAsync(input, output, Values("value")));
        Assert.Empty(output.ToArray());
    }

    [Fact]
    public async Task Fill_UnsupportedControlWithoutValueCanBeKeptExplicitly()
    {
        var bytes = CreateDocument(document =>
        {
            var control = Control("LeavePicture");
            control.SdtProperties!.RemoveAllChildren<SdtContentRichText>();
            control.SdtProperties.Append(new SdtContentPicture());
            document.MainDocumentPart!.Document!.Body!.Append(new Paragraph(control), new Paragraph(Control("Value")));
        });
        using var input = new MemoryStream(bytes);
        using var output = new MemoryStream();
        Assert.Equal(1, (await _processor.FillAsync(input, output, Values("value"), new() { MissingValue = MissingWordTemplateValue.KeepOriginal })).FilledControls);
        using var result = OpenValid(output.ToArray());
        var controls = result.MainDocumentPart!.Document!.Descendants<SdtRun>().ToArray();
        Assert.NotNull(controls[0].SdtProperties!.GetFirstChild<SdtContentPicture>());
        Assert.Equal("Place holder", controls[0].InnerText);
        Assert.Equal("value", controls[1].InnerText);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Fill_ActuallyDecompressesNonXmlEntriesAtEveryPositiveBudget(bool largestLimit)
    {
        var bytes = RewriteArchive(CreateSimple(), archive =>
        {
            using var stream = archive.CreateEntry("payload.bin", CompressionLevel.SmallestSize).Open();
            stream.Write(new byte[1000]);
        });
        bytes = RewriteXml(bytes, "[Content_Types].xml", xml => xml.Replace("</Types>", "<Default Extension=\"bin\" ContentType=\"application/octet-stream\" /></Types>", StringComparison.Ordinal));
        for (var offset = 0; offset < bytes.Length - 30; offset++)
        {
            if (!bytes.AsSpan(offset, 4).SequenceEqual(new byte[] { 0x50, 0x4b, 0x03, 0x04 })) continue;
            var nameLength = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 26));
            var extraLength = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 28));
            if (Encoding.UTF8.GetString(bytes, offset + 30, nameLength) != "payload.bin") continue;
            bytes[offset + 30 + nameLength + extraLength] = 0x07; // Reserved DEFLATE block type.
            break;
        }
        using var input = new MemoryStream(bytes);
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(() => _processor.FillAsync(input, output, Values("value"), new()
        { Limits = new() { MaximumExpandedBytes = largestLimit ? long.MaxValue : 128L * 1024 * 1024 } }));
        Assert.Empty(output.ToArray());
    }

    [Theory]
    [InlineData("corruptZip")]
    [InlineData("invalidBody")]
    [InlineData("duplicateEntry")]
    [InlineData("DTD")]
    [InlineData("strict")]
    public async Task Fill_RejectsMalformedOrUnsupportedPackagesWithoutWriting(string variant)
    {
        var bytes = CreateSimple();
        if (variant == "corruptZip") bytes = bytes[..20];
        else if (variant == "invalidBody") bytes = CreateDocument(document => document.MainDocumentPart!.Document!.Body!.Append(new Run(new Text("Invalid body child"))));
        else if (variant == "duplicateEntry") bytes = RewriteArchive(bytes, archive =>
        {
            using var writer = new StreamWriter(archive.CreateEntry("word/document.xml").Open());
            writer.Write("<duplicate/>");
        });
        else if (variant == "DTD") bytes = RewriteArchive(bytes, archive =>
        {
            using var writer = new StreamWriter(archive.CreateEntry("extra.xml").Open());
            writer.Write("<!DOCTYPE root [<!ENTITY x 'abc'>]><root>&x;</root>");
        });
        else bytes = RewriteXml(bytes, "word/document.xml", xml => xml.Replace("http://schemas.openxmlformats.org/wordprocessingml/2006/main", "http://purl.oclc.org/ooxml/wordprocessingml/main", StringComparison.Ordinal));
        using var input = new MemoryStream(bytes);
        using var output = new MemoryStream();
        var task = _processor.FillAsync(input, output, Values("value"));
        if (variant == "strict") await Assert.ThrowsAsync<NotSupportedException>(() => task);
        else if (variant == "DTD") await Assert.ThrowsAsync<XmlException>(() => task);
        else await Assert.ThrowsAsync<InvalidDataException>(() => task);
        Assert.Empty(output.ToArray());
    }

    [Fact]
    public async Task Fill_CapsXmlByContentTypeEvenWithAnUnusualPartExtension()
    {
        var bytes = RewriteArchive(CreateSimple(), archive =>
        {
            using var writer = new StreamWriter(archive.CreateEntry("payload.dat").Open());
            writer.Write("<data>" + new string('z', 4000) + "</data>");
        });
        bytes = RewriteXml(bytes, "[Content_Types].xml", xml => xml.Replace("</Types>", "<Override PartName=\"/payload.dat\" ContentType=\"application/example+xml\" /></Types>", StringComparison.Ordinal));
        using var input = new MemoryStream(bytes);
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<XmlException>(() => _processor.FillAsync(input, output, Values("value"), new() { Limits = new() { MaximumXmlCharactersPerPart = 3000 } }));
        Assert.Empty(output.ToArray());
    }

    [Fact]
    public async Task Fill_CancellationBeforeReadingAndDuringShortReadsLeavesStreamsOpen()
    {
        foreach (var preCanceled in new[] { true, false })
        {
            using var cancellation = new CancellationTokenSource();
            using var input = new ShortReadStream(CreateSimple(), cancel: preCanceled ? null : cancellation);
            using var output = new MemoryStream();
            if (preCanceled) cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _processor.FillAsync(input, output, Values("value"), cancellationToken: cancellation.Token));
            Assert.Empty(output.ToArray());
            Assert.Equal(preCanceled ? 0 : 7, input.BytesRead);
            Assert.True(input.CanRead);
            Assert.True(output.CanWrite);
        }
    }

    [Fact]
    public async Task Fill_OutputWriteFailureKeepsCallerOwnershipAndInputUnmodified()
    {
        var bytes = CreateSimple();
        using var input = new MemoryStream(bytes);
        using var output = new FailingOutput();
        await Assert.ThrowsAsync<IOException>(() => _processor.FillAsync(input, output, Values("value")));
        Assert.Equal(bytes, input.ToArray());
        Assert.True(input.CanRead);
        Assert.True(output.CanWrite);
    }

    [Fact]
    public async Task Fill_CancellationDuringCallerCopyPropagatesWithPartialOutputAndOpenStreams()
    {
        using var cancellation = new CancellationTokenSource();
        using var input = new MemoryStream(CreateSimple());
        using var output = new CancelingOutput(cancellation);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _processor.FillAsync(input, output, Values("value"), cancellationToken: cancellation.Token));
        Assert.Equal(5, output.Length);
        Assert.True(input.CanRead);
        Assert.True(output.CanWrite);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0xD800)]
    [InlineData(0xDC00)]
    [InlineData(0xFFFE)]
    public async Task Fill_RejectsInvalidXmlCharactersBeforeOutput(int codeUnit)
    {
        using var input = new MemoryStream(CreateSimple());
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<XmlException>(() => _processor.FillAsync(input, output, Values(new string((char)codeUnit, 1))));
        Assert.Empty(output.ToArray());
    }

    [Fact]
    public async Task Fill_RejectsSameStreamAndInvalidLimitsWithoutConsumingInput()
    {
        using var input = new MemoryStream(CreateSimple());
        await Assert.ThrowsAsync<ArgumentException>(() => _processor.FillAsync(input, input, Values("value")));
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<ArgumentException>(() => _processor.FillAsync(input, output, Values("value"), new() { Limits = new() { MaximumEntries = 0 } }));
        Assert.Equal(0, input.Position);
    }

    [Fact]
    public async Task Fill_ValidEmptyDocumentReturnsZeroAndPreservesValidStructure()
    {
        using var input = new MemoryStream(CreateDocument(document => document.MainDocumentPart!.Document = new Document()));
        using var output = new MemoryStream();
        Assert.Equal(0, (await _processor.FillAsync(input, output, Values("unused"))).FilledControls);
        using var document = OpenValid(output.ToArray());
        Assert.Null(document.MainDocumentPart!.Document!.Body);
    }

    [Fact]
    public async Task Fill_LeavesTextboxControlsOutsideSupportedStoriesUntouched()
    {
        var bytes = CreateDocument(document => document.MainDocumentPart!.Document!.Body!.Append(
            new Paragraph(Control("Value")), new Paragraph(new Run(new Picture(new DocumentFormat.OpenXml.Vml.Shape(
                new DocumentFormat.OpenXml.Vml.TextBox(new TextBoxContent(new Paragraph(Control("Textbox")))) )
            { Id = "TextBox1", Style = "width:120pt;height:80pt" })))));
        using (OpenValid(bytes)) { }
        using var input = new MemoryStream(bytes);
        using var output = new MemoryStream();
        Assert.Equal(1, (await _processor.FillAsync(input, output, Values("Filled"))).FilledControls);
        using var result = OpenValid(output.ToArray());
        Assert.Equal("Place holder", result.MainDocumentPart!.Document!.Descendants<TextBoxContent>().Single().InnerText);
    }

    [Fact]
    public async Task Fill_PreservesUnrelatedCustomXmlEvenIfItUsesStrictWordNamespace()
    {
        var bytes = CreateDocument(document =>
        {
            document.MainDocumentPart!.Document!.Body!.Append(new Paragraph(Control("Value")));
            var part = document.MainDocumentPart.AddCustomXmlPart(CustomXmlPartType.CustomXml);
            using var content = new MemoryStream(Encoding.UTF8.GetBytes("<archive xmlns:w='http://purl.oclc.org/ooxml/wordprocessingml/main'><w:document /></archive>"));
            part.FeedData(content);
        });
        using (OpenValid(bytes)) { }
        using var input = new MemoryStream(bytes);
        using var output = new MemoryStream();
        Assert.Equal(1, (await _processor.FillAsync(input, output, Values("Filled"))).FilledControls);
        Assert.Equal(ReadEntry(bytes, "customXml/item.xml"), ReadEntry(output.ToArray(), "customXml/item.xml"));
    }

    private async Task<byte[]> FillSimple(string? value)
    {
        using var input = new MemoryStream(CreateSimple());
        using var output = new MemoryStream();
        await _processor.FillAsync(input, output, Values(value));
        return output.ToArray();
    }

    private static Dictionary<string, string?> Values(string? value) => new() { ["Value"] = value };
    private static byte[] CreateSimple() => CreateDocument(document => document.MainDocumentPart!.Document!.Body!.Append(new Paragraph(Control("Value"))));
    private static byte[] CreateDocument(Action<WordprocessingDocument> populate)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, autoSave: true))
        {
            document.AddMainDocumentPart().Document = new Document(new Body());
            populate(document);
        }
        return stream.ToArray();
    }

    private static SdtRun Control(string? tag, bool? multiline = null)
    {
        var properties = new SdtProperties(new SdtId { Val = Interlocked.Increment(ref _nextId) });
        if (tag is not null) properties.Append(new Tag { Val = tag });
        properties.Append(new ShowingPlaceholder());
        properties.Append(multiline.HasValue ? new SdtContentText { MultiLine = multiline.Value } : new SdtContentRichText());
        return new SdtRun(properties, new SdtContentRun(
            new Run(new RunProperties(new Bold(), new Color { Val = "336699" }), new Text("Place ")),
            new Run(new RunProperties(new Italic()), new Text("holder"))));
    }

    private static SdtBlock Block(string tag) => new(new SdtProperties(new SdtId { Val = Interlocked.Increment(ref _nextId) }, new Tag { Val = tag }, new SdtContentRichText()),
        new SdtContentBlock(new Paragraph(new ParagraphProperties(new Justification { Val = JustificationValues.Center }), new Run(new RunProperties(new Bold()), new Text("Block placeholder")))));

    private static Table TableWith(SdtRun control) => new(new TableProperties(), new TableGrid(new GridColumn { Width = "4000" }),
        new TableRow(new TableCell(new TableCellProperties(new TableCellWidth { Width = "4000", Type = TableWidthUnitValues.Dxa }), new Paragraph(control))));

    private static WordprocessingDocument OpenValid(byte[] bytes)
    {
        var document = WordprocessingDocument.Open(new MemoryStream(bytes), false);
        Assert.Empty(new OpenXmlValidator(FileFormatVersions.Microsoft365).Validate(document));
        return document;
    }

    private static string TextValue(Run run) => string.Concat(run.ChildElements.Select(node => node switch
    { Text text => text.Text, Break => "\n", TabChar => "\t", _ => string.Empty }));
    private static long ExpandedSize(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        return archive.Entries.Sum(entry => entry.Length);
    }
    private static byte[] ReadEntry(byte[] bytes, string name)
    {
        using var stream = new MemoryStream(bytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.GetEntry(name) ?? archive.Entries.Single(item => item.FullName.StartsWith("customXml/item", StringComparison.Ordinal) && item.FullName.EndsWith(".xml", StringComparison.Ordinal));
        using var data = entry.Open();
        using var output = new MemoryStream();
        data.CopyTo(output);
        return output.ToArray();
    }
    private static byte[] RewriteArchive(byte[] bytes, Action<ZipArchive> update)
    {
        using var stream = new MemoryStream();
        stream.Write(bytes);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true)) update(archive);
        return stream.ToArray();
    }
    private static byte[] RewriteXml(byte[] bytes, string name, Func<string, string> change) => RewriteArchive(bytes, archive =>
    {
        var entry = archive.GetEntry(name)!;
        string content;
        using (var reader = new StreamReader(entry.Open())) content = reader.ReadToEnd();
        entry.Delete();
        using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(change(content));
    });

    private sealed class ShortReadStream(byte[] bytes, int prefix = 0, CancellationTokenSource? cancel = null) : Stream
    {
        private int _position = prefix;
        private bool _disposed;
        public int BytesRead { get; private set; }
        public override bool CanRead => !_disposed;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(7, Math.Min(buffer.Length, bytes.Length - _position));
            bytes.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            BytesRead += count;
            if (count > 0) cancel?.Cancel();
            return ValueTask.FromResult(count);
        }
        protected override void Dispose(bool disposing) { _disposed = true; base.Dispose(disposing); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    private sealed class FailingOutput : MemoryStream
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => throw new IOException("Write failure");
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => throw new IOException("Write failure");
    }
    private sealed class CancelingOutput(CancellationTokenSource cancellation) : MemoryStream
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Write(buffer.Span[..5]);
            cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Write(buffer, offset, 5);
            cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
}
