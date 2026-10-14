using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lucene.Net.Index;
using Lucene.Net.Index.Sorter;
using Lucene.Net.Search;
using Lucene.Net.Store;
using MS.Microservice.Search;

const int seed = 20261014;
var count = Number("--metadata", 1_000_000);
var contentCount = Number("--content", 100_000);
var samples = Number("--samples", 60);
if (count < contentCount || contentCount < 1 || samples < 20) throw new ArgumentOutOfRangeException(nameof(args));
var output = Path.GetFullPath(Value("--output", "artifacts/search-benchmark/results-fast-exact.json"));
var indexPath = Path.GetFullPath(Value("--index", Path.Combine(Path.GetDirectoryName(output)!, "index-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"))));
var reuse = args.Contains("--reuse-index");
var process = Process.GetCurrentProcess();
var cases = new List<Scenario>
{
    new("name-common", "report"), new("name-latin-one", "a"), new("name-latin-two", "re"),
    new("name-han-one", "财"), new("name-han-phrase", "财务报告"), new("name-supplementary", "𠮷"),
    new("path-common", "/2025/", SearchTarget.Path), new("name-unique", "00000042"), new("name-missing", "notpresentxyz"),
    new("path-prefix", "/departments/tenant-3/", SearchTarget.Path, SearchMatch.Prefix),
    new("scope-filter", "report", Scope: "other"),
    new("body-common", "document", SearchTarget.Content, Sort: SearchSort.Relevance),
    new("body-topic", "financial", SearchTarget.Content, Sort: SearchSort.Relevance),
    new("body-han-one", "文", SearchTarget.Content, Sort: SearchSort.Relevance),
    new("body-han-phrase", "检索性能", SearchTarget.Content, SearchMatch.Phrase, Sort: SearchSort.Relevance),
    new("body-english-phrase", "quick brown fox", SearchTarget.Content, SearchMatch.Phrase, Sort: SearchSort.Relevance),
    new("body-missing", "nonexistentxyz", SearchTarget.Content, Sort: SearchSort.Relevance),
    new("body-path-order", "文", SearchTarget.Content, Sort: SearchSort.Path),
    new("body-han-phrase-path", "检索性能", SearchTarget.Content, SearchMatch.Phrase, Sort: SearchSort.Path)
};
var selectedCase = Value("--case", "");
if (selectedCase.Length > 0)
{
    cases = cases.Where(scenario => scenario.Name == selectedCase).ToList();
    if (cases.Count == 0) throw new ArgumentException("Unknown --case scenario.");
}
using var index = new DocumentSearchIndex(new SearchIndexOptions { DirectoryPath = indexPath });
var importWatch = Stopwatch.StartNew();
var metadataRandom = new Random(seed);
var contentRandom = new Random(seed);
long contentCharacters = 0;
var batch = new List<SearchChange>(10_000);
for (var id = 0; id < count; id++)
{
    var document = MakeDocument(id, metadataRandom, id < contentCount ? MakeBody(id, contentRandom) : "");
    contentCharacters += document.Content.Length;
    foreach (var scenario in cases) if (Matches(document, scenario)) scenario.Expected++;
    if (!reuse) batch.Add(SearchChange.Upsert(document));
    if (batch.Count == 10_000)
    {
        index.ApplyBatch(batch);
        batch.Clear();
        if ((id + 1) % 100_000 == 0) Console.WriteLine($"Imported {id + 1:N0}/{count:N0} in {importWatch.Elapsed.TotalSeconds:F1}s");
    }
}
for (var id = 0; id < 100; id++)
{
    var document = new SearchDocument("other", "private-" + id, 1, "report.txt", $"/private/{id:D3}.txt");
    foreach (var scenario in cases) if (Matches(document, scenario)) scenario.Expected++;
    if (!reuse) batch.Add(SearchChange.Upsert(document));
}
if (!reuse) { index.ApplyBatch(batch); index.Publish(); }
importWatch.Stop();
var optimizeWatch = Stopwatch.StartNew();
if (!reuse) index.Optimize();
optimizeWatch.Stop();
GC.Collect();
GC.WaitForPendingFinalizers();
GC.Collect();
var measurements = new List<object>();
foreach (var scenario in cases)
{
    var request = new SearchRequest(scenario.Scope, scenario.Text, scenario.Target, scenario.Match,
        Sort: scenario.Sort, SnippetLength: scenario.Target == SearchTarget.Content ? 160 : 0);
    var fast = index.Search(request);
    var exact = index.Search(request with { CountMode = SearchCountMode.Exact });
    if (fast.TotalHits is not null || exact.TotalHits != scenario.Expected || fast.HasMore != (scenario.Expected > 50)
        || !fast.Items.Select(hit => hit.DocumentId).SequenceEqual(exact.Items.Select(hit => hit.DocumentId)))
        throw new InvalidOperationException($"Fast/Exact contract mismatch: {scenario.Name}");
    foreach (var mode in new[] { SearchCountMode.Fast, SearchCountMode.Exact })
    {
        var query = request with { CountMode = mode };
        for (var i = 0; i < 8; i++) _ = index.Search(query);
        var values = new double[samples];
        long allocations = 0;
        for (var i = 0; i < samples; i++)
        {
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var started = Stopwatch.GetTimestamp();
            var result = index.Search(query);
            values[i] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            allocations += GC.GetAllocatedBytesForCurrentThread() - allocated;
            GC.KeepAlive(result);
        }
        Array.Sort(values);
        process.Refresh();
        var measurement = new
        {
            scenario.Name, scenario.Text, scenario.Target, scenario.Scope, scenario.Expected,
            Sort = fast.Sort.ToString(), CountMode = mode.ToString(), Samples = samples,
            P50Milliseconds = Percentile(values, .5), P95Milliseconds = Percentile(values, .95), P99Milliseconds = Percentile(values, .99),
            MeanAllocatedBytes = (double)allocations / samples, ProcessWorkingSetBytes = process.WorkingSet64
        };
        measurements.Add(measurement);
        Console.WriteLine(JsonSerializer.Serialize(measurement));
    }
}
int segments;
int sortedSegments;
using (var directory = FSDirectory.Open(indexPath))
using (var reader = DirectoryReader.Open(directory))
{
    segments = reader.Leaves.Count;
    var sort = new Sort(new SortField("path_sort", SortFieldType.STRING), new SortField("id_sort", SortFieldType.STRING));
    sortedSegments = reader.Leaves.Count(leaf => SortingMergePolicy.IsSorted(leaf.AtomicReader, sort));
}
process.Refresh();
var report = new
{
    TimestampUtc = DateTimeOffset.UtcNow, Seed = seed,
    Environment = new
    {
        Framework = RuntimeInformation.FrameworkDescription, OS = RuntimeInformation.OSDescription,
        Cpu = Environment.GetEnvironmentVariable("SEARCH_POC_CPU") ?? "unavailable",
        LogicalProcessors = Environment.ProcessorCount, RuntimeAvailableMemoryBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
        EngineAssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(DocumentSearchIndex).Assembly.Location)))
    },
    MetadataDocuments = count, ContentDocuments = contentCount, OtherScopeDocuments = 100, ContentCharacters = contentCharacters,
    ImportSeconds = reuse ? (double?)null : importWatch.Elapsed.TotalSeconds,
    OptimizeSeconds = reuse ? (double?)null : optimizeWatch.Elapsed.TotalSeconds,
    IndexBytes = System.IO.Directory.EnumerateFiles(indexPath, "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length),
    PeakWorkingSetBytes = process.PeakWorkingSet64,
    IndexPath = indexPath, Segments = segments, SortedSegments = sortedSegments,
    Semantics = new
    {
        Metadata = "Exact NFC/invariant-lowercase Unicode substring; fixed 1/2/3-gram positional queries. Prefix is anchored SpanFirst, no leading wildcard.",
        PathOrder = "Global normalized path UTF-8 byte lexicographic then DocumentId byte order, identical Fast/Exact.",
        ContentOrder = "Relevance cases use BM25 then snapshot-local Lucene docID; Path cases use global path/DocumentId byte order.",
        Fast = "First 50 plus accurate HasMore; TotalHits=null. Path order may terminate sorted segments after 51 matches. Relevance still evaluates all candidates.",
        Exact = "First 50 and exact TotalHits, same matches/order as Fast. No early termination.",
        Fields = "Returned metadata and optionally first 160 UTF-16 chars of original stored body. No highlighting.",
        Corpus = "Fixed synthetic names/paths and highly repetitive short bilingual documents (~753 UTF-16 chars); 1M rows share main authenticated scope; 100 other-scope rows excluded.",
        Timing = "Single sequential query thread; 8 warmups and fixed samples; includes request validation, analysis, filtering, collection, stored fields, cursor serialization and result allocation.",
        Limits = "Warm FSDirectory/OS cache, no HTTP, no extraction/OCR, no concurrent writes, no cold-cache or real-long-document performance claim. No engine comparison."
    },
    Queries = measurements
};
System.IO.Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"REPORT {output}");

