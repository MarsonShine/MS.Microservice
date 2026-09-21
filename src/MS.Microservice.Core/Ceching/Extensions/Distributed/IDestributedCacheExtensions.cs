using System;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json.Serialization.Metadata;
using System.Text.Json;

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Caching.Distributed
{
	public static partial class IDestributedCacheExtensions
	{
		extension(IDistributedCache cache)
		{
			public async Task<(bool Success, T? Value)> TryGetValueAsync<T>(string key, [NotNull] Func<Task<T?>> getAsync, JsonTypeInfo<T> typeInfo, DistributedCacheEntryOptions? cacheEntryOptions = null, CancellationToken cancellationToken = default)
			{
				ArgumentNullException.ThrowIfNull(getAsync);

				var item = await cache.GetAsync(key, typeInfo, cancellationToken);
				if (IsNullOrEmpty(item))
				{
					item = await getAsync();
					if (IsNullOrEmpty(item)) return (false, default);
					await cache.SetAsync(key, item, typeInfo, cacheEntryOptions?.AbsoluteExpirationRelativeToNow, cacheEntryOptions?.SlidingExpiration, cancellationToken);
				}
				return (true, item);
			}

			public async Task<TCache?> GetAsync<TCache>(string key, JsonTypeInfo<TCache> typeInfo, CancellationToken cancellationToken = default)
			{
				ArgumentNullException.ThrowIfNull(typeInfo);
				var bytes = await cache.GetAsync(key, cancellationToken);
				if (bytes == null) return default;

				cancellationToken.ThrowIfCancellationRequested();
				ReadOnlySpan<byte> json = bytes;
				// 保留原流式解析器接受文件开头 UTF-8 BOM 的行为，不复制或修改缓存字节。
				if (json.StartsWith(Encoding.UTF8.Preamble)) json = json[Encoding.UTF8.Preamble.Length..];
				var value = JsonSerializer.Deserialize(json, typeInfo);
				cancellationToken.ThrowIfCancellationRequested();
				return value;
			}

			public async Task<TCache> GetAsync<TCache>(string key, Func<Task<TCache>> getFromDatabaseAsyncCallback, JsonTypeInfo<TCache> typeInfo, DistributedCacheEntryOptions? cacheEntryOptions = null, CancellationToken cancellationToken = default)
			{
				ArgumentNullException.ThrowIfNull(getFromDatabaseAsyncCallback);

				var obj = await cache.GetAsync(key, typeInfo, cancellationToken);
				if (IsNullOrEmpty(obj))
				{
					var cacheItem = await getFromDatabaseAsyncCallback();
					if (!IsNullOrEmpty(cacheItem))
					{
						await cache.SetAsync(key, cacheItem, typeInfo, cacheEntryOptions?.AbsoluteExpirationRelativeToNow, cacheEntryOptions?.SlidingExpiration, cancellationToken);
					}
					return cacheItem;
				}
				return obj;
			}

			public async Task SetAsync<T>(string key, T obj, JsonTypeInfo<T> typeInfo, TimeSpan? absoluteExpiration = null, TimeSpan? slidingExpiration = null, CancellationToken cancellationToken = default)
			{
				ArgumentNullException.ThrowIfNull(obj);
				ArgumentNullException.ThrowIfNull(typeInfo);

				var bytes = JsonSerializer.SerializeToUtf8Bytes(obj, typeInfo);
				await cache.SetAsync(key
					, bytes
					, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = absoluteExpiration, SlidingExpiration = slidingExpiration }
					, cancellationToken);
			}
		}

		private static bool IsNullOrEmpty<T>([NotNullWhen(false)] T obj)
		{
			if (obj == null)
			{
				return true;
			}
			if (obj is string s)
			{
				return string.IsNullOrEmpty(s);
			}
			if (obj is IEnumerable enumerable)
			{
				return !enumerable.GetEnumerator().MoveNext();
			}
			return false;
		}
	}
}
