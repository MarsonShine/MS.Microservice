using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using MS.Microservice.Lab.Controller;
using static MS.Microservice.Lab.Tests.Tools.CoreToolTestRequests;

namespace MS.Microservice.Lab.Tests.Tools;

[Collection("Tool demos")]
public sealed class FileToolsApiTests
{
    [Fact]
    public async Task FilesSupportUploadMetadataHashDuplicatesDownloadAndDelete()
    {
        using var host = new ToolDemoTestHost();
        var bytes = "alpha\n"u8.ToArray();
        var first = await Upload(host, "first.txt", bytes);
        var second = await Upload(host, "second.txt", bytes);
        var different = await Upload(host, "different.txt", "omega\n"u8.ToArray());
        using var metadataResponse = await host.Client.GetAsync("/api/lab/tools/files/" + first.Key);
        var metadata = await Json<DemoStoredFile>(metadataResponse);
        Assert.Equal(bytes.Length, metadata.Length);
        Assert.Equal(first.Key, metadata.Key);
        using var hashResponse = await host.Client.GetAsync("/api/lab/tools/files/" + first.Key + "/sha256");
        var hash = await Json<DemoFileHash>(hashResponse);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), hash.Sha256);
        using var duplicateResponse = await host.Client.PostAsJsonAsync("/api/lab/tools/files/duplicates",
            new { keys = new[] { first.Key, second.Key, different.Key, first.Key } });
        var group = Assert.Single(await Json<DemoDuplicateGroup[]>(duplicateResponse));
        Assert.Equal(2, group.Keys.Count);
        Assert.Contains(first.Key, group.Keys);
        Assert.Contains(second.Key, group.Keys);
        Assert.DoesNotContain(different.Key, group.Keys);
        using var contentResponse = await host.Client.GetAsync(first.DownloadUrl);
        await Status(contentResponse, HttpStatusCode.OK);
        Assert.Equal(bytes, await contentResponse.Content.ReadAsByteArrayAsync());
        Assert.Equal("application/octet-stream", contentResponse.Content.Headers.ContentType?.MediaType);
        using var deleted = await host.Client.DeleteAsync("/api/lab/tools/files/" + first.Key);
        await Status(deleted, HttpStatusCode.NoContent);
        using var missing = await host.Client.GetAsync(first.DownloadUrl);
        await Status(missing, HttpStatusCode.NotFound);
        using var repeatDelete = await host.Client.DeleteAsync("/api/lab/tools/files/" + first.Key);
        await Status(repeatDelete, HttpStatusCode.NoContent);
    }

    [Theory]
    [InlineData("/content")]
    [InlineData("/sha256")]
    public async Task MissingFileBeforeAnyOwnerDirectoryExistsReturns404(string suffix)
    {
        using var host = new ToolDemoTestHost();
        using var response = await host.Client.GetAsync("/api/lab/tools/files/0123456789abcdef0123456789abcdef.bin" + suffix);
        await Status(response, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ZipCreationExtractionAndIndividualDownloadsFormAWorkingPipeline()
    {
        using var host = new ToolDemoTestHost();
        using var source = Form("Files", "hello.txt", "你好 ZIP"u8.ToArray());
        source.Add(new ByteArrayContent([1, 2, 3, 4]), "Files", "values.bin");
        using var created = await host.Client.PostAsync("/api/lab/tools/files/zip/create", source);
        await Status(created, HttpStatusCode.OK);
        Assert.Equal("application/zip", created.Content.Headers.ContentType?.MediaType);
        var archiveBytes = await created.Content.ReadAsByteArrayAsync();
        using (var input = new MemoryStream(archiveBytes))
        using (var archive = new ZipArchive(input, ZipArchiveMode.Read))
            Assert.Equal(new[] { "hello.txt", "values.bin" }, archive.Entries.Select(entry => entry.FullName).Order(StringComparer.Ordinal));
        using var upload = Form("File", "archive.zip", archiveBytes);
        using var extracted = await host.Client.PostAsync("/api/lab/tools/files/zip/extract", upload);
        var result = await Json<DemoExtractionResult>(extracted);
        Assert.Equal(2, result.Files.Count);
        foreach (var item in result.Files)
        {
            using var download = await host.Client.GetAsync(item.File.DownloadUrl);
            await Status(download, HttpStatusCode.OK);
            var expected = item.RelativePath == "hello.txt" ? "你好 ZIP"u8.ToArray() : new byte[] { 1, 2, 3, 4 };
            Assert.Equal(expected, await download.Content.ReadAsByteArrayAsync());
        }
    }

    private static async Task<DemoStoredFile> Upload(ToolDemoTestHost host, string name, byte[] bytes)
    {
        using var form = Form("File", name, bytes);
        using var response = await host.Client.PostAsync("/api/lab/tools/files", form);
        return await Json<DemoStoredFile>(response, HttpStatusCode.Created);
    }
}
