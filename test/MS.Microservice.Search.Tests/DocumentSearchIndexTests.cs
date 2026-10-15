using System.Text;
using Lucene.Net.Index;
using Lucene.Net.Index.Sorter;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Xunit;

namespace MS.Microservice.Search.Tests;

public sealed class DocumentSearchIndexTests
{
    [Fact]
    public void RevisionsAndTombstonesSurviveRestart_WithoutCrossScopeUpdatesOrResurrection()
    {
        using var fixture = new IndexFixture();
        var first = new SearchDocument("Owner", "id", 7, "report.pdf", "/reports/report.pdf", "document text");
        using (var index = fixture.Open())
        {
            Assert.Equal(new SearchBatchResult(2, 0, 0), index.ApplyBatch([
                SearchChange.Upsert(first), SearchChange.Upsert(first with { Scope = "owner" })]));
            Assert.Equal(1, index.ApplyBatch([SearchChange.Upsert(first)]).Unchanged);
            Assert.Throws<SearchVersionConflictException>(() => index.ApplyBatch([SearchChange.Upsert(first with { Content = "different" })]));
            index.Publish();
            Assert.Single(index.Search(new("Owner", "report")).Items);
            Assert.Empty(index.Search(new("other", "report")).Items);
            Assert.Equal(1, index.ApplyBatch([SearchChange.Delete("Owner", "id", 8)]).Applied);
            index.Publish();
        }
        using (var index = fixture.Open())
        {
            Assert.Equal(1, index.ApplyBatch([SearchChange.Upsert(first)]).Stale);
            Assert.Equal(1, index.ApplyBatch([SearchChange.Delete("Owner", "id", 8)]).Unchanged);
            Assert.Throws<SearchVersionConflictException>(() => index.ApplyBatch([SearchChange.Upsert(first with { Version = 8 })]));
            index.Publish();
            Assert.Empty(index.Search(new("Owner", "report")).Items);
            Assert.Single(index.Search(new("owner", "report")).Items);
            Assert.Equal(1, index.ApplyBatch([SearchChange.Upsert(first with { Version = 9 })]).Applied);
            index.Publish();
            Assert.Single(index.Search(new("Owner", "report")).Items);
        }
    }

    [Fact]
    public void PublicationIsExplicit_AndDisposeReleasesTheWriterWithoutPublishingPendingChanges()
    {
        using var fixture = new IndexFixture();
        using (var index = fixture.Open())
        {
            index.ApplyBatch([SearchChange.Upsert(new("s", "1", 1, "report.txt", "/report.txt"))]);
            Assert.Empty(index.Search(new("s", "report")).Items);
            Assert.Throws<LockObtainFailedException>(() => fixture.Open());
        }
        using var restored = fixture.Open();
        Assert.Empty(restored.Search(new("s", "report")).Items);
        restored.ApplyBatch([SearchChange.Upsert(new("s", "1", 1, "report.txt", "/report.txt"))]);
        restored.Publish();
        Assert.Single(restored.Search(new("s", "report")).Items);
    }

    [Fact]
    public void SubstringsAndPrefixesMatchPositions_IncludingShortHanSupplementaryAndNormalizedUnicode()
    {
        using var fixture = new IndexFixture();
        using var index = fixture.Open();
        index.ApplyBatch([
            SearchChange.Upsert(new("s", "good", 1, "财务报告_abcab_𠮷_cafe\u0301.pdf", "/a/report.pdf")),
            SearchChange.Upsert(new("s", "false", 1, "abXbc.pdf", "/ab/report.pdf")),
            SearchChange.Upsert(new("other", "other", 1, "财务报告_abcab.pdf", "/a/other.pdf"))]);
        index.Publish();
        foreach (var query in new[] { "财", "财务", "务报告", "abcab", "𠮷", "CAFÉ" })
            Assert.Equal("good", Assert.Single(index.Search(new("s", query)).Items).DocumentId);
        Assert.Equal("good", Assert.Single(index.Search(new("s", "abc")).Items).DocumentId);
        Assert.Empty(index.Search(new("s", "abc", Match: SearchMatch.Prefix)).Items);
        Assert.Equal("good", Assert.Single(index.Search(new("s", "财务", Match: SearchMatch.Prefix)).Items).DocumentId);
        Assert.Equal("good", Assert.Single(index.Search(new("s", "/a/", Target: SearchTarget.Path, Match: SearchMatch.Prefix)).Items).DocumentId);
        Assert.Equal("good", Assert.Single(index.Search(new("s", "pdf", DirectoryPrefix: "/a")).Items).DocumentId);
    }

