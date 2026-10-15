namespace MS.Microservice.Search;

public sealed record SearchIndexOptions
{
    public required string DirectoryPath { get; init; }
    public int MaximumBatchSize { get; init; } = 10_000;
    public int MaximumContentCharacters { get; init; } = 2_000_000;
    public long MaximumBatchContentCharacters { get; init; } = 16_000_000;
    public double WriteBufferMegabytes { get; init; } = 128;
}

/// <summary>Host-owned document identity and revision; Content must already be extracted text.</summary>
public sealed record SearchDocument(string Scope, string DocumentId, long Version, string FileName, string Path,
    string Content = "", long LengthBytes = 0, DateTimeOffset? ModifiedAtUtc = null);

public enum SearchChangeKind { Upsert, Delete }

/// <summary>A higher revision replaces the indexed state; deletion retains a revision tombstone.</summary>
public sealed record SearchChange
{
    public SearchChangeKind Kind { get; }
    public string Scope { get; }
    public string DocumentId { get; }
    public long Version { get; }
    public SearchDocument? Document { get; }
    private SearchChange(SearchChangeKind kind, string scope, string documentId, long version, SearchDocument? document)
        => (Kind, Scope, DocumentId, Version, Document) = (kind, scope, documentId, version, document);
    public static SearchChange Upsert(SearchDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new(SearchChangeKind.Upsert, document.Scope, document.DocumentId, document.Version, document);
    }
    public static SearchChange Delete(string scope, string documentId, long version)
        => new(SearchChangeKind.Delete, scope, documentId, version, null);
}

public sealed record SearchBatchResult(int Applied, int Stale, int Unchanged);
public enum SearchTarget { FileName, Path, Content }
public enum SearchMatch { Contains, Prefix, AllTerms, Phrase }
public enum SearchCountMode { Fast, Exact }
public enum SearchSort { Path, Relevance }

/// <summary>Scope is mandatory; all targets default to path order, with substring metadata or all-terms content matching.</summary>
public sealed record SearchRequest(string Scope, string Query, SearchTarget Target = SearchTarget.FileName,
    SearchMatch? Match = null, SearchCountMode CountMode = SearchCountMode.Fast, int Limit = 50,
    SearchSort? Sort = null, string? Extension = null, string? DirectoryPrefix = null,
    DateTimeOffset? ModifiedFromUtc = null, DateTimeOffset? ModifiedToUtc = null,
    int SnippetLength = 0, string? Cursor = null);

public sealed record SearchHit(string DocumentId, long Version, string FileName, string Path, long LengthBytes,
    DateTimeOffset? ModifiedAtUtc, string? Snippet, float? Score);

/// <summary>Fast omits TotalHits; HasMore remains accurate. A cursor is valid only for its current index generation.</summary>
public sealed record SearchResult(long IndexGeneration, SearchSort Sort, IReadOnlyList<SearchHit> Items,
    bool HasMore, long? TotalHits, string? NextCursor);

public sealed class SearchVersionConflictException(string scope, string documentId, long version)
    : InvalidOperationException($"Document {scope}/{documentId} revision {version} already represents different content.");

public sealed class SearchCursorExpiredException()
    : InvalidOperationException("The search index changed; restart the search from its first page.");
