using MS.Microservice.Excel.Tests.Support;
using MS.Microservice.Excel.Aot.Diagnostics;
using MS.Microservice.Excel.Aot;
using NPOI.XSSF.UserModel;
using System.Data;
using System.IO.Pipelines;
using Xunit;

namespace MS.Microservice.Excel.Tests.Aot;

// ═══════════════════════════════════════════════════════════════════════════════
// Collection: disables parallelization to prevent GC/allocation cross-contamination.
// NOTE: Tests use forced-GC baseline mode (GC.Collect before each iteration).
// This provides clean allocation comparison but does NOT represent production
// throughput. Use median for relative comparison, not absolute throughput claims.
// ═══════════════════════════════════════════════════════════════════════════════
[CollectionDefinition("Performance", DisableParallelization = true)]
public sealed class PerformanceCollectionDefinition;

public sealed class PerformanceFactAttribute : FactAttribute
{
    public PerformanceFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("MS_MICROSERVICE_RUN_PERF_TESTS"), "1", StringComparison.Ordinal))
        {
            Skip = "Performance baseline is opt-in. Set MS_MICROSERVICE_RUN_PERF_TESTS=1 to run.";
        }
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// Fixture: accumulates reports across all tests and writes HTML once at the end.
// ═══════════════════════════════════════════════════════════════════════════════
public class PerformanceReportFixture : IDisposable
{
    private readonly List<AggregatedReport> _reports = [];

    public void Add(AggregatedReport report) { lock (_reports) _reports.Add(report); }

    public void Dispose()
    {
        if (_reports.Count == 0) return;

        var html = BuildHtmlReport(_reports);

        var binPath = Path.Combine(AppContext.BaseDirectory, "PerformanceReport.html");
        File.WriteAllText(binPath, html);

        var workspaceRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var workspacePath = Path.Combine(workspaceRoot, "PerformanceReport.html");
        try { File.WriteAllText(workspacePath, html); } catch { /* best effort */ }

        Console.WriteLine($"📊 Performance report: {binPath}");
        Console.WriteLine($"📊 Also copied to:   {workspacePath}");
    }

    // ═══════════════════════════════════════════════════════════════════
    // HTML report builder (moved here so fixture owns the output)
    // ═══════════════════════════════════════════════════════════════════