    [Fact]
    public void FastAndExactKeepGlobalOrderAndAccurateHasMore_AcrossSortedAndNewUnsortedSegmentsAndPages()
    {
        using var fixture = new IndexFixture();
        using var index = fixture.Open();
        index.ApplyBatch(Enumerable.Range(0, 100).Reverse().Select(i => SearchChange.Upsert(
            new("s", i.ToString("D3"), 1, "report.txt", $"/docs/{i:D3}.txt"))));
        index.Optimize();
        using (var directory = FSDirectory.Open(fixture.Path))
        using (var reader = DirectoryReader.Open(directory))
            Assert.All(reader.Leaves, leaf => Assert.True(SortingMergePolicy.IsSorted(leaf.AtomicReader,
                new Sort(new SortField("path_sort", SortFieldType.STRING), new SortField("id_sort", SortFieldType.STRING)))));
        index.ApplyBatch([SearchChange.Upsert(new("s", "new", 1, "report.txt", "/aaa/report.txt"))]);
        index.Publish();
        var request = new SearchRequest("s", "report");
        var fast = index.Search(request);
        var exact = index.Search(request with { CountMode = SearchCountMode.Exact });
        Assert.Null(fast.TotalHits);
        Assert.Equal(101L, exact.TotalHits);
        Assert.True(fast.HasMore);
        Assert.Equal(50, fast.Items.Count);
        Assert.Equal("new", fast.Items[0].DocumentId);
        Assert.Equal(exact.Items.Select(hit => hit.DocumentId), fast.Items.Select(hit => hit.DocumentId));
        var nextFast = index.Search(request with { Cursor = fast.NextCursor });
        var nextExact = index.Search(request with { Cursor = fast.NextCursor, CountMode = SearchCountMode.Exact });
        Assert.Equal(nextExact.Items.Select(hit => hit.DocumentId), nextFast.Items.Select(hit => hit.DocumentId));
        Assert.Equal(101L, nextExact.TotalHits);
        Assert.DoesNotContain(nextFast.Items, hit => fast.Items.Any(first => first.DocumentId == hit.DocumentId));
        var last = index.Search(request with { Cursor = nextFast.NextCursor });
        Assert.Single(last.Items);
        Assert.False(last.HasMore);
        Assert.Null(last.NextCursor);
        Assert.Throws<ArgumentException>(() => index.Search(request with { Scope = "other", Cursor = fast.NextCursor }));
        index.ApplyBatch([SearchChange.Delete("s", "new", 2)]);
        index.Publish();
        Assert.Throws<SearchCursorExpiredException>(() => index.Search(request with { Cursor = fast.NextCursor }));
    }

    [Fact]
    public void ContentSupportsHanCharactersAndPhrases_AndFiltersBeforeTopK_WithOptionalOriginalTextExcerpt()
    {
        using var fixture = new IndexFixture();
        using var index = fixture.Open();
        var modified = new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.FromHours(8));
        index.ApplyBatch([
            SearchChange.Upsert(new("s", "1", 1, "report.PDF", "/docs/report.pdf", "Quick brown fox 中文检索性能 cafe\u0301", 42, modified)),
            SearchChange.Upsert(new("s", "2", 1, "other.txt", "/docs/other.txt", "𠮷brown quick fox 文 archive")),
            SearchChange.Upsert(new("other", "3", 1, "report.pdf", "/docs/private.pdf", "Quick brown fox 中文"))]);
        index.Publish();
        Assert.Equal(2, index.Search(new("s", "文", SearchTarget.Content)).Items.Count);
        Assert.Equal(2, index.Search(new("s", "quick fox", SearchTarget.Content)).Items.Count);
        var request = new SearchRequest("s", "quick brown fox", SearchTarget.Content, SearchMatch.Phrase,
            SearchCountMode.Exact, Extension: ".pdf", ModifiedFromUtc: modified.AddMinutes(-1),
            ModifiedToUtc: modified.AddMinutes(1), SnippetLength: 30);
        var result = index.Search(request);
        var hit = Assert.Single(result.Items);
        Assert.Equal(SearchSort.Path, result.Sort);
        Assert.Equal(1L, result.TotalHits);
        Assert.StartsWith("Quick brown fox", hit.Snippet);
        Assert.Equal(TimeSpan.Zero, hit.ModifiedAtUtc!.Value.Offset);
        Assert.Null(hit.Score);
        Assert.NotNull(Assert.Single(index.Search(request with { Sort = SearchSort.Relevance }).Items).Score);
        Assert.Equal("1", Assert.Single(index.Search(new("s", "检索性能", SearchTarget.Content, SearchMatch.Phrase)).Items).DocumentId);
        Assert.Equal("1", Assert.Single(index.Search(new("s", "CAFÉ", SearchTarget.Content)).Items).DocumentId);
        Assert.Null(Assert.Single(index.Search(request with { Sort = SearchSort.Path, SnippetLength = 0 }).Items).Snippet);
        Assert.Equal("", Assert.Single(index.Search(new("s", "archive", SearchTarget.Content, SnippetLength: 1)).Items).Snippet);
    }

    [Fact]
    public async Task InvalidOrConflictingBatchDoesNotBufferEarlierEntries_AndConcurrentReadersRemainUsable()
    {
        using var fixture = new IndexFixture();
        using var index = fixture.Open();
        var known = new SearchDocument("s", "known", 2, "report.txt", "/report.txt");
        index.ApplyBatch([SearchChange.Upsert(known)]);
        index.Publish();
        Assert.Throws<SearchVersionConflictException>(() => index.ApplyBatch([
            SearchChange.Upsert(known with { DocumentId = "must-not-exist" }), SearchChange.Upsert(known with { Content = "changed" })]));
        Assert.Throws<EncoderFallbackException>(() => index.ApplyBatch([SearchChange.Upsert(known with { FileName = "bad\uD800" })]));
        Assert.Throws<OperationCanceledException>(() => index.ApplyBatch([SearchChange.Delete("s", "known", 3)], new CancellationToken(true)));
        index.Publish();
        Assert.Single(index.Search(new("s", "report")).Items);
        var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 10; i++) Assert.Single(index.Search(new("s", "report")).Items);
        }));
        await Task.WhenAll(readers.Append(Task.Run(() =>
        {
            index.ApplyBatch([SearchChange.Upsert(known with { Version = 3, Content = "new text" })]);
            index.Publish();
        })));
        index.Dispose();
        Assert.Throws<ObjectDisposedException>(() => index.Search(new("s", "report")));
    }

    private sealed class IndexFixture : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ms-search-tests-" + Guid.NewGuid().ToString("N"));
        public DocumentSearchIndex Open() => new(new SearchIndexOptions { DirectoryPath = Path });
        public void Dispose() { if (System.IO.Directory.Exists(Path)) System.IO.Directory.Delete(Path, recursive: true); }
    }
}
