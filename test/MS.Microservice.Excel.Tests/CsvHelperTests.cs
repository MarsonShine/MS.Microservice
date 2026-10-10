using System.Text;
using MS.Microservice.Infrastructure.Utils;
using Xunit;

namespace MS.Microservice.Excel.Tests;

public sealed class CsvHelperTests
{
    [Fact]
    public async Task ReadAsync_QuotedFields_PreserveDelimitersQuotesWhitespaceTabsAndNewlines()
    {
        using var stream = Utf8("name,note,tail\r\n\"张三,李四\",\"first\r\nsecond\nthird\rfourth \"\"quote\"\"\",\r\n a\tb ,simple,end");

        var rows = await ReadAllAsync(stream);

        Assert.Equal(3, rows.Count);
        Assert.Equal(new[] { "name", "note", "tail" }, rows[0].Fields);
        Assert.Equal(new[] { "张三,李四", "first\r\nsecond\nthird\rfourth \"quote\"", "" }, rows[1].Fields);
        Assert.Equal(new[] { " a\tb ", "simple", "end" }, rows[2].Fields);
        Assert.Equal(new long[] { 1, 2, 3 }, rows.Select(row => row.RowNumber));
        Assert.True(stream.CanRead);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)rows[0].Fields).Clear());
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("\r\n", 1)]
    [InlineData("\n\n", 2)]
    [InlineData("\r\r", 2)]
    [InlineData("\"\"", 1)]
    [InlineData("a\r\n", 1)]
    [InlineData("a\rb\nc\r\n", 3)]
    public async Task ReadAsync_EmptyBlankAndTrailingNewlines_HaveExplicitRecordCounts(string csv, int count)
    {
        using var stream = Utf8(csv);
        var rows = await ReadAllAsync(stream);

        Assert.Equal(count, rows.Count);
    }

    [Theory]
    [InlineData(",", new[] { "", "" })]
    [InlineData("a,,", new[] { "a", "", "" })]
    [InlineData("\"\",\"\"", new[] { "", "" })]
    [InlineData("  , \t ", new[] { "  ", " \t " })]
    [InlineData("\"a\"\"b\"", new[] { "a\"b" })]
    public async Task ReadAsync_EmptyFieldsAndEscapes_RetainEveryField(string csv, string[] expected)
    {
        using var stream = Utf8(csv);

        Assert.Equal(expected, Assert.Single(await ReadAllAsync(stream)).Fields);
    }

    [Theory]
    [InlineData("\"open", 1, 1)]
    [InlineData("ok\r\nvalue,\"open\r\nnext", 2, 2)]
    [InlineData("ab\"cd", 1, 1)]
    [InlineData("a,\"b\"suffix", 1, 2)]
    [InlineData("a,\"b\" ", 1, 2)]
    [InlineData("\"a\"\"", 1, 1)]
    public async Task ReadAsync_MalformedQuotes_ReportLogicalRecordAndField(string csv, long rowNumber, int columnNumber)
    {
        using var stream = Utf8(csv);

        var error = await Assert.ThrowsAsync<CsvFormatException>(() => ReadAllAsync(stream));

        Assert.Equal(rowNumber, error.RowNumber);
        Assert.Equal(columnNumber, error.ColumnNumber);
        Assert.True(stream.CanRead);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public async Task ReadAsync_ChunkedBomAndUnicode_PreserveDecoderAndParserState(int chunkSize)
    {
        const string csv = "\uFEFF\"😀,汉字\",\"a\r\nb\"\r\nx,\"\"\"\"\r\n";
        using var stream = new ChunkedReadStream(Encoding.UTF8.GetBytes(csv), chunkSize);

        var rows = await ReadAllAsync(stream);

        Assert.Equal(new[] { "😀,汉字", "a\r\nb" }, rows[0].Fields);
        Assert.Equal(new[] { "x", "\"" }, rows[1].Fields);
        Assert.True(stream.CanRead);
    }

    [Fact]
    public async Task ReadAsync_Bom_IsSkippedOnlyAtStartAndRespectsExplicitEncoding()
    {
        using var utf8 = Utf8("\uFEFFa,\uFEFFb\n\uFEFFc");
        var utf8Rows = await ReadAllAsync(utf8);
        Assert.Equal(new[] { "a", "\uFEFFb" }, utf8Rows[0].Fields);
        Assert.Equal("\uFEFFc", utf8Rows[1].Fields[0]);

        var encoding = new UnicodeEncoding(false, true, true);
        using var utf16 = new ChunkedReadStream([.. encoding.GetPreamble(), .. encoding.GetBytes("中文,😀")], 1);
        Assert.Equal(new[] { "中文", "😀" }, Assert.Single(await ReadAllAsync(utf16, new CsvOptions { Encoding = encoding })).Fields);

        using var quotedBom = Utf8("\"\uFEFFdata\"");
        Assert.Equal("\uFEFFdata", Assert.Single(await ReadAllAsync(quotedBom)).Fields[0]);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    [InlineData(true, 3)]
    public async Task ReadAsync_RepeatedInitialBom_ConsumesExactlyOneRegardlessOfEncodingPreamble(bool configuredPreamble, int markerCount)
    {
        var encoding = new UTF8Encoding(configuredPreamble, true);
        using var stream = new ChunkedReadStream(encoding.GetBytes(new string('\uFEFF', markerCount) + "x"), 1);

        var row = Assert.Single(await ReadAllAsync(stream, new CsvOptions { Encoding = encoding }));

        Assert.Equal(new string('\uFEFF', markerCount - 1) + "x", row.Fields[0]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadAsync_ExplicitUnicodeEncodings_PreambleDoesNotConsumeFollowingDataMarker(bool configuredPreamble)
    {
        Encoding[] encodings =
        [
            new UnicodeEncoding(false, configuredPreamble, true), new UnicodeEncoding(true, configuredPreamble, true),
            new UTF32Encoding(false, configuredPreamble, true), new UTF32Encoding(true, configuredPreamble, true)
        ];
        foreach (var encoding in encodings)
        {
            using var stream = new ChunkedReadStream(encoding.GetBytes("\uFEFF\uFEFFx"), 1);

            Assert.Equal("\uFEFFx", Assert.Single(await ReadAllAsync(stream, new CsvOptions { Encoding = encoding })).Fields[0]);
        }
    }

    [Fact]
    public async Task ReadAsync_InvalidUtf8_IsRejectedWithoutClosingStream()
    {
        using var stream = new MemoryStream([0xEF, 0xBB, 0xBF, 0xC3, 0x28]);

        await Assert.ThrowsAsync<DecoderFallbackException>(() => ReadAllAsync(stream));

        Assert.True(stream.CanRead);
    }

    [Fact]
    public async Task ReadAsync_CurrentStreamPosition_IsRespected()
    {
        using var stream = Utf8("skip\nvalue,tail");
        stream.Position = 5;

        Assert.Equal(new[] { "value", "tail" }, Assert.Single(await ReadAllAsync(stream)).Fields);
    }

    [Theory]
    [InlineData(';', "a;b\nc;\"d;e\"", new[] { "c", "d;e" })]
    [InlineData('\t', "a\tb\nc\t\"d\te\"", new[] { "c", "d\te" })]
    [InlineData('|', "a|b\nc|\"d|e\"", new[] { "c", "d|e" })]
    public async Task ReadAsync_CustomDelimiter_IsLiteral(char delimiter, string csv, string[] expected)
    {
        using var stream = Utf8(csv);

        Assert.Equal(expected, (await ReadAllAsync(stream, new CsvOptions { Delimiter = delimiter }))[1].Fields);
    }

    [Theory]
    [InlineData("abcd", 4)]
    [InlineData("\"a,b\"", 5)]
    [InlineData("a,b", 3)]
    [InlineData("\"a\r\nb\"", 6)]
    public async Task ReadAsync_RecordCharLimit_IncludesCsvSyntaxButExcludesRecordEnding(string csv, int limit)
    {
        using var valid = Utf8(csv + "\r\n");
        Assert.Single(await ReadAllAsync(valid, new CsvOptions { MaxRecordChars = limit }));
        using var oversized = Utf8(csv + "\r\n");

        await Assert.ThrowsAsync<CsvFormatException>(() => ReadAllAsync(oversized, new CsvOptions { MaxRecordChars = limit - 1 }));
    }

    [Fact]
    public async Task ReadAsync_ColumnLimit_ReportsFirstExcessColumn()
    {
        using var valid = Utf8("a,b");
        Assert.Single(await ReadAllAsync(valid, new CsvOptions { MaxColumns = 2 }));
        using var invalid = Utf8("a,b,");

        var error = await Assert.ThrowsAsync<CsvFormatException>(() => ReadAllAsync(invalid, new CsvOptions { MaxColumns = 2 }));

        Assert.Equal(3, error.ColumnNumber);
    }

    [Fact]
    public async Task ReadAsync_LargeInput_IsLazyAndCanStopEarly()
    {
        using var stream = new ChunkedReadStream(Encoding.UTF8.GetBytes("first,row\n" + new string('x', 100_000)), 128, maxBytesToRead: 8192);

        await using (var enumerator = CsvHelper.ReadAsync(stream).GetAsyncEnumerator())
        {
            Assert.True(await enumerator.MoveNextAsync());
            Assert.Equal(new[] { "first", "row" }, enumerator.Current.Fields);
            Assert.InRange(stream.BytesRead, 1, 8192);
        }

        Assert.True(stream.CanRead);
    }

    [Fact]
    public async Task ReadAsync_FieldAcrossCharacterBuffers_IsReassembledWithoutChangingText()
    {
        var field = new string('汉', 4094) + "😀" + new string('汉', 4906) + "\r\n\"quote\"";
        using var stream = Utf8("\"" + field.Replace("\"", "\"\"") + "\",end");

        Assert.Equal(new[] { field, "end" }, Assert.Single(await ReadAllAsync(stream)).Fields);
    }

    [Fact]
    public async Task ReadAsync_PreCanceled_DoesNotRead()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        using var stream = new ChunkedReadStream([1], 1);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ReadAllAsync(stream, cancellationToken: source.Token));

        Assert.Equal(0, stream.BytesRead);
        Assert.True(stream.CanRead);
    }

    [Fact]
    public async Task ReadAsync_CanceledDuringIo_PropagatesAndLeavesStreamOpen()
    {
        using var source = new CancellationTokenSource();
        using var stream = new ChunkedReadStream("a,b\n"u8.ToArray(), 1, afterRead: source.Cancel);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ReadAllAsync(stream, cancellationToken: source.Token));

        Assert.Equal(1, stream.BytesRead);
        Assert.True(stream.CanRead);
    }

    [Fact]
    public async Task ReadAsync_EnumeratorCancellation_IsHonoredBetweenRecords()
    {
        using var source = new CancellationTokenSource();
        using var stream = Utf8("a\nb\n");
        await using var enumerator = CsvHelper.ReadAsync(stream).GetAsyncEnumerator(source.Token);
        Assert.True(await enumerator.MoveNextAsync());
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await enumerator.MoveNextAsync());
    }

    [Fact]
    public async Task WriteAsync_QuotesOnlyRequiredFields_AndRoundTripsNullAsEmpty()
    {
        IReadOnlyList<string?>[] input = [["plain", " a\tb ", "comma,value", "say \"hi\"", "a\r\nb\nc\rd", null, "😀中文"]];
        using var stream = new MemoryStream();

        await CsvHelper.WriteAsync(stream, input);

        Assert.Equal("plain, a\tb ,\"comma,value\",\"say \"\"hi\"\"\",\"a\r\nb\nc\rd\",,😀中文\r\n", Encoding.UTF8.GetString(stream.ToArray()));
        Assert.True(stream.CanWrite);
        stream.Position = 0;
        Assert.Equal(input[0].Select(value => value ?? ""), Assert.Single(await ReadAllAsync(stream)).Fields);
    }

    [Theory]
    [InlineData("\r\n")]
    [InlineData("\n")]
    [InlineData("\r")]
    public async Task WriteAsync_ConfigurableDelimiterAndRecordEnding_RoundTrip(string newLine)
    {
        var options = new CsvOptions { Delimiter = '|', NewLine = newLine };
        using var stream = new MemoryStream();

        await CsvHelper.WriteAsync(stream, new IReadOnlyList<string?>[] { new[] { "a|b", "c\nd" }, new[] { "x", "y" } }, options);

        Assert.Equal($"\"a|b\"|\"c\nd\"{newLine}x|y{newLine}", Encoding.UTF8.GetString(stream.ToArray()));
        stream.Position = 0;
        Assert.Equal(new[] { "a|b", "c\nd" }, (await ReadAllAsync(stream, options))[0].Fields);
    }

    [Fact]
    public async Task WriteAsync_BomEncodingAndCurrentPosition_AreExplicit()
    {
        var options = new CsvOptions { Encoding = new UTF8Encoding(true, true) };
        using var stream = new MemoryStream();
        await CsvHelper.WriteAsync(stream, new IReadOnlyList<string?>[] { new[] { "中文" } }, options);
        var firstOutput = stream.ToArray();
        Assert.True(firstOutput.AsSpan().StartsWith(options.Encoding.GetPreamble()));

        await CsvHelper.WriteAsync(stream, new IReadOnlyList<string?>[] { new[] { "tail" } }, options);
        Assert.Equal(firstOutput.Length + "tail\r\n"u8.Length, stream.Length);
        stream.Position = 0;
        var rows = await ReadAllAsync(stream, options);
        Assert.Equal("中文", rows[0].Fields[0]);
        Assert.Equal("tail", rows[1].Fields[0]);
    }

    [Fact]
    public async Task WriteAsync_Utf16_RoundTripsUnicode()
    {
        var options = new CsvOptions { Encoding = new UnicodeEncoding(false, true, true) };
        using var stream = new MemoryStream();

        await CsvHelper.WriteAsync(stream, new IReadOnlyList<string?>[] { new[] { "😀", "汉字" } }, options);
        stream.Position = 0;

        Assert.Equal(new[] { "😀", "汉字" }, Assert.Single(await ReadAllAsync(stream, options)).Fields);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WriteAsync_LeadingBomCharacter_IsQuotedToPreserveFieldData(bool emitBom)
    {
        var options = new CsvOptions { Encoding = new UTF8Encoding(emitBom, true) };
        using var stream = new MemoryStream();

        await CsvHelper.WriteAsync(stream, new IReadOnlyList<string?>[] { new[] { "\uFEFFdata", "tail" } }, options);
        stream.Position = 0;

        Assert.Equal(new[] { "\uFEFFdata", "tail" }, Assert.Single(await ReadAllAsync(stream, options)).Fields);
    }

    [Fact]
    public async Task WriteAsync_EmptyInputAndSingleEmptyField_HaveDistinctResults()
    {
        using var empty = new MemoryStream();
        await CsvHelper.WriteAsync(empty, []);
        Assert.Equal(0, empty.Length);
        using var blank = new MemoryStream();
        await CsvHelper.WriteAsync(blank, new IReadOnlyList<string?>[] { new[] { "" } });
        Assert.Equal("\r\n", Encoding.UTF8.GetString(blank.ToArray()));
    }

    [Fact]
    public async Task WriteAsync_ZeroFieldAndNullRecords_AreRejectedWithLocation()
    {
        using var output = new MemoryStream();
        var empty = await Assert.ThrowsAsync<CsvFormatException>(() => CsvHelper.WriteAsync(output, new IReadOnlyList<string?>[] { new[] { "ok" }, [] }));
        Assert.Equal(2, empty.RowNumber);
        Assert.Equal(1, empty.ColumnNumber);
        var missing = await Assert.ThrowsAsync<CsvFormatException>(() => CsvHelper.WriteAsync(output, new IReadOnlyList<string?>[] { null! }));
        Assert.Equal(1, missing.RowNumber);
    }

    [Fact]
    public async Task WriteAsync_RecordAndColumnLimits_UseSerializedCsvSize()
    {
        IReadOnlyList<string?>[] rows = [["a\"b", "c"]];
        using var output = new MemoryStream();
        await CsvHelper.WriteAsync(output, rows, new CsvOptions { MaxRecordChars = 8, MaxColumns = 2 });
        output.Position = 0;
        Assert.Single(await ReadAllAsync(output, new CsvOptions { MaxRecordChars = 8, MaxColumns = 2 }));

        var charError = await Assert.ThrowsAsync<CsvFormatException>(() => CsvHelper.WriteAsync(output, rows, new CsvOptions { MaxRecordChars = 7 }));
        Assert.Equal(2, charError.ColumnNumber);
        var columnError = await Assert.ThrowsAsync<CsvFormatException>(() => CsvHelper.WriteAsync(output, rows, new CsvOptions { MaxColumns = 1 }));
        Assert.Equal(2, columnError.ColumnNumber);
    }

    [Fact]
    public async Task WriteAsync_ManyLazyRows_ReachDestinationBeforeEnumerationCompletes()
    {
        using var output = new CountingWriteStream();

        await CsvHelper.WriteAsync(output, GenerateRows());

        Assert.True(output.CanWrite);
        Assert.True(output.BytesWritten > 50_000);

        IEnumerable<IReadOnlyList<string?>> GenerateRows()
        {
            for (var index = 0; index < 10_000; index++)
            {
                if (index == 5000)
                    Assert.True(output.BytesWritten > 0);
                yield return new[] { index.ToString(), "value" };
            }
        }
    }

    [Fact]
    public async Task WriteAsync_PreCanceled_DoesNotEnumerateOrWrite()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        using var output = new CountingWriteStream();
        var rows = Enumerable.Repeat("unused", 1).Select<string, IReadOnlyList<string?>>(_ => throw new InvalidOperationException("Should not enumerate."));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CsvHelper.WriteAsync(output, rows, cancellationToken: source.Token));

        Assert.Equal(0, output.BytesWritten);
        Assert.True(output.CanWrite);
    }

    [Fact]
    public async Task WriteAsync_CanceledDuringIo_PropagatesAndLeavesStreamOpen()
    {
        using var source = new CancellationTokenSource();
        using var output = new CountingWriteStream(afterWrite: source.Cancel);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CsvHelper.WriteAsync(output,
            new IReadOnlyList<string?>[] { new[] { new string('x', 9000) } }, cancellationToken: source.Token));

        Assert.True(output.CanWrite);
        Assert.True(output.BytesWritten > 0);
        Assert.True(output.SawCancelableWrite);
    }

    [Fact]
    public async Task WriteAsync_EnumerationFailure_DoesNotCloseStream()
    {
        using var output = new MemoryStream();
        var rows = Enumerable.Repeat("unused", 1).Select<string, IReadOnlyList<string?>>(_ => throw new InvalidOperationException("Input failed."));

        await Assert.ThrowsAsync<InvalidOperationException>(() => CsvHelper.WriteAsync(output, rows));

        Assert.True(output.CanWrite);
    }

    [Fact]
    public async Task WriteAsync_EnumerationAndCleanupFail_PreservesOriginalException()
    {
        var original = new InvalidOperationException("Original enumeration failure.");
        using var output = new FlushFailingStream();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => CsvHelper.WriteAsync(output, FailingRows()));

        Assert.Same(original, error);
        Assert.Equal(1, output.FlushCount);
        Assert.True(output.CanWrite);

        IEnumerable<IReadOnlyList<string?>> FailingRows()
        {
            yield return new[] { "buffered" };
            throw original;
        }
    }

    [Fact]
    public async Task WriteAsync_RecordLimitAndCleanupFail_PreservesLocatedFormatError()
    {
        using var output = new FlushFailingStream();

        var error = await Assert.ThrowsAsync<CsvFormatException>(() => CsvHelper.WriteAsync(output,
            new IReadOnlyList<string?>[] { new[] { "ok" }, new[] { "oversized" } }, new CsvOptions { MaxRecordChars = 2 }));

        Assert.Equal(2, error.RowNumber);
        Assert.Equal(1, error.ColumnNumber);
        Assert.Equal(1, output.FlushCount);
        Assert.True(output.CanWrite);
    }

    [Fact]
    public async Task WriteAsync_CancellationAndCleanupFail_PreservesCancellationToken()
    {
        using var source = new CancellationTokenSource();
        using var output = new FlushFailingStream();

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CsvHelper.WriteAsync(output, CancelAfterFirst(), cancellationToken: source.Token));

        Assert.Equal(source.Token, error.CancellationToken);
        Assert.Equal(1, output.FlushCount);
        Assert.True(output.CanWrite);

        IEnumerable<IReadOnlyList<string?>> CancelAfterFirst()
        {
            yield return new[] { "buffered" };
            source.Cancel();
            yield return new[] { "next" };
        }
    }

    [Fact]
    public async Task WriteAsync_OnlyCleanupFails_PropagatesCleanupError()
    {
        using var output = new FlushFailingStream(failOnFlush: 2);

        var error = await Assert.ThrowsAsync<IOException>(() => CsvHelper.WriteAsync(output, new IReadOnlyList<string?>[] { new[] { "valid" } }));

        Assert.Same(output.Failure, error);
        Assert.Equal(2, output.FlushCount);
        Assert.True(output.CanWrite);
    }

    [Fact]
    public async Task WriteAsync_FinalFlushAndCleanupFail_PreservesFirstIoError()
    {
        using var output = new FlushFailingStream(distinctFailures: true);

        var error = await Assert.ThrowsAsync<IOException>(() => CsvHelper.WriteAsync(output, new IReadOnlyList<string?>[] { new[] { "valid" } }));

        Assert.Same(output.Failure, error);
        Assert.Equal(2, output.FlushCount);
        Assert.True(output.CanWrite);
    }

    [Fact]
    public async Task WriteAsync_ProcessingAndBothCleanupOperationsFail_PreservesFormatError()
    {
        var rows = new DisposalFailingRows(["oversized"]);
        using var output = new FlushFailingStream();

        var error = await Assert.ThrowsAsync<CsvFormatException>(() => CsvHelper.WriteAsync(output, rows, new CsvOptions { MaxRecordChars = 1 }));

        Assert.Equal(1, error.RowNumber);
        Assert.Equal(1, error.ColumnNumber);
        Assert.Equal(1, rows.DisposeCount);
        Assert.Equal(1, output.FlushCount);
        Assert.True(output.CanWrite);
    }

    [Fact]
    public async Task WriteAsync_EnumeratorCleanupAndWriterCleanupFail_PreservesEnumeratorError()
    {
        var rows = new DisposalFailingRows(["valid"]);
        using var output = new FlushFailingStream();

        var error = await Assert.ThrowsAsync<IOException>(() => CsvHelper.WriteAsync(output, rows));

        Assert.Same(rows.Failure, error);
        Assert.Equal(1, rows.DisposeCount);
        Assert.Equal(1, output.FlushCount);
        Assert.True(output.CanWrite);
    }

    [Fact]
    public async Task WriteAsync_GetEnumeratorAndWriterCleanupFail_PreservesAcquisitionError()
    {
        var original = new InvalidOperationException("Source acquisition failure.");
        using var output = new FlushFailingStream();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => CsvHelper.WriteAsync(output, new AcquisitionFailingRows(original)));

        Assert.Same(original, error);
        Assert.Equal(1, output.FlushCount);
        Assert.True(output.CanWrite);
    }

    [Fact]
    public async Task WriteAsync_MoveNextAndBothCleanupOperationsFail_PreservesEnumerationError()
    {
        var original = new InvalidOperationException("Source enumeration failure.");
        var rows = new DisposalFailingRows(["valid"], original);
        using var output = new FlushFailingStream();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => CsvHelper.WriteAsync(output, rows));

        Assert.Same(original, error);
        Assert.Equal(1, rows.DisposeCount);
        Assert.Equal(1, output.FlushCount);
        Assert.True(output.CanWrite);
    }

    [Fact]
    public async Task InvalidOptions_AreRejectedByReadAndWrite()
    {
        var invalid = new[]
        {
            new CsvOptions { Delimiter = '"' }, new CsvOptions { Delimiter = '\n' }, new CsvOptions { Delimiter = '\r' },
            new CsvOptions { Delimiter = '\0' }, new CsvOptions { Delimiter = '\uFEFF' }, new CsvOptions { Delimiter = '\uD800' },
            new CsvOptions { NewLine = "invalid" }, new CsvOptions { Encoding = null! },
            new CsvOptions { MaxRecordChars = 0 }, new CsvOptions { MaxColumns = 0 }
        };
        foreach (var options in invalid)
        {
            using var input = Utf8("a");
            using var output = new MemoryStream();
            await Assert.ThrowsAnyAsync<ArgumentException>(() => ReadAllAsync(input, options));
            await Assert.ThrowsAnyAsync<ArgumentException>(() => CsvHelper.WriteAsync(output, [], options));
        }
    }

    [Fact]
    public async Task NullAndUnusableStreams_AreRejected()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => ReadAllAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => CsvHelper.WriteAsync(null!, []));
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<ArgumentNullException>(() => CsvHelper.WriteAsync(output, null!));
        using var unreadable = new CountingWriteStream();
        await Assert.ThrowsAsync<ArgumentException>(() => ReadAllAsync(unreadable));
        using var unwritable = new ChunkedReadStream([], 1);
        await Assert.ThrowsAsync<ArgumentException>(() => CsvHelper.WriteAsync(unwritable, []));
    }

    private static MemoryStream Utf8(string text) => new(Encoding.UTF8.GetBytes(text));

    private static async Task<List<CsvRow>> ReadAllAsync(Stream stream, CsvOptions? options = null, CancellationToken cancellationToken = default)
    {
        var rows = new List<CsvRow>();
        await foreach (var row in CsvHelper.ReadAsync(stream, options, cancellationToken))
            rows.Add(row);
        return rows;
    }

    private sealed class FlushFailingStream(int failOnFlush = 1, bool distinctFailures = false) : MemoryStream
    {
        public IOException Failure { get; } = new("Original flush failure.");
        public int FlushCount { get; private set; }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            FlushCount++;
            if (FlushCount < failOnFlush)
                return base.FlushAsync(cancellationToken);
            return Task.FromException(distinctFailures && FlushCount > failOnFlush ? new IOException("Cleanup failure.") : Failure);
        }
    }

    private sealed class AcquisitionFailingRows(Exception failure) : IEnumerable<IReadOnlyList<string?>>
    {
        public IEnumerator<IReadOnlyList<string?>> GetEnumerator() => throw failure;
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class DisposalFailingRows(IReadOnlyList<string?> row, Exception? moveNextFailure = null) : IEnumerable<IReadOnlyList<string?>>
    {
        public IOException Failure { get; } = new("Source cleanup failure.");
        public int DisposeCount { get; private set; }
        public IEnumerator<IReadOnlyList<string?>> GetEnumerator() => new Enumerator(this, row, moveNextFailure);
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        private sealed class Enumerator(DisposalFailingRows owner, IReadOnlyList<string?> row, Exception? moveNextFailure) : IEnumerator<IReadOnlyList<string?>>
        {
            private bool _moved;
            public IReadOnlyList<string?> Current => row;
            object System.Collections.IEnumerator.Current => Current;
            public bool MoveNext()
            {
                if (_moved)
                {
                    if (moveNextFailure is not null)
                        throw moveNextFailure;
                    return false;
                }
                _moved = true;
                return true;
            }
            public void Reset() => throw new NotSupportedException();
            public void Dispose()
            {
                owner.DisposeCount++;
                throw owner.Failure;
            }
        }
    }

    private sealed class ChunkedReadStream(byte[] bytes, int chunkSize, int maxBytesToRead = int.MaxValue, Action? afterRead = null) : Stream
    {
        private bool _disposed;
        public int BytesRead { get; private set; }
        public override bool CanRead => !_disposed;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var count = Math.Min(Math.Min(buffer.Length, chunkSize), bytes.Length - BytesRead);
            if (BytesRead + count > maxBytesToRead)
                throw new InvalidOperationException("Read ahead exceeded the test budget.");
            bytes.AsSpan(BytesRead, count).CopyTo(buffer);
            BytesRead += count;
            afterRead?.Invoke();
            return count;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Read(buffer.Span));
        }

        protected override void Dispose(bool disposing)
        {
            _disposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class CountingWriteStream(Action? afterWrite = null) : Stream
    {
        private bool _disposed;
        public long BytesWritten { get; private set; }
        public bool SawCancelableWrite { get; private set; }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => !_disposed;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            BytesWritten += buffer.Length;
            afterWrite?.Invoke();
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SawCancelableWrite |= cancellationToken.CanBeCanceled;
            Write(buffer.Span);
            return ValueTask.CompletedTask;
        }

        protected override void Dispose(bool disposing)
        {
            _disposed = true;
            base.Dispose(disposing);
        }
    }
}
