using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using MS.Microservice.Reference.AotWeb;
using Xunit;

namespace MS.Microservice.Reference.Web.Tests;

public sealed class AotHostTests
{
    [Fact]
    public async Task LivenessHasAnExplicitJsonContract()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var response = await fixture.Client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("healthy", body.RootElement.GetProperty("status").GetString());
        Assert.Single(body.RootElement.EnumerateObject());
    }

    [Theory]
    [InlineData("/health/ready")]
    [InlineData("/api/v1/profiles")]
    [InlineData("/api/operations/messages/failures")]
    [InlineData("/api/v1/Account/login")]
    public async Task UnimplementedRoutesReturnProblemDetails(string path)
    {
        await using var fixture = await Fixture.CreateAsync();
        using var response = await fixture.Client.GetAsync(path);
        await AssertProblem(response, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UnsupportedLivenessMethodReturns405()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var response = await fixture.Client.PostAsync("/health/live", null);
        await AssertProblem(response, HttpStatusCode.MethodNotAllowed);
    }

    [Theory]
    [InlineData("https://aot-client.example", true)]
    [InlineData("https://other.example", false)]
    public async Task CorsUsesConfiguredOrigins(string origin, bool allowed)
    {
        await using var fixture = await Fixture.CreateAsync();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/health/live");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        using var response = await fixture.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(allowed, response.Headers.TryGetValues("Access-Control-Allow-Origin", out var origins));
        if (allowed) Assert.Equal(origin, Assert.Single(origins!));
    }

    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal((int)status, body.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("traceId").GetString()));
    }

    private sealed class Fixture(WebApplication? application, HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;

        public static async Task<Fixture> CreateAsync()
        {
            // The same HTTP assertions can target a separately started native executable.
            var nativeUrl = Environment.GetEnvironmentVariable("REFERENCE_AOT_WEB_URL");
            if (!string.IsNullOrWhiteSpace(nativeUrl))
            {
                var address = new Uri(nativeUrl);
                if (address.Scheme != "http" || !address.IsLoopback)
                    throw new InvalidOperationException("REFERENCE_AOT_WEB_URL must use a loopback HTTP address.");
                return new(null, ConfigureClient(new HttpClient { BaseAddress = address }));
            }
            var builder = AotReferenceHost.CreateBuilder(["--environment", "Production",
                "--Cors:Origins:0", "https://aot-client.example"]);
            builder.WebHost.UseTestServer();
            AotReferenceHost.AddServices(builder);
            var app = builder.Build();
            AotReferenceHost.MapApplication(app);
            await app.StartAsync();
            return new(app, ConfigureClient(app.GetTestClient()));
        }

        private static HttpClient ConfigureClient(HttpClient client)
        {
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return client;
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            if (application is not null) await application.DisposeAsync();
        }
    }
}
