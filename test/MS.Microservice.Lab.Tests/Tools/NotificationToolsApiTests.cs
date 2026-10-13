using System.Net;
using System.Net.Http.Json;
using MS.Microservice.Lab.Controller;
using MS.Microservice.Notifications.Inbox;
using static MS.Microservice.Lab.Tests.Tools.CoreToolTestRequests;

namespace MS.Microservice.Lab.Tests.Tools;

[Collection("Tool demos")]
public sealed class NotificationToolsApiTests
{
    [Fact]
    public async Task TemplateRenderingAndInboxPublishListReadUseActualComponents()
    {
        using var host = new ToolDemoTestHost();
        using var rendered = await host.Client.PostAsJsonAsync("/api/lab/tools/notifications/templates/render",
            new { template = "Hello {{Name}}!", values = new Dictionary<string, string> { ["Name"] = "<Alice>" }, htmlEncodeValues = true });
        Assert.Equal("Hello &lt;Alice&gt;!", (await Json<TemplateRenderResult>(rendered)).Text);
        using var published = await host.Client.PostAsJsonAsync("/api/lab/tools/notifications/inbox",
            new { subject = "处理完成", body = "下载结果已生成。", actionPath = "/tools/results" });
        var message = await Json<InboxNotification>(published, HttpStatusCode.Created);
        using var listed = await host.Client.GetAsync("/api/lab/tools/notifications/inbox?skip=0&take=20");
        var before = await Json<InboxPage>(listed);
        Assert.Equal(1, before.TotalCount);
        Assert.Equal(1, before.UnreadCount);
        Assert.Equal(message.Id, Assert.Single(before.Items).Id);
        using var read = await host.Client.PostAsync("/api/lab/tools/notifications/inbox/" + message.Id + "/read", null);
        var receipt = await Json<InboxReadResult>(read);
        using var repeated = await host.Client.PostAsync("/api/lab/tools/notifications/inbox/" + message.Id + "/read", null);
        Assert.Equal(receipt.ReadAtUtc, (await Json<InboxReadResult>(repeated)).ReadAtUtc);
        using var afterResponse = await host.Client.GetAsync("/api/lab/tools/notifications/inbox");
        var after = await Json<InboxPage>(afterResponse);
        Assert.Equal(0, after.UnreadCount);
        Assert.Equal(receipt.ReadAtUtc, Assert.Single(after.Items).ReadAtUtc);
    }

    [Theory]
    [InlineData("Name")]
    [InlineData("User.Name")]
    [InlineData("Api_Token")]
    public async Task TemplateDictionaryKeysArePassedVerbatim(string key)
    {
        using var host = new ToolDemoTestHost();
        using var response = await host.Client.PostAsJsonAsync("/api/lab/tools/notifications/templates/render", new
        {
            template = "Hello {{" + key + "}}!",
            values = new Dictionary<string, string> { [key] = "Literal {{Other}}" }
        });

        Assert.Equal("Hello Literal {{Other}}!", (await Json<TemplateRenderResult>(response)).Text);
    }

    [Fact]
    public async Task TemplateVariableCaseMismatchIsNotSilentlyNormalized()
    {
        using var host = new ToolDemoTestHost();
        using var response = await host.Client.PostAsJsonAsync("/api/lab/tools/notifications/templates/render", new
        {
            template = "Hello {{Name}}!", values = new Dictionary<string, string> { ["name"] = "Alice" }
        });

        await Status(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UnconfiguredEmailAndSmsReturn503WithoutSendingMessages()
    {
        using var host = new ToolDemoTestHost(new Dictionary<string, string?>
        {
            ["ToolDemos:Smtp:Enabled"] = "false", ["ToolDemos:Sms:Enabled"] = "false"
        });
        using var email = await host.Client.PostAsJsonAsync("/api/lab/tools/notifications/email",
            new { to = new[] { "recipient@example.invalid" }, subject = "演示", textBody = "未配置时不会发送。" });
        await Status(email, HttpStatusCode.ServiceUnavailable);
        using var sms = await host.Client.PostAsJsonAsync("/api/lab/tools/notifications/sms",
            new { phoneNumber = "13900000000", signName = "演示签名", templateCode = "SMS_123456" });
        await Status(sms, HttpStatusCode.ServiceUnavailable);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.ServiceUnavailable)]
    [InlineData("false", HttpStatusCode.ServiceUnavailable)]
    [InlineData("true", HttpStatusCode.BadRequest)]
    public async Task SmtpRequiresExplicitEnablementBeforeBusinessValidation(string? enabled, HttpStatusCode expected)
    {
        var configuration = new Dictionary<string, string?>
        {
            ["ToolDemos:Smtp:Host"] = "127.0.0.1", ["ToolDemos:Smtp:Port"] = "2525",
            ["ToolDemos:Smtp:TlsMode"] = "RequireStartTls", ["ToolDemos:Smtp:FromAddress"] = "sender@example.invalid",
            ["ToolDemos:Smtp:UserName"] = "test-user", ["ToolDemos:Smtp:Password"] = "test-password"
        };
        if (enabled is not null) configuration["ToolDemos:Smtp:Enabled"] = enabled;
        using var host = new ToolDemoTestHost(configuration);
        // Header rejection happens before SmtpEmailSender creates a client or opens any connection.
        using var response = await host.Client.PostAsJsonAsync("/api/lab/tools/notifications/email", new
        {
            to = new[] { "invalid\r\nrecipient@example.invalid" }, subject = "Demo", textBody = "No message is sent."
        });

        await Status(response, expected);
        Assert.Contains(expected == HttpStatusCode.BadRequest ? "header" : "ToolDemos:Smtp", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(null, HttpStatusCode.ServiceUnavailable)]
    [InlineData("false", HttpStatusCode.ServiceUnavailable)]
    [InlineData("true", HttpStatusCode.BadRequest)]
    public async Task SmsRequiresExplicitEnablementBeforeBusinessValidation(string? enabled, HttpStatusCode expected)
    {
        var configuration = new Dictionary<string, string?>
        {
            ["ToolDemos:Sms:AccessKeyId"] = "local-test-key", ["ToolDemos:Sms:AccessKeySecret"] = "local-test-secret"
        };
        if (enabled is not null) configuration["ToolDemos:Sms:Enabled"] = enabled;
        using var host = new ToolDemoTestHost(configuration);
        // The invalid code is rejected by AliyunSmsSender before its first SDK send call.
        using var response = await host.Client.PostAsJsonAsync("/api/lab/tools/notifications/sms", new
        {
            phoneNumber = "13900000000", signName = "Demo", templateCode = "NOT_A_TEMPLATE"
        });

        await Status(response, expected);
        Assert.Contains(expected == HttpStatusCode.BadRequest ? "SMS_" : "ToolDemos:Sms", await response.Content.ReadAsStringAsync());
    }
}
