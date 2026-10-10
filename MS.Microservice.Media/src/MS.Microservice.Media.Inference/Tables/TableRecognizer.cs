using System.Globalization;
using System.Text.Json;
using MS.Microservice.Media.Inference.Ocr;
using MS.Microservice.Media.Video;

namespace MS.Microservice.Media.Inference.Tables;

/// <summary>Recognizes axis-aligned, closed ruled grids and assigns actual OCR regions to rectangular cells.</summary>
public sealed class TableRecognizer
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly TableRecognitionOptions _options;
    private readonly OcrProcessor _ocr;

    public TableRecognizer(TableRecognitionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _ocr = new OcrProcessor(options.Ocr);
        if (options.MinimumLineLength is < 8 or > 512 || options.LineTolerance is < 1 or > 8
            || !double.IsFinite(options.MinimumBoundaryCoverage) || options.MinimumBoundaryCoverage is < .75 or > 1
            || options.MaximumTables is < 1 or > 16 || options.MaximumRows is < 1 or > 128 || options.MaximumColumns is < 1 or > 128
            || options.MaximumGridCells is < 1 or > 2048)
            throw new ArgumentException("Invalid table geometry or output budgets.", nameof(options));
        _options = options with { Ocr = _ocr.Options };
    }

    /// <summary>Consumes the caller stream at its current position and keeps it open. Coordinates refer to the EXIF-normalized image.</summary>
    public async Task<TableRecognitionResult> RecognizeAsync(Stream inputImage, CancellationToken cancellationToken = default)
    {
        var image = await OcrImageWorkspace.CreateAsync(inputImage, _options.Ocr, cancellationToken).ConfigureAwait(false);
        Exception? failure = null;
        try
        {
            var arguments = _ocr.CreateWorkerArguments("table_worker.py", image.ImagePath);
            Add("--line-length", _options.MinimumLineLength);
            Add("--line-tolerance", _options.LineTolerance);
            Add("--boundary-coverage", _options.MinimumBoundaryCoverage);
            Add("--max-tables", _options.MaximumTables);
            Add("--max-rows", _options.MaximumRows);
            Add("--max-columns", _options.MaximumColumns);
            Add("--max-grid-cells", _options.MaximumGridCells);
            var json = await new MediaProcessRunner(_options.Ocr.Timeout).RunAsync(_options.Ocr.PythonExecutablePath, arguments, cancellationToken).ConfigureAwait(false);
            var result = JsonSerializer.Deserialize<TableRecognitionResult>(json, JsonOptions) ?? throw new InvalidDataException("The table worker returned no result.");
            ValidateResult(result, image.Width, image.Height, _options);
            cancellationToken.ThrowIfCancellationRequested();
            return result with
            {
                Ocr = result.Ocr with { Regions = Array.AsReadOnly(result.Ocr.Regions.Select(r => r with { Corners = Array.AsReadOnly(r.Corners.ToArray()) }).ToArray()) },
                Tables = Array.AsReadOnly(result.Tables.Select(t => t with { Cells = Array.AsReadOnly(t.Cells.Select(c => c with { RegionIndices = Array.AsReadOnly(c.RegionIndices.ToArray()) }).ToArray()) }).ToArray()),
                UnassignedRegionIndices = Array.AsReadOnly(result.UnassignedRegionIndices.ToArray())
            };
            void Add(string name, IFormattable value) { arguments.Add(name); arguments.Add(value.ToString(null, CultureInfo.InvariantCulture)); }
        }
        catch (Exception error) { failure = error; throw; }
        finally { image.Delete(failure); }
    }

    internal static void ValidateResult(TableRecognitionResult result, int width, int height, TableRecognitionOptions options)
    {
        if (result.Ocr is null || result.Tables is null || result.UnassignedRegionIndices is null || result.Tables.Count > options.MaximumTables)
            throw new InvalidDataException("Invalid table result collections.");
        OcrProcessor.ValidateResult(result.Ocr, width, height, options.Ocr);
        var assigned = new bool[result.Ocr.Regions.Count];
        long gridCells = 0;
        foreach (var table in result.Tables)
        {
            if (table is null || table.Cells is null || table.RowCount < 1 || table.ColumnCount < 1
                || table.RowCount > options.MaximumRows || table.ColumnCount > options.MaximumColumns
                || (long)table.RowCount * table.ColumnCount < 2 || !ValidBounds(table.Bounds, width, height)
                || table.Bounds.Width < table.ColumnCount * options.MinimumLineLength || table.Bounds.Height < table.RowCount * options.MinimumLineLength)
                throw new InvalidDataException("Invalid table dimensions or bounds.");
            gridCells += (long)table.RowCount * table.ColumnCount;
            if (gridCells > options.MaximumGridCells) throw new InvalidDataException("Table grid exceeds its cell budget.");
            var occupied = new bool[table.RowCount * table.ColumnCount];
            foreach (var cell in table.Cells)
            {
                if (cell is null || cell.RegionIndices is null || cell.Text is null || cell.Row < 0 || cell.Column < 0 || cell.RowSpan < 1 || cell.ColumnSpan < 1
                    || cell.RowSpan > table.RowCount - cell.Row || cell.ColumnSpan > table.ColumnCount - cell.Column
                    || !ValidBounds(cell.Bounds, width, height) || cell.Bounds.Width < cell.ColumnSpan * options.MinimumLineLength
                    || cell.Bounds.Height < cell.RowSpan * options.MinimumLineLength || cell.Bounds.X < table.Bounds.X || cell.Bounds.Y < table.Bounds.Y
                    || (long)cell.Bounds.X + cell.Bounds.Width > (long)table.Bounds.X + table.Bounds.Width
                    || (long)cell.Bounds.Y + cell.Bounds.Height > (long)table.Bounds.Y + table.Bounds.Height)
                    throw new InvalidDataException("Invalid cell bounds or span.");
                for (var row = cell.Row; row < cell.Row + cell.RowSpan; row++)
                for (var column = cell.Column; column < cell.Column + cell.ColumnSpan; column++)
                {
                    var index = row * table.ColumnCount + column;
                    if (occupied[index]) throw new InvalidDataException("Cell spans overlap.");
                    occupied[index] = true;
                }
                foreach (var index in cell.RegionIndices) Assign(index);
                if (cell.Text != string.Join("\n", cell.RegionIndices.Select(i => result.Ocr.Regions[i].Text)))
                    throw new InvalidDataException("Cell text does not match its OCR regions.");
            }
            if (occupied.Any(value => !value)) throw new InvalidDataException("Cell spans leave holes in the grid.");
        }
        foreach (var index in result.UnassignedRegionIndices) Assign(index);
        if (assigned.Any(value => !value)) throw new InvalidDataException("An OCR region was silently dropped.");
        void Assign(int index)
        {
            if (index < 0 || index >= assigned.Length || assigned[index]) throw new InvalidDataException("Invalid or duplicated OCR region assignment.");
            assigned[index] = true;
        }
    }

    private static bool ValidBounds(TableBounds? bounds, int width, int height) => bounds is { X: >= 0, Y: >= 0, Width: > 0, Height: > 0 }
        && (long)bounds.X + bounds.Width <= width && (long)bounds.Y + bounds.Height <= height;
}
