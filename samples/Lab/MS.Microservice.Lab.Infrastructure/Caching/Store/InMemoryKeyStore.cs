using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MS.Microservice.Infrastructure.Caching.Store
{
	internal class InMemoryKeyStore : IKeyStore
	{
		private readonly ConcurrentDictionary<string, CacheMetadata> _keyStore = new();
		public async Task<IEnumerable<CacheMetadata>> GetAllKeyMetadataAsync()
		{
			return await Task.FromResult(_keyStore.Values);
		}

		public Task<CacheMetadata?> GetKeyMetadataAsync(string key)
		{
			_keyStore.TryGetValue(key, out var metadata);
			return Task.FromResult(metadata);
		}

		public async Task LogOperationAsync(string key, CacheOperation operation)
		{
			var metadata = await GetKeyMetadataAsync(key);
			if (metadata != null)
			{
				metadata.AddOperation(operation);
				await UpdateKeyMetadataAsync(metadata);
			}
		}

		public async Task RemoveKeyMetadataAsync(string key)
		{
			_keyStore.TryRemove(key, out _);
			await Task.CompletedTask;
		}

		public async Task StoreKeyMetadataAsync(CacheMetadata metadata)
		{
			_keyStore.TryAdd(metadata.Key, metadata);
			await Task.CompletedTask;
		}

		public async Task UpdateKeyMetadataAsync(CacheMetadata metadata)
		{
			_keyStore.AddOrUpdate(metadata.Key, metadata, (k, v) => metadata);
			await Task.CompletedTask;
		}
	}
}
