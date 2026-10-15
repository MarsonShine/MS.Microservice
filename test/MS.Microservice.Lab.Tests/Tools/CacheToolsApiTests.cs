using System.Net;
using System.Net.Http.Json;
using MS.Microservice.Lab.Controller;

namespace MS.Microservice.Lab.Tests.Tools;

[Collection("Tool demos")]
public sealed class CacheToolsApiTests
{
    [Fact]
    public async Task RepeatedReadsReuseValueAndCommittedUpdateInvalidatesOnlyCurrentOwner()
    {
        using var host = new ToolDemoTestHost();
        var first = (await host.Client.GetFromJsonAsync<CacheDemoResult>("/api/lab/tools/cache"))!;
        var second = (await host.Client.GetFromJsonAsync<CacheDemoResult>("/api/lab/tools/cache"))!;
        Assert.Equal(first.SourceReads, second.SourceReads);
        using var update = await host.Client.PutAsJsonAsync("/api/lab/tools/cache", new CacheDemoWriteRequest { Value = "new-value" });
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        var refreshed = (await host.Client.GetFromJsonAsync<CacheDemoResult>("/api/lab/tools/cache"))!;
        Assert.Equal("new-value", refreshed.Value); Assert.Equal(first.SourceReads + 1, refreshed.SourceReads);
        host.Client.DefaultRequestHeaders.Remove("X-Test-User"); host.Client.DefaultRequestHeaders.Add("X-Test-User", "bob");
        var other = (await host.Client.GetFromJsonAsync<CacheDemoResult>("/api/lab/tools/cache"))!;
        Assert.Equal("初始示例值", other.Value);
    }
}
