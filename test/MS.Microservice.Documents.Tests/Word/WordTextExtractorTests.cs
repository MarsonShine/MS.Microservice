using System.IO.Compression;
using System.Text;
using System.Xml;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using MS.Microservice.Documents.Word;
using Xunit;

namespace MS.Microservice.Documents.Tests.Word;

public sealed class WordTextExtractorTests
{
    private const string WordNs = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private readonly WordTextExtractor _extractor = new();

    [Fact]
    public async Task Extract_PreservesBodyAndTableSeparatorsThenReadsDistinctReferencedStoriesInPathOrder()
    {
        var body = "<w:p><w:r><w:t>A </w:t><w:tab/><w:t>中文</w:t><w:br/><w:t>line</w:t></w:r>"
            + "<w:del><w:r><w:delText>deleted</w:delText></w:r></w:del>"
            + "<w:ins><w:r><w:t>新</w:t></w:r></w:ins></w:p>"
            + "<w:tbl><w:tr><w:tc><w:p><w:r><w:t>Cell A</w:t></w:r></w:p></w:tc>"
            + "<w:tc><w:p><w:r><w:t>Cell B</w:t></w:r></w:p></w:tc></w:tr></w:tbl>"
            + "<w:sectPr><w:headerReference r:id='B'/><w:headerReference r:id='A'/><w:headerReference r:id='Again'/>"
            + "<w:headerReference r:id='External'/><w:footerReference r:id='Footer'/></w:sectPr>";
        var bytes = Package(body, new()
        {
            ["word/_rels/document.xml.rels"] = Relations(
                "<Relationship Id='B' Type='" + RelNs + "/header' Target='header-b.xml'/>"
                + "<Relationship Id='A' Type='" + RelNs + "/header' Target='header-a.xml'/>"
                + "<Relationship Id='Again' Type='" + RelNs + "/header' Target='header-a.xml'/>"
                + "<Relationship Id='Unused' Type='" + RelNs + "/header' Target='unused.xml'/>"
                + "<Relationship Id='External' Type='" + RelNs + "/header' Target='https://invalid.test/header.xml' TargetMode='External'/>"
                + "<Relationship Id='Footer' Type='" + RelNs + "/footer' Target='footer.xml'/>") ,
            ["word/header-a.xml"] = Story("hdr", "Header A"),
            ["word/header-b.xml"] = Story("hdr", "Header B"),
            ["word/footer.xml"] = Story("ftr", "Footer"),
            ["word/unused.xml"] = "<!DOCTYPE bad><bad/>"
        });
        using var stream = new MemoryStream(bytes);
        Assert.Equal("A \t中文\nline新\nCell A\tCell B\nHeader A\nHeader B\nFooter", await _extractor.ExtractAsync(stream));
        Assert.True(stream.CanRead);
        Assert.Equal(bytes, stream.ToArray());
        stream.Position = 0;
        Assert.Equal("A \t中文\nline新\nCell A\tCell B", await _extractor.ExtractAsync(stream, new() { IncludeHeadersAndFooters = false }));
    }

