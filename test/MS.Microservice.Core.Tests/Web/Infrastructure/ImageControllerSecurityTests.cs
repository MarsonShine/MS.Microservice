using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MS.Microservice.Web.Controller;
using MS.Microservice.Web.Infrastructure.Uploads;
using NSubstitute;
using System.Reflection;
using System.Text;

namespace MS.Microservice.Core.Tests.Web.Infrastructure;

public sealed class ImageControllerSecurityTests : IDisposable
{
    private readonly string _contentRoot = Path.Combine(
        Path.GetTempPath(),
        "MS.Microservice.UploadTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Controller_RequiresAuthorizationAndCapsMultipartBody()
    {
        var controllerType = typeof(ImageController);

        Assert.NotNull(controllerType.GetCustomAttribute<AuthorizeAttribute>());
        Assert.Equal(
            SampleUploadOptions.MaximumRequestBodyBytes,
            controllerType.GetCustomAttribute<RequestFormLimitsAttribute>()!.MultipartBodyLengthLimit);
        Assert.NotNull(controllerType.GetCustomAttribute<RequestSizeLimitAttribute>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("../uploads")]
    [InlineData("C:\\uploads")]
    [InlineData("uploads/../private")]
    public void StorageDirectory_WhenUnsafe_IsRejected(string path)
    {
        Assert.False(SampleUploadOptions.IsSafeStorageDirectory(path));
    }

    [Fact]
    public async Task Upload_WhenImageExceedsLimit_ReturnsPayloadTooLarge()
    {
        var controller = CreateController(maxImageBytes: 4);
        var file = CreateFormFile(
            [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A],
            "image.png",
            "image/png");

        var result = await controller.Upload(file);

        var response = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, response.StatusCode);
        Assert.False(Directory.Exists(Path.Combine(_contentRoot, "uploads")));
    }

    [Fact]
    public async Task Upload_WhenSignatureDoesNotMatch_ReturnsUnsupportedMediaType()
    {
        var controller = CreateController();
        var file = CreateFormFile(
            Encoding.UTF8.GetBytes("not-a-png"),
            "image.png",
            "image/png");

        var result = await controller.Upload(file);

        var response = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status415UnsupportedMediaType, response.StatusCode);
        Assert.False(Directory.Exists(Path.Combine(_contentRoot, "uploads")));
    }

    [Fact]
    public async Task Upload_WhenImageIsValid_UsesRandomNameInsideStorageDirectory()
    {
        var controller = CreateController();
        byte[] content = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02];
        var file = CreateFormFile(content, "../../user-controlled.png", "image/png");

        var result = await controller.Upload(file);

        var response = Assert.IsType<OkObjectResult>(result);
        var storedFileName = response.Value!
            .GetType()
            .GetProperty("fileName")!
            .GetValue(response.Value)!
            .ToString()!;
        var storageDirectory = Path.Combine(_contentRoot, "uploads");
        var storedFiles = Directory.GetFiles(storageDirectory);
        Assert.Single(storedFiles);
        Assert.Equal(storedFileName, Path.GetFileName(storedFiles[0]));
        Assert.DoesNotContain("user-controlled", storedFileName, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(".png", storedFileName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(content, await File.ReadAllBytesAsync(storedFiles[0]));
    }

    [Fact]
    public async Task LocalUploadStorage_WhenCopyFails_RemovesPartialFile()
    {
        Directory.CreateDirectory(_contentRoot);
        var environment = Substitute.For<IWebHostEnvironment>();
        environment.ContentRootPath.Returns(_contentRoot);
        var options = Options.Create(new SampleUploadOptions { StorageDirectory = "uploads" });
        var storage = new LocalUploadStorage(environment, options);
        await using var content = new ThrowingCopyStream();

        await Assert.ThrowsAsync<IOException>(
            () => storage.SaveAsync(content, ".png", CancellationToken.None));

        var storageDirectory = Path.Combine(_contentRoot, "uploads");
        Assert.True(Directory.Exists(storageDirectory));
        Assert.Empty(Directory.GetFiles(storageDirectory));
    }

    [Fact]
    public async Task ExcelReader_WhenFileExceedsLimit_ReturnsPayloadTooLarge()
    {
        var controller = CreateController(maxExcelBytes: 4);
        var file = CreateFormFile(
            [0x50, 0x4B, 0x03, 0x04, 0x01],
            "books.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

        var result = await controller.ExcelReader(file);

        var response = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task ExcelReader_WhenSignatureDoesNotMatch_ReturnsUnsupportedMediaType()
    {
        var controller = CreateController();
        var file = CreateFormFile(
            Encoding.UTF8.GetBytes("not-an-xlsx"),
            "books.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

        var result = await controller.ExcelReader(file);

        var response = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status415UnsupportedMediaType, response.StatusCode);
    }

    public void Dispose()
    {
        var fullPath = Path.GetFullPath(_contentRoot);
        var safeParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "MS.Microservice.UploadTests"))
            .TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (fullPath.StartsWith(safeParent, StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(fullPath))
        {
            Directory.Delete(fullPath, recursive: true);
        }
    }

    private ImageController CreateController(
        long maxImageBytes = 5 * 1024 * 1024,
        long maxExcelBytes = 10 * 1024 * 1024)
    {
        Directory.CreateDirectory(_contentRoot);
        var environment = Substitute.For<IWebHostEnvironment>();
        environment.ContentRootPath.Returns(_contentRoot);
        var options = Options.Create(new SampleUploadOptions
            {
                MaxImageBytes = maxImageBytes,
                MaxExcelBytes = maxExcelBytes,
                StorageDirectory = "uploads"
            });
        var controller = new ImageController(
            options,
            new FileUploadValidator(),
            new LocalUploadStorage(environment, options))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        return controller;
    }

    private static FormFile CreateFormFile(byte[] content, string fileName, string contentType)
    {
        var stream = new MemoryStream(content, writable: false);
        return new FormFile(stream, 0, content.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }

    private sealed class ThrowingCopyStream : MemoryStream
    {
        public override async Task CopyToAsync(
            Stream destination,
            int bufferSize,
            CancellationToken cancellationToken)
        {
            await destination.WriteAsync(new byte[] { 0x01 }, cancellationToken);
            throw new IOException("Simulated copy failure.");
        }
    }
}
