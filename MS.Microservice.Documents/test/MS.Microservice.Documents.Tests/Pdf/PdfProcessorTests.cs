using MS.Microservice.Documents.Pdf;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using SkiaSharp;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;
using PdfRectangle = PdfSharp.Pdf.PdfRectangle;

namespace MS.Microservice.Documents.Tests.Pdf;

public sealed class PdfProcessorTests
{
    private readonly PdfProcessor _processor = new();

    [Fact]
    public async Task Merge_PreservesTextVectorPagesDimensionsRotationAndFirstMetadata()
    {
        using var first = new MemoryStream(CreatePdf(["First"], rotation: 90));
        using var second = new MemoryStream(CreatePdf(["Second"], width: 140, height: 90));
        using var output = new MemoryStream();
        await _processor.MergeAsync([first, second], output);
        var bytes = output.ToArray();
        using var metadataInput = new MemoryStream(bytes);
        var info = await _processor.InspectAsync(metadataInput);
        using var textInput = new MemoryStream(bytes);
        var text = await _processor.ExtractTextAsync(textInput);

        Assert.Equal("Fixture title", info.Title);
        Assert.Equal("Fixture author", info.Author);
        Assert.Equal(new[] { new PdfPageInfo(1, 100, 80, 90), new PdfPageInfo(2, 140, 90, 0) }, info.Pages);
        Assert.Contains("First", text[0].Text, StringComparison.Ordinal);
        Assert.Contains("Second", text[1].Text, StringComparison.Ordinal);
        using var rasterInput = new MemoryStream(bytes);
        using var raster = new MemoryStream();
        var rendered = await _processor.RenderPageAsync(rasterInput, 1, raster, dpi: 72);
        Assert.Equal((80, 100), (rendered.Width, rendered.Height));
        Assert.True(first.CanRead);
        Assert.True(second.CanRead);
        Assert.True(output.CanWrite);
    }