    private static string BuildHtmlReport(List<AggregatedReport> reports)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html lang=\"zh\"><head><meta charset=\"UTF-8\">");
        sb.AppendLine("<title>ExcelHelper Performance Baseline</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("body{font-family:'Segoe UI',system-ui,sans-serif;background:#0d1117;color:#c9d1d9;margin:20px;}");
        sb.AppendLine("h1{color:#58a6ff;border-bottom:1px solid #30363d;padding-bottom:8px;}h2{color:#79c0ff;margin-top:28px;}h3{color:#a5d6ff;margin:16px 0 4px;}");
        sb.AppendLine("table{border-collapse:collapse;width:100%;margin:12px 0;font-size:13px;}");
        sb.AppendLine("th,td{border:1px solid #30363d;padding:5px 8px;text-align:right;}");
        sb.AppendLine("th{background:#161b22;color:#8b949e;position:sticky;top:0;}td:first-child,th:first-child{text-align:left;}");
        sb.AppendLine("tr:hover{background:#1c2333;}");
        sb.AppendLine(".bar-bg{display:inline-block;height:6px;border-radius:2px;width:80px;vertical-align:middle;background:#21262d;}");
        sb.AppendLine(".bar{display:inline-block;height:6px;border-radius:2px;background:linear-gradient(90deg,#58a6ff,#3fb950);vertical-align:top;}");
        sb.AppendLine(".tag-exp{color:#7ee787;}.tag-imp{color:#d2a8ff;}.tag-io{color:#ffa657;}");
        sb.AppendLine(".phase-row{font-size:12px;color:#8b949e;}");
        sb.AppendLine("</style></head><body>");
        sb.AppendLine("<h1>&#x1F4CA; ExcelHelper Performance Baseline</h1>");
        sb.AppendLine($"<p style=\"color:#8b949e;\">Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss} | .NET 10 Release | ⚠ Forced-GC Baseline (not production throughput)</p>");

        sb.AppendLine("<div style=\"background:#161b22;border:1px solid #30363d;border-radius:6px;padding:12px 16px;margin:16px 0;\">");
        sb.AppendLine($"<strong>Tests:</strong> {reports.Count} | <strong>Iterations/test:</strong> 5 | <strong>Warmup:</strong> 2 | <strong>Parallelism:</strong> disabled</div>");

        // Summary table
        var categories = reports.GroupBy(r => CategoryOf(r.Name)).OrderBy(g => g.Key);
        foreach (var cat in categories)
        {
            sb.AppendLine($"<h2>{cat.Key}</h2>");
            sb.AppendLine("<table><thead><tr><th>Test</th><th>n</th><th>Min</th><th>Median</th><th>Avg</th><th>Max</th><th>&#x3C3;</th><th>Alloc Avg</th><th>Chart</th></tr></thead><tbody>");

            double maxTime = cat.Max(r => r.TimeMaxMs);
            foreach (var r in cat.OrderBy(r => r.Name))
            {
                double pct = maxTime > 0 ? r.TimeMedianMs / maxTime * 100 : 0;
                var bar = $"<span class=\"bar-bg\"><span class=\"bar\" style=\"width:{pct:F0}%\"></span></span>";
                var tag = r.Name.Contains("Export") ? "tag-exp" : r.Name.Contains("Import") ? "tag-imp" : "tag-io";

                sb.Append($"<tr><td class=\"{tag}\">{r.Name}</td><td>{r.Iterations}</td>");
                sb.Append($"<td>{r.TimeMinMs:F1}</td><td><strong>{r.TimeMedianMs:F1}</strong></td><td>{r.TimeAvgMs:F1}</td><td>{r.TimeMaxMs:F1}</td><td>{r.TimeStdDevMs:F1}</td>");
                sb.Append($"<td>{Fmt(r.AllocAvgBytes)}</td><td>{bar}</td></tr>");

                // Phase breakdown
                if (r.PhaseSummaries is { Count: > 0 })
                {
                    sb.Append("<tr class=\"phase-row\"><td colspan=\"9\">");
                    sb.Append("<details><summary>Phase breakdown</summary><table style=\"width:auto;margin:4px 0 4px 20px;\">");
                    sb.Append("<tr><th>Phase</th><th>Median (ms)</th><th>% Time</th><th>Alloc Avg</th></tr>");
                    foreach (var ps in r.PhaseSummaries.OrderByDescending(p => p.MedianMs))
                    {
                        sb.Append($"<tr><td>{ps.Name}</td><td>{ps.MedianMs:F2}</td><td>{ps.PctTime:F1}%</td><td>{Fmt(ps.AvgAllocBytes)}</td></tr>");
                    }
                    sb.AppendLine("</table></details></td></tr>");
                }
            }
            sb.AppendLine("</tbody></table>");
        }

        sb.AppendLine("<hr style=\"border-color:#30363d;margin:32px 0 12px;\">");
        sb.AppendLine("<p style=\"color:#8b949e;font-size:11px;\">");
        sb.AppendLine("<strong>⚠ Forced-GC Baseline</strong> — GC.Collect+WaitForPendingFinalizers before each iteration.<br>");
        sb.AppendLine("This provides clean relative comparison but does NOT represent production throughput.<br>");
        sb.AppendLine("Median is recommended for comparison; σ indicates measurement stability.<br>");
        sb.AppendLine("Narrow=5 cols, Wide=30 cols, DT=DataTable (10 cols).</p></body></html>");
        return sb.ToString();
    }

    private static string CategoryOf(string name) =>
        name.Contains("Export") ? "Export 导出" :
        name.Contains("Import") ? "Import 导入" : "Other";

    private static string Fmt(long b) => b switch
    {
        >= 1024 * 1024 => $"{b / 1048576.0:F2} MB",
        >= 1024 => $"{b / 1024.0:F1} KB",
        _ => $"{b} B"
    };
}

// ═══════════════════════════════════════════════════════════════════════════════
// Test class
// ═══════════════════════════════════════════════════════════════════════════════

[Collection("Performance")]
[Trait("Category", "Performance")]
public class PerformanceBaselineTests : IClassFixture<PerformanceReportFixture>
{
    private readonly PerformanceReportFixture _fixture;

    private const int SmallRows = 1_000;
    private const int MediumRows = 10_000;
    private const int LargeRows = 50_000;
    private const int WarmupIterations = 2;
    private const int MeasureIterations = 5;

    public PerformanceBaselineTests(PerformanceReportFixture fixture)
    {
        _fixture = fixture;
    }

    // ═══════════════════════════════════════════════════════════════════
    // Export – Object – Narrow (5 cols)
    // ═══════════════════════════════════════════════════════════════════

    [PerformanceFact] public void Export_Obj_Narrow_Small()  => RunSync("Export_Obj_Narrow_Small",  () => ExportCore<ExportDto5>(SmallRows));
    [PerformanceFact] public void Export_Obj_Narrow_Medium() => RunSync("Export_Obj_Narrow_Medium", () => ExportCore<ExportDto5>(MediumRows));
    [PerformanceFact] public void Export_Obj_Narrow_Large()  => RunSync("Export_Obj_Narrow_Large",  () => ExportCore<ExportDto5>(LargeRows));

