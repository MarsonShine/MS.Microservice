using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using MS.Microservice.Documents.Internal;

namespace MS.Microservice.Documents.Word;

/// <summary>Extracts stored text from DOCX body, tables and optional headers/footers without rendering or changing the package.</summary>
public sealed class WordTextExtractor
{
    private const string WordNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string StrictWordNamespace = "http://purl.oclc.org/ooxml/wordprocessingml/main";
    private const string RelationshipNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string StrictRelationshipNamespace = "http://purl.oclc.org/ooxml/officeDocument/relationships";
    private const string DocxContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml";

    /// <summary>Reads from the current position and keeps the caller's stream open; text order follows XML structure rather than page layout.</summary>
    public async Task<string> ExtractAsync(Stream source, WordTextExtractionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead) throw new ArgumentException("A readable DOCX stream is required.", nameof(source));
        options ??= new();
        if (options.MaximumInputBytes < 1 || options.MaximumEntries < 1 || options.MaximumExpandedXmlBytes < 1
            || options.MaximumXmlCharactersPerPart < 1 || options.MaximumTextCharacters < 1)
            throw new ArgumentException("Word text extraction limits must be positive.", nameof(options));
        cancellationToken.ThrowIfCancellationRequested();
        using var input = new BoundedBuffer(options.MaximumInputBytes, "DOCX input");
        await CopyBoundedAsync(source, input, options.MaximumInputBytes, cancellationToken).ConfigureAwait(false);
        input.Position = 0;
        using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count > options.MaximumEntries) throw new InvalidDataException("DOCX has too many ZIP entries.");
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
            if (!entries.TryAdd(entry.FullName, entry)) throw new InvalidDataException("DOCX has duplicate ZIP entries.");

        var reader = new PackageReader(entries, options);
        var relationships = await reader.RelationshipsAsync("_rels/.rels", cancellationToken).ConfigureAwait(false);
        var mainRelationships = relationships.Values.Where(item => IsRelationship(item.Type, "officeDocument")).ToArray();
        if (mainRelationships.Length != 1 || mainRelationships[0].External)
            throw new InvalidDataException("DOCX must have one internal main document relationship.");
        var mainPath = ResolveTarget(string.Empty, mainRelationships[0].Target);
        var types = await reader.XmlAsync("[Content_Types].xml", cancellationToken).ConfigureAwait(false);
        XNamespace contentTypeNamespace = "http://schemas.openxmlformats.org/package/2006/content-types";
        if (types.Root?.Name != contentTypeNamespace + "Types") throw new InvalidDataException("Invalid DOCX content types.");
        var mainTypes = types.Root.Elements(contentTypeNamespace + "Override")
            .Where(item => ResolveTarget(string.Empty, RequiredAttribute(item, "PartName")) == mainPath)
            .Select(item => RequiredAttribute(item, "ContentType")).ToArray();
        if (mainTypes.Length == 0)
            mainTypes = types.Root.Elements(contentTypeNamespace + "Default")
                .Where(item => string.Equals(RequiredAttribute(item, "Extension"), Path.GetExtension(mainPath).TrimStart('.'), StringComparison.OrdinalIgnoreCase))
                .Select(item => RequiredAttribute(item, "ContentType")).ToArray();
        if (mainTypes.Length > 1) throw new InvalidDataException("DOCX has duplicate applicable content types.");
        if (mainTypes.Length != 1 || mainTypes[0] != DocxContentType)
            throw new NotSupportedException("Text extraction supports DOCX documents, not macro documents or templates.");

        var text = new StringBuilder();
        var references = new HashSet<string>(StringComparer.Ordinal);
        await reader.StoryAsync(mainPath, "document", text, references, cancellationToken).ConfigureAwait(false);
        if (options.IncludeHeadersAndFooters && references.Count != 0)
        {
            var slash = mainPath.LastIndexOf('/');
            var relationPath = (slash < 0 ? string.Empty : mainPath[..(slash + 1)]) + "_rels/" + mainPath[(slash + 1)..] + ".rels";
            var parts = await reader.RelationshipsAsync(relationPath, cancellationToken).ConfigureAwait(false);
            foreach (var kind in new[] { "header", "footer" })
            {
                var paths = new HashSet<string>(StringComparer.Ordinal);
                foreach (var id in references)
                {
                    if (!parts.TryGetValue(id, out var part)) throw new InvalidDataException("DOCX has an unresolved header/footer reference.");
                    if (!part.External && IsRelationship(part.Type, kind)) paths.Add(ResolveTarget(mainPath, part.Target));
                }
                foreach (var path in paths.Order(StringComparer.Ordinal))
                    await reader.StoryAsync(path, kind == "header" ? "hdr" : "ftr", text, null, cancellationToken).ConfigureAwait(false);
            }
        }
        return text.ToString().TrimEnd('\n', '\t');
    }

    private static bool IsRelationship(string type, string name) =>
        type == RelationshipNamespace + "/" + name || type == StrictRelationshipNamespace + "/" + name;

    private static string RequiredAttribute(XElement element, string name) =>
        (string?)element.Attribute(name) is { Length: > 0 } value ? value : throw new InvalidDataException($"Missing DOCX {name} attribute.");

    private static string ResolveTarget(string sourcePart, string target)
    {
        var basis = new Uri("https://docx.invalid/" + sourcePart);
        if (target.Contains('\\') || !Uri.TryCreate(basis, target, out var result)
            || result.Scheme != "https" || result.Host != "docx.invalid" || !result.IsDefaultPort
            || result.UserInfo.Length != 0 || result.Query.Length != 0 || result.Fragment.Length != 0)
            throw new InvalidDataException("Invalid internal DOCX relationship target.");
        var path = Uri.UnescapeDataString(result.AbsolutePath).TrimStart('/');
        if (path.Length == 0 || path.Contains('\\') || path.Split('/').Any(segment => segment is "." or ".."))
            throw new InvalidDataException("Invalid internal DOCX part path.");
        return path;
    }

    private static async Task CopyBoundedAsync(Stream source, BoundedBuffer destination, int maximumBytes, CancellationToken cancellationToken)
    {
        var buffer = new byte[(int)Math.Min(81920L, (long)maximumBytes + 1)];
        int read;
        while ((read = await source.ReadAsync(buffer.AsMemory(0,
            (int)Math.Min(buffer.Length, (long)maximumBytes - destination.Length + 1)), cancellationToken).ConfigureAwait(false)) != 0)
            destination.Write(buffer, 0, read);
    }

    private sealed record Relationship(string Type, string Target, bool External);

    private sealed class PackageReader(Dictionary<string, ZipArchiveEntry> entries, WordTextExtractionOptions options)
    {
        private int _remainingBytes = options.MaximumExpandedXmlBytes;

        private async Task<BoundedBuffer> ReadAsync(string path, CancellationToken cancellationToken)
        {
            if (!entries.TryGetValue(path, out var entry)) throw new InvalidDataException($"Missing DOCX part: {path}");
            if (entry.Length > _remainingBytes) throw new InvalidDataException("Expanded DOCX XML exceeds the byte limit.");
            var buffer = new BoundedBuffer(_remainingBytes, "Expanded DOCX XML");
            try
            {
                using var stream = entry.Open();
                await CopyBoundedAsync(stream, buffer, _remainingBytes, cancellationToken).ConfigureAwait(false);
                _remainingBytes -= (int)buffer.Length;
                buffer.Position = 0;
                return buffer;
            }
            catch { buffer.Dispose(); throw; }
        }

        private XmlReader CreateReader(Stream stream) => XmlReader.Create(stream, new XmlReaderSettings
        {
            Async = true, DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = options.MaximumXmlCharactersPerPart
        });

        internal async Task<XDocument> XmlAsync(string path, CancellationToken cancellationToken)
        {
            using var buffer = await ReadAsync(path, cancellationToken).ConfigureAwait(false);
            using var xml = CreateReader(buffer);
            return await XDocument.LoadAsync(xml, LoadOptions.None, cancellationToken).ConfigureAwait(false);
        }

        internal async Task<Dictionary<string, Relationship>> RelationshipsAsync(string path, CancellationToken cancellationToken)
        {
            var xml = await XmlAsync(path, cancellationToken).ConfigureAwait(false);
            XNamespace ns = "http://schemas.openxmlformats.org/package/2006/relationships";
            if (xml.Root?.Name != ns + "Relationships") throw new InvalidDataException("Invalid DOCX relationships.");
            var result = new Dictionary<string, Relationship>(StringComparer.Ordinal);
            foreach (var element in xml.Root.Elements(ns + "Relationship"))
            {
                var relationship = new Relationship(RequiredAttribute(element, "Type"), RequiredAttribute(element, "Target"),
                    (string?)element.Attribute("TargetMode") == "External");
                if (!result.TryAdd(RequiredAttribute(element, "Id"), relationship)) throw new InvalidDataException("Duplicate DOCX relationship ID.");
            }
            return result;
        }

        internal async Task StoryAsync(string path, string root, StringBuilder text, HashSet<string>? references, CancellationToken cancellationToken)
        {
            using var buffer = await ReadAsync(path, cancellationToken).ConfigureAwait(false);
            using var xml = CreateReader(buffer);
            var validRoot = false;
            var bodyDepth = root == "document" ? -1 : 0;
            var textDepth = -1;
            var deletedDepth = -1;
            while (await xml.ReadAsync().ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var isWord = xml.NamespaceURI is WordNamespace or StrictWordNamespace;
                if (xml.Depth == 0 && xml.NodeType == XmlNodeType.Element)
                {
                    if (!isWord || xml.LocalName != root) throw new InvalidDataException($"Invalid DOCX {root} root.");
                    validRoot = true;
                }
                if (!isWord)
                {
                    if (textDepth >= 0 && xml.NodeType is XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.SignificantWhitespace or XmlNodeType.Whitespace)
                        Append(text, xml.Value);
                    continue;
                }
                if (xml.NodeType == XmlNodeType.EndElement)
                {
                    if (xml.Depth == textDepth) textDepth = -1;
                    if (xml.Depth == deletedDepth) { deletedDepth = -1; continue; }
                    if (deletedDepth >= 0) continue;
                    if (bodyDepth >= 0)
                    {
                        switch (xml.LocalName)
                        {
                            case "p": Append(text, "\n"); break;
                            case "tc": TrimEnd(text, '\n'); Append(text, "\t"); break;
                            case "tr": TrimEnd(text, '\t'); Append(text, "\n"); break;
                        }
                    }
                    if (root == "document" && xml.Depth == bodyDepth) bodyDepth = -1;
                    continue;
                }
                if (xml.NodeType != XmlNodeType.Element) continue;
                if (deletedDepth >= 0) continue;
                if (xml.LocalName is "del" or "moveFrom") { if (!xml.IsEmptyElement) deletedDepth = xml.Depth; continue; }
                if (root == "document" && xml.LocalName == "body") bodyDepth = xml.Depth;
                if (bodyDepth < 0) continue;
                switch (xml.LocalName)
                {
                    case "t": if (!xml.IsEmptyElement) textDepth = xml.Depth; break;
                    case "tab": Append(text, "\t"); break;
                    case "br" or "cr": Append(text, "\n"); break;
                    case "noBreakHyphen": Append(text, "\u2011"); break;
                    case "softHyphen": Append(text, "\u00ad"); break;
                    case "headerReference" or "footerReference":
                        var id = xml.GetAttribute("id", RelationshipNamespace) ?? xml.GetAttribute("id", StrictRelationshipNamespace);
                        if (!string.IsNullOrEmpty(id)) references?.Add(id);
                        break;
                    case "p" when xml.IsEmptyElement: Append(text, "\n"); break;
                    case "tc" when xml.IsEmptyElement: Append(text, "\t"); break;
                    case "tr" when xml.IsEmptyElement: Append(text, "\n"); break;
                }
            }
            if (!validRoot) throw new InvalidDataException($"Missing DOCX {root} root.");
        }

        private void Append(StringBuilder text, string value)
        {
            if (value.Length > options.MaximumTextCharacters - text.Length) throw new InvalidDataException("DOCX text exceeds the character limit.");
            text.Append(value);
        }

        private static void TrimEnd(StringBuilder text, char value)
        {
            while (text.Length != 0 && text[^1] == value) text.Length--;
        }
    }
}
