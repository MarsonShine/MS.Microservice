using Microsoft.AspNetCore.Http;
using MS.Microservice.AspNetCore.Uploads;
using Xunit;

namespace MS.Microservice.AspNetCore.Tests.Uploads;

public sealed class FileUploadValidatorTests
{
    private readonly FileUploadValidator _validator = new();

    public static TheoryData<UploadFileKind, string, string, byte[]> SupportedFiles => new()
    {
        { UploadFileKind.Image, "photo.JpG", "IMAGE/JPEG", [0xFF, 0xD8, 0xFF] },
        { UploadFileKind.Image, "photo.jpeg", "image/jpeg", [0xFF, 0xD8, 0xFF] },
        { UploadFileKind.Image, "photo.png", "image/png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A] },
        { UploadFileKind.Image, "photo.webp", "image/webp", "RIFF\x01\x02\x03\x04WEBP"u8.ToArray() },
        { UploadFileKind.Excel, "sheet.XLS", "APPLICATION/VND.MS-EXCEL", [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1] },
        { UploadFileKind.Excel, "sheet.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", [0x50, 0x4B, 0x03, 0x04] }
    };

    [Theory]
    [MemberData(nameof(SupportedFiles))]
    public async Task MatchingMetadataAndSignatureAtSizeLimit_AcceptedThroughPartialNonseekableReads(
        UploadFileKind kind, string fileName, string contentType, byte[] content)
    {
        var file = new TestFormFile(content, fileName, contentType, chunkSize: 1);

        var result = await _validator.ValidateAsync(file, kind, content.Length);

        Assert.True(result.IsValid);
        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        Assert.Empty(result.Message);
        Assert.Equal(Path.GetExtension(fileName).ToLowerInvariant(), result.Extension);
        Assert.Equal(content.Length, file.OpenedStream!.BytesRead);
        Assert.True(file.OpenedStream.IsDisposed);
    }

    [Theory]
    [InlineData(UploadFileKind.Image, "请选择非空 图片 文件。")]
    [InlineData(UploadFileKind.Excel, "请选择非空 Excel 文件。")]
    public async Task MissingOrEmptyFile_ReturnsBadRequestWithoutOpeningStream(UploadFileKind kind, string message)
    {
        var emptyFile = new TestFormFile([], "empty.png", "image/png");

        var missing = await _validator.ValidateAsync(null, kind, 1);
        var empty = await _validator.ValidateAsync(emptyFile, kind, 1);

        AssertFailure(missing, StatusCodes.Status400BadRequest, message);
        AssertFailure(empty, StatusCodes.Status400BadRequest, message);
        Assert.Null(emptyFile.OpenedStream);
    }

    [Theory]
    [MemberData(nameof(SupportedFiles))]
    public async Task AboveSizeLimit_RejectsBeforeOpeningStream(
        UploadFileKind kind, string fileName, string contentType, byte[] content)
    {
        var file = new TestFormFile(content, fileName, contentType);

        var result = await _validator.ValidateAsync(file, kind, content.Length - 1);

        var displayName = kind == UploadFileKind.Image ? "图片" : "Excel";
        AssertFailure(result, StatusCodes.Status413PayloadTooLarge, $"{displayName} 文件超过允许大小。");
        Assert.Null(file.OpenedStream);
    }

    [Theory]
    [InlineData(UploadFileKind.Image, "photo.gif", "image/gif")]
    [InlineData(UploadFileKind.Image, "photo", "image/png")]
    [InlineData(UploadFileKind.Image, "photo.png", "image/jpeg")]
    [InlineData(UploadFileKind.Image, "photo.png", "application/octet-stream")]
    [InlineData(UploadFileKind.Image, "photo.png", "")]
    [InlineData(UploadFileKind.Image, "photo.png", "image/png; charset=utf-8")]
    [InlineData(UploadFileKind.Image, "sheet.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData(UploadFileKind.Excel, "sheet.xlsx", "application/vnd.ms-excel")]
    [InlineData(UploadFileKind.Excel, "sheet.csv", "text/csv")]
    [InlineData(UploadFileKind.Excel, "photo.png", "image/png")]
    public async Task UnsupportedOrMismatchedMetadata_RejectsWithoutOpeningStream(
        UploadFileKind kind, string fileName, string contentType)
    {
        var file = new TestFormFile([0x01], fileName, contentType);

        var result = await _validator.ValidateAsync(file, kind, 10);

        var displayName = kind == UploadFileKind.Image ? "图片" : "Excel";
        AssertFailure(result, StatusCodes.Status415UnsupportedMediaType, $"不支持的 {displayName} 类型。");
        Assert.Null(file.OpenedStream);
    }

    [Theory]
    [MemberData(nameof(SupportedFiles))]
    public async Task TruncatedSignature_RejectsAndDisposesReadStream(
        UploadFileKind kind, string fileName, string contentType, byte[] content)
    {
        var file = new TestFormFile(content[..^1], fileName, contentType, chunkSize: 1);

        var result = await _validator.ValidateAsync(file, kind, 100);

        var displayName = kind == UploadFileKind.Image ? "图片" : "Excel";
        AssertFailure(result, StatusCodes.Status415UnsupportedMediaType, $"{displayName} 文件头与声明类型不一致。");
        Assert.True(file.OpenedStream!.IsDisposed);
    }

    [Theory]
    [InlineData("photo.png", "image/png", "RIFF0000WEBP")]
    [InlineData("photo.webp", "image/webp", "RIFF0000WAVE")]
    [InlineData("photo.webp", "image/webp", "RIFX0000WEBP")]
    public async Task WrongSignature_RejectsDespiteMatchingMetadata(string fileName, string contentType, string content)
    {
        var file = new TestFormFile(System.Text.Encoding.ASCII.GetBytes(content), fileName, contentType);

        var result = await _validator.ValidateAsync(file, UploadFileKind.Image, 100);

        AssertFailure(result, StatusCodes.Status415UnsupportedMediaType, "图片 文件头与声明类型不一致。");
        Assert.True(file.OpenedStream!.IsDisposed);
    }

    [Theory]
    [MemberData(nameof(SupportedFiles))]
    public async Task ValidLargeContent_OnlyReadsBoundedHeader(
        UploadFileKind kind, string fileName, string contentType, byte[] signature)
    {
        var content = new byte[1024];
        signature.CopyTo(content, 0);
        var file = new TestFormFile(content, fileName, contentType, chunkSize: 2);

        var result = await _validator.ValidateAsync(file, kind, content.Length);

        Assert.True(result.IsValid);
        Assert.Equal(kind == UploadFileKind.Image ? 12 : 8, file.OpenedStream!.BytesRead);
        Assert.True(file.OpenedStream.IsDisposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationBeforeOrDuringRead_PropagatesAndDisposesReadStream(bool duringRead)
    {
        using var cancellation = new CancellationTokenSource();
        var file = new TestFormFile("RIFF0000WEBP"u8.ToArray(), "photo.webp", "image/webp", chunkSize: 1)
        {
            AfterRead = duringRead ? cancellation.Cancel : null
        };
        if (!duringRead)
        {
            cancellation.Cancel();
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _validator.ValidateAsync(
            file, UploadFileKind.Image, 100, cancellation.Token));

        Assert.True(file.OpenedStream!.IsDisposed);
        Assert.Equal(duringRead ? 1 : 0, file.OpenedStream.BytesRead);
    }

    [Fact]
    public async Task ReadFailure_PropagatesAndDisposesReadStream()
    {
        var file = new TestFormFile("RIFF0000WEBP"u8.ToArray(), "photo.webp", "image/webp")
        {
            FailRead = true
        };

        await Assert.ThrowsAsync<IOException>(() => _validator.ValidateAsync(file, UploadFileKind.Image, 100));

        Assert.True(file.OpenedStream!.IsDisposed);
    }

    [Fact]
    public async Task Validation_LeavesFormFileSourceReusableForSubsequentProcessing()
    {
        byte[] content = [0xFF, 0xD8, 0xFF, 0x01, 0x02];
        await using var source = new MemoryStream(content, writable: false);
        var file = new FormFile(source, 0, content.Length, "file", "photo.jpg")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/jpeg"
        };

        var result = await _validator.ValidateAsync(file, UploadFileKind.Image, 100);

        Assert.True(result.IsValid);
        Assert.True(source.CanRead);
        await using var reopened = file.OpenReadStream();
        using var copied = new MemoryStream();
        await reopened.CopyToAsync(copied);
        Assert.Equal(content, copied.ToArray());
    }

    private static void AssertFailure(FileUploadValidationResult result, int statusCode, string message)
    {
        Assert.False(result.IsValid);
        Assert.Equal(statusCode, result.StatusCode);
        Assert.Equal(message, result.Message);
        Assert.Empty(result.Extension);
    }

    private sealed class TestFormFile(byte[] content, string fileName, string contentType, int chunkSize = int.MaxValue) : IFormFile
    {
        public string ContentType => contentType;
        public string ContentDisposition => string.Empty;
        public IHeaderDictionary Headers { get; } = new HeaderDictionary();
        public long Length => content.Length;
        public string Name => "file";
        public string FileName => fileName;
        public Action? AfterRead { get; init; }
        public bool FailRead { get; init; }
        public TrackingReadStream? OpenedStream { get; private set; }

        public Stream OpenReadStream() => OpenedStream = new TrackingReadStream(content, chunkSize, AfterRead, FailRead);
        public void CopyTo(Stream target) => throw new NotSupportedException();
        public Task CopyToAsync(Stream target, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class TrackingReadStream(byte[] content, int chunkSize, Action? afterRead, bool failRead)
        : MemoryStream(content, writable: false)
    {
        public override bool CanSeek => false;
        public int BytesRead { get; private set; }
        public bool IsDisposed { get; private set; }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (failRead)
            {
                throw new IOException("Cannot read uploaded content.");
            }

            var read = base.Read(buffer.Span[..Math.Min(buffer.Length, chunkSize)]);
            BytesRead += read;
            afterRead?.Invoke();
            return ValueTask.FromResult(read);
        }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }
}