    // Export – Object – Wide (30 cols)
    [PerformanceFact] public void Export_Obj_Wide_Small()  => RunSync("Export_Obj_Wide_Small",  () => ExportCore<ExportDto30>(SmallRows));
    [PerformanceFact] public void Export_Obj_Wide_Medium() => RunSync("Export_Obj_Wide_Medium", () => ExportCore<ExportDto30>(MediumRows));
    [PerformanceFact] public void Export_Obj_Wide_Large()  => RunSync("Export_Obj_Wide_Large",  () => ExportCore<ExportDto30>(LargeRows));

    // Export – DataTable
    [PerformanceFact] public void Export_DT_Small()  => RunSync("Export_DT_Small",  () => ExportDTCore(SmallRows));
    [PerformanceFact] public void Export_DT_Medium() => RunSync("Export_DT_Medium", () => ExportDTCore(MediumRows));
    [PerformanceFact] public void Export_DT_Large()  => RunSync("Export_DT_Large",  () => ExportDTCore(LargeRows));

    // ═══════════════════════════════════════════════════════════════════
    // Import – Object – Narrow (5 cols)
    // ═══════════════════════════════════════════════════════════════════

    [PerformanceFact] public void Import_Obj_Narrow_Small()  => RunSync("Import_Obj_Narrow_Small",  () => ImportCore<ImportDto5>(SmallRows));
    [PerformanceFact] public void Import_Obj_Narrow_Medium() => RunSync("Import_Obj_Narrow_Medium", () => ImportCore<ImportDto5>(MediumRows));
    [PerformanceFact] public void Import_Obj_Narrow_Large()  => RunSync("Import_Obj_Narrow_Large",  () => ImportCore<ImportDto5>(LargeRows));

    // Import – Object – Wide (30 cols)
    [PerformanceFact] public void Import_Obj_Wide_Small()  => RunSync("Import_Obj_Wide_Small",  () => ImportCore<ImportDto30>(SmallRows));
    [PerformanceFact] public void Import_Obj_Wide_Medium() => RunSync("Import_Obj_Wide_Medium", () => ImportCore<ImportDto30>(MediumRows));
    [PerformanceFact] public void Import_Obj_Wide_Large()  => RunSync("Import_Obj_Wide_Large",  () => ImportCore<ImportDto30>(LargeRows));

    // ═══════════════════════════════════════════════════════════════════
    // Import – PipeReader / Non-seekable (async, true async measurement)
    // ═══════════════════════════════════════════════════════════════════

    [PerformanceFact] public Task Import_PipeReader_Medium() => RunAsync("Import_PipeReader_Medium", () => ImportPipeReaderCore(MediumRows));
    [PerformanceFact] public Task Import_PipeReader_Large()  => RunAsync("Import_PipeReader_Large",  () => ImportPipeReaderCore(LargeRows));

    [PerformanceFact] public void Import_NonSeekable_Medium() => RunSync("Import_NonSeekable_Medium", () => ImportNonSeekableCore(MediumRows));
    [PerformanceFact] public void Import_NonSeekable_Large()  => RunSync("Import_NonSeekable_Large",  () => ImportNonSeekableCore(LargeRows));

    // ═══════════════════════════════════════════════════════════════════
    // Sync measurement runner
    // ═══════════════════════════════════════════════════════════════════

    private void RunSync(string name, Func<PerformanceReport> measuredAction)
    {
        // Warmup
        for (int i = 0; i < WarmupIterations; i++) measuredAction();

        var results = new List<PerformanceReport>(MeasureIterations);
        for (int i = 0; i < MeasureIterations; i++)
        {
            ForceGC();
            results.Add(measuredAction());
        }

        _fixture.Add(Aggregate(name, results));
    }

    // ═══════════════════════════════════════════════════════════════════
    // Async measurement runner (true async, no .GetAwaiter().GetResult())
    // ═══════════════════════════════════════════════════════════════════

    private async Task RunAsync(string name, Func<Task<PerformanceReport>> measuredAction)
    {
        // Warmup
        for (int i = 0; i < WarmupIterations; i++) await measuredAction();

        var results = new List<PerformanceReport>(MeasureIterations);
        for (int i = 0; i < MeasureIterations; i++)
        {
            ForceGC();
            results.Add(await measuredAction());
        }

        _fixture.Add(Aggregate(name, results));
    }

    private static void ForceGC()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    // ═══════════════════════════════════════════════════════════════════
    // Core measured actions — data prep is OUTSIDE measurement; probe is
    // INJECTED into ExcelHelper.DiagnosticProbe for phase-level data.
    // ═══════════════════════════════════════════════════════════════════

    private static PerformanceReport ExportCore<T>(int rowCount) where T : class, IBenchmarkDto<T>, new()
    {
        // Data prep (outside measurement)
        var data = GenerateExportDtos<T>(rowCount);

        // Measurement
        var probe = new PerformanceProbe().Start();
        var helper = new ExcelHelper { DiagnosticProbe = probe };
        byte[] result = helper.Export(data, "Sheet1", T.Map);
        Assert.True(result.Length > 0);
        return probe.Stop();
    }