    [Fact]
    public async Task Extract_ReadsSignedAndProtectedDocumentsWithoutInvokingTemplateWritePolicies()
    {
        using var input = new MemoryStream();
        using (var document = WordprocessingDocument.Create(input, WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            main.Document = new Document(new Body(new Paragraph(new Run(new Text("Read only")))));
            main.AddNewPart<DocumentSettingsPart>().Settings = new Settings(new DocumentProtection { Enforcement = true });
            document.AddDigitalSignatureOriginPart();
        }
        input.Position = 0;
        Assert.Equal("Read only", await _extractor.ExtractAsync(input));
        Assert.True(input.CanRead);
    }

    [Fact]
    public async Task Extract_SupportsStrictNamespaceAndNonstandardMainPartWithoutExpandingUnrelatedBinary()
    {
        var bytes = Package("<w:p><w:r><w:t>Strict 文字</w:t></w:r></w:p>", mainPath: "content/main.xml",
            wordNamespace: "http://purl.oclc.org/ooxml/wordprocessingml/main");
        bytes = AddEntry(bytes, "word/media/image.bin", new byte[1_000_000]);
        using var stream = new ShortReadStream(bytes);
        Assert.Equal("Strict 文字", await _extractor.ExtractAsync(stream, new() { MaximumExpandedXmlBytes = 3000 }));
        Assert.True(stream.CanRead);
    }

    [Theory]
    [InlineData("default")]
    [InlineData("overrideWins")]
    [InlineData("macroOverride")]
    [InlineData("duplicateDefault")]
    public async Task Extract_ResolvesOpcContentTypeDefaultsAndOverrides(string variant)
    {
        const string docx = "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml";
        var declarations = "<Default Extension='xml' ContentType='" + (variant == "overrideWins" ? "application/xml" : docx) + "'/>";
        if (variant == "overrideWins") declarations += "<Override PartName='/word/document.xml' ContentType='" + docx + "'/>";
        if (variant == "macroOverride") declarations += "<Override PartName='/word/document.xml' ContentType='application/vnd.ms-word.document.macroEnabled.main+xml'/>";
        if (variant == "duplicateDefault") declarations += "<Default Extension='xml' ContentType='" + docx + "'/>";
        using var input = new MemoryStream(Package("<w:p><w:r><w:t>Content type</w:t></w:r></w:p>", new()
        {
            ["[Content_Types].xml"] = "<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'>" + declarations + "</Types>"
        }));
        if (variant == "macroOverride") await Assert.ThrowsAsync<NotSupportedException>(() => _extractor.ExtractAsync(input));
        else if (variant == "duplicateDefault") await Assert.ThrowsAsync<InvalidDataException>(() => _extractor.ExtractAsync(input));
        else Assert.Equal("Content type", await _extractor.ExtractAsync(input));
    }

    [Theory]
    [InlineData("input")]
    [InlineData("entries")]
    [InlineData("expandedXml")]
    [InlineData("text")]
    public async Task Extract_RejectsExceededResourceBudgets(string budget)
    {
        var bytes = Package("<w:p><w:r><w:t>bounded text</w:t></w:r></w:p>");
        var options = budget switch
        {
            "input" => new WordTextExtractionOptions { MaximumInputBytes = bytes.Length - 1 },
            "entries" => new WordTextExtractionOptions { MaximumEntries = 2 },
            "expandedXml" => new WordTextExtractionOptions { MaximumExpandedXmlBytes = 1 },
            _ => new WordTextExtractionOptions { MaximumTextCharacters = 5 }
        };
        using var stream = new MemoryStream(bytes);
        await Assert.ThrowsAsync<InvalidDataException>(() => _extractor.ExtractAsync(stream, options));
        Assert.True(stream.CanRead);
    }

    [Theory]
    [InlineData("DTD")]
    [InlineData("xmlCharacters")]
    public async Task Extract_ProhibitsDtdAndCapsXmlCharacters(string variant)
    {
        var bytes = variant == "DTD"
            ? Package("", new() { ["word/document.xml"] = "<!DOCTYPE w:document [<!ENTITY x SYSTEM 'file:///does-not-exist'>]>"
                + "<w:document xmlns:w='" + WordNs + "'><w:body><w:p><w:r><w:t>&x;</w:t></w:r></w:p></w:body></w:document>" })
            : Package("<w:p><w:r><w:t>" + new string('x', 2000) + "</w:t></w:r></w:p>");
        using var stream = new MemoryStream(bytes);
        await Assert.ThrowsAsync<XmlException>(() => _extractor.ExtractAsync(stream,
            new() { MaximumXmlCharactersPerPart = variant == "DTD" ? 16000 : 1000 }));
    }

    [Fact]
    public async Task Extract_ReadsCurrentPositionAndPropagatesCancellationWithoutClosingSource()
    {
        var bytes = Package("<w:p><w:r><w:t>Position</w:t></w:r></w:p>");
        using var prefixed = new MemoryStream();
        prefixed.Write([0, 1, 2]);
        prefixed.Write(bytes);
        prefixed.Position = 3;
        Assert.Equal("Position", await _extractor.ExtractAsync(prefixed, new() { MaximumInputBytes = bytes.Length }));
        using var cancellation = new CancellationTokenSource();
        using var shortReads = new ShortReadStream(bytes, cancellation);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _extractor.ExtractAsync(shortReads, cancellationToken: cancellation.Token));
        Assert.True(shortReads.CanRead);
        using var canceled = new MemoryStream(bytes);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _extractor.ExtractAsync(canceled, cancellationToken: cancellation.Token));
        Assert.Equal(0, canceled.Position);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("externalMain")]
    [InlineData("encrypted")]
    public async Task Extract_RejectsInvalidPackages(string variant)
    {
        var bytes = Package("");
        if (variant == "duplicate") bytes = AddEntry(bytes, "word/document.xml", Encoding.UTF8.GetBytes("<duplicate/>"));
        if (variant == "externalMain") bytes = Package("", new()
        {
            ["_rels/.rels"] = Relations("<Relationship Id='Main' Type='" + RelNs
                + "/officeDocument' Target='https://invalid.test/doc.xml' TargetMode='External'/>")
        });
        if (variant == "encrypted") bytes = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
        using var stream = new MemoryStream(bytes);
        await Assert.ThrowsAsync<InvalidDataException>(() => _extractor.ExtractAsync(stream));
    }

    private static byte[] Package(string body, Dictionary<string, string>? extra = null,
        string mainPath = "word/document.xml", string wordNamespace = WordNs)
    {
        var files = new Dictionary<string, string>
        {
            ["[Content_Types].xml"] = "<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'>"
                + "<Override PartName='/" + mainPath + "' ContentType='application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml'/></Types>",
            ["_rels/.rels"] = Relations("<Relationship Id='Main' Type='" + RelNs + "/officeDocument' Target='" + mainPath + "'/>") ,
            [mainPath] = "<w:document xmlns:w='" + wordNamespace + "' xmlns:r='" + RelNs + "'><w:body>" + body + "</w:body></w:document>"
        };
        if (extra is not null) foreach (var pair in extra) files[pair.Key] = pair.Value;
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var pair in files)
            {
                using var writer = new StreamWriter(archive.CreateEntry(pair.Key).Open(), new UTF8Encoding(false));
                writer.Write(pair.Value);
            }
        return stream.ToArray();
    }

    private static byte[] AddEntry(byte[] bytes, string name, byte[] content)
    {
        using var stream = new MemoryStream();
        stream.Write(bytes);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        using (var entry = archive.CreateEntry(name).Open()) entry.Write(content);
        return stream.ToArray();
    }

    private static string Relations(string content) => "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'>"
        + content + "</Relationships>";
    private static string Story(string root, string text) => "<w:" + root + " xmlns:w='" + WordNs
        + "'><w:p><w:r><w:t>" + text + "</w:t></w:r></w:p></w:" + root + ">";

    private sealed class ShortReadStream(byte[] bytes, CancellationTokenSource? cancel = null) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var result = base.ReadAsync(buffer[..Math.Min(7, buffer.Length)], cancellationToken);
            cancel?.Cancel();
            return result;
        }
    }
}
