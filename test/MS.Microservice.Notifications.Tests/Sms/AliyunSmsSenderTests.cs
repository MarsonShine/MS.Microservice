using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using AlibabaCloud.SDK.Dysmsapi20170525;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MS.Microservice.Notifications.Sms;
using Xunit;

namespace MS.Microservice.Notifications.Tests.Sms;

public sealed class AliyunSmsSenderTests
{
    [Theory]
    [InlineData("13900000000")]
    [InlineData("+8613900000000")]
    [InlineData("008613900000000")]
    public async Task Send_UsesRealSdkEncodingSignatureAndSubmissionReceipt(string phone)
    {
        await using var server = await SmsServer.StartAsync();
        var sender = server.Sender();
        var before = DateTimeOffset.UtcNow;
        var receipt = await sender.SendAsync(new(phone, "测试签名", "SMS_123456")
        {
            TemplateParameters = new Dictionary<string, string> { ["name"] = "张三 & + 😀", ["code"] = "012345" },
            OutId = "job-123"
        });
        var request = Assert.Single(server.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal(phone, request.Fields["PhoneNumbers"]);
        Assert.Equal("测试签名", request.Fields["SignName"]);
        Assert.Equal("SMS_123456", request.Fields["TemplateCode"]);
        Assert.Equal("job-123", request.Fields["OutId"]);
        using var values = JsonDocument.Parse(request.Fields["TemplateParam"]);
        Assert.Equal("张三 & + 😀", values.RootElement.GetProperty("name").GetString());
        Assert.Equal("012345", values.RootElement.GetProperty("code").GetString());
        Assert.True(request.Headers.ContainsKey("Authorization") || request.Fields.ContainsKey("Signature"));
        Assert.Equal(("request-1", "biz-1", "job-123"), (receipt.RequestId, receipt.BizId, receipt.OutId));
        Assert.InRange(receipt.AcceptedAtUtc, before, DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Send_WithoutVariablesOmitsOptionalTemplateJsonAndSupportsStsCredentials()
    {
        await using var server = await SmsServer.StartAsync();
        var receipt = await server.Sender(securityToken: "temporary-token").SendAsync(Message());
        var request = Assert.Single(server.Requests);
        Assert.False(request.Fields.ContainsKey("TemplateParam"));
        Assert.True(request.Headers.TryGetValue("x-acs-security-token", out var header) && header == "temporary-token"
            || request.Fields.TryGetValue("SecurityToken", out var query) && query == "temporary-token");
        Assert.Null(receipt.OutId);
    }

    [Fact]
    public async Task BusinessRejectionIsNotAcceptanceAndIsNotRetried()
    {
        await using var server = await SmsServer.StartAsync("{\"Code\":\"isv.BUSINESS_LIMIT_CONTROL\",\"Message\":\"limited\",\"RequestId\":\"reject-1\"}");
        var error = await Assert.ThrowsAsync<SmsRejectedException>(() => server.Sender().SendAsync(Message()));
        Assert.Equal(("isv.BUSINESS_LIMIT_CONTROL", "reject-1", "limited"), (error.Code, error.RequestId, error.ProviderMessage));
        Assert.Single(server.Requests);
    }

    [Fact]
    public async Task HttpFailureIsNotRetriedByTheSdk()
    {
        await using var server = await SmsServer.StartAsync("{\"Code\":\"InternalError\",\"Message\":\"failure\",\"RequestId\":\"failed-1\"}", 500);
        await Assert.ThrowsAnyAsync<Exception>(() => server.Sender().SendAsync(Message()));
        Assert.Single(server.Requests);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"Code\":\"OK\"}")]
    [InlineData("{\"Code\":\"OK\",\"RequestId\":\"request-1\"}")]
    public async Task IncompleteSuccessResponseDoesNotFabricateAReceipt(string response)
    {
        await using var server = await SmsServer.StartAsync(response);
        await Assert.ThrowsAsync<InvalidDataException>(() => server.Sender().SendAsync(Message()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("+123")]
    [InlineData("13900000000,13800000000")]
    [InlineData("139 00000000")]
    [InlineData("13900000000\r\n")]
    [InlineData("１２３４５６７８９０１")]
    [InlineData("123456789012345678901")]
    public async Task InvalidPhoneIsRejectedBeforeNetwork(string phone)
    {
        await using var server = await SmsServer.StartAsync();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => server.Sender().SendAsync(Message() with { PhoneNumber = phone }));
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task InvalidTemplateSignOrUnicodeDoesNotSend()
    {
        await using var server = await SmsServer.StartAsync();
        var invalid = new[]
        {
            Message() with { TemplateCode = "SMS_" }, Message() with { TemplateCode = "other" },
            Message() with { SignName = "line\nbreak" }, Message() with { SignName = new string('\ud800', 1) },
            Message() with { TemplateParameters = new Dictionary<string, string> { ["x"] = new string('\ud800', 1) } },
            Message() with { TemplateParameters = new Dictionary<string, string> { ["x"] = new string('a', 2049) } },
            Message() with { TemplateParameters = Enumerable.Range(0, 33).ToDictionary(index => "x" + index, _ => "value") }
        };
        foreach (var message in invalid)
            await Assert.ThrowsAnyAsync<ArgumentException>(() => server.Sender().SendAsync(message));
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task EncodedTemplateBudgetUsesActualJsonBytes()
    {
        await using var server = await SmsServer.StartAsync();
        var values = new Dictionary<string, string> { ["code"] = "123456" };
        var length = JsonSerializer.SerializeToUtf8Bytes(values).Length;
        await server.Sender(templateBudget: length).SendAsync(Message() with { TemplateParameters = values });
        await Assert.ThrowsAsync<ArgumentException>(() => server.Sender(templateBudget: length - 1).SendAsync(Message() with { TemplateParameters = values }));
        Assert.Single(server.Requests);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(250)]
    [InlineData(500)]
    public async Task FiniteSdkRequestTimeoutDoesNotCauseAutomaticRetry(int timeout)
    {
        await using var server = await SmsServer.StartAsync(delay: TimeSpan.FromSeconds(2));
        var task = server.Sender(timeout: timeout).SendAsync(Message());
        await Assert.ThrowsAnyAsync<Exception>(() => task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(task.IsCompleted, "The SDK operation itself must finish; a timeout around the test is not cancellation.");
        Assert.Single(server.Requests);
    }

    [Fact]
    public async Task PerRequestTimeoutIsHonoredWhenTheSdkReusesItsHttpClient()
    {
        await using var server = await SmsServer.StartAsync(delay: TimeSpan.FromMilliseconds(300));
        await server.Sender(timeout: 2000).SendAsync(Message());
        await Assert.ThrowsAnyAsync<Exception>(() => server.Sender(timeout: 100).SendAsync(Message()));
        await server.Sender(timeout: 2000).SendAsync(Message());
        Assert.Equal(3, server.Requests.Count);
    }

    [Fact]
    public void ProductionConfigurationUsesHttpsAndValidatesBeforeClientCreation()
    {
        AlibabaCloud.OpenApiClient.Models.Config? captured = null;
        var sender = new AliyunSmsSender(new() { AccessKeyId = "dummy-ak", AccessKeySecret = "dummy-secret" }, config =>
        {
            captured = config;
            return new Client(config);
        });
        Assert.NotNull(sender);
        Assert.Equal("https", captured!.Protocol);
        Assert.Equal("dysmsapi.aliyuncs.com", captured.Endpoint);
        Assert.False(captured.EnableUsageDataCollection);
        Assert.NotNull(captured.Credential);
        var calls = 0;
        foreach (var invalid in new[] { 0, -1, 100_001, int.MaxValue })
            Assert.Throws<ArgumentException>(() => new AliyunSmsSender(new() { AccessKeyId = "dummy", AccessKeySecret = "secret", TimeoutMilliseconds = invalid }, config =>
            { calls++; return new Client(config); }));
        Assert.Equal(0, calls);
    }

    private static SmsMessage Message() => new("13900000000", "测试签名", "SMS_123456");

    private sealed record SmsRequest(string Method, Dictionary<string, string> Fields, Dictionary<string, string> Headers);

    private sealed class SmsServer(WebApplication app, string endpoint) : IAsyncDisposable
    {
        public ConcurrentQueue<SmsRequest> Requests { get; } = new();

        public static async Task<SmsServer> StartAsync(string response = "{\"Code\":\"OK\",\"RequestId\":\"request-1\",\"BizId\":\"biz-1\"}",
            int statusCode = 200, TimeSpan delay = default)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
            var app = builder.Build();
            SmsServer? server = null;
            app.Run(async context =>
            {
                var fields = context.Request.Query.ToDictionary(pair => pair.Key, pair => pair.Value.ToString(), StringComparer.Ordinal);
                if (context.Request.HasFormContentType)
                    foreach (var pair in await context.Request.ReadFormAsync()) fields[pair.Key] = pair.Value.ToString();
                server!.Requests.Enqueue(new(context.Request.Method, fields,
                    context.Request.Headers.ToDictionary(pair => pair.Key, pair => pair.Value.ToString(), StringComparer.OrdinalIgnoreCase)));
                if (delay > TimeSpan.Zero)
                {
                    try { await Task.Delay(delay, context.RequestAborted); }
                    catch (OperationCanceledException) { return; }
                }
                context.Response.StatusCode = statusCode;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(response);
            });
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            server = new(app, new Uri(address).Authority);
            return server;
        }

        public AliyunSmsSender Sender(string? securityToken = null, int templateBudget = 16 * 1024, int timeout = 30_000)
            => new(new() { AccessKeyId = "dummy-ak", AccessKeySecret = "dummy-secret", SecurityToken = securityToken,
                MaximumTemplateBytes = templateBudget, TimeoutMilliseconds = timeout }, config =>
            {
                config.Protocol = "http";
                config.Endpoint = endpoint;
                return new Client(config);
            });

        public async ValueTask DisposeAsync()
        {
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await app.StopAsync(stop.Token);
            await app.DisposeAsync();
        }
    }
}
