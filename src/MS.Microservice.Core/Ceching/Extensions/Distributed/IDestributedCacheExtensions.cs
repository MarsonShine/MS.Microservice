using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Caching.Distributed
{
    public static class IDestributedCacheExtensions
    {
        public static async Task<TCache?> GetAsync<TCache>(this IDistributedCache cache, string key, CancellationToken cancellationToken = default)
        {
            var bytes = await cache.GetAsync(key, cancellationToken);
            if (bytes == null) return default;

            return await JsonSerializer.DeserializeAsync<TCache>(new MemoryStream(bytes), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = false,
                Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
            }, cancellationToken: cancellationToken);
        }

        public static async Task<TCache> GetAsync<TCache>(this IDistributedCache cache, string key, Func<Task<TCache>> getFromDatabaseAsyncCallback, CancellationToken cancellationToken = default)
        {
            if (getFromDatabaseAsyncCallback == null)
            {
                throw new ArgumentNullException(nameof(getFromDatabaseAsyncCallback));
            }

            var obj = await cache.GetAsync<TCache>(key, cancellationToken);
            if (obj == null)
            {
                var cacheItem = await getFromDatabaseAsyncCallback();
                if (cacheItem != null)
                {
                    await SetAsync(cache, key, cacheItem, null, null, cancellationToken);
                }
                return cacheItem;
            }
            return obj;
        }

        public static async Task SetAsync(this IDistributedCache cache, string key, object obj, TimeSpan? absoluteExpiration, TimeSpan? slidingExpiration, CancellationToken cancellationToken = default)
        {
            if (obj == null)
            {
                throw new ArgumentNullException(nameof(obj));
            }
            var bytes = JsonSerializer.SerializeToUtf8Bytes(obj,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = false,
                    Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
                });
            await cache.SetAsync(key
                , bytes
                , new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = absoluteExpiration, SlidingExpiration = slidingExpiration }
                , cancellationToken);
        }
    }
}
