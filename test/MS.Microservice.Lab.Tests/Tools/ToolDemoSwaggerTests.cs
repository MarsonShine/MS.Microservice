using System.Net;
using System.Text.Json;

namespace MS.Microservice.Lab.Tests.Tools;

[Collection("Tool demos")]
public sealed class ToolDemoSwaggerTests
{
    [Fact]
    public async Task SwaggerDescribesCoreToolOperationsAndMultipartUploads()
    {
        using var host = new ToolDemoTestHost();
        using var response = await host.Client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = json.RootElement.GetProperty("paths");
        var tools = paths.EnumerateObject().Where(path => path.Name.StartsWith("/api/lab/tools/", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(tools);
        foreach (var path in tools)
        foreach (var operation in path.Value.EnumerateObject())
        {
            Assert.True(operation.Value.TryGetProperty("summary", out var summary) && !string.IsNullOrWhiteSpace(summary.GetString()), path.Name);
            Assert.True(operation.Value.TryGetProperty("description", out var description) && !string.IsNullOrWhiteSpace(description.GetString()), path.Name);
        }
        var image = paths.GetProperty("/api/lab/tools/images/process").GetProperty("post");
        Assert.True(image.GetProperty("requestBody").GetProperty("content").TryGetProperty("multipart/form-data", out _));
        var code = paths.GetProperty("/api/lab/tools/codes/generate").GetProperty("post");
        Assert.True(code.GetProperty("responses").GetProperty("200").GetProperty("content").TryGetProperty("image/png", out _));
        Assert.Contains("Python", paths.GetProperty("/api/lab/tools/inference/ocr").GetProperty("post").GetProperty("description").GetString());
    }

    [Fact]
    public async Task SwaggerUsesBinaryFileSchemasAndJsonProblemResponses()
    {
        using var host = new ToolDemoTestHost();
        using var response = await host.Client.GetAsync("/swagger/v1/swagger.json");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = json.RootElement.GetProperty("paths");
        var errors = paths.GetProperty("/api/lab/tools/inference/upscale").GetProperty("post").GetProperty("responses").GetProperty("503").GetProperty("content");
        Assert.True(errors.TryGetProperty("application/problem+json", out _), errors.ToString());
        foreach (var (route, mime) in new[]
        {
            ("/api/lab/tools/codes/generate", "image/png"),
            ("/api/lab/tools/images/process", "image/jpeg"),
            ("/api/lab/tools/videos/process", "video/mp4"),
            ("/api/lab/tools/audio/edit", "audio/wav"),
            ("/api/lab/tools/documents/pdf/select", "application/pdf"),
            ("/api/lab/tools/inference/upscale", "image/png")
        })
        {
            var content = paths.GetProperty(route).GetProperty("post").GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty(mime);
            Assert.Equal("binary", content.GetProperty("schema").GetProperty("format").GetString());
        }
        foreach (var route in new[] { "/api/lab/tools/images/process", "/api/lab/tools/videos/process", "/api/lab/tools/documents/pdf/select", "/api/lab/tools/text/json" })
        {
            var errorContent = paths.GetProperty(route).GetProperty("post").GetProperty("responses").GetProperty("400").GetProperty("content");
            Assert.True(errorContent.TryGetProperty("application/problem+json", out _), route + errorContent);
        }
    }

    [Fact]
    public async Task PersistentTaskSearchRequireAuthenticationAndToolsAreRemovedOutsideLab()
    {
        using var host = new ToolDemoTestHost();
        host.Client.DefaultRequestHeaders.Remove("X-Test-User");
        using var unauthenticated = await host.Client.GetAsync("/api/lab/tools/tasks");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        using var privateSearch = await host.Client.GetAsync("/api/lab/tools/search?query=report");
        Assert.Equal(HttpStatusCode.Unauthorized, privateSearch.StatusCode);
        using var production = new ToolDemoTestHost(includeLab: false);
        using var response = await production.Client.GetAsync("/swagger/v1/swagger.json");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain(json.RootElement.GetProperty("paths").EnumerateObject(), path => path.Name.StartsWith("/api/lab/tools/", StringComparison.Ordinal));
    }
}
