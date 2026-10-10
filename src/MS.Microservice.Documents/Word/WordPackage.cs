using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;

namespace MS.Microservice.Documents.Word;

internal static class WordPackage
{
    internal static OpenSettings Settings(WordTemplateLimits limits) => new()
    {
        AutoSave = false,
        MaxCharactersInPart = limits.MaximumXmlCharactersPerPart
    };

    internal static void Validate(WordprocessingDocument document, CancellationToken cancellationToken)
    {
        if (document.DocumentType != WordprocessingDocumentType.Document || document.DigitalSignatureOriginPart is not null)
            throw new NotSupportedException("Only unsigned DOCX documents are supported.");
        if (document.MainDocumentPart?.DocumentSettingsPart?.Settings?
            .GetFirstChild<DocumentFormat.OpenXml.Wordprocessing.DocumentProtection>()?.Enforcement?.Value == true)
            throw new NotSupportedException("Protected documents cannot be filled.");
        var error = new OpenXmlValidator(FileFormatVersions.Microsoft365) { MaxNumberOfErrors = 1 }
            .Validate(document, cancellationToken).FirstOrDefault();
        if (error is not null) throw new InvalidDataException($"Invalid DOCX: {error.Description}");
    }

    internal static async Task CheckAsync(MemoryStream data, WordTemplateLimits limits, CancellationToken cancellationToken)
    {
        data.Position = 0;
        using var archive = new ZipArchive(data, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count > limits.MaximumEntries) throw new InvalidDataException("DOCX has too many ZIP entries.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long declared = 0;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!names.Add(entry.FullName)) throw new InvalidDataException("DOCX has duplicate ZIP entries.");
            if (entry.Length > limits.MaximumExpandedBytes - declared) throw new InvalidDataException("Expanded DOCX exceeds the byte limit.");
            declared += entry.Length;
        }
        var types = archive.GetEntry("[Content_Types].xml") ?? throw new InvalidDataException("Missing DOCX content types.");
        XDocument contentTypes;
        using (var stream = types.Open())
        using (var reader = XmlReader.Create(stream, XmlSettings(limits)))
            contentTypes = await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken).ConfigureAwait(false);
        XNamespace ns = "http://schemas.openxmlformats.org/package/2006/content-types";
        var defaults = contentTypes.Root?.Elements(ns + "Default").ToDictionary(
            node => (string)node.Attribute("Extension")!, node => (string)node.Attribute("ContentType")!, StringComparer.OrdinalIgnoreCase) ?? [];
        var overrides = contentTypes.Root?.Elements(ns + "Override").ToDictionary(
            node => ((string)node.Attribute("PartName")!).TrimStart('/'), node => (string)node.Attribute("ContentType")!, StringComparer.OrdinalIgnoreCase) ?? [];
        var buffer = new byte[81920];
        long actual = 0;
        foreach (var entry in archive.Entries)
        {
            using (var stream = entry.Open())
            {
                int read;
                while ((read = await stream.ReadAsync(buffer.AsMemory(0,
                    1 + (int)Math.Min(buffer.Length - 1, limits.MaximumExpandedBytes - actual)), cancellationToken).ConfigureAwait(false)) != 0)
                {
                    if (read > limits.MaximumExpandedBytes - actual) throw new InvalidDataException("Expanded DOCX exceeds the byte limit.");
                    actual += read;
                }
            }
            overrides.TryGetValue(entry.FullName, out var type);
            if (type is null) defaults.TryGetValue(Path.GetExtension(entry.FullName).TrimStart('.'), out type);
            if (!entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                && !entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)
                && type?.EndsWith("xml", StringComparison.OrdinalIgnoreCase) != true) continue;
            using var xmlStream = entry.Open();
            using var xml = XmlReader.Create(xmlStream, XmlSettings(limits));
            while (await xml.ReadAsync().ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (xml.Depth == 0 && type?.Contains(".wordprocessingml.", StringComparison.OrdinalIgnoreCase) == true
                    && xml.NamespaceURI == "http://purl.oclc.org/ooxml/wordprocessingml/main")
                    throw new NotSupportedException("Strict DOCX is not supported.");
            }
        }
        data.Position = 0;
    }

    private static XmlReaderSettings XmlSettings(WordTemplateLimits limits) => new()
    {
        Async = true,
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersInDocument = limits.MaximumXmlCharactersPerPart
    };
}
