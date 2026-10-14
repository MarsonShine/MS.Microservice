using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Analysis.Miscellaneous;
using Lucene.Net.Analysis.NGram;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Analysis.Util;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Index.Sorter;
using Lucene.Net.Search;
using Lucene.Net.Search.Similarities;
using Lucene.Net.Store;
using Lucene.Net.Util;

namespace MS.Microservice.Search;

/// <summary>A local, single-writer persistent index with concurrent readers and explicit publication.</summary>
public sealed partial class DocumentSearchIndex : IDisposable
{
    private const string Schema = "ms-document-search/1";
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly Sort PathSort = new(new SortField("path_sort", SortFieldType.STRING), new SortField("id_sort", SortFieldType.STRING));
    private readonly SearchIndexOptions _options;
    private readonly string _identity;
    private readonly FSDirectory _directory;
    private readonly Analyzer _analyzer;
    private readonly IndexWriter _writer;
    private readonly SearcherManager _searchers;
    private readonly object _writes = new();
    private readonly object _lifetime = new();
    private readonly Dictionary<string, Revision> _pending = new(StringComparer.Ordinal);
    private int _operations;
    private bool _closing;
    private bool _closed;

    public DocumentSearchIndex(SearchIndexOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.DirectoryPath);
        if (!System.IO.Path.IsPathFullyQualified(options.DirectoryPath))
            throw new ArgumentException("Use an absolute, trusted index directory.", nameof(options));
        if (options.MaximumBatchSize is < 1 or > 100_000 || options.MaximumContentCharacters is < 1 or > 16_000_000
            || options.MaximumBatchContentCharacters is < 1 or > 256_000_000
            || !double.IsFinite(options.WriteBufferMegabytes) || options.WriteBufferMegabytes is < 16 or > 1024)
            throw new ArgumentOutOfRangeException(nameof(options));
        _options = options with { DirectoryPath = System.IO.Path.GetFullPath(options.DirectoryPath) };
        _directory = FSDirectory.Open(_options.DirectoryPath);
        IndexWriter? writer = null;
        Analyzer? analyzer = null;
        try
        {
            var identity = Guid.NewGuid().ToString("N");
            var exists = DirectoryReader.IndexExists(_directory);
            if (exists)
            {
                using var reader = DirectoryReader.Open(_directory);
                if (!reader.IndexCommit.UserData.TryGetValue("schema", out var schema) || schema != Schema)
                    throw new InvalidDataException("This directory contains an incompatible index; choose a new index directory.");
                if (!reader.IndexCommit.UserData.TryGetValue("identity", out identity) || !Guid.TryParseExact(identity, "N", out _))
                    throw new InvalidDataException("The index identity is missing or invalid.");
            }
            analyzer = CreateAnalyzer();
            writer = new IndexWriter(_directory, new IndexWriterConfig(LuceneVersion.LUCENE_48, analyzer)
            {
                OpenMode = OpenMode.CREATE_OR_APPEND,
                RAMBufferSizeMB = _options.WriteBufferMegabytes,
                Similarity = new BM25Similarity(),
                MergePolicy = new SortingMergePolicy(new TieredMergePolicy(), PathSort)
            });
            writer.SetCommitData(new Dictionary<string, string> { ["schema"] = Schema, ["identity"] = identity });
            if (!exists) writer.Commit();
            _analyzer = analyzer;
            _identity = identity;
            _writer = writer;
            // A directory reader sees committed publications only, including after process restart.
            _searchers = new SearcherManager(_directory, new Bm25Factory());
        }
        catch
        {
            try { writer?.Rollback(); }
            finally { analyzer?.Dispose(); _directory.Dispose(); }
            throw;
        }
    }

    /// <summary>Buffers validated revisions; call Publish to commit and expose them. Cancellation can leave a partial buffered batch.</summary>
    public SearchBatchResult ApplyBatch(IEnumerable<SearchChange> changes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);
        using var lease = Enter();
        var prepared = new List<Prepared>();
        var contentCharacters = 0L;
        foreach (var change in changes)
        {
            ArgumentNullException.ThrowIfNull(change);
            cancellationToken.ThrowIfCancellationRequested();
            if (prepared.Count == _options.MaximumBatchSize) throw new ArgumentOutOfRangeException(nameof(changes), "The batch exceeds MaximumBatchSize.");
            if (change.Document is { } source)
            {
                contentCharacters = checked(contentCharacters + (source.Content?.Length ?? 0));
                if (contentCharacters > _options.MaximumBatchContentCharacters)
                    throw new ArgumentOutOfRangeException(nameof(changes), "The batch exceeds MaximumBatchContentCharacters.");
            }
            prepared.Add(Prepare(change));
        }
        lock (_writes)
        {
            var planned = new Dictionary<string, Revision>(StringComparer.Ordinal);
            var writes = new List<Prepared>();
            var stale = 0;
            var unchanged = 0;
            var searcher = _searchers.Acquire();
            try
            {
                foreach (var item in prepared)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!planned.TryGetValue(item.Key, out var current) && !_pending.TryGetValue(item.Key, out current))
                        current = ReadRevision(searcher, item.Key);
                    if (current is not null && item.Change.Version < current.Version) { stale++; continue; }
                    if (current is not null && item.Change.Version == current.Version)
                    {
                        if (item.Fingerprint != current.Fingerprint)
                            throw new SearchVersionConflictException(item.Change.Scope, item.Change.DocumentId, item.Change.Version);
                        unchanged++;
                        continue;
                    }
                    planned[item.Key] = new(item.Change.Version, item.Fingerprint);
                    writes.Add(item);
                }
            }
            finally { _searchers.Release(searcher); }
            foreach (var item in writes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _writer.UpdateDocument(new Term("key", item.Key), BuildDocument(item));
                _pending[item.Key] = new(item.Change.Version, item.Fingerprint);
            }
            return new(writes.Count, stale, unchanged);
        }
    }

    /// <summary>Commits buffered changes and refreshes the shared search snapshot; native I/O is cooperatively cancellable.</summary>
    public long Publish(CancellationToken cancellationToken = default)
    {
        using var lease = Enter();
        lock (_writes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return PublishCore();
        }
    }

    /// <summary>Explicitly merges an import into one sorted segment and publishes it; never required per query or small update.</summary>
    public long Optimize(CancellationToken cancellationToken = default)
    {
        using var lease = Enter();
        lock (_writes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _writer.ForceMerge(1, doWait: true);
            return PublishCore();
        }
    }

    private long PublishCore()
    {
        _writer.Commit();
        _searchers.MaybeRefreshBlocking();
        _pending.Clear();
        var searcher = _searchers.Acquire();
        try { return ((DirectoryReader)searcher.IndexReader).Version; }
        finally { _searchers.Release(searcher); }
    }

    private Prepared Prepare(SearchChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        ValidateText(change.Scope, 256, nameof(change.Scope));
        ValidateText(change.DocumentId, 256, nameof(change.DocumentId));
        if (change.Version < 1) throw new ArgumentOutOfRangeException(nameof(change.Version));
        var document = change.Document;
        if (document is not null)
        {
            ValidateText(document.FileName, 512, nameof(document.FileName));
            ValidateText(document.Path, 4096, nameof(document.Path));
            ValidateText(document.Content, _options.MaximumContentCharacters, nameof(document.Content), allowEmpty: true);
            if (document.LengthBytes < 0) throw new ArgumentOutOfRangeException(nameof(document.LengthBytes));
        }
        var key = Hash(change.Scope, change.DocumentId);
        var fingerprint = Hash(change.Kind.ToString(), document?.FileName ?? "", document?.Path ?? "", document?.Content ?? "",
            document?.LengthBytes.ToString(CultureInfo.InvariantCulture) ?? "",
            document?.ModifiedAtUtc?.UtcTicks.ToString(CultureInfo.InvariantCulture) ?? "");
        return new(change, key, fingerprint);
    }

    private static Document BuildDocument(Prepared prepared)
    {
        var change = prepared.Change;
        var source = change.Document;
        var path = source is null ? "" : NormalizePath(source.Path);
        var document = new Document
        {
            new StringField("key", prepared.Key, Field.Store.NO),
            new StringField("scope", Hash(change.Scope), Field.Store.NO),
            new StringField("live", source is null ? "0" : "1", Field.Store.NO),
            new StoredField("id", change.DocumentId),
            new StoredField("version", change.Version),
            new StoredField("fingerprint", prepared.Fingerprint),
            new SortedDocValuesField("path_sort", new BytesRef(path)),
            new SortedDocValuesField("id_sort", new BytesRef(change.DocumentId))
        };
        if (source is null) return document;
        document.Add(new StoredField("name", source.FileName));
        document.Add(new StoredField("path", source.Path));
        document.Add(new StoredField("length", source.LengthBytes));
        document.Add(new StringField("extension", Normalize(System.IO.Path.GetExtension(source.FileName).TrimStart('.')), Field.Store.NO));
        if (source.ModifiedAtUtc is { } modified) document.Add(new Int64Field("modified", modified.UtcTicks, Field.Store.YES));
        for (var gram = 1; gram <= 3; gram++)
        {
            document.Add(new TextField($"name_g{gram}", Normalize(source.FileName), Field.Store.NO));
            document.Add(new TextField($"path_g{gram}", path, Field.Store.NO));
        }
        document.Add(new TextField("body", Normalize(source.Content), Field.Store.NO));
        document.Add(new StoredField("body_text", source.Content));
        return document;
    }

    private static Revision? ReadRevision(IndexSearcher searcher, string key)
    {
        if (searcher.IndexReader.NumDocs == 0) return null;
        var hits = searcher.Search(new TermQuery(new Term("key", key)), 1).ScoreDocs;
        if (hits.Length == 0) return null;
        var document = searcher.Doc(hits[0].Doc, new HashSet<string> { "version", "fingerprint" });
        return new(long.Parse(document.Get("version"), CultureInfo.InvariantCulture), document.Get("fingerprint"));
    }

    private static Analyzer CreateAnalyzer()
    {
        var fields = new Dictionary<string, Analyzer>(StringComparer.Ordinal);
        foreach (var field in new[] { "name", "path" })
            for (var gram = 1; gram <= 3; gram++) fields[$"{field}_g{gram}"] = new FixedGramAnalyzer(gram);
        fields["body"] = new StandardAnalyzer(LuceneVersion.LUCENE_48, CharArraySet.Empty);
        return new PerFieldAnalyzerWrapper(new KeywordAnalyzer(), fields);
    }

    private static void ValidateText(string value, int maximum, string parameter, bool allowEmpty = false)
    {
        ArgumentNullException.ThrowIfNull(value, parameter);
        if ((!allowEmpty && string.IsNullOrWhiteSpace(value)) || value.Length > maximum)
            throw new ArgumentOutOfRangeException(parameter);
        _ = Utf8.GetByteCount(value);
    }

    private static string Normalize(string value) => value.Normalize(NormalizationForm.FormC).ToLowerInvariant();
    private static string NormalizePath(string value) => Normalize(value.Replace('\\', '/'));
    private static string Hash(params string[] parts)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[4];
        foreach (var part in parts)
        {
            var bytes = Utf8.GetBytes(part);
            BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
            hash.AppendData(length);
            hash.AppendData(bytes);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private Operation Enter()
    {
        lock (_lifetime)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            _operations++;
            return new(this);
        }
    }

    private void Leave()
    {
        lock (_lifetime) { if (--_operations == 0) Monitor.PulseAll(_lifetime); }
    }

    /// <summary>Waits for active operations, then discards unpublished changes and releases the writer lock.</summary>
    public void Dispose()
    {
        lock (_lifetime)
        {
            if (_closing)
            {
                while (!_closed) Monitor.Wait(_lifetime);
                return;
            }
            _closing = true;
            while (_operations != 0) Monitor.Wait(_lifetime);
            try
            {
                try { _searchers.Dispose(); }
                finally
                {
                    try { _writer.Rollback(); }
                    finally { _analyzer.Dispose(); _directory.Dispose(); }
                }
            }
            finally { _closed = true; Monitor.PulseAll(_lifetime); }
        }
    }

    private readonly record struct Operation(DocumentSearchIndex Owner) : IDisposable
    {
        public void Dispose() => Owner.Leave();
    }
    private sealed record Prepared(SearchChange Change, string Key, string Fingerprint);
    private sealed record Revision(long Version, string Fingerprint);
    private sealed class FixedGramAnalyzer(int gram) : Analyzer
    {
        protected override TokenStreamComponents CreateComponents(string fieldName, TextReader reader)
        {
            var tokenizer = new NGramTokenizer(LuceneVersion.LUCENE_48, reader, gram, gram);
            return new(tokenizer);
        }
    }
    private sealed class Bm25Factory : SearcherFactory
    {
        public override IndexSearcher NewSearcher(IndexReader reader) => new(reader) { Similarity = new BM25Similarity() };
    }
}
