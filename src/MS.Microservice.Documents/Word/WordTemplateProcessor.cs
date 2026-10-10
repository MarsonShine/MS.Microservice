using System.Xml;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using MS.Microservice.Documents.Internal;
using Lock = DocumentFormat.OpenXml.Wordprocessing.Lock;

namespace MS.Microservice.Documents.Word;

/// <summary>Fills tagged text content controls in DOCX body, tables, headers and footers.</summary>
public sealed class WordTemplateProcessor
{
    /// <summary>Edits a bounded copy and validates it before writing; caller streams remain open.</summary>
    public async Task<WordTemplateResult> FillAsync(Stream template, Stream destination,
        IReadOnlyDictionary<string, string?> values, WordTemplateOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(values);
        if (!template.CanRead || !destination.CanWrite || ReferenceEquals(template, destination))
            throw new ArgumentException("Provide distinct readable input and writable output streams.");
        options ??= new();
        var limits = options.Limits ?? throw new ArgumentException("Limits are required.", nameof(options));
        if (!Enum.IsDefined(options.MissingValue) || limits.MaximumInputBytes < 1 || limits.MaximumOutputBytes < 1
            || limits.MaximumExpandedBytes < 1 || limits.MaximumXmlCharactersPerPart < 1
            || limits.MaximumEntries < 1 || limits.MaximumReplacementCharacters < 1)
            throw new ArgumentException("Word template limits must be positive and policies valid.", nameof(options));
        cancellationToken.ThrowIfCancellationRequested();
        var supplied = new Dictionary<string, string?>(values, StringComparer.Ordinal);
        using var source = new BoundedBuffer(limits.MaximumInputBytes, "DOCX input");
        var buffer = new byte[(int)Math.Min(81920L, (long)limits.MaximumInputBytes + 1)];
        int read;
        while ((read = await template.ReadAsync(buffer.AsMemory(0,
            (int)Math.Min(buffer.Length, (long)limits.MaximumInputBytes - source.Length + 1)), cancellationToken).ConfigureAwait(false)) != 0)
            source.Write(buffer, 0, read);
        await WordPackage.CheckAsync(source, limits, cancellationToken).ConfigureAwait(false);
        using var edited = new BoundedBuffer(limits.MaximumOutputBytes, "DOCX output");
        source.Position = 0;
        await source.CopyToAsync(edited, cancellationToken).ConfigureAwait(false);
        edited.Position = 0;
        var filled = 0;
        using (var document = WordprocessingDocument.Open(edited, true, WordPackage.Settings(limits)))
        {
            WordPackage.Validate(document, cancellationToken);
            var main = document.MainDocumentPart ?? throw new InvalidDataException("Missing DOCX main part.");
            var roots = new List<OpenXmlPartRootElement> { main.Document ?? throw new InvalidDataException("Missing DOCX document root.") };
            roots.AddRange(main.HeaderParts.Select(part => part.Header ?? throw new InvalidDataException("Missing DOCX header root.")));
            roots.AddRange(main.FooterParts.Select(part => part.Footer ?? throw new InvalidDataException("Missing DOCX footer root.")));
            long characters = 0;
            foreach (var root in roots)
            {
                foreach (var control in root.Descendants<SdtElement>().Where(control => !control.Ancestors<TextBoxContent>().Any()).ToArray())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var tag = control.SdtProperties?.GetFirstChild<Tag>()?.Val?.Value;
                    if (string.IsNullOrEmpty(tag)) continue;
                    if (!supplied.TryGetValue(tag, out var text))
                    {
                        if (options.MissingValue == MissingWordTemplateValue.KeepOriginal) continue;
                        throw new KeyNotFoundException($"Missing Word template value: {tag}");
                    }
                    text ??= string.Empty;
                    if (text.Length > limits.MaximumReplacementCharacters - characters)
                        throw new InvalidDataException("Word replacement text exceeds the character limit.");
                    characters += text.Length;
                    XmlConvert.VerifyXmlChars(text);
                    Replace(control, text);
                    filled++;
                }
                root.Save();
            }
            cancellationToken.ThrowIfCancellationRequested();
        }
        await WordPackage.CheckAsync(edited, limits, cancellationToken).ConfigureAwait(false);
        using (var verified = WordprocessingDocument.Open(edited, false, WordPackage.Settings(limits)))
            WordPackage.Validate(verified, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        edited.Position = 0;
        await edited.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        return new(filled);
    }

    private static void Replace(SdtElement control, string text)
    {
        var properties = control.SdtProperties!;
        // Restrict target semantics rather than silently breaking bindings, choices or locks.
        if (properties.ChildElements.Any(child => child is not (RunProperties or SdtId or SdtAlias or Tag
            or SdtPlaceholder or ShowingPlaceholder or TemporarySdt or Lock or SdtContentText or SdtContentRichText
            or DocumentFormat.OpenXml.Office2013.Word.Color or DocumentFormat.OpenXml.Office2013.Word.Appearance)))
            throw new NotSupportedException("Only unbound plain or rich text controls can be filled.");
        var locking = properties.GetFirstChild<Lock>()?.Val?.Value;
        if (locking == LockingValues.ContentLocked || locking == LockingValues.SdtContentLocked)
            throw new NotSupportedException("The target content control is locked.");
        if (control.Ancestors<SdtElement>().Any() || control.Descendants<SdtElement>().Any())
            throw new NotSupportedException("Nested target content controls are not supported.");
        var plain = properties.GetFirstChild<SdtContentText>();
        if (plain is not null && plain.MultiLine?.Value != true && text.AsSpan().IndexOfAny('\r', '\n') >= 0)
            throw new ArgumentException("This plain text control does not allow line breaks.", nameof(text));
        OpenXmlCompositeElement content = control switch
        {
            SdtRun run => run.SdtContentRun ?? throw new InvalidDataException("Missing control content."),
            SdtBlock block when block.SdtContentBlock is { } blockContent
                && blockContent.ChildElements.Count == 1 && blockContent.FirstChild is Paragraph paragraph => paragraph,
            _ => throw new NotSupportedException("Use inline text controls or a block control containing one paragraph.")
        };
        if (content.ChildElements.Any(child => child is not (Run or ParagraphProperties))
            || content.Elements<Run>().Any(run => run.ChildElements.Any(child => child is not (RunProperties or Text or Break or TabChar or CarriageReturn))))
            throw new NotSupportedException("Target content must contain only ordinary text runs.");
        var first = content.Elements<Run>().FirstOrDefault();
        var replacement = new Run();
        var formatting = first?.RunProperties ?? properties.GetFirstChild<RunProperties>();
        if (formatting is not null) replacement.Append(formatting.CloneNode(true));
        var start = 0;
        for (var index = 0; index <= text.Length; index++)
        {
            if (index < text.Length && text[index] is not ('\r' or '\n' or '\t')) continue;
            if (index > start) replacement.Append(new Text(text[start..index]) { Space = SpaceProcessingModeValues.Preserve });
            if (index < text.Length)
            {
                replacement.Append(text[index] == '\t' ? new TabChar() : new Break());
                if (text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n') index++;
            }
            start = index + 1;
        }
        if (replacement.ChildElements.All(child => child is RunProperties)) replacement.Append(new Text(string.Empty));
        foreach (var run in content.Elements<Run>().ToArray()) run.Remove();
        content.Append(replacement);
        properties.RemoveAllChildren<ShowingPlaceholder>();
    }
}
