using MS.Microservice.Media.Inference.Ocr;
using MS.Microservice.Media.Inference.Tables;
using MS.Microservice.Media.Inference.Tests.Ocr;
using MS.Microservice.Media.Video;
using SkiaSharp;
using Xunit;

namespace MS.Microservice.Media.Inference.Tests.Tables;

public sealed class TableRecognizerTests
{
    [Theory]
    [InlineData("length", 7)]
    [InlineData("length", 513)]
    [InlineData("tolerance", 0)]
    [InlineData("tolerance", 9)]
    [InlineData("tables", 0)]
    [InlineData("tables", 17)]
    [InlineData("rows", 0)]
    [InlineData("rows", 129)]
    [InlineData("columns", 0)]
    [InlineData("columns", 129)]
    [InlineData("cells", 0)]
    [InlineData("cells", 2049)]
    public void Options_RejectInvalidGeometryAndBudgets(string field, int value)
    {
        using var root = new TestRoot();
        var options = LocalOptions(root);
        options = field switch
        {
            "length" => options with { MinimumLineLength = value }, "tolerance" => options with { LineTolerance = value },
            "tables" => options with { MaximumTables = value }, "rows" => options with { MaximumRows = value },
            "columns" => options with { MaximumColumns = value }, _ => options with { MaximumGridCells = value }
        };
        Assert.Throws<ArgumentException>(() => new TableRecognizer(options));
    }

