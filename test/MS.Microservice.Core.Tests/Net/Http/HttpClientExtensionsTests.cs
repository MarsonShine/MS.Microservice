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

        using HttpResponseMessage response = await client.GetAsync("orders", new QueryPayload
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

    [Fact]
    public async Task GetAsync_ShouldEscapeReservedCharactersInQueryValues()
    {
        var handler = new RecordingHandler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new ResponsePayload { Message = "ok" }) }));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };

        await client.GetAsync<ResponsePayload>("orders", new EscapingPayload
        {
            Keyword = "中文 空格&x=1",
            Tags = ["a&b", "c d"]
        });

        // 未转义时 "&" 会把一个参数值切断成新的参数、空格与中文直接进 URL，这里锁定修复后的形状。
        Assert.Equal(
            "https://example.test/orders?Keyword=%E4%B8%AD%E6%96%87%20%E7%A9%BA%E6%A0%BC%26x%3D1&Tags=a%26b&Tags=c%20d",
            handler.LastRequest!.RequestUri!.AbsoluteUri);
    }

    [Theory]
    [InlineData("orders?fixed=1#anchor", false, "orders?fixed=1&Added=a%20%26%20b#anchor")]
    [InlineData("orders#anchor", false, "orders?Added=a%20%26%20b#anchor")]
    [InlineData("orders?", false, "orders?Added=a%20%26%20b")]
    [InlineData("orders?fixed=1&", false, "orders?fixed=1&Added=a%20%26%20b")]
    [InlineData("orders?fixed=1#anchor", true, "orders?fixed=1#anchor")]
    [InlineData("orders#anchor", true, "orders#anchor")]
    public async Task GetAsync_MergesParametersBeforeFragmentWithoutAddingEmptyQuery(string api, bool empty, string expected)
    {
        var handler = new RecordingHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new ResponsePayload { Message = "ok" })
        }));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        var body = new { Added = empty ? null : "a & b" };

        await client.GetAsync<ResponsePayload>(api, body);
        Assert.Equal("https://example.test/" + expected, handler.LastRequest!.RequestUri!.AbsoluteUri);
        using var raw = await client.GetAsync(api, body);
        Assert.Equal("https://example.test/" + expected, handler.LastRequest!.RequestUri!.AbsoluteUri);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, "application/json", "{\"Message\":\"ok\"}", true)]
    [InlineData(HttpStatusCode.OK, "text/plain", "plain", false)]
    [InlineData(HttpStatusCode.OK, "application/json", "{broken", false)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "application/json", "{}", false)]
    public async Task GetAsync_GenericDisposesResponseAndContentOnEveryReturnPath(
        HttpStatusCode status, string contentType, string payload, bool hasResult)
    {
        var content = new TrackingContent(payload, contentType);
        using var response = new TrackingResponse(status) { Content = content };
        using var client = new HttpClient(new RecordingHandler(_ => Task.FromResult<HttpResponseMessage>(response)))
        {
            BaseAddress = new Uri("https://example.test/")
        };

        var result = await client.GetAsync<ResponsePayload>("item", body: null!);

        Assert.Equal(hasResult, result is not null);
        Assert.True(response.Disposed);
        Assert.True(content.Disposed);
    }

    [Fact]
    public async Task GetAsync_GenericTreatsMissingContentTypeAsNonJson()
    {
        var content = new TrackingContent("{\"Message\":\"ok\"}", "application/json");
        content.Headers.ContentType = null;
        using var response = new TrackingResponse(HttpStatusCode.OK) { Content = content };
        using var client = new HttpClient(new RecordingHandler(_ => Task.FromResult<HttpResponseMessage>(response)))
        {
            BaseAddress = new Uri("https://example.test/")
        };

        Assert.Null(await client.GetAsync<ResponsePayload>("item", body: null!));
        Assert.True(response.Disposed);
        Assert.True(content.Disposed);
    }

    [Fact]
    public async Task GetAsync_RawResponseOwnershipRemainsWithTheCaller()
    {
        var content = new TrackingContent("{}", "application/json");
        using var expected = new TrackingResponse(HttpStatusCode.OK) { Content = content };
        using var client = new HttpClient(new RecordingHandler(_ => Task.FromResult<HttpResponseMessage>(expected)))
        {
            BaseAddress = new Uri("https://example.test/")
        };

        var response = await client.GetAsync("item", body: null!);

        Assert.Same(expected, response);
        Assert.False(expected.Disposed);
        Assert.False(content.Disposed);
        response.Dispose();
        Assert.True(expected.Disposed);
        Assert.True(content.Disposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetAsync_CancellationOverloadsCancelAnActiveRequest(bool generic)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new TokenHandler(async (_, token) =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("The canceled request continued.");
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        using var cancellation = new CancellationTokenSource();
        Task call = generic
            ? client.GetAsync<ResponsePayload>(api: "item", body: new { Id = 1 }, cancellationToken: cancellation.Token)
            : client.GetAsync(api: "item", body: new { Id = 1 }, cancellationToken: cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetAsync_CancellationDuringBodyReadDisposesTheUnreturnedResponse(bool generic)
    {
        var content = new WaitingContent();
        using var response = new TrackingResponse(HttpStatusCode.OK) { Content = content };
        using var client = new HttpClient(new TokenHandler((_, _) => Task.FromResult<HttpResponseMessage>(response)))
        {
            BaseAddress = new Uri("https://example.test/")
        };
        using var cancellation = new CancellationTokenSource();
        Task call = generic
            ? client.GetAsync<ResponsePayload>("item", null!, cancellation.Token)
            : client.GetAsync("item", null!, cancellation.Token);
        await content.Reading.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(response.Disposed);
        Assert.True(content.Disposed);
    }

    private sealed class TokenHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => send(request, cancellationToken);
    }

    private sealed class WaitingContent : HttpContent
    {
        public TaskCompletionSource Reading { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Disposed { get; private set; }
        protected override Task SerializeToStreamAsync(System.IO.Stream stream, TransportContext? context)
            => SerializeToStreamAsync(stream, context, CancellationToken.None);
        protected override async Task SerializeToStreamAsync(System.IO.Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            Reading.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override void Dispose(bool disposing) { Disposed |= disposing; base.Dispose(disposing); }
    }

    private sealed class TrackingResponse(HttpStatusCode status) : HttpResponseMessage(status)
    {
        public bool Disposed { get; private set; }
        protected override void Dispose(bool disposing) { Disposed |= disposing; base.Dispose(disposing); }
    }

    private sealed class TrackingContent(string payload, string contentType) : StringContent(payload, Encoding.UTF8, contentType)
    {
        public bool Disposed { get; private set; }
        protected override void Dispose(bool disposing) { Disposed |= disposing; base.Dispose(disposing); }
    }

    private sealed class EscapingPayload
    {
        public string? Keyword { get; set; }
        public string[]? Tags { get; set; }
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