    private static PerformanceReport ExportDTCore(int rowCount)
    {
        var dt = CreateDataTable(rowCount, cols: 10);

        var probe = new PerformanceProbe().Start();
        var helper = new ExcelHelper { DiagnosticProbe = probe };
        byte[] result = helper.Export(dt, "Sheet1");
        Assert.True(result.Length > 0);
        return probe.Stop();
    }

    private static PerformanceReport ImportCore<T>(int rowCount) where T : class, IBenchmarkDto<T>, new()
    {
        // Data prep (outside measurement — workbook bytes generated once, measured import only)
        byte[] workbookBytes = GenerateImportWorkbookBytes(rowCount, colCountForImport<T>());

        var probe = new PerformanceProbe().Start();
        var helper = new ExcelHelper { DiagnosticProbe = probe }
            .InitSheetIndex(0)
            .InitStartReadRowIndex(0, 1);
        var result = helper.Import<T>("test.xlsx", workbookBytes, T.Map);
        Assert.Equal(rowCount, result.Count);
        return probe.Stop();
    }

    private static async Task<PerformanceReport> ImportPipeReaderCore(int rowCount)
    {
        byte[] workbookBytes = GenerateImportWorkbookBytes(rowCount, colCount: 5);

        // Write data into pipe (outside measurement)
        var pipe = new Pipe();
        await pipe.Writer.WriteAsync(workbookBytes);
        await pipe.Writer.CompleteAsync();

        // Measurement
        var probe = new PerformanceProbe().Start();
        var helper = new ExcelHelper { DiagnosticProbe = probe }
            .InitSheetIndex(0)
            .InitStartReadRowIndex(0, 1);
        var result = await helper.ImportAsync<ImportDto5>("test.xlsx", pipe.Reader, ImportDto5.Map);
        Assert.Equal(rowCount, result.Count);
        return probe.Stop();
    }

    private static PerformanceReport ImportNonSeekableCore(int rowCount)
    {
        byte[] workbookBytes = GenerateImportWorkbookBytes(rowCount, colCount: 5);

        using var nonSeekable = new NonSeekableStream(new MemoryStream(workbookBytes));

        var probe = new PerformanceProbe().Start();
        var helper = new ExcelHelper { DiagnosticProbe = probe }
            .InitSheetIndex(0)
            .InitStartReadRowIndex(0, 1);
        var result = helper.Import<ImportDto5>("test.xlsx", nonSeekable, ImportDto5.Map);
        Assert.Equal(rowCount, result.Count);
        return probe.Stop();
    }

    private static int colCountForImport<T>() => typeof(T) == typeof(ImportDto5) ? 5 : 30;

    // ═══════════════════════════════════════════════════════════════════
    // Data generators (outside measurement)
    // ═══════════════════════════════════════════════════════════════════

    private static List<T> GenerateExportDtos<T>(int rows) where T : class, IBenchmarkDto<T>, new()
    {
        var list = new List<T>(rows);
        for (int i = 0; i < rows; i++) { var d = new T(); d.Fill(i); list.Add(d); }
        return list;
    }

    private static DataTable CreateDataTable(int rows, int cols)
    {
        var dt = new DataTable("Test");
        for (int c = 0; c < cols; c++) dt.Columns.Add($"Col{c}", typeof(string));
        for (int r = 0; r < rows; r++) { var row = dt.NewRow(); for (int c = 0; c < cols; c++) row[c] = $"R{r}C{c}"; dt.Rows.Add(row); }
        return dt;
    }

    private static byte[] GenerateImportWorkbookBytes(int rows, int colCount)
    {
        using var wb = new XSSFWorkbook();
        var sheet = wb.CreateSheet("Sheet1");
        var tr = sheet.CreateRow(0);
        for (int c = 0; c < colCount; c++) tr.CreateCell(c).SetCellValue($"Col{c}");
        for (int r = 0; r < rows; r++)
        {
            var row = sheet.CreateRow(r + 1);
            for (int c = 0; c < colCount; c++)
            {
                if (c == 0)
                    row.CreateCell(c).SetCellValue($"Name_{r}");
                else
                    row.CreateCell(c).SetCellValue((double)(r + c));
            }
        }
        using var ms = new MemoryStream();
        wb.Write(ms);
        return ms.ToArray();
    }

    // ═══════════════════════════════════════════════════════════════════
    // Aggregation — includes phase-level summaries
    // ═══════════════════════════════════════════════════════════════════

