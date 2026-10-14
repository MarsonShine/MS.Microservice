using System.Globalization;
using System.Text;
using System.Text.Json;
using Lucene.Net.Analysis.TokenAttributes;
using Lucene.Net.Index;
using Lucene.Net.Index.Sorter;
using Lucene.Net.Search;
using Lucene.Net.Search.Spans;
using Lucene.Net.Util;

namespace MS.Microservice.Search;

public sealed partial class DocumentSearchIndex
{
    private static readonly HashSet<string> ResultFields = ["id", "version", "name", "path", "length", "modified"];

    /// <summary>Searches a committed snapshot; Fast omits the total, while Exact counts every match with identical ordering.</summary>
    public SearchResult Search(SearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var lease = Enter();
        var match = request.Match ?? (request.Target == SearchTarget.Content ? SearchMatch.AllTerms : SearchMatch.Contains);
        var order = request.Sort ?? SearchSort.Path;
        ValidateRequest(request, match, order);
        cancellationToken.ThrowIfCancellationRequested();
        var query = BuildQuery(request, match);
        var signature = Hash(_identity, request.Scope, request.Target == SearchTarget.Path ? NormalizePath(request.Query) : Normalize(request.Query),
            request.Target.ToString(), match.ToString(), order.ToString(),
            request.Extension is null ? "<any>" : Normalize(request.Extension.TrimStart('.')),
            request.DirectoryPrefix is null ? "<any>" : DirectoryPrefix(request.DirectoryPrefix),
            request.ModifiedFromUtc?.UtcTicks.ToString(CultureInfo.InvariantCulture) ?? "",
            request.ModifiedToUtc?.UtcTicks.ToString(CultureInfo.InvariantCulture) ?? "");
        var searcher = _searchers.Acquire();
        try
        {
            var generation = ((DirectoryReader)searcher.IndexReader).Version;
            var cursor = ReadCursor(request.Cursor, generation, signature, searcher.IndexReader.MaxDoc);
            ITopDocsCollector top;
            ICollector collector;
            if (order == SearchSort.Path)
            {
                var after = cursor is null ? null : new FieldDoc(cursor.Document, 0, [new BytesRef(cursor.Path), new BytesRef(cursor.Id)]);
                top = TopFieldCollector.Create(PathSort, request.Limit + 1, after,
                    fillFields: true, trackDocScores: false, trackMaxScore: false, docsScoredInOrder: true);
                collector = (ICollector)top;
                // Paging collectors also see hits before their cursor; applying this wrapper there would truncate valid later hits.
                if (request.CountMode == SearchCountMode.Fast && cursor is null)
                    collector = new EarlyTerminatingSortingCollector(collector, PathSort, request.Limit + 1);
            }
            else
            {
                var after = cursor is null ? null : new ScoreDoc(cursor.Document, cursor.Score);
                top = TopScoreDocCollector.Create(request.Limit + 1, after, docsScoredInOrder: true);
                collector = (ICollector)top;
            }
            searcher.Search(query, new CancellationCollector(collector, cancellationToken));
            var found = top.GetTopDocs();
            var hasMore = found.ScoreDocs.Length > request.Limit;
            var fields = request.SnippetLength == 0 ? ResultFields : new HashSet<string>(ResultFields) { "body_text" };
            var results = new List<SearchHit>(Math.Min(request.Limit, found.ScoreDocs.Length));
            foreach (var hit in found.ScoreDocs.Take(request.Limit))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var document = searcher.Doc(hit.Doc, fields);
                var text = document.Get("body_text");
                var modified = document.Get("modified");
                results.Add(new(document.Get("id"), long.Parse(document.Get("version"), CultureInfo.InvariantCulture),
                    document.Get("name"), document.Get("path"), long.Parse(document.Get("length"), CultureInfo.InvariantCulture),
                    modified is null ? null : new DateTimeOffset(long.Parse(modified, CultureInfo.InvariantCulture), TimeSpan.Zero),
                    text is null ? null : Excerpt(text, request.SnippetLength),
                    order == SearchSort.Relevance ? hit.Score : null));
            }
            string? next = null;
            if (hasMore && results.Count > 0)
            {
                var last = results[^1];
                var lastScore = found.ScoreDocs[results.Count - 1];
                next = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new CursorData(generation, signature,
                    lastScore.Doc, order == SearchSort.Relevance ? lastScore.Score : 0, NormalizePath(last.Path), last.DocumentId)));
            }
            return new(generation, order, results.AsReadOnly(), hasMore,
                request.CountMode == SearchCountMode.Exact ? found.TotalHits : null, next);
        }
        finally { _searchers.Release(searcher); }
    }

    private Query BuildQuery(SearchRequest request, SearchMatch match)
    {
        var field = request.Target switch { SearchTarget.FileName => "name", SearchTarget.Path => "path", _ => "body" };
        var text = request.Target == SearchTarget.Path ? NormalizePath(request.Query) : Normalize(request.Query);
        Query query;
        if (request.Target != SearchTarget.Content)
        {
            field += "_g" + Math.Min(3, text.EnumerateRunes().Count());
            var tokens = Tokens(field, text);
            if (match == SearchMatch.Prefix)
            {
                SpanQuery[] terms = tokens.Select(token => (SpanQuery)new SpanTermQuery(new Term(field, token.Term))).ToArray();
                SpanQuery span = terms.Length == 1 ? terms[0] : new SpanNearQuery(terms, slop: 0, inOrder: true);
                query = new SpanFirstQuery(span, tokens.Count);
            }
            else query = Phrase(field, tokens);
        }
        else
        {
            var tokens = Tokens(field, text);
            if (tokens.Count == 0) throw new ArgumentException("The query contains no searchable terms.", nameof(request));
            if (match == SearchMatch.Phrase) query = Phrase(field, tokens);
            else
            {
                var all = new BooleanQuery();
                foreach (var term in tokens.Select(token => token.Term).Distinct(StringComparer.Ordinal))
                    all.Add(new TermQuery(new Term(field, term)), Occur.MUST);
                query = all;
            }
        }
        var filtered = new BooleanQuery
        {
            { query, Occur.MUST },
            { new TermQuery(new Term("scope", Hash(request.Scope))), Occur.MUST },
            { new TermQuery(new Term("live", "1")), Occur.MUST }
        };
        if (request.Extension is not null)
            filtered.Add(new TermQuery(new Term("extension", Normalize(request.Extension.TrimStart('.')))), Occur.MUST);
        if (request.DirectoryPrefix is not null)
        {
            var prefix = DirectoryPrefix(request.DirectoryPrefix);
            var fieldName = "path_g" + Math.Min(3, prefix.EnumerateRunes().Count());
            var tokens = Tokens(fieldName, prefix);
            SpanQuery[] terms = tokens.Select(token => (SpanQuery)new SpanTermQuery(new Term(fieldName, token.Term))).ToArray();
            filtered.Add(new SpanFirstQuery(terms.Length == 1 ? terms[0] : new SpanNearQuery(terms, 0, true), tokens.Count), Occur.MUST);
        }
        if (request.ModifiedFromUtc is not null || request.ModifiedToUtc is not null)
            filtered.Add(NumericRangeQuery.NewInt64Range("modified", request.ModifiedFromUtc?.UtcTicks,
                request.ModifiedToUtc?.UtcTicks, true, true), Occur.MUST);
        return filtered;
    }

    private List<(string Term, int Position)> Tokens(string field, string text)
    {
        using var stream = _analyzer.GetTokenStream(field, text);
        var term = stream.AddAttribute<ICharTermAttribute>();
        var position = stream.AddAttribute<IPositionIncrementAttribute>();
        var tokens = new List<(string, int)>();
        var absolute = -1;
        stream.Reset();
        while (stream.IncrementToken()) { absolute += position.PositionIncrement; tokens.Add((term.ToString(), absolute)); }
        stream.End();
        return tokens;
    }

    private static Query Phrase(string field, List<(string Term, int Position)> tokens)
    {
        if (tokens.Count == 1) return new TermQuery(new Term(field, tokens[0].Term));
        var phrase = new PhraseQuery();
        foreach (var token in tokens) phrase.Add(new Term(field, token.Term), token.Position);
        return phrase;
    }

    private static void ValidateRequest(SearchRequest request, SearchMatch match, SearchSort order)
    {
        ValidateText(request.Scope, 256, nameof(request.Scope));
        ValidateText(request.Query, 256, nameof(request.Query));
        if (!Enum.IsDefined(request.Target) || !Enum.IsDefined(match) || !Enum.IsDefined(order) || !Enum.IsDefined(request.CountMode)
            || request.Limit is < 1 or > 200 || request.SnippetLength is < 0 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(request));
        if (request.Target != SearchTarget.Content && (match is not (SearchMatch.Contains or SearchMatch.Prefix) || order != SearchSort.Path)
            || request.Target == SearchTarget.Content && match is not (SearchMatch.AllTerms or SearchMatch.Phrase))
            throw new ArgumentException("Metadata supports Contains/Prefix with Path order; content supports AllTerms/Phrase.", nameof(request));
        if (request.Extension is not null) ValidateText(request.Extension, 64, nameof(request.Extension), allowEmpty: true);
        if (request.DirectoryPrefix is not null) ValidateText(request.DirectoryPrefix, 256, nameof(request.DirectoryPrefix));
        if (request.ModifiedFromUtc?.UtcTicks > request.ModifiedToUtc?.UtcTicks)
            throw new ArgumentException("ModifiedFromUtc must not follow ModifiedToUtc.", nameof(request));
        if (request.Cursor?.Length > 65_536) throw new ArgumentOutOfRangeException(nameof(request.Cursor));
    }

    private static string DirectoryPrefix(string value) => NormalizePath(value).TrimEnd('/') + "/";

    private static string Excerpt(string value, int maximum)
    {
        var end = Math.Min(maximum, value.Length);
        if (end > 0 && end < value.Length && char.IsHighSurrogate(value[end - 1])) end--;
        return value[..end];
    }

    private static CursorData? ReadCursor(string? value, long generation, string signature, int maxDoc)
    {
        if (value is null) return null;
        CursorData? cursor;
        try { cursor = JsonSerializer.Deserialize<CursorData>(Convert.FromBase64String(value)); }
        catch (Exception error) when (error is FormatException or JsonException)
        { throw new ArgumentException("The search cursor is invalid.", nameof(value), error); }
        if (cursor is null || cursor.Signature != signature || cursor.Document < 0
            || !float.IsFinite(cursor.Score) || cursor.Path is null || cursor.Id is null)
            throw new ArgumentException("The search cursor does not belong to this query.", nameof(value));
        if (cursor.Generation != generation) throw new SearchCursorExpiredException();
        if (cursor.Document >= maxDoc) throw new ArgumentException("The search cursor refers to a missing document.", nameof(value));
        return cursor;
    }

    private sealed record CursorData(long Generation, string Signature, int Document, float Score, string Path, string Id);
    private sealed class CancellationCollector(ICollector inner, CancellationToken cancellationToken) : ICollector
    {
        private int _collected;
        public bool AcceptsDocsOutOfOrder => inner.AcceptsDocsOutOfOrder;
        public void SetScorer(Scorer scorer) => inner.SetScorer(scorer);
        public void SetNextReader(AtomicReaderContext context) { cancellationToken.ThrowIfCancellationRequested(); inner.SetNextReader(context); }
        public void Collect(int doc)
        {
            if ((_collected++ & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
            inner.Collect(doc);
        }
    }
}
