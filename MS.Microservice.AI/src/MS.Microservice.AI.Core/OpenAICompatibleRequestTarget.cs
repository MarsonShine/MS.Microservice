using System.Net.Http.Headers;

namespace MS.Microservice.AI.Core;

internal readonly struct OpenAICompatibleRequestTarget(Uri endpoint, string model, string? credential, string? apiKeyHeader = null)
{
    public string Model { get; } = model;

    public HttpRequestMessage CreateRequest(string? accept, IDictionary<string, string> headers)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        if (apiKeyHeader is null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
        }
        else
        {
            request.Headers.Add(apiKeyHeader, credential);
        }

        if (!string.IsNullOrWhiteSpace(accept))
        {
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        }

        foreach (var header in headers)
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return request;
    }
}