    private static AggregatedReport Aggregate(string name, List<PerformanceReport> reports)
    {
        var times = reports.Select(r => r.TotalElapsed.TotalMilliseconds).OrderBy(t => t).ToList();
        var allocs = reports.Select(r => r.TotalAllocatedBytes).OrderBy(a => a).ToList();

        // Phase aggregation: merge phases across iterations, compute median per phase
        var phaseMap = new Dictionary<string, List<PhaseRecord>>();
        foreach (var report in reports)
        {
            foreach (var phase in report.Phases)
            {
                if (!phaseMap.TryGetValue(phase.Name, out var list))
                    phaseMap[phase.Name] = list = [];
                list.Add(phase);
            }
        }

        var phaseSummaries = phaseMap
            .Select(kvp =>
            {
                var pts = kvp.Value.Select(p => p.Elapsed.TotalMilliseconds).OrderBy(t => t).ToList();
                var pas = kvp.Value.Select(p => p.AllocatedBytes).OrderBy(a => a).ToList();
                double avgTotalMs = reports.Count > 0 ? reports.Average(r => r.TotalElapsed.TotalMilliseconds) : 1;
                double pct = avgTotalMs > 0 ? pts[pts.Count / 2] / avgTotalMs * 100 : 0;
                return new PhaseSummary(
                    Name: kvp.Key,
                    MedianMs: pts[pts.Count / 2],
                    AvgAllocBytes: (long)pas.Average(),
                    PctTime: pct);
            })
            .ToList();

        return new AggregatedReport(
            Name: name,
            Iterations: reports.Count,
            TimeMinMs: times.First(),
            TimeMedianMs: times[times.Count / 2],
            TimeAvgMs: times.Average(),
            TimeMaxMs: times.Last(),
            TimeStdDevMs: StdDev(times),
            AllocMinBytes: allocs.First(),
            AllocMedianBytes: allocs[allocs.Count / 2],
            AllocAvgBytes: (long)allocs.Average(),
            AllocMaxBytes: allocs.Last(),
            PhaseSummaries: phaseSummaries);
    }

    private static double StdDev(List<double> values)
    {
        double avg = values.Average();
        return Math.Sqrt(values.Average(v => (v - avg) * (v - avg)));
    }
}

// ═══════════════════════════════════════════════════════════════════════
// Aggregated report records
// ═══════════════════════════════════════════════════════════════════════

public sealed record AggregatedReport(
    string Name,
    int Iterations,
    double TimeMinMs,
    double TimeMedianMs,
    double TimeAvgMs,
    double TimeMaxMs,
    double TimeStdDevMs,
    long AllocMinBytes,
    long AllocMedianBytes,
    long AllocAvgBytes,
    long AllocMaxBytes,
    List<PhaseSummary>? PhaseSummaries);

public sealed record PhaseSummary(
    string Name,
    double MedianMs,
    long AvgAllocBytes,
    double PctTime);

// ═══════════════════════════════════════════════════════════════════════
// Benchmark DTO interface
// ═══════════════════════════════════════════════════════════════════════

public interface IBenchmarkDto<T> where T : class
{
    static abstract ExcelModelMap<T> Map { get; }
    void Fill(int rowIndex);
}

// ═══════════════════════════════════════════════════════════════════════
// Export DTOs — exactly 5 or 30 ExcelColumn attributes
// ═══════════════════════════════════════════════════════════════════════

public class ExportDto5 : IBenchmarkDto<ExportDto5>
{
        public static ExcelModelMap<ExportDto5> Map { get; } = new(static () => new ExportDto5(),
            ExcelColumn<ExportDto5>.Create("Col0", static r => r.Col0, static (r, v) => r.Col0 = v!, ExcelValueConverters.String),
            ExcelColumn<ExportDto5>.Create("Col1", static r => r.Col1, static (r, v) => r.Col1 = v!, ExcelValueConverters.String),
            ExcelColumn<ExportDto5>.Create("Col2", static r => r.Col2, static (r, v) => r.Col2 = v!, ExcelValueConverters.String),
            ExcelColumn<ExportDto5>.Create("Col3", static r => r.Col3, static (r, v) => r.Col3 = v!, ExcelValueConverters.String),
            ExcelColumn<ExportDto5>.Create("Col4", static r => r.Col4, static (r, v) => r.Col4 = v!, ExcelValueConverters.String));


    public string Col0 { get; set; } = "";
    public string Col1 { get; set; } = "";
    public string Col2 { get; set; } = "";
    public string Col3 { get; set; } = "";
    public string Col4 { get; set; } = "";
    public void Fill(int r) { Col0 = $"R{r}C0"; Col1 = $"R{r}C1"; Col2 = $"R{r}C2"; Col3 = $"R{r}C3"; Col4 = $"R{r}C4"; }
}

