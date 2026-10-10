using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MS.Microservice.Storage.S3.Tests;

// A local wire fixture, not an S3/MinIO implementation: real SDK requests and independent SigV4 verification.
internal sealed class S3WireServer(WebApplication application) : IAsyncDisposable
{
    internal const string AccessKey = "LOCALTESTACCESS";
    private const string Secret = "local-test-secret";
    private readonly WebApplication _application = application;
    internal string Url { get; private set; } = "";
    internal ConcurrentDictionary<string, byte[]> Objects { get; } = new(StringComparer.Ordinal);
    internal ConcurrentQueue<WireRequest> Requests { get; } = new();
    internal HttpStatusCode? ForceErrorStatus { get; set; }
    internal string ForceErrorCode { get; set; } = "AccessDenied";
    internal bool HoldPutResponse { get; set; }
    internal bool HoldDownload { get; set; }
    internal TaskCompletionSource PutCommitted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource DownloadAborted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal static async Task<S3WireServer> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        var app = builder.Build();
        var server = new S3WireServer(app);
        app.Run(server.HandleAsync);
        await app.StartAsync();
        server.Url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        return server;
    }

    internal AmazonS3Client CreateClient() => new(new BasicAWSCredentials(AccessKey, Secret), new AmazonS3Config
    {
        ServiceURL = Url, AuthenticationRegion = "us-east-1", ForcePathStyle = true, UseHttp = true,
        MaxErrorRetry = 0, RequestChecksumCalculation = RequestChecksumCalculation.WHEN_SUPPORTED
    });

    private async Task HandleAsync(HttpContext context)
    {
        try
        {
            using var payload = new MemoryStream();
            await context.Request.Body.CopyToAsync(payload, context.RequestAborted);
            var body = payload.ToArray();
            var path = context.Request.Path.Value!;
            var valid = VerifySignature(context, body);
            Requests.Enqueue(new(context.Request.Method, path, context.Request.Headers.IfNoneMatch.ToString(), valid,
                context.Request.Headers.Keys.Any(name => name.StartsWith("x-amz-checksum-", StringComparison.OrdinalIgnoreCase)), body));
            if (!valid) { await ErrorAsync(context, HttpStatusCode.Forbidden, "SignatureDoesNotMatch"); return; }
            if (ForceErrorStatus is { } forced) { await ErrorAsync(context, forced, ForceErrorCode); return; }
            context.Response.Headers["x-amz-request-id"] = "local-request";
            switch (context.Request.Method)
            {
                case "PUT":
                    if (context.Request.Headers.IfNoneMatch != "*") { await ErrorAsync(context, HttpStatusCode.BadRequest, "MissingCondition"); return; }
                    if (!Objects.TryAdd(path, body)) { await ErrorAsync(context, HttpStatusCode.PreconditionFailed, "PreconditionFailed"); return; }
                    PutCommitted.TrySetResult();
                    if (HoldPutResponse) await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted);
                    context.Response.Headers.ETag = "\"" + Hex(MD5.HashData(body)) + "\"";
                    context.Response.ContentLength = 0;
                    break;
                case "HEAD":
                case "GET":
                    if (!Objects.TryGetValue(path, out var content)) { await ErrorAsync(context, HttpStatusCode.NotFound, "NoSuchKey"); return; }
                    context.Response.Headers.LastModified = new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc).ToString("R", CultureInfo.InvariantCulture);
                    context.Response.Headers.ETag = "\"" + Hex(MD5.HashData(content)) + "\"";
                    context.Response.ContentType = "application/octet-stream";
                    context.Response.ContentLength = HoldDownload && context.Request.Method == "GET" ? 1_000_000 : content.Length;
                    if (context.Request.Method == "GET")
                    {
                        if (HoldDownload)
                        {
                            await context.Response.Body.WriteAsync(new byte[] { 42 }, context.RequestAborted);
                            await context.Response.Body.FlushAsync(context.RequestAborted);
                            try { await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted); }
                            catch (OperationCanceledException) { DownloadAborted.TrySetResult(); }
                        }
                        else await context.Response.Body.WriteAsync(content, context.RequestAborted);
                    }
                    break;
                case "DELETE":
                    Objects.TryRemove(path, out _);
                    context.Response.StatusCode = StatusCodes.Status204NoContent;
                    break;
                default: await ErrorAsync(context, HttpStatusCode.MethodNotAllowed, "MethodNotAllowed"); break;
            }
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
    }

    private static async Task ErrorAsync(HttpContext context, HttpStatusCode status, string code)
    {
        context.Response.StatusCode = (int)status;
        context.Response.ContentType = "application/xml";
        context.Response.Headers["x-amz-request-id"] = "local-request";
        if (context.Request.Method != "HEAD")
            await context.Response.WriteAsync($"<Error><Code>{code}</Code><Message>Local wire fixture error</Message><RequestId>local-request</RequestId></Error>", context.RequestAborted);
    }

    private static bool VerifySignature(HttpContext context, byte[] body)
    {
        const string algorithm = "AWS4-HMAC-SHA256";
        var authorization = context.Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith(algorithm + " ", StringComparison.Ordinal)) return false;
        var fields = authorization[(algorithm.Length + 1)..].Split(',').Select(value => value.Trim().Split('=', 2))
            .ToDictionary(pair => pair[0], pair => pair[1], StringComparer.Ordinal);
        var credentials = fields["Credential"].Split('/');
        if (credentials[0] != AccessKey || credentials[2] != "us-east-1" || credentials[3] != "s3") return false;
        var signedHeaders = fields["SignedHeaders"];
        var headers = string.Concat(signedHeaders.Split(';').Select(name => name + ":" +
            string.Join(' ', context.Request.Headers[name].ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)) + "\n"));
        var query = string.Join('&', (context.Request.QueryString.Value ?? "").TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2)).Select(pair => (Name: Uri.EscapeDataString(Uri.UnescapeDataString(pair[0])),
                Value: Uri.EscapeDataString(Uri.UnescapeDataString(pair.Length == 2 ? pair[1] : ""))))
            .OrderBy(pair => pair.Name, StringComparer.Ordinal).ThenBy(pair => pair.Value, StringComparer.Ordinal)
            .Select(pair => pair.Name + "=" + pair.Value));
        var payloadHash = context.Request.Headers["x-amz-content-sha256"].ToString();
        if (payloadHash.Length == 64 && payloadHash != Hex(SHA256.HashData(body))) return false;
        var canonical = $"{context.Request.Method}\n{context.Request.Path}\n{query}\n{headers}\n{signedHeaders}\n{payloadHash}";
        var scope = string.Join('/', credentials.Skip(1));
        var toSign = $"{algorithm}\n{context.Request.Headers["x-amz-date"]}\n{scope}\n{Hex(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))}";
        var signingKey = Encoding.UTF8.GetBytes("AWS4" + Secret);
        foreach (var part in credentials.Skip(1)) signingKey = HMACSHA256.HashData(signingKey, Encoding.UTF8.GetBytes(part));
        return fields["Signature"] == Hex(HMACSHA256.HashData(signingKey, Encoding.UTF8.GetBytes(toSign)));
    }

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

    public async ValueTask DisposeAsync() { await _application.StopAsync(); await _application.DisposeAsync(); }
    internal sealed record WireRequest(string Method, string Path, string IfNoneMatch, bool SignatureValid, bool HasChecksum, byte[] Body);
}
