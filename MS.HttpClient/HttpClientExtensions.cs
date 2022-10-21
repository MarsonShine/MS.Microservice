using MS.Microservice.Core.Extension;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace MS.WebHttpClient
{
    public static class HttpClientExtensions
    {
        public static async Task<T> GetAsync<T>(this HttpClient client, string api, object body)
        {
            var message = await GetAsync(client, api, body);
            return await ReadAsObjectAsync<T>(message);
        }
        public static async Task<HttpResponseMessage> GetAsync(this HttpClient client, string api, object body)
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
            var properties = queryBody.GetType().GetProperties();
            if (!properties.Any()) return "";
            string[] queryStringLocals = new string[properties.Length];

            properties.ForEach((property, index) =>
            {
                var value = property.GetValue(queryBody);
                if (value == null) return;
                // 判断是否枚举
                if (IsNullable(property.PropertyType))
                {
                    var enumType = Nullable.GetUnderlyingType(property.PropertyType)!;
                    if (enumType.IsEnum)
                    {
                        var enumValue = Convert.ChangeType(value, Enum.GetUnderlyingType(enumType));
                        value = enumValue;
                    }
                }
                if (IsArray(property.PropertyType))
                {
                    var arrayObj = value as IEnumerable;
                    var subQueryStrings = new Queue<string>();
                    foreach (var item in arrayObj!)
                    {
                        subQueryStrings.Enqueue($"{property.Name}={item}");
                    }
                    queryStringLocals[index] = string.Join("&", subQueryStrings);
                }
                else
                {
                    queryStringLocals[index] = $"{property.Name}={value}";
                }
            });

            return string.Join("&", queryStringLocals.Where(p => !string.IsNullOrEmpty(p)));
        }

        private static async Task<T> ReadAsObjectAsync<T>(HttpResponseMessage message)
        {
            try
            {
                if (null != message && message.IsSuccessStatusCode)
                {
                    if (message.Content is object && message.Content.Headers.ContentType!.MediaType == "application/json")
                    {
                        var contentStream = await message.Content.ReadAsStreamAsync();
                        try
                        {
                            return (await JsonSerializer.DeserializeAsync<T>(contentStream, new JsonSerializerOptions { IgnoreNullValues = true, PropertyNameCaseInsensitive = true }))!;
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

        static bool IsNullable(Type type) => Nullable.GetUnderlyingType(type) != null;
        static bool IsArray(Type type) => type.IsArray && typeof(IEnumerable).IsAssignableFrom(type);
    }
}
