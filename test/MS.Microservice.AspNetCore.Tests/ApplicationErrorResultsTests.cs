using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MS.Microservice.AspNetCore.Tests;

public sealed class ApplicationErrorResultsTests
{
    [Theory]
    [InlineData("validation", HttpStatusCode.BadRequest)]
    [InlineData("unauthorized", HttpStatusCode.Unauthorized)]
    [InlineData("not_found", HttpStatusCode.NotFound)]
    [InlineData("conflict", HttpStatusCode.Conflict)]
    public async Task KnownErrorsKeepPublicMessageAndDetails(string code, HttpStatusCode status)
    {
        using var response = await SendAsync(code, "public explanation", ["first", "second"]);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(status, response.StatusCode);
        Assert.Equal("public explanation", json.RootElement.GetProperty("title").GetString());
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
        Assert.Equal(2, json.RootElement.GetProperty("details").GetArrayLength());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("traceId").GetString()));
    }

    [Theory]
    [InlineData("unexpected")]
    [InlineData("database_down")]
    public async Task UnknownErrorsNeverExposeExceptionTextOrDetails(string code)
    {
        using var response = await SendAsync(code, "password=secret; internal stack", ["private connection string"]);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("An unexpected error occurred.", json.RootElement.GetProperty("title").GetString());
        Assert.Equal("unexpected", json.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("secret", body, StringComparison.Ordinal);
        Assert.DoesNotContain("private", body, StringComparison.Ordinal);
        Assert.False(json.RootElement.TryGetProperty("details", out _));
    }

    [Fact]
    public void MissingCodeIsRejectedBeforeCreatingAResult()
        => Assert.Throws<ArgumentException>(() => ApplicationErrorResults.ToProblem("", "error"));

    private static async Task<HttpResponseMessage> SendAsync(string code, string message, string[] details)
    {
        var builder = ServiceHost.CreateBuilder([]);
        builder.WebHost.UseTestServer();
        builder.Services.AddPlatformHttp(builder.Configuration);
        await using var app = builder.Build();
        app.UsePlatformHttp();
        app.MapGet("/error", () => ApplicationErrorResults.ToProblem(code, message, details));
        await app.StartAsync();
        using var client = app.GetTestClient();
        return await client.GetAsync("/error");
    }
}
