using System.Runtime.CompilerServices;
using System.Text;

namespace MS.Microservice.Infrastructure.Utils;

/// <summary>A logical CSV record; row numbering starts at one and includes blank records.</summary>
public sealed record CsvRow(long RowNumber, IReadOnlyList<string> Fields);

/// <summary>A CSV syntax or size error with one-based logical record and field positions.</summary>
public sealed class CsvFormatException(string message, long rowNumber, int columnNumber)
    : FormatException($"CSV row {rowNumber}, column {columnNumber}: {message}")
{
    public long RowNumber { get; } = rowNumber;
    public int ColumnNumber { get; } = columnNumber;
}

/// <summary>Bounded, asynchronous CSV record reading and writing on caller-owned streams.</summary>
public static class CsvHelper
{
    /// <summary>
    /// Reads from the current position, preserving field text and embedded newline characters.
    /// Accepts CRLF, LF and CR record endings and consumes at most one initial encoding marker.
    /// Uses the explicit encoding without guessing it; the stream remains open, including on errors.
    /// </summary>
    public static async IAsyncEnumerable<CsvRow> ReadAsync(
        Stream source, CsvOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead)
            throw new ArgumentException("The stream must be readable.", nameof(source));
        options ??= new CsvOptions();
        options.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        using var reader = new StreamReader(source, options.Encoding, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
        var buffer = new char[4096];
        var field = new StringBuilder();
        var fields = new List<string>();
        var state = FieldState.Start;
        long rowNumber = 1;
        long recordChars = 0;
        var hasRecord = false;
        // StreamReader consumes the configured preamble even with encoding detection disabled.
        var firstCharacter = options.Encoding.GetPreamble().Length == 0;
        var skipLineFeed = false;

        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) != 0)
        {
            for (var index = 0; index < read; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var character = buffer[index];
                if (firstCharacter)
                {
                    firstCharacter = false;
                    if (character == '\uFEFF')
                        continue;
                }
                if (skipLineFeed)
                {
                    skipLineFeed = false;
                    if (character == '\n')
                        continue;
                }

                if (state != FieldState.Quoted && character is '\r' or '\n')
                {
                    AddField();
                    yield return new CsvRow(rowNumber++, fields.AsReadOnly());
                    fields = [];
                    state = FieldState.Start;
                    recordChars = 0;
                    hasRecord = false;
                    skipLineFeed = character == '\r';
                    continue;
                }

                if (++recordChars > options.MaxRecordChars)
                    throw Error("The record exceeds MaxRecordChars.");
                hasRecord = true;
                switch (state)
                {
                    case FieldState.Start:
                        if (character == '"')
                            state = FieldState.Quoted;
                        else if (character == options.Delimiter)
                            AddField();
                        else
                        {
                            field.Append(character);
                            state = FieldState.Unquoted;
                        }
                        break;
                    case FieldState.Unquoted:
                        if (character == options.Delimiter)
                        {
                            AddField();
                            state = FieldState.Start;
                        }
                        else if (character == '"')
                            throw Error("A quote must start at the beginning of a field.");
                        else
                            field.Append(character);
                        break;
                    case FieldState.Quoted:
                        if (character == '"')
                            state = FieldState.AfterQuote;
                        else
                            field.Append(character);
                        break;
                    case FieldState.AfterQuote:
                        if (character == '"')
                        {
                            field.Append('"');
                            state = FieldState.Quoted;
                        }
                        else if (character == options.Delimiter)
                        {
                            AddField();
                            state = FieldState.Start;
                        }
                        else
                            throw Error("Only a delimiter or record ending may follow a closing quote.");
                        break;
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (state == FieldState.Quoted)
            throw Error("The quoted field has no closing quote.");
        if (hasRecord)
        {
            AddField();
            yield return new CsvRow(rowNumber, fields.AsReadOnly());
        }

        void AddField()
        {
            if (fields.Count >= options.MaxColumns)
                throw Error("The record exceeds MaxColumns.");
            fields.Add(field.ToString());
            field.Clear();
        }

        CsvFormatException Error(string message) => new(message, rowNumber, fields.Count + 1);
    }

    /// <summary>
    /// Writes records lazily, quoting delimiters, quotes and newlines. Null fields become empty fields;
    /// empty records are rejected. The encoding controls the BOM, and the stream remains open.
    /// Cancellation or failure may leave partial output; callers own publication and rollback.
    /// </summary>
    public static async Task WriteAsync(
        Stream destination, IEnumerable<IReadOnlyList<string?>> rows, CsvOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(rows);
        if (!destination.CanWrite)
            throw new ArgumentException("The stream must be writable.", nameof(destination));
        options ??= new CsvOptions();
        options.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        var writer = new StreamWriter(destination, options.Encoding, bufferSize: 4096, leaveOpen: true);
        var delimiter = options.Delimiter.ToString();
        long rowNumber = 0;
        IEnumerator<IReadOnlyList<string?>>? enumerator = null;
        try
        {
            enumerator = rows.GetEnumerator();
            while (enumerator.MoveNext())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = enumerator.Current;
                rowNumber++;
                if (row is null || row.Count == 0)
                    throw new CsvFormatException("A record must contain at least one field.", rowNumber, 1);
                if (row.Count > options.MaxColumns)
                    throw new CsvFormatException("The record exceeds MaxColumns.", rowNumber, options.MaxColumns + 1);

                long recordChars = 0;
                for (var column = 0; column < row.Count; column++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var value = row[column] ?? string.Empty;
                    recordChars += (long)value.Length + (column == 0 ? 0 : 1);
                    if (recordChars > options.MaxRecordChars)
                        throw new CsvFormatException("The record exceeds MaxRecordChars.", rowNumber, column + 1);
                    var quote = value.StartsWith('\uFEFF');
                    long quoteCount = 0;
                    foreach (var character in value)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (character == '"')
                            quoteCount++;
                        if (character == options.Delimiter || character is '"' or '\r' or '\n')
                            quote = true;
                    }
                    if (quote)
                        recordChars += quoteCount + 2;
                    if (recordChars > options.MaxRecordChars)
                        throw new CsvFormatException("The record exceeds MaxRecordChars.", rowNumber, column + 1);

                    if (column != 0)
                        await writer.WriteAsync(delimiter.AsMemory(), cancellationToken).ConfigureAwait(false);
                    if (quote)
                    {
                        await writer.WriteAsync("\"".AsMemory(), cancellationToken).ConfigureAwait(false);
                        await writer.WriteAsync(value.Replace("\"", "\"\"", StringComparison.Ordinal).AsMemory(), cancellationToken).ConfigureAwait(false);
                        await writer.WriteAsync("\"".AsMemory(), cancellationToken).ConfigureAwait(false);
                    }
                    else
                        await writer.WriteAsync(value.AsMemory(), cancellationToken).ConfigureAwait(false);
                }

                await writer.WriteAsync(options.NewLine.AsMemory(), cancellationToken).ConfigureAwait(false);
            }

            var completedEnumerator = enumerator;
            enumerator = null;
            completedEnumerator.Dispose();
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch
        {
            try
            {
                enumerator?.Dispose();
            }
            catch
            {
                // Preserve the processing failure if source cleanup also fails.
            }
            try
            {
                await writer.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // Preserve the processing failure if cleanup also fails.
            }
            throw;
        }

        await writer.DisposeAsync().ConfigureAwait(false);
    }

    private enum FieldState { Start, Unquoted, Quoted, AfterQuote }
}
