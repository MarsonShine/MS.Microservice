using System;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Caching.Distributed
{
	public static partial class IDestributedCacheExtensions
	{
		// 私有配置初始化后不再修改；复用编码器和按类型缓存的 JSON 元数据。
		private static readonly JsonSerializerOptions JsonOptions = new()
		{
			PropertyNameCaseInsensitive = false,
			Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
		};

		extension(IDistributedCache cache)
		{
			public async Task<(bool Success, T? Value)> TryGetValueAsync<T>(string key, [NotNull] Func<Task<T?>> getAsync, DistributedCacheEntryOptions? cacheEntryOptions = null, CancellationToken cancellationToken = default)
			{
				ArgumentNullException.ThrowIfNull(getAsync);

				var item = await cache.GetAsync<T>(key, cancellationToken);
				if (IsNullOrEmpty(item))
				{
					item = await getAsync();
					if (IsNullOrEmpty(item)) return (false, default);
					await cache.SetAsync(key, item, cacheEntryOptions?.AbsoluteExpirationRelativeToNow, cacheEntryOptions?.SlidingExpiration, cancellationToken);
				}
				return (true, item);
			}

			public async Task<TCache?> GetAsync<TCache>(string key, CancellationToken cancellationToken = default)
			{
				var bytes = await cache.GetAsync(key, cancellationToken);
				if (bytes == null) return default;

				cancellationToken.ThrowIfCancellationRequested();
				ReadOnlySpan<byte> json = bytes;
				// 保留原流式解析器接受文件开头 UTF-8 BOM 的行为，不复制或修改缓存字节。
				if (json.StartsWith(Encoding.UTF8.Preamble)) json = json[Encoding.UTF8.Preamble.Length..];
				var value = JsonSerializer.Deserialize<TCache>(json, JsonOptions);
				cancellationToken.ThrowIfCancellationRequested();
				return value;
			}

			public async Task<TCache> GetAsync<TCache>(string key, Func<Task<TCache>> getFromDatabaseAsyncCallback, DistributedCacheEntryOptions? cacheEntryOptions = null, CancellationToken cancellationToken = default)
			{
				ArgumentNullException.ThrowIfNull(getFromDatabaseAsyncCallback);

				var obj = await cache.GetAsync<TCache>(key, cancellationToken);
				if (IsNullOrEmpty(obj))
				{
					var cacheItem = await getFromDatabaseAsyncCallback();
					if (!IsNullOrEmpty(cacheItem))
					{
						await cache.SetAsync(key, cacheItem, cacheEntryOptions?.AbsoluteExpirationRelativeToNow, cacheEntryOptions?.SlidingExpiration, cancellationToken);
					}
					return cacheItem;
				}
				return obj;
			}

			public async Task SetAsync(string key, object obj, TimeSpan? absoluteExpiration, TimeSpan? slidingExpiration, CancellationToken cancellationToken = default)
			{
				ArgumentNullException.ThrowIfNull(obj);

				var bytes = JsonSerializer.SerializeToUtf8Bytes(obj, JsonOptions);
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
