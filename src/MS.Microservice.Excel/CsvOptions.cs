using System.Text;

namespace MS.Microservice.Infrastructure.Utils;

/// <summary>CSV syntax, encoding and per-record limits. No header or type conversion is implicit.</summary>
public sealed record CsvOptions
{
    public char Delimiter { get; init; } = ',';
    public string NewLine { get; init; } = "\r\n";
    public Encoding Encoding { get; init; } = new UTF8Encoding(false, true);
    public int MaxRecordChars { get; init; } = 1_048_576;
    public int MaxColumns { get; init; } = 16_384;

    internal void Validate()
    {
        if (Delimiter is '"' or '\r' or '\n' or '\0' or '\uFEFF' || char.IsSurrogate(Delimiter))
            throw new ArgumentException("The delimiter cannot be a quote, newline, NUL, BOM or surrogate.", nameof(Delimiter));
        if (NewLine is not ("\r\n" or "\n" or "\r"))
            throw new ArgumentException("The output newline must be CRLF, LF or CR.", nameof(NewLine));
        ArgumentNullException.ThrowIfNull(Encoding);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxRecordChars);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxColumns);
    }
}
