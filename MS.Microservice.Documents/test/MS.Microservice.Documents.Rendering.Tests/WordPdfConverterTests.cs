using System.ComponentModel;
using System.Diagnostics;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;
using MS.Microservice.Documents.Pdf;
using SkiaSharp;
using Xunit;

namespace MS.Microservice.Documents.Rendering.Tests;

[Collection("Rendering native")]
public sealed class WordPdfConverterTests : IDisposable
{
    private readonly string _temporary = Path.Combine(Path.GetTempPath(), "ms-word-tests-" + Guid.NewGuid().ToString("N"));
    internal static string? OfficePath => Environment.GetEnvironmentVariable("LIBREOFFICE_TEST_PATH")
        ?? (File.Exists("C:/Program Files/LibreOffice/program/soffice.com") ? "C:/Program Files/LibreOffice/program/soffice.com" : null);
    public WordPdfConverterTests() => Directory.CreateDirectory(_temporary);
    public void Dispose() => Directory.Delete(_temporary, recursive: true);
    private WordPdfConverter Converter(bool native = true, string? dotnet = null) => new(new()
    {
        LibreOfficeExecutablePath = native ? OfficePath! : Environment.ProcessPath!, TemporaryDirectory = _temporary, DotNetHostPath = dotnet ?? "dotnet"
    });

    [LibreOfficeFact]
    public async Task Convert_RealUnicodeTableHeadersImagePaginationNonSeekableAndPrefix()
    {
        var original = WordPdfFixtures.Create();
        var snapshot = original.ToArray();
        using var input = new ShortReads([9, 8, 7, .. original], 3);
        using var output = new MemoryStream();
        output.Write([6, 5, 4]);
        var result = await Converter().ConvertAsync(input, output, new() { Limits = new() { MaximumInputBytes = original.Length } });
        Assert.Equal(2, result.Pages);
        Assert.Equal(output.Length - 3, result.Bytes);
        Assert.Equal(new byte[] { 6, 5, 4 }, output.ToArray()[..3]);
        Assert.Equal(snapshot, original);
        Assert.Equal(original.Length, input.BytesRead);
        Assert.True(input.CanRead);
        Assert.True(output.CanWrite);
        using var pdf = new MemoryStream(output.ToArray()[3..]);
        var processor = new PdfProcessor();
        var text = await processor.ExtractTextAsync(pdf);
        Assert.Contains("中文", text[0].Text, StringComparison.Ordinal);
        Assert.Contains("😀", text[0].Text, StringComparison.Ordinal);
        Assert.Contains("张三", text[0].Text, StringComparison.Ordinal);
        Assert.Contains("123.45", text[0].Text, StringComparison.Ordinal);
        Assert.Contains("<&>", text[0].Text, StringComparison.Ordinal);
        Assert.Contains("固定页眉", text[0].Text, StringComparison.Ordinal);
        Assert.Contains("固定页脚", text[1].Text, StringComparison.Ordinal);
        Assert.Contains("Page 2", text[1].Text, StringComparison.Ordinal);
        pdf.Position = 0;
        using var png = new MemoryStream();
        await processor.RenderPageAsync(pdf, 1, png, dpi: 72);
        using var raster = SKBitmap.Decode(png.ToArray());
        Assert.True(raster.Pixels.Count(pixel => pixel.Red > 200 && pixel.Blue < 60 && pixel.Green < 60) > 1000);
        Assert.Empty(Directory.GetFileSystemEntries(_temporary));
    }

    [LibreOfficeFact]
    public async Task Convert_PageFieldsAcrossRunsAreComputedAndLandscapeJpegIsPreserved()
    {
        var data = WordPdfFixtures.Create(mime: "image/jpeg", image: WordPdfFixtures.Image(SKEncodedImageFormat.Jpeg), edit: doc =>
        {
            var main = doc.MainDocumentPart!;
            var numbering = WordPdfFixtures.Paragraph("Page ");
            numbering.Append(new SimpleField(new Run(new Text("999"))) { Instruction = "PAGE" }, new Run(new Text(" of ") { Space = SpaceProcessingModeValues.Preserve }));
            numbering.Append(WordPdfFixtures.ComplexField(" NUM", "PAGES \\* Arabic ").ChildElements.Select(node => node.CloneNode(true)));
            main.HeaderParts.Single().Header!.Append(numbering);
            var size = main.Document!.Body!.Elements<SectionProperties>().Single().GetFirstChild<PageSize>()!;
            size.Width = 16838;
            size.Height = 11906;
            size.Orient = PageOrientationValues.Landscape;
        });
        using var input = new MemoryStream(data);
        using var output = new MemoryStream();
        await Converter().ConvertAsync(input, output);
        output.Position = 0;
        var processor = new PdfProcessor();
        var info = await processor.InspectAsync(output);
        Assert.InRange(info.Pages[0].WidthPoints, 841, 843);
        Assert.InRange(info.Pages[0].HeightPoints, 594, 596);
        output.Position = 0;
        var text = await processor.ExtractTextAsync(output);
        Assert.Contains("Page 1 of 2", text[0].Text, StringComparison.Ordinal);
        Assert.Contains("Page 2 of 2", text[1].Text, StringComparison.Ordinal);
        Assert.DoesNotContain("999", string.Join(' ', text.Select(page => page.Text)), StringComparison.Ordinal);
        Assert.Empty(Directory.GetFileSystemEntries(_temporary));
    }

