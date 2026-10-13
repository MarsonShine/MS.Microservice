using System.Net;
using static MS.Microservice.Lab.Tests.Tools.CoreToolTestRequests;

namespace MS.Microservice.Lab.Tests.Tools;

[Collection("Tool demos")]
public sealed class S3ToolsApiTests
{


    [Fact]
    public async Task UnconfiguredS3Returns503WithoutSendingObjects()
    {
        using var host = new ToolDemoTestHost(new Dictionary<string, string?>
        {
            ["ToolDemos:S3:Enabled"] = "false"
        });
        using var response = await host.Client.GetAsync("/api/lab/tools/s3/0123456789abcdef0123456789abcdef.bin");
        await Status(response, HttpStatusCode.ServiceUnavailable);
    }
}
