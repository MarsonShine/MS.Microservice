using MS.Microservice.Search;

internal static class LongDocumentCases
{
    public static void Run()
    {
        var directory = Directory.CreateTempSubdirectory("ms-search-benchmark-");
        try
        {
            using var index = new DocumentSearchIndex(new SearchIndexOptions { DirectoryPath = directory.FullName });
            const string segment = "Long 正文😀 audit ";
            string body = string.Concat(Enumerable.Repeat(segment, 1 + 131072 / segment.Length))[..131072];
            var documents = Enumerable.Range(0, 12)
                .Select(i => new SearchDocument("scope", $"{i:D2}", 2, $"report{i:D2}.txt", $"/a/{i:D2}", body))
                .ToArray();
            index.ApplyBatch(documents.Select(SearchChange.Upsert));
            index.Publish();
            Console.WriteLine($"Search / published index: {directory.EnumerateFiles().Sum(file => file.Length)} B");

            SearchChange[] unchanged = [SearchChange.Upsert(documents[0])];
            SearchChange[] stale = [SearchChange.Upsert(documents[0] with { Version = 1 })];
            MeasuredOperation.Run("Search / unchanged 128K", 24, () => index.ApplyBatch(unchanged),
                result => result == new SearchBatchResult(0, 0, 1));
            MeasuredOperation.Run("Search / stale 128K", 24, () => index.ApplyBatch(stale),
                result => result == new SearchBatchResult(0, 1, 0));

            var first = new SearchRequest("scope", "report", Limit: 8);
            string cursor = index.Search(first).NextCursor ?? throw new InvalidDataException("Expected a continuation.");
            CheckPage("Search / first page no snippet", first, 0, 8, more: true);
            CheckPage("Search / first page snippet 160", first with { SnippetLength = 160 }, 0, 8, more: true);
            CheckPage("Search / last page with cursor", first with { Cursor = cursor }, 8, 4, more: false);
            CheckPage("Search / single page", first with { Query = "report00" }, 0, 1, more: false);
            CheckPage("Search / no hits", first with { Query = "missing" }, 0, 0, more: false);

            void CheckPage(string name, SearchRequest request, int start, int count, bool more)
            {
                var ids = documents.Skip(start).Take(count).Select(document => document.DocumentId).ToArray();
                string? snippet = request.SnippetLength == 0 ? null : body[..request.SnippetLength];
                MeasuredOperation.Run(name, 40, () => index.Search(request),
                    page => page.HasMore == more && (page.NextCursor is not null) == more && page.TotalHits is null
                        && page.Items.Select(hit => hit.DocumentId).SequenceEqual(ids)
                        && page.Items.All(hit => hit.Snippet == snippet && hit.Version == 2));
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