public class ExportDto30 : IBenchmarkDto<ExportDto30>
{
        public static ExcelModelMap<ExportDto30> Map { get; } = new(static () => new ExportDto30(),
            ExcelColumn<ExportDto30>.Create("Col0", static r => r.Col0, static (r, v) => r.Col0 = v!, ExcelValueConverters.String),
            ExcelColumn<ExportDto30>.Create("Col1", static r => r.Col1, static (r, v) => r.Col1 = v!, ExcelValueConverters.String),
            ExcelColumn<ExportDto30>.Create("Col2", static r => r.Col2, static (r, v) => r.Col2 = v!, ExcelValueConverters.String),
            ExcelColumn<ExportDto30>.Create("Col3", static r => r.Col3, static (r, v) => r.Col3 = v!, ExcelValueConverters.String),
            ExcelColumn<ExportDto30>.Create("Col4", static r => r.Col4, static (r, v) => r.Col4 = v!, ExcelValueConverters.String),
            ExcelColumn<ExportDto30>.Create("Col5", static r => r.Col5, static (r, v) => r.Col5 = v!, ExcelValueConverters.String),
            ExcelColumn<ExportDto30>.Create("Col6", static r => r.Col6, static (r, v) => r.Col6 = v!, ExcelValueConverters.String),
            ExcelColumn<ExportDto30>.Create("Col7", static r => r.Col7, static (r, v) => r.Col7 = v!, ExcelValueConverters.String),
            ExcelColumn<ExportDto30>.Create("Col8", static r => r.Col8, static (r, v) => r.Col8 = v!, ExcelValueConverters.String),
            ExcelColumn<ExportDto30>.Create("Col9", static r => r.Col9, static (r, v) => r.Col9 = v!, ExcelValueConverters.String),
            ExcelColumn<ExportDto30>.Create("Col10", static r => r.Col10, static (r, v) => r.Col10 = v!, ExcelValueConverters.String),
            ExcelColumn<ExportDto30>.Create("Col11", static r => r.Col11, static (r, v) => r.Col11 = v!, ExcelValueConverters.String),
            ExcelColumn<ExportDto30>.Create("Col12", static r => r.Col12, static (r, v) => r.Col12 = v!, ExcelValueConverters.String),
            ExcelColumn<ExportDto30>.Create("Col13", static r => r.Col13, static (r, v) => r.Col13 = v!, ExcelValueConverters.String),
            ExcelColumn<ExportDto30>.Create("Col14", static r => r.Col14, static (r, v) => r.Col14 = v!, ExcelValueConverters.String),
            ExcelColumn<ExportDto30>.Create("Col15", static r => r.Col15, static (r, v) => r.Col15 = v!, ExcelValueConverters.String),
            ExcelColumn<ExportDto30>.Create("Col16", static r => r.Col16, static (r, v) => r.Col16 = v!, ExcelValueConverters.String),
            ExcelColumn<ExportDto30>.Create("Col17", static r => r.Col17, static (r, v) => r.Col17 = v!, ExcelValueConverters.String),
            ExcelColumn<ExportDto30>.Create("Col18", static r => r.Col18, static (r, v) => r.Col18 = v!, ExcelValueConverters.String),
            ExcelColumn<ExportDto30>.Create("Col19", static r => r.Col19, static (r, v) => r.Col19 = v!, ExcelValueConverters.String),
            ExcelColumn<ExportDto30>.Create("Col20", static r => r.Col20, static (r, v) => r.Col20 = v!, ExcelValueConverters.String),
            ExcelColumn<ExportDto30>.Create("Col21", static r => r.Col21, static (r, v) => r.Col21 = v!, ExcelValueConverters.String),
            ExcelColumn<ExportDto30>.Create("Col22", static r => r.Col22, static (r, v) => r.Col22 = v!, ExcelValueConverters.String),
            ExcelColumn<ExportDto30>.Create("Col23", static r => r.Col23, static (r, v) => r.Col23 = v!, ExcelValueConverters.String),
            ExcelColumn<ExportDto30>.Create("Col24", static r => r.Col24, static (r, v) => r.Col24 = v!, ExcelValueConverters.String),
            ExcelColumn<ExportDto30>.Create("Col25", static r => r.Col25, static (r, v) => r.Col25 = v!, ExcelValueConverters.String),
            ExcelColumn<ExportDto30>.Create("Col26", static r => r.Col26, static (r, v) => r.Col26 = v!, ExcelValueConverters.String),
            ExcelColumn<ExportDto30>.Create("Col27", static r => r.Col27, static (r, v) => r.Col27 = v!, ExcelValueConverters.String),
            ExcelColumn<ExportDto30>.Create("Col28", static r => r.Col28, static (r, v) => r.Col28 = v!, ExcelValueConverters.String),
            ExcelColumn<ExportDto30>.Create("Col29", static r => r.Col29, static (r, v) => r.Col29 = v!, ExcelValueConverters.String));


