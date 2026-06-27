using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MS.WebHttpClient;
using Xunit;

namespace MS.Microservice.Core.Tests.Net.Http;

public sealed class HttpClientExtensionsTests
{
    [Fact]
    public async Task GetAsync_ShouldBuildQueryString_ForNullableEnumAndArrayValues()
    {
        var handler = new RecordingHandler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new ResponsePayload { Message = "ok" })
            }));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };

        ResponsePayload? result = await client.GetAsync<ResponsePayload>("orders", new QueryPayload
        {
            Id = 42,
            Status = QueryStatus.Ready,
            Tags = ["alpha", "beta"]
        });

        HttpResponseMessage response = await client.GetAsync("orders", new QueryPayload
        {
            Id = 7,
            Tags = ["solo"]
        });

        Assert.NotNull(result);
        Assert.Equal("ok", result!.Message);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string[] segments = handler.LastRequest!.RequestUri!.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("Id=7", segments);
        Assert.Contains("Tags=solo", segments);
    }

    [Fact]
    public async Task GetAsync_Generic_ShouldReturnNull_WhenContentIsNotJson()
    {
        var handler = new RecordingHandler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("plain", Encoding.UTF8, "text/plain")
            }));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };

        ResponsePayload? result = await client.GetAsync<ResponsePayload>("plain", body: null!);

        Assert.Null(result);
        Assert.Equal(string.Empty, handler.LastRequest!.RequestUri!.Query);
    }

    [Fact]
    public async Task GetAsync_Generic_ShouldReturnNull_WhenJsonIsInvalid()
    {
        var handler = new RecordingHandler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{bad json", Encoding.UTF8, "application/json")
            }));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };

        ResponsePayload? result = await client.GetAsync<ResponsePayload>("broken", new QueryPayload { Id = 1 });

        Assert.Null(result);
        Assert.Contains("Id=1", handler.LastRequest!.RequestUri!.Query);
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return await responder(request);
        }
    }

    private sealed class ResponsePayload
    {
        public string? Message { get; set; }
    }

    private sealed class QueryPayload
    {
        public int Id { get; set; }
        public QueryStatus? Status { get; set; }
        public string[]? Tags { get; set; }
    }

    private enum QueryStatus
    {
        None = 0,
        Ready = 1
    }
}
