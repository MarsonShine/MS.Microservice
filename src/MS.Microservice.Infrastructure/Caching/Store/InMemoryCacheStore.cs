using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace MS.Microservice.Infrastructure.Caching.Store
{
	public class InMemoryCacheStore(IKeyStore keyStore) : ICacheStore
	{
		private readonly ConcurrentDictionary<string, object> _cache = new();
		private readonly IKeyStore _keyStore = keyStore;

		public async Task<bool> ExistsAsync(string key)
		{
			await _keyStore.LogOperationAsync(key, CacheOperation.Success(CacheOperationType.Get, DateTimeOffset.Now));
			return _cache.ContainsKey(key);
		}

		public async Task<T?> GetAsync<T>(string key)
		{
			if (_cache.TryGetValue(key, out var item) && item is T typedItem)
			{
				await _keyStore.LogOperationAsync(key, CacheOperation.Success(CacheOperationType.Get, DateTimeOffset.Now));
				return typedItem;
			}
			return default;
		}

		public async Task<T?> GetOrAddAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null)
		{
			var cache = await GetAsync<T>(key);
			if (cache == null)
			{
				var value = await factory() ?? throw new ArgumentNullException(nameof(factory));
				await SetAsync(key, value, expiration);
				return value;
			}
			return cache;
		}

		public async Task RemoveAsync(string key)
		{
			if (_cache.TryRemove(key, out _))
				await _keyStore.RemoveKeyMetadataAsync(key);
		}

		public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null)
		{
			var metadata = new CacheMetadata
			{
				LastAccessTime = DateTimeOffset.Now,
				CreatedTime = DateTimeOffset.Now,
				ExpirationTime = expiration,
				Key = key,
				SetCount = 1,
				ValueType = typeof(T),
				Operations = [CacheOperation.Success(CacheOperationType.Set, DateTimeOffset.Now)],
			};
			_cache.TryAdd(key, value!);
			await _keyStore.StoreKeyMetadataAsync(metadata);
		}
	}
}