    public string Col0 { get; set; } = "";
    public string Col1 { get; set; } = "";
    public string Col2 { get; set; } = "";
    public string Col3 { get; set; } = "";
    public string Col4 { get; set; } = "";
    public string Col5 { get; set; } = "";
    public string Col6 { get; set; } = "";
    public string Col7 { get; set; } = "";
    public string Col8 { get; set; } = "";
    public string Col9 { get; set; } = "";
    public string Col10 { get; set; } = "";
    public string Col11 { get; set; } = "";
    public string Col12 { get; set; } = "";
    public string Col13 { get; set; } = "";
    public string Col14 { get; set; } = "";
    public string Col15 { get; set; } = "";
    public string Col16 { get; set; } = "";
    public string Col17 { get; set; } = "";
    public string Col18 { get; set; } = "";
    public string Col19 { get; set; } = "";
    public string Col20 { get; set; } = "";
    public string Col21 { get; set; } = "";
    public string Col22 { get; set; } = "";
    public string Col23 { get; set; } = "";
    public string Col24 { get; set; } = "";
    public string Col25 { get; set; } = "";
    public string Col26 { get; set; } = "";
    public string Col27 { get; set; } = "";
    public string Col28 { get; set; } = "";
    public string Col29 { get; set; } = "";
    public void Fill(int r)
    {
        Col0 = $"R{r}C0"; Col1 = $"R{r}C1"; Col2 = $"R{r}C2"; Col3 = $"R{r}C3"; Col4 = $"R{r}C4";
        Col5 = $"R{r}C5"; Col6 = $"R{r}C6"; Col7 = $"R{r}C7"; Col8 = $"R{r}C8"; Col9 = $"R{r}C9";
        Col10 = $"R{r}C10"; Col11 = $"R{r}C11"; Col12 = $"R{r}C12"; Col13 = $"R{r}C13"; Col14 = $"R{r}C14";
        Col15 = $"R{r}C15"; Col16 = $"R{r}C16"; Col17 = $"R{r}C17"; Col18 = $"R{r}C18"; Col19 = $"R{r}C19";
        Col20 = $"R{r}C20"; Col21 = $"R{r}C21"; Col22 = $"R{r}C22"; Col23 = $"R{r}C23"; Col24 = $"R{r}C24";
        Col25 = $"R{r}C25"; Col26 = $"R{r}C26"; Col27 = $"R{r}C27"; Col28 = $"R{r}C28"; Col29 = $"R{r}C29";
    }
}

// ═══════════════════════════════════════════════════════════════════════
// Import DTOs — Col0=string, rest=int
// ═══════════════════════════════════════════════════════════════════════

public class ImportDto5 : IBenchmarkDto<ImportDto5>
{
        public static ExcelModelMap<ImportDto5> Map { get; } = new(static () => new ImportDto5(),
            ExcelColumn<ImportDto5>.Create("Col0", static r => r.Col0, static (r, v) => r.Col0 = v!, ExcelValueConverters.String),
            ExcelColumn<ImportDto5>.Create("Col1", static r => r.Col1, static (r, v) => r.Col1 = v, ExcelValueConverters.Int32),
            ExcelColumn<ImportDto5>.Create("Col2", static r => r.Col2, static (r, v) => r.Col2 = v, ExcelValueConverters.Int32),
            ExcelColumn<ImportDto5>.Create("Col3", static r => r.Col3, static (r, v) => r.Col3 = v, ExcelValueConverters.Int32),
            ExcelColumn<ImportDto5>.Create("Col4", static r => r.Col4, static (r, v) => r.Col4 = v, ExcelValueConverters.Int32));


    public string Col0 { get; set; } = "";
    public int Col1 { get; set; }
    public int Col2 { get; set; }
    public int Col3 { get; set; }
    public int Col4 { get; set; }
    public void Fill(int r) { }
}

