using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using MS.Microservice.Documents.Word;

namespace MS.Microservice.Documents.Rendering;

internal static class WordRenderPolicy
{
    private const string WordNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly Regex PageInstruction = new(@"\A\s*(?:PAGE|NUMPAGES|SECTION|SECTIONPAGES)(?:\s+\\\*\s+(?:ARABIC|ROMAN|ALPHABETIC|MERGEFORMAT|CHARFORMAT))*\s*\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    internal static async Task ValidateAsync(byte[] data, WordPdfLimits limits, CancellationToken cancellationToken = default)
    {
        using var stream = new MemoryStream(data, writable: false);
        await WordPackage.CheckAsync(stream, new WordPackageLimits(limits.MaximumEntries, limits.MaximumExpandedBytes, limits.MaximumXmlCharactersPerPart), cancellationToken).ConfigureAwait(false);
        using var document = WordprocessingDocument.Open(stream, false, WordPackage.Settings(limits.MaximumXmlCharactersPerPart));
        if (document.DocumentType != WordprocessingDocumentType.Document || document.DigitalSignatureOriginPart is not null
            || document.MainDocumentPart?.Document is null
            || document.MainDocumentPart.DocumentSettingsPart?.Settings?.Descendants<DocumentFormat.OpenXml.Wordprocessing.DocumentProtection>().Any(node => node.Enforcement?.Value == true) == true)
            throw new NotSupportedException("Only unsigned, unprotected Transitional DOCX documents are supported.");
        var error = new OpenXmlValidator(FileFormatVersions.Microsoft365) { MaxNumberOfErrors = 1 }.Validate(document, cancellationToken).FirstOrDefault();
        if (error is not null) throw new InvalidDataException("Invalid DOCX: " + error.Description);
        CheckRelationships(document);
        foreach (var part in document.GetAllParts())
        {
            cancellationToken.ThrowIfCancellationRequested();
            CheckRelationships(part);
            if (part is VbaProjectPart or EmbeddedObjectPart or EmbeddedPackagePart or AlternativeFormatImportPart or VmlDrawingPart
                || part.DataPartReferenceRelationships.Any())
                throw new NotSupportedException("Macros, embedded objects/packages, media, VML and alternative-format content are unsupported.");
            if (part is ImagePart image)
            {
                var mime = image.ContentType.ToLowerInvariant();
                if (mime is not ("image/png" or "image/jpeg")) throw new NotSupportedException("DOCX rendering only supports embedded PNG/JPEG images.");
                using var encoded = image.GetStream(FileMode.Open, FileAccess.Read);
                var bytes = await WordPdfConverter.ReadAsync(encoded, (int)Math.Min(int.MaxValue, limits.MaximumExpandedBytes), "DOCX image", cancellationToken).ConfigureAwait(false);
                RenderImageValidator.Validate(bytes, mime, limits.MaximumImagePixels);
            }
            if (part.RootElement is { } root) CheckFields(root);
        }
    }

    private static void CheckRelationships(OpenXmlPartContainer part)
    {
        if (part.ExternalRelationships.Any() || part.HyperlinkRelationships.Any(link => link.IsExternal))
            throw new NotSupportedException("External DOCX relationships, including navigation hyperlinks, are unsupported.");
    }

    private static void CheckFields(OpenXmlElement root)
    {
        var fields = new Stack<(StringBuilder Instruction, bool Separated)>();
        foreach (var node in root.Descendants())
        {
            if (fields.TryPeek(out var command) && !command.Separated
                && (node is OpenXmlLeafTextElement && (node.NamespaceUri != WordNamespace || node.LocalName != "instrText")
                    || node.Parent is DocumentFormat.OpenXml.Wordprocessing.Run && (node.NamespaceUri != WordNamespace || node.LocalName is not ("rPr" or "instrText" or "fldChar"))
                    || node.NamespaceUri == WordNamespace && node.LocalName == "fldSimple"))
                throw new NotSupportedException("Field commands must contain only instruction text; other inline content and nested commands are unsupported.");
            if (node.NamespaceUri == "urn:schemas-microsoft-com:vml")
                throw new NotSupportedException("VML content is unsupported.");
            if (node.LocalName == "OLEObject" && node.NamespaceUri == "urn:schemas-microsoft-com:office:office")
                throw new NotSupportedException("OLE content is unsupported.");
            if (node.NamespaceUri != WordNamespace) continue;
            switch (node.LocalName)
            {
                case "altChunk": case "object":
                    throw new NotSupportedException("Alternative-format and embedded object content are unsupported.");
                case "fldSimple":
                    CheckInstruction(node.GetAttribute("instr", WordNamespace).Value);
                    break;
                case "fldChar":
                    if (node.HasChildren) throw new NotSupportedException("Field data and form fields are unsupported.");
                    switch (node.GetAttribute("fldCharType", WordNamespace).Value)
                    {
                        case "begin":
                            if (fields.TryPeek(out var parent) && !parent.Separated)
                                throw new NotSupportedException("Nested fields within field commands are unsupported.");
                            if (fields.Count >= 64) throw new InvalidDataException("DOCX fields are nested too deeply.");
                            fields.Push((new(), false));
                            break;
                        case "separate" when fields.TryPop(out var field) && !field.Separated:
                            fields.Push((field.Instruction, true));
                            break;
                        case "end" when fields.TryPop(out var ended):
                            CheckInstruction(ended.Instruction.ToString());
                            break;
                        default: throw new InvalidDataException("Malformed DOCX field structure.");
                    }
                    break;
                case "instrText": case "delInstrText":
                    if (!fields.TryPeek(out var current) || current.Separated)
                        throw new InvalidDataException("DOCX instruction text is outside a field instruction.");
                    if (node.InnerText.Length > 1024 - current.Instruction.Length)
                        throw new NotSupportedException("DOCX field instructions cannot exceed 1024 characters.");
                    current.Instruction.Append(node.InnerText);
                    break;
            }
        }
        if (fields.Count != 0) throw new InvalidDataException("Unclosed DOCX field.");
    }

    private static void CheckInstruction(string? instruction)
    {
        if (instruction is null || instruction.Length > 1024 || !PageInstruction.IsMatch(instruction))
            throw new NotSupportedException("Only PAGE, NUMPAGES, SECTION and SECTIONPAGES fields with supported general-format switches are supported.");
    }
}