    [Theory]
    [InlineData(.74)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Options_RejectInvalidBoundaryCoverage(double value)
    {
        using var root = new TestRoot();
        Assert.Throws<ArgumentException>(() => new TableRecognizer(LocalOptions(root) with { MinimumBoundaryCoverage = value }));
    }

    [Fact]
    public void OutputValidation_EnforcesUniqueCompleteRegionAssignmentsAndRectangularGridCoverage()
    {
        var options = new TableRecognitionOptions { Ocr = new() { PythonExecutablePath = "unused", ModelDirectory = "unused" } };
        OcrResult ocr = new(100, 60, [new("text", .9, [new(5, 5), new(45, 5), new(45, 20), new(5, 20)])]);
        RecognizedCell left = new(0, 0, 1, 1, new(0, 0, 50, 60), "text", [0]);
        RecognizedCell right = new(0, 1, 1, 1, new(50, 0, 50, 60), "", []);
        RecognizedTable table = new(1, 2, new(0, 0, 100, 60), [left, right]);
        TableRecognitionResult result = new(ocr, [table], []);
        TableRecognizer.ValidateResult(result, 100, 60, options);
        foreach (var invalid in new[]
        {
            result with { UnassignedRegionIndices = [0] }, result with { UnassignedRegionIndices = [-1] },
            result with { Tables = [table with { Cells = [left with { RegionIndices = [] }, right] }] },
            result with { Tables = [table with { Cells = [left, right with { Text = "text", RegionIndices = [0] }] }] },
            result with { Tables = [table with { Cells = [left with { RegionIndices = [1] }, right] }] },
            result with { Tables = [table with { Cells = [left with { Text = "wrong" }, right] }] },
            result with { Tables = [table with { Cells = [left with { RowSpan = 2 }, right] }] },
            result with { Tables = [table with { Cells = [left, right with { Column = 0 }] }] },
            result with { Tables = [table with { Cells = [left] }] },
            result with { Tables = [table with { Cells = [left with { Bounds = new(99, 0, 50, 60) }, right] }] },
            result with { Tables = [table with { Cells = [left with { Bounds = new(0, 0, 39, 60) }, right with { Bounds = new(39, 0, 61, 60) }] }] },
            result with { Tables = [table with { RowCount = 0 }] },
            result with { Tables = [table with { ColumnCount = 1, Cells = [left] }] }
        }) Assert.Throws<InvalidDataException>(() => TableRecognizer.ValidateResult(invalid, 100, 60, options));
        Assert.Throws<InvalidDataException>(() => TableRecognizer.ValidateResult(result, 100, 60, options with { MaximumGridCells = 1 }));
    }

    [Fact]
    public void PackagedTableWorkers_AreCopiedTransitively()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "media-inference");
        Assert.True(File.Exists(Path.Combine(directory, "table_worker.py")));
        Assert.True(File.Exists(Path.Combine(directory, "table_pipeline.py")));
    }

    [Fact]
    public async Task InvalidAndPreCanceledInputs_LeaveCallerOpenWithoutCreatingWorkspace()
    {
        using var root = new TestRoot();
        var recognizer = new TableRecognizer(LocalOptions(root));
        using var input = new MemoryStream([1, 2, 3]);
        await Assert.ThrowsAsync<InvalidDataException>(() => recognizer.RecognizeAsync(input));
        input.Position = 0;
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => recognizer.RecognizeAsync(input, canceled.Token));
        Assert.Equal(0, input.Position);
        Assert.True(input.CanRead);
        Assert.Empty(Directory.GetDirectories(root.Path));
    }

    [OcrTheory]
    [InlineData("bilingual-grid.png")]
    [InlineData("transparent-grid.png")]
    [InlineData("exif-6.jpg")]
    public async Task ActualBilingualGrid_ReturnsExactCellsAndNormalizedPixelBounds(string name)
    {
        using var root = new TestRoot();
        using var input = new MemoryStream([0, .. Fixture(name)]);
        input.Position = 1;
        var result = await Create(root.Path).RecognizeAsync(input);
        var table = Assert.Single(result.Tables);
        Assert.True(result.ReviewRecommended);
        Assert.Equal(700, result.Ocr.Width);
        Assert.Equal(440, result.Ocr.Height);
        Assert.Equal(new TableBounds(40, 40, 600, 360), table.Bounds);
        Assert.Equal(3, table.RowCount);
        Assert.Equal(3, table.ColumnCount);
        Assert.Equal(["名称", "数量", "Price", "苹果", "12", "3.50", "香蕉", "8", "2.00"], table.Cells.Select(c => c.Text));
        Assert.All(table.Cells, cell =>
        {
            Assert.Equal(1, cell.RowSpan);
            Assert.Equal(1, cell.ColumnSpan);
            Assert.Equal(new TableBounds(40 + cell.Column * 200, 40 + cell.Row * 120, 200, 120), cell.Bounds);
            var region = result.Ocr.Regions[Assert.Single(cell.RegionIndices)];
            Assert.InRange(region.Corners.Average(p => p.X), cell.Bounds.X, cell.Bounds.X + cell.Bounds.Width);
            Assert.InRange(region.Corners.Average(p => p.Y), cell.Bounds.Y, cell.Bounds.Y + cell.Bounds.Height);
        });
        Assert.Empty(result.UnassignedRegionIndices);
        Assert.True(input.CanRead);
        Assert.Throws<NotSupportedException>(() => ((IList<RecognizedTable>)result.Tables).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<RecognizedCell>)table.Cells).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<int>)table.Cells[0].RegionIndices).Clear());
        Assert.Empty(Directory.GetFileSystemEntries(root.Path));
    }

    [OcrTheory]
    [InlineData("colspan.png", 0, 0, 1, 2, "商品清单")]
    [InlineData("rowspan.png", 1, 0, 2, 1, "水果")]
    [InlineData("merged-block.png", 0, 0, 2, 2, "Merged")]
    public async Task ActualMergedCells_ReturnPhysicalRectangularSpans(string name, int row, int column, int rowSpan, int columnSpan, string text)
    {
        using var root = new TestRoot();
        using var input = new MemoryStream(Fixture(name));
        var result = await Create(root.Path).RecognizeAsync(input);
        var table = Assert.Single(result.Tables);
        var merged = Assert.Single(table.Cells, c => c.Row == row && c.Column == column);
        Assert.Equal(rowSpan, merged.RowSpan);
        Assert.Equal(columnSpan, merged.ColumnSpan);
        Assert.Equal(text, merged.Text);
        Assert.Equal(9 - rowSpan * columnSpan + 1, table.Cells.Count);
        Assert.Empty(result.UnassignedRegionIndices);
        Assert.Empty(Directory.GetFileSystemEntries(root.Path));
    }

    [OcrTheory]
    [InlineData("missing-left.png")]
    [InlineData("partial-line.png")]
    [InlineData("nonrectangular.png")]
    [InlineData("rotated.png")]
    [InlineData("perspective.png")]
    [InlineData("plain-text.png")]
    [InlineData("grid-glyph.png")]
    public async Task UnsupportedNetworksAndOrdinaryText_ReturnNoTableAndKeepActualOcr(string name)
    {
        using var root = new TestRoot();
        using var input = new MemoryStream(Fixture(name));
        var result = await Create(root.Path).RecognizeAsync(input);
        Assert.Empty(result.Tables);
        Assert.NotEmpty(result.Ocr.Regions);
        Assert.Equal(Enumerable.Range(0, result.Ocr.Regions.Count), result.UnassignedRegionIndices);
        Assert.Empty(Directory.GetFileSystemEntries(root.Path));
    }

    [OcrFact]
    public async Task BlankImage_ReturnsNeitherInventedGridNorText()
    {
        using var root = new TestRoot();
        using var bitmap = new SKBitmap(700, 440);
        bitmap.Erase(SKColors.White);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        using var input = new MemoryStream(encoded.ToArray());
        var result = await Create(root.Path).RecognizeAsync(input);
        Assert.Empty(result.Tables);
        Assert.Empty(result.Ocr.Regions);
        Assert.Empty(result.UnassignedRegionIndices);
    }

    [OcrFact]
    public async Task TwoTables_StaySeparateAndAssignEveryRegionExactlyOnce()
    {
        using var root = new TestRoot();
        using var input = new MemoryStream(Fixture("two-tables.png"));
        var result = await Create(root.Path).RecognizeAsync(input);
        Assert.Equal(2, result.Tables.Count);
        Assert.Equal(40, result.Tables[0].Bounds.X);
        Assert.Equal(740, result.Tables[1].Bounds.X);
        Assert.Equal(18, result.Ocr.Regions.Count);
        Assert.Equal(Enumerable.Range(0, 18), result.Tables.SelectMany(t => t.Cells).SelectMany(c => c.RegionIndices).Order());
        Assert.Empty(result.UnassignedRegionIndices);
    }

    [OcrFact]
    public async Task MultilineEmptyCellsAndOutsideText_ArePreservedWithoutInventedContent()
    {
        using var root = new TestRoot();
        using var input = new MemoryStream(Fixture("multiline-outside.png"));
        var result = await Create(root.Path).RecognizeAsync(input);
        var table = Assert.Single(result.Tables);
        Assert.Equal("Line one\nLine two", table.Cells[0].Text);
        Assert.All(table.Cells.Skip(1), c => { Assert.Equal("", c.Text); Assert.Empty(c.RegionIndices); });
        Assert.Equal("Outside", result.Ocr.Regions[Assert.Single(result.UnassignedRegionIndices)].Text);
    }

    [OcrFact]
    public async Task ActualTextCrossingCellBorder_RemainsUnassignedWithoutBeingDroppedOrDuplicated()
    {
        using var root = new TestRoot();
        using var input = new MemoryStream(Fixture("crossing-text.png"));
        var result = await Create(root.Path).RecognizeAsync(input);
        var table = Assert.Single(result.Tables);
        Assert.NotEmpty(result.Ocr.Regions);
        Assert.All(table.Cells, c => { Assert.Empty(c.RegionIndices); Assert.Equal("", c.Text); });
        Assert.Equal(Enumerable.Range(0, result.Ocr.Regions.Count), result.UnassignedRegionIndices);
        Assert.Empty(Directory.GetFileSystemEntries(root.Path));
    }

    [OcrTheory]
    [InlineData("tables")]
    [InlineData("rows")]
    [InlineData("columns")]
    [InlineData("cells")]
    public async Task ActualGridBudgets_FailWithoutTruncatingTables(string budget)
    {
        using var root = new TestRoot();
        using var input = new MemoryStream(Fixture(budget == "tables" ? "two-tables.png" : "bilingual-grid.png"));
        var recognizer = Create(root.Path, o => budget switch
        {
            "tables" => o with { MaximumTables = 1 }, "rows" => o with { MaximumRows = 2 },
            "columns" => o with { MaximumColumns = 2 }, _ => o with { MaximumGridCells = 8 }
        });
        var error = await Assert.ThrowsAsync<MediaProcessException>(() => recognizer.RecognizeAsync(input));
        Assert.Contains("budget", error.StandardError, StringComparison.Ordinal);
        Assert.True(input.CanRead);
        Assert.Empty(Directory.GetFileSystemEntries(root.Path));
    }

    [OcrFact]
    public async Task IndependentScratchLimitAndCancellation_CleanOnlyOwnedArtifacts()
    {
        using var root = new TestRoot();
        using var input = new MemoryStream(Fixture("bilingual-grid.png"));
        var limited = Create(root.Path, o => o with { Ocr = o.Ocr with { MaximumIntermediateBytes = 10 } });
        await Assert.ThrowsAsync<InvalidDataException>(() => limited.RecognizeAsync(input));
        input.Position = 0;
        var keep = Path.Combine(root.Path, "keep.txt");
        await File.WriteAllTextAsync(keep, "keep");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create(root.Path).RecognizeAsync(input, cancellation.Token));
        Assert.True(input.CanRead);
        Assert.Equal("keep", await File.ReadAllTextAsync(keep));
        Assert.Single(Directory.GetFileSystemEntries(root.Path));
    }

    private static TableRecognizer Create(string temporary, Func<TableRecognitionOptions, TableRecognitionOptions>? transform = null)
    {
        var options = new TableRecognitionOptions { Ocr = new() { PythonExecutablePath = Environment.GetEnvironmentVariable(OcrFactAttribute.Python)!,
            ModelDirectory = Environment.GetEnvironmentVariable(OcrFactAttribute.Models)!, TemporaryDirectory = temporary, CpuThreads = 2 } };
        return new(transform?.Invoke(options) ?? options);
    }

    private static TableRecognitionOptions LocalOptions(TestRoot root)
    {
        foreach (var name in new[] { OcrProcessor.DetectionFile, OcrProcessor.RecognitionFile, OcrProcessor.DictionaryFile })
            File.WriteAllBytes(Path.Combine(root.Path, name), [1]);
        return new() { Ocr = new() { PythonExecutablePath = Environment.ProcessPath!, ModelDirectory = root.Path, TemporaryDirectory = root.Path } };
    }

    private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(FixtureRoot(), "Fixtures", name));
    private static string FixtureRoot([System.Runtime.CompilerServices.CallerFilePath] string file = "") => Path.GetDirectoryName(file)!;
    private sealed class TestRoot : IDisposable
    {
        internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ms-table-tests-" + Guid.NewGuid().ToString("N"));
        internal TestRoot() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
