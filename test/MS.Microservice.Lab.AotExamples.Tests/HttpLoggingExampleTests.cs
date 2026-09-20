using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static MS.Microservice.TestSupport.HttpLoggingTestSupport;
using LegacyHandler = MS.Microservice.Lab.AotExamples.Legacy.LoggingHttpClientHandler;
using StaticHandler = MS.Microservice.Lab.AotExamples.Static.LoggingHttpClientHandler;

namespace MS.Microservice.Lab.AotExamples.Tests;

public sealed class HttpLoggingExampleTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DisabledLogging_ShowsLegacyBufferingAndStaticStreaming(bool legacy)
    {
        var content = new ProbeContent("first-last"u8.ToArray(), gated: true);
        DelegatingHandler handler = legacy
            ? new LegacyHandler(NullLogger<LegacyHandler>.Instance)
            : new StaticHandler(NullLogger<StaticHandler>.Instance);
        handler.InnerHandler = new CallbackHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));
        using var client = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test");
        using var response = await client.SendAsync(request, CancellationToken.None);
        using var destination = new MemoryStream();
        var copying = response.Content.CopyToAsync(destination);
        try
        {
            await content.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(legacy ? 0 : content.Prefix.Length, destination.Length);
        }
        finally
        {
            content.Release.TrySetResult();
            await copying.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.Equal("first-last"u8.ToArray(), destination.ToArray());
    }

    [Theory]
    [InlineData("")]
    [InlineData("中文正文")]
    public async Task EnabledExamples_PreservePayloadButStaticLogsContainCompletedStrings(string text)
    {
        var legacyLogger = new CapturingLogger<LegacyHandler>();
        var staticLogger = new CapturingLogger<StaticHandler>();
        await Exercise(new LegacyHandler(legacyLogger), text);
        await Exercise(new StaticHandler(staticLogger), text);

        Assert.Equal(2, legacyLogger.Entries.Count);
        Assert.Equal(2, staticLogger.Entries.Count);
        Assert.Equal(legacyLogger.Entries[0].Fields["@Payload"]!.ToString(), staticLogger.Entries[0].Fields["@Payload"]);
        Assert.Equal(legacyLogger.Entries[1].Fields["@Response"]!.ToString(), staticLogger.Entries[1].Fields["@Response"]);
        Assert.IsType<string>(staticLogger.Entries[0].Fields["@Payload"]);
        Assert.IsType<string>(staticLogger.Entries[1].Fields["@Response"]);
        foreach (var entry in staticLogger.Entries) Assert.Equal(entry.Message, entry.Render());
    }

    [Fact]
    public async Task LegacyWrapper_PreservesPayloadAndHeadersForTheArchivedInterface()
    {
        using var wrapper = new LegacyHandler.LoggableHttpContent(new StringContent("payload", Encoding.UTF8, "text/plain"));
        using var destination = new MemoryStream();

        await wrapper.CopyToAsync(destination);

        Assert.Equal("payload", Encoding.UTF8.GetString(destination.ToArray()));
        Assert.Equal("text/plain", wrapper.Headers.ContentType!.MediaType);
        Assert.Equal("payload", new LegacyHandler.LazyContentLogger(wrapper, CancellationToken.None).ToString());
        Assert.Equal("null", new LegacyHandler.LazyContentLogger(null, CancellationToken.None).ToString());
    }

    private static async Task Exercise(DelegatingHandler handler, string text)
    {
        var responseContent = new ProbeContent(Encoding.UTF8.GetBytes(text));
        handler.InnerHandler = new CallbackHandler(async (request, token) =>
        {
            Assert.Equal(text, await request.Content!.ReadAsStringAsync(token));
            return new(HttpStatusCode.OK) { Content = responseContent };
        });
        using var client = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.test")
        {
            Content = new ProbeContent(Encoding.UTF8.GetBytes(text))
        };
        using var response = await client.SendAsync(request, CancellationToken.None);
        Assert.Equal(text, await response.Content.ReadAsStringAsync());
        Assert.Equal(1, responseContent.Serializations);
    }
}