public class ImportDto30 : IBenchmarkDto<ImportDto30>
{
        public static ExcelModelMap<ImportDto30> Map { get; } = new(static () => new ImportDto30(),
            ExcelColumn<ImportDto30>.Create("Col0", static r => r.Col0, static (r, v) => r.Col0 = v!, ExcelValueConverters.String),
            ExcelColumn<ImportDto30>.Create("Col1", static r => r.Col1, static (r, v) => r.Col1 = v, ExcelValueConverters.Int32),
            ExcelColumn<ImportDto30>.Create("Col2", static r => r.Col2, static (r, v) => r.Col2 = v, ExcelValueConverters.Int32),
            ExcelColumn<ImportDto30>.Create("Col3", static r => r.Col3, static (r, v) => r.Col3 = v, ExcelValueConverters.Int32),
            ExcelColumn<ImportDto30>.Create("Col4", static r => r.Col4, static (r, v) => r.Col4 = v, ExcelValueConverters.Int32),
            ExcelColumn<ImportDto30>.Create("Col5", static r => r.Col5, static (r, v) => r.Col5 = v, ExcelValueConverters.Int32),
            ExcelColumn<ImportDto30>.Create("Col6", static r => r.Col6, static (r, v) => r.Col6 = v, ExcelValueConverters.Int32),
            ExcelColumn<ImportDto30>.Create("Col7", static r => r.Col7, static (r, v) => r.Col7 = v, ExcelValueConverters.Int32),
            ExcelColumn<ImportDto30>.Create("Col8", static r => r.Col8, static (r, v) => r.Col8 = v, ExcelValueConverters.Int32),
            ExcelColumn<ImportDto30>.Create("Col9", static r => r.Col9, static (r, v) => r.Col9 = v, ExcelValueConverters.Int32),
            ExcelColumn<ImportDto30>.Create("Col10", static r => r.Col10, static (r, v) => r.Col10 = v, ExcelValueConverters.Int32),
            ExcelColumn<ImportDto30>.Create("Col11", static r => r.Col11, static (r, v) => r.Col11 = v, ExcelValueConverters.Int32),
            ExcelColumn<ImportDto30>.Create("Col12", static r => r.Col12, static (r, v) => r.Col12 = v, ExcelValueConverters.Int32),
            ExcelColumn<ImportDto30>.Create("Col13", static r => r.Col13, static (r, v) => r.Col13 = v, ExcelValueConverters.Int32),
            ExcelColumn<ImportDto30>.Create("Col14", static r => r.Col14, static (r, v) => r.Col14 = v, ExcelValueConverters.Int32),
            ExcelColumn<ImportDto30>.Create("Col15", static r => r.Col15, static (r, v) => r.Col15 = v, ExcelValueConverters.Int32),
            ExcelColumn<ImportDto30>.Create("Col16", static r => r.Col16, static (r, v) => r.Col16 = v, ExcelValueConverters.Int32),
            ExcelColumn<ImportDto30>.Create("Col17", static r => r.Col17, static (r, v) => r.Col17 = v, ExcelValueConverters.Int32),
            ExcelColumn<ImportDto30>.Create("Col18", static r => r.Col18, static (r, v) => r.Col18 = v, ExcelValueConverters.Int32),
            ExcelColumn<ImportDto30>.Create("Col19", static r => r.Col19, static (r, v) => r.Col19 = v, ExcelValueConverters.Int32),
            ExcelColumn<ImportDto30>.Create("Col20", static r => r.Col20, static (r, v) => r.Col20 = v, ExcelValueConverters.Int32),
            ExcelColumn<ImportDto30>.Create("Col21", static r => r.Col21, static (r, v) => r.Col21 = v, ExcelValueConverters.Int32),
            ExcelColumn<ImportDto30>.Create("Col22", static r => r.Col22, static (r, v) => r.Col22 = v, ExcelValueConverters.Int32),
            ExcelColumn<ImportDto30>.Create("Col23", static r => r.Col23, static (r, v) => r.Col23 = v, ExcelValueConverters.Int32),
            ExcelColumn<ImportDto30>.Create("Col24", static r => r.Col24, static (r, v) => r.Col24 = v, ExcelValueConverters.Int32),
            ExcelColumn<ImportDto30>.Create("Col25", static r => r.Col25, static (r, v) => r.Col25 = v, ExcelValueConverters.Int32),
            ExcelColumn<ImportDto30>.Create("Col26", static r => r.Col26, static (r, v) => r.Col26 = v, ExcelValueConverters.Int32),
            ExcelColumn<ImportDto30>.Create("Col27", static r => r.Col27, static (r, v) => r.Col27 = v, ExcelValueConverters.Int32),
            ExcelColumn<ImportDto30>.Create("Col28", static r => r.Col28, static (r, v) => r.Col28 = v, ExcelValueConverters.Int32),
            ExcelColumn<ImportDto30>.Create("Col29", static r => r.Col29, static (r, v) => r.Col29 = v, ExcelValueConverters.Int32));


    public string Col0 { get; set; } = "";
    public int Col1 { get; set; }
    public int Col2 { get; set; }
    public int Col3 { get; set; }
    public int Col4 { get; set; }
    public int Col5 { get; set; }
    public int Col6 { get; set; }
    public int Col7 { get; set; }
    public int Col8 { get; set; }
    public int Col9 { get; set; }
    public int Col10 { get; set; }
    public int Col11 { get; set; }
    public int Col12 { get; set; }
    public int Col13 { get; set; }
    public int Col14 { get; set; }
    public int Col15 { get; set; }
    public int Col16 { get; set; }
    public int Col17 { get; set; }
    public int Col18 { get; set; }
    public int Col19 { get; set; }
    public int Col20 { get; set; }
    public int Col21 { get; set; }
    public int Col22 { get; set; }
    public int Col23 { get; set; }
    public int Col24 { get; set; }
    public int Col25 { get; set; }
    public int Col26 { get; set; }
    public int Col27 { get; set; }
    public int Col28 { get; set; }
    public int Col29 { get; set; }
    public void Fill(int r) { }
}
