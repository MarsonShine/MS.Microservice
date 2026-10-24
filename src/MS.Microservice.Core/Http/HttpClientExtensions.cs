using MS.Microservice.Core.Net.Http;
using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using System.Threading;
using System.Threading.Tasks;

namespace MS.WebHttpClient
{
    public static partial class HttpClientExtensions
    {
        // HTTP 忽略属性名大小写；与缓存规则不同，因此单独持有不可变的配置。
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true,
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
        };

        extension(HttpClient client)
        {
            public Task<T> GetAsync<T>(string api, object body)
                => client.GetAsync<T>(api, body, CancellationToken.None);

            public Task<HttpResponseMessage> GetAsync(string api, object body)
                => client.GetAsync(api, body, CancellationToken.None);

            public async Task<T> GetAsync<T>(string api, object body, CancellationToken cancellationToken)
            {
                using var message = await GetAsyncCore(client, api, body, cancellationToken).ConfigureAwait(false);
                if (!message.IsSuccessStatusCode || message.Content.Headers.ContentType?.MediaType != "application/json")
                    return default!;
                try
                {
                    return (await message.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken).ConfigureAwait(false))!;
                }
                catch (JsonException) { return default!; }
            }

            public Task<HttpResponseMessage> GetAsync(string api, object body, CancellationToken cancellationToken)
                => GetAsyncCore(client, api, body, cancellationToken);
        }

        private static async Task<HttpResponseMessage> GetAsyncCore(HttpClient client, string api, object body,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(client);
            return await client.GetAsync(QueryStringParameters.BuildUrl(api, body), cancellationToken).ConfigureAwait(false);
        }
    }
}