static SearchDocument MakeDocument(int id, Random random, string body)
{
    string[] labels = ["report", "financial", "财务报告", "照片", "analysis", "archive", "design", "会议纪要", "𠮷野记录", "status", "sample", "技术文档"];
    var name = $"{labels[random.Next(labels.Length)]}_{id:D8}.pdf";
    return new("main", id.ToString("D8", CultureInfo.InvariantCulture), 1, name,
        $"/departments/tenant-{id % 8}/team-{id % 17}/{2023 + id % 4}/{name}", body);
}
static string MakeBody(int id, Random random)
{
    string[] labels = ["report", "financial", "财务报告", "照片", "analysis", "archive", "design", "会议纪要", "𠮷野记录", "status", "sample", "技术文档"];
    _ = random.Next(labels.Length);
    var topic = labels[random.Next(labels.Length)];
    var paragraphs = 4 + random.Next(5);
    var builder = new StringBuilder($"Document {id:D8}. {topic}. ");
    var phrase = id % 4 == 0 ? "quick brown fox" : "quick red fox";
    for (var i = 0; i < paragraphs; i++)
        builder.Append($"The {phrase} studies {topic} in quarterly operations. 中文文档内容，检索性能需要真实验证。部门记录与权限隔离。 Identifier {id:D8}, paragraph {i}. ");
    return builder.ToString();
}
static bool Matches(SearchDocument document, Scenario scenario)
{
    if (document.Scope != scenario.Scope) return false;
    var source = scenario.Target switch { SearchTarget.FileName => document.FileName, SearchTarget.Path => document.Path, _ => document.Content };
    source = source.Normalize().ToLowerInvariant();
    return scenario.Match == SearchMatch.Prefix ? source.StartsWith(scenario.Text, StringComparison.Ordinal) : source.Contains(scenario.Text, StringComparison.Ordinal);
}
static double Percentile(double[] values, double percentile) => values[(int)Math.Ceiling(values.Length * percentile) - 1];
int Number(string key, int fallback) => int.Parse(Value(key, fallback.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);
string Value(string key, string fallback)
{
    var position = Array.IndexOf(args, key);
    return position < 0 ? fallback : args[position + 1];
}
sealed class Scenario(string Name, string Text, SearchTarget Target = SearchTarget.FileName,
    SearchMatch? Match = null, SearchSort? Sort = null, string Scope = "main")
{
    public string Name { get; } = Name;
    public string Text { get; } = Text;
    public SearchTarget Target { get; } = Target;
    public SearchMatch? Match { get; } = Match;
    public SearchSort? Sort { get; } = Sort;
    public string Scope { get; } = Scope;
    public int Expected { get; set; }
}
