using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MS.Microservice.Infrastructure.Caching
{
	public class CacheManager(IKeyStore keyStore, ICacheStore cacheStore, CacheOperationLogOptions options)
	{
		private readonly IKeyStore _keyStore = keyStore;
		private readonly ICacheStore _cache = cacheStore;
		private readonly CacheOperationLogOptions _options = options;
		private readonly OperationLogBuffer _operationBuffer = new OperationLogBuffer(keyStore, options);

		public Task<bool> ExistsAsync(string key)
		{
			throw new NotImplementedException();
		}

		public async Task<CacheItem<T>?> GetAsync<T>(string key)
		{
			var operation = new CacheOperation
			{
				//Key = key,
				OperationType = CacheOperationType.Get,
				OperationTime = DateTime.UtcNow
			};

			try
			{
				var value = await _cache.GetAsync<CacheItem<T>>(key);
				if (value != null)
				{
					operation.IsSuccess = true;
					_operationBuffer.AddOperation(operation);
					return value;
				}

				operation.IsSuccess = false;
				_operationBuffer.AddOperation(operation);
				return default;
			}
			catch (Exception ex)
			{
				operation.IsSuccess = false;
				operation.ErrorMessage = ex.Message;
				_operationBuffer.AddOperation(operation);
				throw;
			}
		}

		public Task<CacheItem<T>> GetOrAddAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null)
		{
			throw new NotImplementedException();
		}

		public Task RemoveAsync(string key)
		{
			throw new NotImplementedException();
		}

		public Task SetAsync<T>(string key, T value, TimeSpan? expiration = null)
		{
			throw new NotImplementedException();
		}
	}
}
