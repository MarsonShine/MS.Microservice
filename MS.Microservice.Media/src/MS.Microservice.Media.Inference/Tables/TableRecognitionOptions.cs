using MS.Microservice.Media.Inference.Ocr;

namespace MS.Microservice.Media.Inference.Tables;

public sealed record TableRecognitionOptions
{
    public required OcrOptions Ocr { get; init; }
    public int MinimumLineLength { get; init; } = 40;
    public int LineTolerance { get; init; } = 3;
    public double MinimumBoundaryCoverage { get; init; } = 0.9;
    public int MaximumTables { get; init; } = 8;
    public int MaximumRows { get; init; } = 64;
    public int MaximumColumns { get; init; } = 64;
    public int MaximumGridCells { get; init; } = 512;
}

public sealed record TableBounds(int X, int Y, int Width, int Height);

/// <summary>Zero-based row/column; region indices refer to the single OCR result. Text uses geometric order with newline separators.</summary>
public sealed record RecognizedCell(int Row, int Column, int RowSpan, int ColumnSpan, TableBounds Bounds, string Text, IReadOnlyList<int> RegionIndices);
public sealed record RecognizedTable(int RowCount, int ColumnCount, TableBounds Bounds, IReadOnlyList<RecognizedCell> Cells);
public sealed record TableRecognitionResult(OcrResult Ocr, IReadOnlyList<RecognizedTable> Tables, IReadOnlyList<int> UnassignedRegionIndices)
{
    public bool ReviewRecommended => true;
}
