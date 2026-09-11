using MS.Microservice.Core.Extension;
using MS.Microservice.Core.Net.Http;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using System.Threading.Tasks;

namespace MS.WebHttpClient
{
    public static partial class HttpClientExtensions
    {
        extension(HttpClient client)
        {
            public async Task<T> GetAsync<T>(string api, object body)
            {
                var message = await GetAsyncCore(client, api, body);
                return await ReadAsObjectAsync<T>(message);
            }

            public Task<HttpResponseMessage> GetAsync(string api, object body)
                => GetAsyncCore(client, api, body);
        }

        private static async Task<HttpResponseMessage> GetAsyncCore(HttpClient client, string api, object body)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            string queryString = "";
            if (body != null)
            {
                queryString = "?" + BuildQueryString(body);
            }
            return await client.GetAsync(api + queryString);
        }

        private static string BuildQueryString(object queryBody)
        {
            var parameters = new List<string>();
            // 与 LogHttpClient 共用同一套参数写入：编译后的属性读取 + 保留字符转义。
            var before = parameters.Count;
            if (QueryStringParameters.Dispatch(queryBody, parameters) == before) return "";
            return string.Join("&", parameters);
        }

        private static async Task<T> ReadAsObjectAsync<T>(HttpResponseMessage message)
        {
            try
            {
                if (null != message && message.IsSuccessStatusCode)
                {
                    if (message.Content is not null && message.Content.Headers.ContentType!.MediaType == "application/json")
                    {
                        var contentStream = await message.Content.ReadAsStreamAsync();
                        try
                        {
                            return (await JsonSerializer.DeserializeAsync<T>(contentStream, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, PropertyNameCaseInsensitive = true, Encoder = JavaScriptEncoder.Create(UnicodeRanges.All) }))!;
                        }
                        catch (JsonException)
                        {

                        }
                    }
                }
                return default!;
            }
            catch (Exception)
            {
                throw;
            }

        }
    }
}