    [Fact]
    public async Task Merge_ThreeInputsAcceptExactTotalAndRejectOneByteOverBudget()
    {
        var bytes = new[] { CreatePdf(["First"]), CreatePdf(["Second"]), CreatePdf(["Third"]) };
        var total = bytes.Sum(value => (long)value.Length);
        foreach (var budget in new[] { total, total - 1 })
        {
            using var first = new ShortReadStream(bytes[0]);
            using var second = new ShortReadStream(bytes[1]);
            using var third = new ShortReadStream(bytes[2]);
            using var output = new MemoryStream();
            var options = new PdfOptions { Limits = new() { MaximumMergedInputBytes = budget } };
            if (budget == total)
            {
                await _processor.MergeAsync([first, second, third], output, options);
                using var result = new MemoryStream(output.ToArray());
                Assert.Equal(3, (await _processor.InspectAsync(result)).Pages.Count);
            }
            else
            {
                await Assert.ThrowsAsync<InvalidDataException>(() => _processor.MergeAsync([first, second, third], output, options));
                Assert.Empty(output.ToArray());
            }
            Assert.Equal(bytes[2].Length, third.BytesRead);
            Assert.True(first.CanRead);
            Assert.True(second.CanRead);
            Assert.True(third.CanRead);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public async Task Merge_ReadsOnlyRemainingAggregateBudgetPlusOneProbeByte(int remaining)
    {
        var bytes = CreatePdf(["First"]);
        using var first = new ShortReadStream(bytes);
        using var second = new ShortReadStream(bytes);
        using var third = new ShortReadStream([.. bytes, .. new byte[1024 * 1024]]);
        using var untouched = new ShortReadStream(bytes);
        using var output = new MemoryStream();
        var options = new PdfOptions { Limits = new() { MaximumMergedInputBytes = bytes.Length * 2L + remaining } };

        await Assert.ThrowsAsync<InvalidDataException>(() => _processor.MergeAsync([first, second, third, untouched], output, options));

        Assert.Equal(remaining + 1, third.BytesRead);
        Assert.InRange(third.MaximumRequestedRead, 1, remaining + 1);
        Assert.Equal(0, untouched.ReadCalls);
        Assert.Empty(output.ToArray());
        Assert.True(first.CanRead);
        Assert.True(second.CanRead);
        Assert.True(third.CanRead);
        Assert.True(untouched.CanRead);
    }

    [Fact]
    public async Task Merge_CancellationDuringLaterInputStopsConsumptionAndLeavesAllCallersOpen()
    {
        var bytes = CreatePdf(["First"]);
        using var cancellation = new CancellationTokenSource();
        using var first = new ShortReadStream(bytes);
        using var second = new ShortReadStream(bytes) { AfterRead = cancellation.Cancel };
        using var third = new ShortReadStream(bytes);
        using var output = new MemoryStream();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _processor.MergeAsync([first, second, third], output,
            new() { Limits = new() { MaximumMergedInputBytes = bytes.Length * 3L } }, cancellation.Token));

        Assert.Equal(bytes.Length, first.BytesRead);
        Assert.InRange(second.BytesRead, 1, 7);
        Assert.Equal(0, third.ReadCalls);
        Assert.Empty(output.ToArray());
        Assert.True(first.CanRead);
        Assert.True(second.CanRead);
        Assert.True(third.CanRead);
    }

    [Fact]
    public async Task Select_PreservesRequestedOrderAndRepeatedPages()
    {
        using var input = new MemoryStream(CreatePdf(["First", "Second", "Third"]));
        using var output = new MemoryStream();
        await _processor.SelectPagesAsync(input, output, [3, 1, 3]);
        using var textInput = new MemoryStream(output.ToArray());
        var pages = await _processor.ExtractTextAsync(textInput);

        Assert.Equal(3, pages.Count);
        Assert.Contains("Third", pages[0].Text, StringComparison.Ordinal);
        Assert.Contains("First", pages[1].Text, StringComparison.Ordinal);
        Assert.Contains("Third", pages[2].Text, StringComparison.Ordinal);
        Assert.True(input.CanRead);
    }

    [Fact]
    public async Task Split_ProducesIndependentOnePageDocumentsSequentially()
    {
        using var input = new MemoryStream(CreatePdf(["First", "Second", "Third"]));
        var parts = new List<PdfPart>();
        await foreach (var part in _processor.SplitAsync(input)) parts.Add(part);

        Assert.Equal(new[] { 1, 2, 3 }, parts.Select(part => part.PageNumber));
        foreach (var part in parts)
        {
            using var source = new MemoryStream(part.Content);
            var text = await _processor.ExtractTextAsync(source);
            Assert.Single(text);
            Assert.Contains(new[] { "First", "Second", "Third" }[part.PageNumber - 1], text[0].Text, StringComparison.Ordinal);
        }
        Assert.True(input.CanRead);
    }

    [Fact]
    public async Task ExtractText_ScannedImageOnlyPageReturnsEmptyText()
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(100);
        page.Height = XUnit.FromPoint(80);
        using var png = new MemoryStream(Png(4, 4, (_, _) => SKColors.Blue));
        using (var image = XImage.FromStream(png))
        using (var graphics = XGraphics.FromPdfPage(page)) graphics.DrawImage(image, 0, 0, 100, 80);
        using var bytes = new MemoryStream();
        document.Save(bytes, closeStream: false);
        bytes.Position = 0;

        var text = await _processor.ExtractTextAsync(bytes);

        Assert.Single(text);
        Assert.Empty(text[0].Text);
        Assert.True(bytes.CanRead);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public async Task Overlay_UsesDisplayedCoordinatesAfterRotationAndPreservesUnderlyingText(int rotation)
    {
        var original = CreatePdf(["Original"], rotation: rotation);
        var width = rotation is 90 or 270 ? 80 : 100;
        var height = rotation is 90 or 270 ? 100 : 80;
        using var input = new MemoryStream(original);
        using var overlay = new MemoryStream(Png(width, height, (x, y) => x < 10 && y < 10 ? SKColors.Red : SKColors.Transparent));
        using var output = new MemoryStream();
        await _processor.AddOverlayAsync(input, overlay, output);
        using var renderInput = new MemoryStream(output.ToArray());
        using var rendered = new MemoryStream();
        var result = await _processor.RenderPageAsync(renderInput, 1, rendered, dpi: 72);
        using var bitmap = SKBitmap.Decode(result.Png);

        Assert.Equal((width, height), (result.Width, result.Height));
        Assert.Equal(SKColors.Red, bitmap.GetPixel(3, 3));
        Assert.Equal(SKColors.Blue, bitmap.GetPixel(width - 3, height - 3));
        using var textInput = new MemoryStream(output.ToArray());
        Assert.Contains("Original", (await _processor.ExtractTextAsync(textInput))[0].Text, StringComparison.Ordinal);
        Assert.True(input.CanRead);
        Assert.True(overlay.CanRead);
        Assert.True(output.CanWrite);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    public async Task Overlay_VisibleCropBoxOffsetIsRespected(int rotation)
    {
        var bytes = CreatePdf(["Crop"], width: 100, height: 100, rotation: rotation, crop: true);
        using var input = new MemoryStream(bytes);
        using var overlay = new MemoryStream(Png(60, 60, (x, y) => x < 12 && y < 12 ? SKColors.Red : SKColors.Transparent));
        using var output = new MemoryStream();
        await _processor.AddOverlayAsync(input, overlay, output);
        using var renderInput = new MemoryStream(output.ToArray());
        using var rendered = new MemoryStream();
        var result = await _processor.RenderPageAsync(renderInput, 1, rendered, dpi: 72);
        using var bitmap = SKBitmap.Decode(result.Png);

        Assert.Equal((60, 60), (result.Width, result.Height));
        Assert.Equal(SKColors.Red, bitmap.GetPixel(3, 3));
        Assert.Equal(SKColors.Blue, bitmap.GetPixel(55, 55));
    }

    [Fact]
    public async Task Encryption_InspectionRetainsSourceStateForUserAndOwnerPasswords()
    {
        var bytes = CreatePdf(["Protected"], encrypted: true);
        using var noPassword = new MemoryStream(bytes);
        await Assert.ThrowsAnyAsync<Exception>(() => _processor.InspectAsync(noPassword));
        foreach (var password in new[] { "read-password", "owner-password" })
        {
            using var input = new MemoryStream(bytes);
            var settings = new PdfOptions { Password = password };
            Assert.True((await _processor.InspectAsync(input, settings)).IsEncrypted);
            Assert.DoesNotContain(password, settings.ToString()!, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("overlay")]
    [InlineData("select")]
    [InlineData("split")]
    [InlineData("merge")]
    public async Task Encryption_AllRewritePathsRefuseImplicitCleartext(string operation)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Rewrite(operation, CreatePdf(["Protected"], encrypted: true),
            new() { Password = "owner-password" }));
    }

    [Theory]
    [InlineData("overlay")]
    [InlineData("select")]
    [InlineData("split")]
    [InlineData("merge")]
    public async Task Encryption_AllRewritePathsSupportExplicitAes256OutputPasswords(string operation)
    {
        var bytes = await Rewrite(operation, CreatePdf(["Protected"], encrypted: true), new()
        {
            Password = "owner-password", OutputUserPassword = "新-reader", OutputOwnerPassword = "new-owner"
        });
        using var noPassword = new MemoryStream(bytes);
        await Assert.ThrowsAnyAsync<Exception>(() => _processor.InspectAsync(noPassword));
        foreach (var password in new[] { "新-reader", "new-owner" })
        {
            using var input = new MemoryStream(bytes);
            Assert.True((await _processor.InspectAsync(input, new() { Password = password })).IsEncrypted);
            input.Position = 0;
            Assert.Contains(await _processor.ExtractTextAsync(input, new() { Password = password }),
                page => page.Text.Contains("Protected", StringComparison.Ordinal));
        }
        using var rasterInput = new MemoryStream(bytes);
        using var raster = new MemoryStream();
        Assert.NotEmpty((await _processor.RenderPageAsync(rasterInput, 1, raster, 72, new() { Password = "新-reader" })).Png);
    }

    [Theory]
    [InlineData("overlay")]
    [InlineData("select")]
    [InlineData("split")]
    [InlineData("merge")]
    public async Task Encryption_AllRewritePathsAllowExplicitCleartextOptIn(string operation)
    {
        var bytes = await Rewrite(operation, CreatePdf(["Protected"], encrypted: true),
            new() { Password = "owner-password", AllowUnencryptedOutput = true });
        using var input = new MemoryStream(bytes);
        Assert.False((await _processor.InspectAsync(input)).IsEncrypted);
        input.Position = 0;
        Assert.Contains(await _processor.ExtractTextAsync(input), page => page.Text.Contains("Protected", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Encryption_OutputPasswordByteBoundariesUnicodeAndNormalizationAreValidated()
    {
        var bytes = CreatePdf(["Original"]);
        foreach (var password in new[] { new string('a', 127), new string('密', 42) })
        {
            var encrypted = await Rewrite("select", bytes, new() { OutputUserPassword = password, OutputOwnerPassword = "owner" });
            using var input = new MemoryStream(encrypted);
            Assert.True((await _processor.InspectAsync(input, new() { Password = password })).IsEncrypted);
        }
        PdfOptions[] invalid =
        [
            new() { OutputUserPassword = "user" }, new() { OutputOwnerPassword = "owner" },
            new() { OutputUserPassword = "", OutputOwnerPassword = "owner" },
            new() { OutputUserPassword = "user", OutputOwnerPassword = "" },
            new() { OutputUserPassword = new string('a', 128), OutputOwnerPassword = "owner" },
            new() { OutputUserPassword = new string('密', 43), OutputOwnerPassword = "owner" },
            new() { OutputUserPassword = new string('\uD800', 1), OutputOwnerPassword = "owner" },
            new() { OutputUserPassword = new string('\u2167', 32), OutputOwnerPassword = "owner" }
        ];
        foreach (var settings in invalid)
        {
            using var input = new MemoryStream(bytes);
            using var output = new MemoryStream();
            await Assert.ThrowsAnyAsync<ArgumentException>(() => _processor.SelectPagesAsync(input, output, [1], settings));
            Assert.Equal(0, input.Position);
            Assert.Empty(output.ToArray());
        }
    }

    [Theory]
    [InlineData("\u00AD", false)]
    [InlineData("\u034F", false)]
    [InlineData("\u00AD", true)]
    [InlineData("\u034F", true)]
    public async Task Encryption_OutputPasswordCannotNormalizeToPasswordlessAccess(string password, bool owner)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Rewrite("select", CreatePdf(["Private"]), new()
        {
            OutputUserPassword = owner ? "reader" : password, OutputOwnerPassword = owner ? password : "owner"
        }));
    }

    [Fact]
    public async Task Encryption_NonemptyNormalizedUnicodePasswordsRemainUsable()
    {
        var output = await Rewrite("select", CreatePdf(["Private"]), new()
        {
            OutputUserPassword = "rea\uFB00der", OutputOwnerPassword = "own\u2167er"
        });
        foreach (var password in new[] { "rea\uFB00der", "reaffder", "own\u2167er", "ownVIIIer" })
        {
            using var input = new MemoryStream(output);
            Assert.True((await _processor.InspectAsync(input, new() { Password = password })).IsEncrypted);
            input.Position = 0;
            Assert.Contains("Private", (await _processor.ExtractTextAsync(input, new() { Password = password }))[0].Text, StringComparison.Ordinal);
            input.Position = 0;
            using var raster = new MemoryStream();
            Assert.NotEmpty((await _processor.RenderPageAsync(input, 1, raster, 72, new() { Password = password })).Png);
        }
    }

    [Fact]
    public async Task Encryption_BackendIncompatibleSaslMappingFailsBeforePublishing()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Rewrite("select", CreatePdf(["Private"]), new()
        {
            OutputUserPassword = "rea\u00ADder", OutputOwnerPassword = "own\u034Fer"
        }));
    }

    [Fact]
    public async Task Encryption_LegacyPasswordUsesOriginalValueBeforeNormalizationCandidate()
    {
        const string original = "raw\u00B5";
        using var input = new MemoryStream(CreatePdf(["Legacy"], encrypted: true, legacyUserPassword: original));
        Assert.True((await _processor.InspectAsync(input, new() { Password = original })).IsEncrypted);
        input.Position = 0;
        Assert.Contains("Legacy", (await _processor.ExtractTextAsync(input, new() { Password = original }))[0].Text, StringComparison.Ordinal);
        input.Position = 0;
        using var raster = new MemoryStream();
        Assert.NotEmpty((await _processor.RenderPageAsync(input, 1, raster, 72, new() { Password = original })).Png);
        input.Position = 0;
        await Assert.ThrowsAnyAsync<Exception>(() => _processor.InspectAsync(input, new() { Password = "raw\u03BC" }));
    }

    [Fact]
    public async Task RenderPages_UsesOneBasedExplicitOrderRepeatedPagesAndDpi()
    {
        using var input = new MemoryStream(CreatePdf(["First", "Second"], width: 72, height: 72));
        var rendered = new List<PdfRenderedPage>();
        await foreach (var page in _processor.RenderPagesAsync(input, [2, 1, 2], dpi: 144)) rendered.Add(page);

        Assert.Equal(new[] { 2, 1, 2 }, rendered.Select(page => page.PageNumber));
        Assert.All(rendered, page => Assert.Equal((144, 144), (page.Width, page.Height)));
        Assert.True(input.CanRead);
    }

    [Fact]
    public async Task Render_FractionalPointDimensionsMatchActualRasterBudgetAndSubpixelTargetsFail()
    {
        using var input = new MemoryStream(CreatePdf(["Fraction"], width: 100.9, height: 80.9));
        using var output = new MemoryStream();
        var result = await _processor.RenderPageAsync(input, 1, output, 72,
            new() { Limits = new() { MaximumRasterDimension = 100, MaximumRasterPixels = 8000 } });
        Assert.Equal((100, 80), (result.Width, result.Height));
        using var tiny = new MemoryStream(CreatePdf(["Tiny"], width: 1, height: 1));
        using var rejected = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(() => _processor.RenderPageAsync(tiny, 1, rejected, dpi: 1));
        Assert.Empty(rejected.ToArray());
    }

    [Fact]
    public async Task Overlay_NonzeroMediaBoxOriginUsesVisiblePageCoordinates()
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        page.MediaBox = new PdfRectangle(new XRect(10, 20, 100, 80));
        using (var graphics = XGraphics.FromPdfPage(page)) graphics.DrawRectangle(XBrushes.Blue, 10, -20, 100, 80);
        using var input = new MemoryStream();
        document.Save(input, closeStream: false);
        input.Position = 0;
        using var overlay = new MemoryStream(Png(100, 80, (x, y) => x < 10 && y < 10 ? SKColors.Red : SKColors.Transparent));
        using var output = new MemoryStream();
        await _processor.AddOverlayAsync(input, overlay, output);
        using var renderedInput = new MemoryStream(output.ToArray());
        using var raster = new MemoryStream();
        var rendered = await _processor.RenderPageAsync(renderedInput, 1, raster, 72);
        using var bitmap = SKBitmap.Decode(rendered.Png);
        Assert.Equal(SKColors.Red, bitmap.GetPixel(3, 3));
        Assert.Equal(SKColors.Blue, bitmap.GetPixel(95, 75));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3)]
    public async Task InvalidPageIndicesAreRejectedWithoutWritingOutput(int page)
    {
        using var input = new MemoryStream(CreatePdf(["First", "Second"]));
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _processor.SelectPagesAsync(input, output, [page]));
        input.Position = 0;
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _processor.RenderPageAsync(input, page, output));
        Assert.Empty(output.ToArray());
        Assert.True(input.CanRead);
    }

    [Fact]
    public async Task Limits_ExactInputBudgetSucceedsAndInputOutputPagesRasterBudgetsFailBeforeCallerOutput()
    {
        var bytes = CreatePdf(["First", "Second"]);
        using var exact = new MemoryStream(bytes);
        Assert.Equal(2, (await _processor.InspectAsync(exact, new() { Limits = new() { MaximumInputBytes = bytes.Length } })).Pages.Count);
        PdfOptions[] invalid =
        [
            new() { Limits = new() { MaximumInputBytes = bytes.Length - 1 } },
            new() { Limits = new() { MaximumPages = 1 } },
            new() { Limits = new() { MaximumOutputBytes = 100 } }
        ];
        foreach (var options in invalid)
        {
            using var input = new MemoryStream(bytes);
            using var output = new MemoryStream();
            await Assert.ThrowsAnyAsync<Exception>(() => _processor.SelectPagesAsync(input, output, [1, 2], options));
            Assert.Empty(output.ToArray());
        }
        foreach (var limits in new[]
        {
            new PdfLimits { MaximumRasterDimension = 99 }, new PdfLimits { MaximumRasterPixels = 7999 },
            new PdfLimits { MaximumOutputBytes = 1 }
        })
        {
            using var input = new MemoryStream(bytes);
            using var output = new MemoryStream();
            await Assert.ThrowsAsync<InvalidDataException>(() => _processor.RenderPageAsync(input, 1, output, 72, new() { Limits = limits }));
            Assert.Empty(output.ToArray());
        }
        using var repeated = new MemoryStream(bytes);
        using var repeatedOutput = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(() => _processor.SelectPagesAsync(repeated, repeatedOutput, [1, 1, 2],
            new() { Limits = new() { MaximumPages = 2 } }));
    }

    [Fact]
    public async Task MergeTotalAndEmptySelectionRespectPageLimits()
    {
        using var first = new MemoryStream(CreatePdf(["First", "Second"]));
        using var second = new MemoryStream(CreatePdf(["Third"]));
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(() => _processor.MergeAsync([first, second], output,
            new() { Limits = new() { MaximumPages = 2 } }));
        first.Position = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => _processor.SelectPagesAsync(first, output, []));
        await Assert.ThrowsAsync<InvalidDataException>(() => _processor.MergeAsync([], output));
        Assert.Empty(output.ToArray());
    }

    [Fact]
    public async Task Streams_CurrentPositionsAndNonseekableShortReadsRemainSupported()
    {
        var bytes = CreatePdf(["Original"]);
        using var input = new MemoryStream([99, 98, .. bytes]) { Position = 2 };
        using var output = new MemoryStream();
        output.WriteByte(97);
        await _processor.SelectPagesAsync(input, output, [1]);
        using var selected = new ShortReadStream(output.ToArray()[1..]);
        var text = await _processor.ExtractTextAsync(selected);

        Assert.Equal(97, output.ToArray()[0]);
        Assert.Contains("Original", text[0].Text, StringComparison.Ordinal);
        Assert.True(input.CanRead);
        Assert.True(selected.CanRead);
        Assert.True(output.CanWrite);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_PreOrDuringReadLeavesStreamsOpenAndOutputEmpty(bool duringRead)
    {
        using var cancellation = new CancellationTokenSource();
        using var input = new ShortReadStream(CreatePdf(["First"])) { AfterRead = duringRead ? cancellation.Cancel : null };
        using var output = new MemoryStream();
        if (!duringRead) cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _processor.SelectPagesAsync(input, output, [1], cancellationToken: cancellation.Token));
        Assert.True(input.CanRead);
        Assert.True(output.CanWrite);
        Assert.Empty(output.ToArray());
    }

    [Fact]
    public async Task Split_CancellationAfterFirstPartStopsEnumeration()
    {
        using var input = new MemoryStream(CreatePdf(["First", "Second"]));
        using var cancellation = new CancellationTokenSource();
        await using var parts = _processor.SplitAsync(input, cancellationToken: cancellation.Token).GetAsyncEnumerator();
        Assert.True(await parts.MoveNextAsync());
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => parts.MoveNextAsync().AsTask());
        Assert.True(input.CanRead);
    }

    [Fact]
    public async Task CorruptPdfAndInvalidDpiOrSameStreamDoNotMutateCallerOutput()
    {
        using var corrupt = new MemoryStream([1, 2, 3, 4]);
        using var output = new MemoryStream();
        await Assert.ThrowsAnyAsync<Exception>(() => _processor.SelectPagesAsync(corrupt, output, [1]));
        using var input = new MemoryStream(CreatePdf(["First"]));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _processor.RenderPageAsync(input, 1, output, dpi: 1201));
        await Assert.ThrowsAsync<ArgumentException>(() => _processor.SelectPagesAsync(input, input, [1]));
        Assert.Empty(output.ToArray());
    }

    private async Task<byte[]> Rewrite(string operation, byte[] bytes, PdfOptions options)
    {
        using var input = new MemoryStream(bytes);
        using var overlay = new MemoryStream(Png(2, 2, (_, _) => SKColors.Transparent));
        using var plain = new MemoryStream(CreatePdf(["Unencrypted"]));
        using var output = new MemoryStream();
        try
        {
            switch (operation)
            {
                case "overlay": await _processor.AddOverlayAsync(input, overlay, output, options); break;
                case "select": await _processor.SelectPagesAsync(input, output, [1], options); break;
                case "merge": await _processor.MergeAsync([plain, input], output, options); break;
                case "split":
                    await foreach (var part in _processor.SplitAsync(input, options)) await output.WriteAsync(part.Content);
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(operation));
            }
            return output.ToArray();
        }
        catch { Assert.Empty(output.ToArray()); throw; }
        finally
        {
            Assert.True(input.CanRead);
            Assert.True(overlay.CanRead);
            Assert.True(plain.CanRead);
            Assert.True(output.CanWrite);
        }
    }

    private static byte[] CreatePdf(string[] labels, double width = 100, double height = 80, int rotation = 0, bool crop = false,
        bool encrypted = false, string? legacyUserPassword = null)
    {
        using var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        foreach (var label in labels) builder.AddPage(width, height).AddText(label, 12, new PdfPoint(5, height - 18), font);
        using var bytes = new MemoryStream(builder.Build());
        using var document = PdfReader.Open(bytes, PdfDocumentOpenMode.Modify);
        document.Info.Title = "Fixture title";
        document.Info.Author = "Fixture author";
        foreach (var page in document.Pages)
        {
            using var graphics = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Prepend);
            graphics.DrawRectangle(XBrushes.Blue, 0, 0, width, height);
            page.Rotate = rotation;
            if (crop) page.CropBox = new PdfRectangle(new XRect(20, 20, 60, 60));
        }
        if (encrypted)
        {
            document.SecuritySettings.UserPassword = legacyUserPassword ?? "read-password";
            document.SecuritySettings.OwnerPassword = "owner-password";
        }
        using var output = new MemoryStream();
        document.Save(output, closeStream: false);
        return output.ToArray();
    }

    private static byte[] Png(int width, int height, Func<int, int, SKColor> color)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++) bitmap.SetPixel(x, y, color(x, y));
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }

    private sealed class ShortReadStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream _inner = new(bytes);
        public Action? AfterRead { get; init; }
        public int ReadCalls { get; private set; }
        public int BytesRead { get; private set; }
        public int MaximumRequestedRead { get; private set; }
        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, Math.Min(count, 7));
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCalls++;
            MaximumRequestedRead = Math.Max(MaximumRequestedRead, buffer.Length);
            var count = await _inner.ReadAsync(buffer[..Math.Min(buffer.Length, 7)], cancellationToken);
            BytesRead += count;
            AfterRead?.Invoke();
            return count;
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _inner.Dispose(); base.Dispose(disposing); }
    }
}