    [LibreOfficeTheory]
    [InlineData("pages")]
    [InlineData("bytes")]
    public async Task Convert_RealPdfBudgetFailureNeverPublishesTruncatedPdf(string kind)
    {
        using var input = new MemoryStream(WordPdfFixtures.Create(pages: 3));
        using var output = new MemoryStream();
        output.Write([4, 3, 2]);
        var options = kind == "pages" ? new WordPdfOptions { Limits = new() { MaximumPages = 1 } }
            : new WordPdfOptions { Limits = new() { MaximumPdfBytes = 100 } };
        await Assert.ThrowsAsync<InvalidDataException>(() => Converter().ConvertAsync(input, output, options));
        Assert.Equal(new byte[] { 4, 3, 2 }, output.ToArray());
        Assert.True(input.CanRead);
        Assert.Empty(Directory.GetFileSystemEntries(_temporary));
    }

    [LibreOfficeFact]
    public async Task Convert_CancellationOfRunningLibreOfficeStopsOwnedProcessesBeforeReturning()
    {
        var baseline = RuntimeProcesses();
        using var cancellation = new CancellationTokenSource();
        using var input = new MemoryStream(WordPdfFixtures.Create(pages: 1000));
        using var output = new MemoryStream();
        var task = Converter().ConvertAsync(input, output, cancellationToken: cancellation.Token);
        var native = await WaitForNativeAsync(task, baseline);
        var clock = Stopwatch.StartNew();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(12));
        Assert.Empty(native.Intersect(RuntimeProcesses()));
        Assert.Empty(output.ToArray());
        Assert.Empty(Directory.GetFileSystemEntries(_temporary));
    }

    [LibreOfficeFact]
    public async Task Convert_TimeoutIncludesRunningNativeWork()
    {
        var baseline = RuntimeProcesses();
        using var input = new MemoryStream(WordPdfFixtures.Create(pages: 5000));
        using var output = new MemoryStream();
        var task = Converter().ConvertAsync(input, output, new() { Timeout = TimeSpan.FromSeconds(10) });
        var native = await WaitForNativeAsync(task, baseline);
        await Assert.ThrowsAsync<TimeoutException>(() => task);
        Assert.Empty(native.Intersect(RuntimeProcesses()));
        Assert.Empty(output.ToArray());
        Assert.Empty(Directory.GetFileSystemEntries(_temporary));
    }

    [LibreOfficeTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Convert_CallerCopyFailureOrCancellationLeavesOnlyCallerPartialOutput(bool cancel)
    {
        using var stopping = new CancellationTokenSource();
        using var input = new MemoryStream(WordPdfFixtures.Create(pages: 1));
        using var output = new PartialOutput(cancel ? stopping : null);
        var task = Converter().ConvertAsync(input, output, cancellationToken: stopping.Token);
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        else await Assert.ThrowsAsync<IOException>(() => task);
        Assert.Equal(5, output.Length);
        Assert.True(input.CanRead);
        Assert.True(output.CanWrite);
        Assert.Empty(Directory.GetFileSystemEntries(_temporary));
    }

    [WindowsFact]
    public async Task Convert_PreCanceledChunkCancellationSourceLimitAndBadHostKeepOwnership()
    {
        var data = WordPdfFixtures.Create();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        using var input = new ShortReads(data);
        using var output = new MemoryStream();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Converter(false).ConvertAsync(input, output, cancellationToken: canceled.Token));
        Assert.Equal(0, input.BytesRead);
        using var duringRead = new CancellationTokenSource();
        using var shortInput = new ShortReads(data, cancellation: duringRead);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Converter(false).ConvertAsync(shortInput, output, cancellationToken: duringRead.Token));
        Assert.Equal(7, shortInput.BytesRead);
        using var oversized = new ShortReads(data);
        await Assert.ThrowsAsync<InvalidDataException>(() => Converter(false).ConvertAsync(oversized, output, new() { Limits = new() { MaximumInputBytes = 100 } }));
        Assert.Equal(101, oversized.BytesRead);
        using var missingHost = new MemoryStream(data);
        await Assert.ThrowsAsync<Win32Exception>(() => Converter(false, "missing-dotnet-" + Guid.NewGuid().ToString("N")).ConvertAsync(missingHost, output));
        Assert.Empty(output.ToArray());
        Assert.True(input.CanRead);
        Assert.True(shortInput.CanRead);
        Assert.True(oversized.CanRead);
        Assert.Empty(Directory.GetFileSystemEntries(_temporary));
    }

    [WindowsTheory]
    [InlineData("same")]
    [InlineData("input")]
    [InlineData("output")]
    [InlineData("limit")]
    [InlineData("timeout")]
    [InlineData("pixels")]
    public async Task Convert_RejectsInvalidStreamsAndOptionsBeforeReading(string variant)
    {
        using var source = new MemoryStream([1, 2, 3]);
        using var destination = new MemoryStream();
        if (variant == "input") source.Dispose();
        if (variant == "output") destination.Dispose();
        var options = variant switch
        {
            "limit" => new WordPdfOptions { Limits = new() { MaximumPages = 10_001 } },
            "timeout" => new() { Timeout = TimeSpan.Zero },
            "pixels" => new() { Limits = new() { MaximumImagePixels = 0 } },
            _ => new()
        };
        await Assert.ThrowsAsync<ArgumentException>(() => Converter(false).ConvertAsync(source, variant == "same" ? source : destination, options));
        Assert.Empty(Directory.GetFileSystemEntries(_temporary));
    }

    [WindowsFact]
    public async Task Convert_UnsupportedAndCorruptDocxRejectedInsideWorkerBeforeNativeStart()
    {
        var data = WordPdfFixtures.Create(edit: doc => doc.MainDocumentPart!.AddHyperlinkRelationship(new Uri("file:///canary"), true));
        using var input = new MemoryStream(data);
        using var output = new MemoryStream();
        var unsupported = await Assert.ThrowsAsync<InvalidOperationException>(() => Converter(false).ConvertAsync(input, output));
        Assert.Contains("External DOCX relationships", unsupported.Message, StringComparison.Ordinal);
        using var corrupt = new MemoryStream([1, 2, 3]);
        await Assert.ThrowsAsync<InvalidDataException>(() => Converter(false).ConvertAsync(corrupt, output));
        Assert.Empty(output.ToArray());
        Assert.Empty(Directory.GetFileSystemEntries(_temporary));
    }

    private static HashSet<int> RuntimeProcesses()
    {
        var directory = OfficePath is null ? null : Path.GetDirectoryName(Path.GetFullPath(OfficePath));
        var ids = new HashSet<int>();
        foreach (var process in Process.GetProcesses())
        using (process)
        {
            try
            {
                if (process.ProcessName.StartsWith("soffice", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(Path.GetDirectoryName(process.MainModule?.FileName), directory, StringComparison.OrdinalIgnoreCase)) ids.Add(process.Id);
            }
            catch (Exception error) when (error is Win32Exception or InvalidOperationException) { }
        }
        return ids;
    }

    private static async Task<HashSet<int>> WaitForNativeAsync(Task task, HashSet<int> baseline)
    {
        var clock = Stopwatch.StartNew();
        while (!task.IsCompleted && clock.Elapsed < TimeSpan.FromSeconds(20))
        {
            var native = RuntimeProcesses();
            native.ExceptWith(baseline);
            if (native.Count > 0) return native;
            await Task.Delay(25);
        }
        if (task.IsCompleted) await task;
        throw new InvalidOperationException("LibreOffice did not start within the test observation budget.");
    }

    private sealed class ShortReads(byte[] bytes, int prefix = 0, CancellationTokenSource? cancellation = null) : Stream
    {
        private int _position = prefix;
        private bool _disposed;
        public int BytesRead { get; private set; }
        public override bool CanRead => !_disposed;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(7, Math.Min(buffer.Length, bytes.Length - _position));
            bytes.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            BytesRead += count;
            if (count > 0) cancellation?.Cancel();
            return ValueTask.FromResult(count);
        }
        protected override void Dispose(bool disposing) { _disposed = true; base.Dispose(disposing); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class PartialOutput(CancellationTokenSource? cancellation) : MemoryStream
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Write(buffer.Span[..5]);
            if (cancellation is null) throw new IOException("Partial output failure");
            cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }
    }
}

public sealed class LibreOfficeFactAttribute : FactAttribute
{
    public LibreOfficeFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Windows process-job supervision is required.";
        else if (WordPdfConverterTests.OfficePath is not { } path || !File.Exists(path)) Skip = "Set LIBREOFFICE_TEST_PATH to an installed LibreOffice soffice.com executable.";
    }
}
public sealed class LibreOfficeTheoryAttribute : TheoryAttribute
{
    public LibreOfficeTheoryAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Windows process-job supervision is required.";
        else if (WordPdfConverterTests.OfficePath is not { } path || !File.Exists(path)) Skip = "Set LIBREOFFICE_TEST_PATH to an installed LibreOffice soffice.com executable.";
    }
}
