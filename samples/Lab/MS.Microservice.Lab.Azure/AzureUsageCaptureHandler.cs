using System.Text.Json;
using Microsoft.Extensions.Options;
using MS.Microservice.AI.Core;

namespace MS.Microservice.Lab.Azure;

internal sealed class AzureUsageCaptureHandler(IOptions<AIOptions> options) : DelegatingHandler
{
    public static readonly AsyncLocal<AzureHttpObservation?> Current = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        if (Current.Value is not { } observation)
        {
            return response;
        }

        observation.Status = (int)response.StatusCode;
        foreach (var header in new[] { "apim-request-id", "x-request-id", "request-id", "x-ms-request-id" })
        {
            if (response.Headers.TryGetValues(header, out var values))
            {
                observation.RequestId = values.FirstOrDefault();
                break;
            }
        }

        if (!response.IsSuccessStatusCode || request.RequestUri?.AbsolutePath is not
            ("/openai/v1/responses" or "/openai/v1/images/generations"))
        {
            return response;
        }

        try
        {
            await CaptureUsageAsync(response.Content, observation, cancellationToken);
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private async Task CaptureUsageAsync(HttpContent content, AzureHttpObservation observation, CancellationToken cancellationToken)
    {
        var maximum = options.Value.Providers["Azure"].MaxResponseBytes;
        await content.LoadIntoBufferAsync(maximum, cancellationToken);
        var stream = await content.ReadAsStreamAsync(cancellationToken);
        if (!stream.CanSeek || stream.Length > maximum)
        {
            return;
        }

        // Core reuses the buffered body to produce the public response. Keep only Azure's usage object here.
        var position = stream.Position;
        try
        {
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (document.RootElement.ValueKind is JsonValueKind.Object &&
                document.RootElement.TryGetProperty("usage", out var usage) && usage.ValueKind is JsonValueKind.Object)
            {
                observation.Usage = usage.Clone();
            }
        }
        catch (JsonException)
        {
            // Let the provider classify malformed responses through its normal parsing path.
        }
        finally
        {
            stream.Position = position;
        }
    }
}

internal sealed class AzureHttpObservation
{
    public int? Status { get; set; }
    public string? RequestId { get; set; }
    public JsonElement? Usage { get; set; }
}
